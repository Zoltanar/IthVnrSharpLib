using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace IthVnrSharpLib
{
	public sealed class HookTextThread : TextThread
	{
		private const uint MaxHook = 64;
		private static readonly Encoding ShiftJis = Encoding.GetEncoding("SHIFT-JIS");
		private Encoding _prefEncoding = Encoding.Unicode;
		private byte[] _lastCopyBytes;

		public override object MergeProperty => HookCode;

		public override string PersistentIdentifier => HookNameless;
		public override string DisplayIdentifier => $"{HookNameless} {HookCode}";

		public override string Text
		{
			get
			{
				var result = GetTextForEncoding(PrefEncoding);
				return result;
			}
		}

		public uint Addr => Parameter.hook;

		private ConcurrentArrayList<byte> Bytes { get; } = new(TextTrimAt * 4, TextTrimCount * 4);
		private ConcurrentList<byte> CurrentBytes { get; } = new();
		public bool EncodingDefined { get; set; }

		public string HookCode { get; private set; }
		private string HookNameless { get; set; }
		public ThreadParameter Parameter { get; set; }
		public override Encoding PrefEncoding
		{
			get => _prefEncoding;
			set
			{
				_prefEncoding = value;
				if (GameThread != null) GameThread.Encoding = value.WebName;
				OnPropertyChanged();
			}
		}

		public override bool EncodingCanChange { get; } = true;
		public IntPtr ProcessRecordPtr { get; set; }
		private uint Status
		{
			get => VnrProxy.TextThread_GetStatus(Id);
			set => VnrProxy.TextThread_SetStatus(Id, value);
		}

		public HookTextThread(IntPtr id) : base(id)
		{
		}

		public override void AddText(object value)
		{
			if (value is not byte[] bytes) throw new NotSupportedException();
			CurrentBytes.AddRange(bytes);
		}

		public override void Clear(bool clearCurrent)
		{
			if (clearCurrent) CurrentBytes.Clear();
			Bytes.Clear();
		}

		public override string SearchForText(string searchTerm, bool searchAllEncodings)
		{
			if (searchAllEncodings)
			{
				foreach (var encoding in IthVnrViewModel.Encodings)
				{
					var linesForEncoding =
						GetTextForEncoding(encoding).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
					var firstLineForEncoding = linesForEncoding.FirstOrDefault(l => l.Contains(searchTerm));
					if (firstLineForEncoding != null) return firstLineForEncoding;
				}

				return null;
			}

			var textLines = Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			var firstLineWith = textLines.FirstOrDefault(l => l.Contains(searchTerm));
			return firstLineWith;
		}

		public override string ToString() => DisplayName != null
			? $"{(IsPaused ? "[Paused]" : string.Empty)}{(IsPosting ? "[Posting]" : string.Empty)}{DisplayName}"
			: $"[{Id}] EntryString hasn't been set";

		protected override int GetCharacterCount() => Bytes.AggregateCount + CurrentBytes.Count;

		private static List<FontFamily> FontFamiliesSupportingChar(IEnumerable<FontFamily> fontFamilies, char characterToCheck)
		{
			var supportedFamilies = new List<FontFamily>();
			int unicodeValue = Convert.ToUInt16(characterToCheck);

			foreach (var family in fontFamilies)
			{
				var typefaces = family.GetTypefaces();
				foreach (Typeface typeface in typefaces)
				{
					typeface.TryGetGlyphTypeface(out var glyph);
					if (glyph == null || !glyph.CharacterToGlyphMap.TryGetValue(unicodeValue, out _)) continue;
					family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out _);
					supportedFamilies.Add(family);
					break;
				}
			}

			return supportedFamilies;
		}

		private static void GetStrings(byte[] currentBytes, out string utf8, out string utf16, out string sjis)
		{
			utf8 = Encoding.UTF8.GetString(currentBytes);
			utf16 = Encoding.Unicode.GetString(currentBytes);
			sjis = ShiftJis.GetString(currentBytes);
		}

		private static unsafe uint IsUnicodeHook(ProcessRecord processRecord, uint hookAddr)
		{
			uint res = 0;
			WinAPI.WaitForSingleObject(processRecord.hookman_mutex, 0);
			for (int i = 0; i < MaxHook; i++)
			{
				var newPtr = IntPtr.Add(processRecord.hookman_map, i * sizeof(Hook));
				var hook = Marshal.PtrToStructure<Hook>(newPtr);
				var hookAddress = hook.Address();
				if (hookAddress != hookAddr) continue;
				res = hook.Type() & (uint)HookParamType.USING_UNICODE;
				break;
			}

			WinAPI.NtReleaseMutant(processRecord.hookman_mutex, IntPtr.Zero);
			return res;
		}

		private static string ToHexString(uint number) => $"{number:X}";
		private static string ToHexString(long number) => $"{(int)number:X}";

		public string GetTextForEncoding(Encoding encoding)
		{
			string curString = encoding.GetString(CurrentBytes.ArrayCopy());
			string result;
			lock (Bytes.SyncRoot)
			{
				result = string.Join(Environment.NewLine, Bytes.ReadOnlyList.Select(x => PrefEncoding.GetString(x)))
								 + Environment.NewLine + curString;
			}

			return result;
		}

		public void SetEncoding(Encoding prefEncoding)
		{
			if (prefEncoding != null)
			{
				PrefEncoding = prefEncoding;
				EncodingDefined = true;
				return;
			}

			var bytes = Bytes.ToAggregateArray();
			EncodingBools encoding = new EncodingBools();
			try
			{
				GetStrings(bytes, out string utf8, out string utf16, out string sjis);
				if (utf8.Any(c => c == 65533)) encoding.IsUtf8 = false;
				if (utf16.Any(c => c == 65533)) encoding.IsUtf16 = false;
				if (sjis.Any(c => c == 65533)) encoding.IsSJis = false;
				if (utf8.IndexOf('\0') >= 0)
				{
					encoding.IsUtf8 = false;
					return;
				}

				if (encoding.Bools.All(v => v.HasValue && !v.Value))
				{
					PrefEncoding = Encoding.Unicode;
				}

				var stillNotFalse = encoding.Bools.Count(v => !v.HasValue);
				if (stillNotFalse == 1)
				{
					PrefEncoding = encoding.GetBest();
					return;
				}

				const string fontsFolder = @"C:\Windows\Fonts\"; //todo make editable
				const string cjkFontName = @"MS Mincho"; //todo make editable
				ICollection<FontFamily> fontFamilies = Fonts.GetFontFamilies(@"C:\Windows\Fonts\");
				var cjkFont = fontFamilies.FirstOrDefault(x => x.Source.EndsWith(cjkFontName));
				if (cjkFont == null)
					throw new InvalidOperationException($"Font '{cjkFontName}' is required but not found in '{fontsFolder}'");
				if (encoding.IsUtf16 != false)
				{
					foreach (var t in utf16)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, t);
						if (supportedFamilies.Contains(cjkFont)) continue;
						encoding.IsUtf16 = false;
						break;
					}
				}

				if (encoding.IsUtf8 != false)
				{
					foreach (var c in utf8)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, c);
						if (supportedFamilies.Contains(cjkFont)) continue;
						encoding.IsUtf8 = false;
						break;
					}
				}

				if (encoding.IsSJis != false)
				{
					foreach (var c in sjis)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, c);
						if (supportedFamilies.Contains(cjkFont)) continue;
						encoding.IsSJis = false;
						break;
					}
				}

				PrefEncoding = encoding.GetBest();
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
			}
			finally
			{
				EncodingDefined = true;
			}
		}

		public void SetEntryString()
		{
			Number = VnrProxy.TextThread_GetNumber(Id);
			var hookName = GetHookName(Parameter.pid, Parameter.hook);
			HookNameless = $"0x{Parameter.hook:X}:0x{Parameter.retn:X}:0x{Parameter.spl:X}";
			var hookFull = $"{HookNameless}:{hookName}";
			var threadString = $"{Number:0000}:{Parameter.pid}:{hookFull}";
			HookCode = GetLink();
			DisplayName = $"{threadString} ({HookCode})";
		}

		public void SetUnicodeStatus(IntPtr processRecord, uint hook)
		{
			//VnrProxy.SetTextThreadUnicodeStatus(Id, processRecord, hook);
			SetTextThreadUnicodeStatus(Marshal.PtrToStructure<ProcessRecord>(processRecord), hook);
		}

		private unsafe IntPtr GetAllocationBase(IntPtr addr)
		{
			if (ProcessRecordPtr == IntPtr.Zero) return IntPtr.Zero;
			var pr = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			if (WinAPI.VirtualQueryEx(pr.process_handle, addr, out var info,
				(uint)sizeof(WinAPI.MEMORY_BASIC_INFORMATION)) == 0) return IntPtr.Zero;
			return (info.Type & 0x1000000) != 0 ? info.AllocationBase : IntPtr.Zero;
		}

		private string GetCode(HookParam hp)
		{
			string code = "/H";
			char c;
			if ((hp.type & (uint)HookParamType.PRINT_DWORD) != 0) c = 'H';
			else if ((hp.type & (uint)HookParamType.USING_UNICODE) != 0)
			{
				if ((hp.type & (uint)HookParamType.USING_STRING) != 0) c = 'Q';
				else if ((hp.type & (uint)HookParamType.STRING_LAST_CHAR) != 0) c = '\0';
				else c = 'W';
			}
			else
			{
				if ((hp.type & (uint)HookParamType.USING_STRING) != 0) c = 'S';
				else if ((hp.type & (uint)HookParamType.BIG_ENDIAN) != 0) c = 'A';
				else if ((hp.type & (uint)HookParamType.STRING_LAST_CHAR) != 0) c = 'E';
				else c = 'B';
			}

			if (c != '\0') code += c;
			if ((hp.type & (uint)HookParamType.NO_CONTEXT) != 0) code += 'N';
			if (hp.offset >> 31 != 0) code += "-" + ToHexString(-(hp.offset + 4));
			else code += ToHexString(hp.offset);
			if ((hp.type & (uint)HookParamType.DATA_INDIRECT) != 0)
			{
				if (hp.index >> 31 != 0) code += "*-" + ToHexString(-hp.index);
				else code += "*" + ToHexString(hp.index);
			}

			if ((hp.type & (uint)HookParamType.USING_SPLIT) != 0)
			{
				if (hp.split >> 31 != 0) code += ":-" + ToHexString(-(4 + hp.split));
				else
					code += ":" + ToHexString(hp.split);
			}

			if ((hp.type & (uint)HookParamType.SPLIT_INDIRECT) != 0)
			{
				if (hp.split_index >> 31 != 0) code += "*-" + ToHexString(-hp.split_index);
				else code += "*" + ToHexString(hp.split_index);
			}

			if (ProcessId != 0)
			{
				IntPtr allocationBase = GetAllocationBase(new IntPtr(hp.address));
				if (allocationBase != IntPtr.Zero)
				{
					string path = GetModuleFileNameAsString(allocationBase);
					if (!string.IsNullOrWhiteSpace(path))
					{
						string fileName = path.Substring(path.LastIndexOf('\\') + 1);
						uint relativeHookAddress = hp.address - (uint)allocationBase;
						code += "@" + ToHexString(relativeHookAddress) + ":" + fileName;
						return code;
					}
				}
			}

			if (hp.module != 0)
			{
				code += "@" + ToHexString(hp.address) + "!" + ToHexString(hp.module);
				if (hp.function != 0) code += "!" + ToHexString(hp.function);
			}
			else
			{
				// The original address is stored in the function field
				// if (module == NULL && function != NULL).
				// MODULE_OFFSET and FUNCTION_OFFSET are removed from HookParam.type in
				// TextHook::UnsafeInsertHookCode() and can not be used here.
				if (hp.function != 0) code += "@" + ToHexString(hp.function);
				else code += "@" + ToHexString(hp.address) + ":";
			}

			return code;
		}

		private unsafe string GetHookName(uint pid, uint hookAddr)
		{
			IntPtr handle = IntPtr.Zero;
			try
			{
				if (pid == 0) return null;
				if (ProcessRecordPtr == IntPtr.Zero) return null;
				var processRecord = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
				handle = processRecord.hookman_mutex;
				WinAPI.WaitForSingleObject(processRecord.hookman_mutex, 0);
				for (int i = 0; i < MaxHook; i++)
				{
					var newPtr = IntPtr.Add(processRecord.hookman_map, i * sizeof(Hook));
					var hook = Marshal.PtrToStructure<Hook>(newPtr);
					var hookAddress = hook.Address();
					if (hookAddress != hookAddr) continue;
					int len = hook.NameLength();
					if (len >= 512) len = 512;
					byte[] buffer = new byte[len];
					WinAPI.ReadProcessMemory(processRecord.process_handle, hook.hook_name, buffer, buffer.Length, out _);
					return Encoding.UTF8.GetString(buffer, 0, buffer[len - 1] == 0 ? len - 1 : len);
				}
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				WinAPI.NtReleaseMutant(handle, IntPtr.Zero);
			}

			return null;
		}

		private unsafe bool GetHookParam(out HookParam hp)
		{
			hp = new HookParam();
			if (ProcessId == 0) return false;
			if (ProcessRecordPtr == IntPtr.Zero) return false;
			var processRecord = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			bool result = false;
			WinAPI.WaitForSingleObject(processRecord.hookman_mutex, 0);
			for (int i = 0; i < MaxHook; i++)
			{
				var newPtr = IntPtr.Add(processRecord.hookman_map, i * sizeof(Hook));
				var hook = Marshal.PtrToStructure<Hook>(newPtr);
				var hookAddress = hook.Address();
				if (hookAddress != Addr) continue;
				hp = hook.hp;
				result = true;
				break;
			}

			WinAPI.NtReleaseMutant(processRecord.hookman_mutex, IntPtr.Zero);
			return result;
		}

		private string GetLink()
		{
			if (LinkTo != null) return $"->{LinkTo.Number}";
			if (ProcessId == 0) return "ConsoleOutput";
			return GetHookParam(out var hp) ? GetCode(hp) : null;
		}

		private string GetModuleFileNameAsString(IntPtr allocationBase)
		{
			if (ProcessRecordPtr == IntPtr.Zero) return "";
			var pr = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			var path = new StringBuilder(512);
			return WinAPI.GetModuleFileNameEx(pr.process_handle, allocationBase, path, 512) != 0 ? path.ToString() : "";
		}

		/// <summary>
		/// Returns text from bytes.
		/// </summary>
		private string OnTick(byte[] bytes)
		{
			string text = PrefEncoding.GetString(bytes);
			UpdateDisplay(this, new TextOutputEventArgs(this, text, "Internal", false));
			return text;
		}

		private void CopyTextToClipboard(string text, byte[] bytes)
		{
			if ((DateTime.UtcNow - LastCopyTime).TotalMilliseconds > 20 && _lastCopyBytes != bytes)
			{
				var thread = new Thread(() =>
				{
					try
					{
						StaticHelpers.LogToDebug($"Copying to clipboard at {DateTime.UtcNow:HH\\:mm\\:ss\\:fff}\t{text}");
						Clipboard.SetText(text);
					}
					catch (Exception ex)
					{
						StaticHelpers.LogToFile(ex);
					}
				});
				thread.SetApartmentState(ApartmentState.STA); //Set the thread to STA
				thread.Start();
				thread.Join(); //Wait for the thread to end
			}
			LastCopyTime = DateTime.UtcNow;
			_lastCopyBytes = bytes;
		}

		private void SetTextThreadUnicodeStatus(ProcessRecord processRecord, uint hook)
		{
			if (IsUnicodeHook(processRecord, hook) != 0) Status |= (uint)HookParamType.USING_UNICODE;
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs e)
		{
			lock (TimerTickLock)
			{
				Timer?.Close();
				Timer = null;
				if (CurrentBytes.Count == 0) return;
				byte[] currentBytes = CurrentBytes.ArrayCopy();
				CurrentBytes.Clear();
				//if(_monitorThread != null) _monitorPairs[DateTime.UtcNow] = currentBytes.Length;
				if (currentBytes.Length > 2000) return; //todo report error, make 2000 a setting
				Bytes.Add(currentBytes);
				if (!EncodingDefined && (IsDisplay || IsPosting)) SetEncoding(null);
				if (IsDisplay) OnPropertyChanged(nameof(Text));
				if (!IsPosting) return;
				var text = OnTick(currentBytes);
				if (CopyToClipboard) CopyTextToClipboard(text, currentBytes);
			}
		}

		private class EncodingBools
		{
			public List<bool?> Bools { get; } = new List<bool?> { null, null, null };

			public bool? IsSJis
			{
				get => Bools[0];
				set => Bools[0] = value;
			}

			public bool? IsUtf16
			{
				get => Bools[2];
				set => Bools[2] = value;
			}

			public bool? IsUtf8
			{
				get => Bools[1];
				set => Bools[1] = value;
			}

			public Encoding GetBest()
			{
				switch (Bools.FindIndex(x => !x.HasValue || x.Value))
				{
					case 0: return ShiftJis;
					case 1: return Encoding.UTF8;
					case 2: return Encoding.Unicode;
					default: return Encoding.Unicode;
				}
			}
		}
	}
}