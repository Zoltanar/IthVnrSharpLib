using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using IthVnrSharpLib.Properties;
using Timer = System.Timers.Timer;

namespace IthVnrSharpLib
{
	public delegate bool TextOutputEvent(object sender, TextOutputEventArgs e);

	public class TextThread : INotifyPropertyChanged
	{
		public static TextOutputEvent UpdateDisplay;
		public static VNR VnrProxy;
		public static Func<bool> CopyToClipboardFunc;
		private static bool CopyToClipboard => CopyToClipboardFunc();
		public static IthVnrViewModel ViewModel;
		private static readonly Encoding ShiftJis = Encoding.GetEncoding("SHIFT-JIS");
		private const uint MaxHook = 64;
		private static readonly Regex LatinOnlyRegex = new(@"^[a-zA-Z0-9:\/\\\r\n .!?,;@()_$^""]+$");

		public bool Removed { get; set; }
		public IntPtr Id { get; set; }
		public virtual bool IsConsole { get; set; }

		public bool IsDisplay
		{
			get => _isDisplay;
			set
			{
				if (_isDisplay == value) return;
				_isDisplay = value;
				OnPropertyChanged();
				if (!IsConsole) GameThread.IsDisplay = _isDisplay;
			} 
		}

		public virtual bool IsPaused
		{
			get => _isPaused;
			set
			{
				if (_isPaused == value) return;
				_isPaused = value;
				if (!IsConsole) GameThread.IsPaused = _isPaused;
				OnPropertyChanged();
				if (_isPaused || _monitorThread != null) return;
				_monitorThread = new Thread(StartMonitor) { IsBackground = true };
				_monitorThread.Start();
			}
		}

		public virtual bool IsPosting
		{
			get => _isPosting;
			set
			{
				_isPosting = value;
				if (!IsConsole) GameThread.IsPosting = _isPosting;
				if (_isPosting) CloseByteSection(this, null);
				OnPropertyChanged();
			}
		}

		public ConcurrentArrayList<byte> Bytes { get; } = new (300, 200);

		public virtual Encoding PrefEncoding
		{
			get => _prefEncoding;
			set
			{
				_prefEncoding = value;
				if (!IsConsole) GameThread.Encoding = value.WebName;
				OnPropertyChanged();
			}
		}

		public string HookCode { get; private set; }
		public string HookName { get; private set; }
		public string HookNameless { get; private set; }
		public string HookFull { get; private set; }
		public Dictionary<IntPtr, TextThread> MergedThreads { get; } = new ();
		public ConcurrentList<byte> CurrentBytes { get; } = new ();
		public ThreadParameter Parameter { get; set; }
		public string EntryString => ThreadString == null ? null : $"{ThreadString}({HookCode})";
		public virtual bool EncodingDefined { get; set; }
		public uint ProcessId => Parameter.pid;
		public uint Addr => Parameter.hook;
		public uint Status
		{
			get => VnrProxy.TextThread_GetStatus(Id);
			set => VnrProxy.TextThread_SetStatus(Id, value);
		}
		public ushort Number { get; private set; }
		public string ThreadString { get; protected set; }
		public virtual string Text
		{
			get
			{
				string curString = PrefEncoding.GetString(CurrentBytes.ArrayCopy());
				string result;
				lock (Bytes.SyncRoot)
				{
					result = string.Join(Environment.NewLine, Bytes.ReadOnlyList.Select(x => PrefEncoding.GetString(x)))
							 + Environment.NewLine + curString;
				}
				return result;
			}
		}

		public TextThread LinkTo { get; set; }
		public IntPtr ProcessRecordPtr { get; set; }

		public static Encoding[] AllEncodings => IthVnrViewModel.Encodings;
		public GameTextThread GameThread { get; set; }

		private static readonly object TimerTickLock = new ();
		private Timer _timer;
		private DateTime _lastUpdateTime = DateTime.MinValue;
		private byte[] _lastUpdateBytes;
		private Thread _monitorThread;
		private bool _isPosting;
		private bool _isPaused;
		private readonly Dictionary<DateTime, int> _monitorPairs = new ();
		private bool _isDisplay = true;
		private Encoding _prefEncoding = Encoding.Unicode;

		public TextThread()
		{
			_monitorThread = new Thread(StartMonitor) { IsBackground = true };
			_monitorThread.Start();
		}
		
		public void SetUnicodeStatus(IntPtr processRecord, uint hook)
		{
			//VnrProxy.SetTextThreadUnicodeStatus(Id, processRecord, hook);
			SetTextThreadUnicodeStatus(Marshal.PtrToStructure<ProcessRecord>(processRecord), hook);
		}

		public override string ToString() => EntryString != null ? 
			$"{(IsPaused ? "[Paused]" : string.Empty)}{(IsPosting ? "[Posting]" : string.Empty)}{EntryString}" : 
			$"[{Id}] EntryString hasn't been set";

		public void StartTimer()
		{
			lock (TimerTickLock)
			{
				if (_timer == null)
				{
					_timer = new Timer
					{
						AutoReset = false,
						Enabled = true,
						Interval = 100 //todo make this a setting (splitting interval)
					};
					_timer.Elapsed += CloseByteSection;
				}
				_timer.Stop();
				_timer.Start();
			}
		}

		public void SetEntryString()
		{
			Number = VnrProxy.TextThread_GetNumber(Id);
			HookName = GetHookName(Parameter.pid, Parameter.hook);
			HookNameless = $"0x{Parameter.hook:X}:0x{Parameter.retn:X}:0x{Parameter.spl:X}";
			HookFull = $"{HookNameless}:{HookName}";
			ThreadString = $"{Number:X4}:{Parameter.pid}:{HookFull}";
			HookCode = GetLink();
		}

		private void OnTickCopy(byte[] bytes)
		{
			var currentText = PrefEncoding.GetString(bytes);
			if ((DateTime.UtcNow - _lastUpdateTime).TotalMilliseconds > 20 && _lastUpdateBytes != bytes)
			{
				var thread = new Thread(() =>
				{
					try
					{
						Debug.WriteLine($"Copying to clipboard at {DateTime.UtcNow:HH\\:mm\\:ss\\:fff}\t{currentText}");
						Clipboard.SetText(currentText);
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
			_lastUpdateTime = DateTime.UtcNow;
			_lastUpdateBytes = bytes;
			UpdateDisplay(this, new TextOutputEventArgs(this, currentText, "Internal", true));
		}

		private void OnTick(byte[] bytes)
		{
			string text = PrefEncoding.GetString(bytes);
			UpdateDisplay(this, new TextOutputEventArgs(this, text, "Internal", false));
		}

		public void CloseByteSection(object sender, ElapsedEventArgs e)
		{
			try
			{
				if (IsConsole) return;
				lock (TimerTickLock)
				{
					if (CurrentBytes.Count == 0) return;
					byte[] currentBytes = CurrentBytes.ArrayCopy();
					CurrentBytes.Clear();
					//if(_monitorThread != null) _monitorPairs[DateTime.UtcNow] = currentBytes.Length;
					if (currentBytes.Length > 2000) return; //todo report error, make 2000 a setting
					Bytes.Add(currentBytes);
					if (!IsPosting) return;
					if (!EncodingDefined) SetEncoding(null);
					if (CopyToClipboard) OnTickCopy(currentBytes);
					else OnTick(currentBytes);
				}
			}
			finally
			{
				_timer?.Close();
				_timer = null;
			}
		}

		private static void GetStrings(byte[] currentBytes, out string utf8, out string utf16, out string sjis)
		{
			utf8 = Encoding.UTF8.GetString(currentBytes);
			utf16 = Encoding.Unicode.GetString(currentBytes);
			sjis = ShiftJis.GetString(currentBytes);
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
				ICollection<FontFamily> fontFamilies = Fonts.GetFontFamilies(@"C:\Windows\Fonts\");
				var msMincho = fontFamilies.FirstOrDefault(x => x.Source.EndsWith("MS Mincho"));
				if (encoding.IsUtf16 != false)
				{
					foreach (var t in utf16)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, t);
						if (supportedFamilies.Contains(msMincho)) continue;
						encoding.IsUtf16 = false;
						break;
					}
				}
				if (encoding.IsUtf8 != false)
				{
					foreach (var c in utf8)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, c);
						if (!supportedFamilies.Contains(msMincho))
						{
							encoding.IsUtf8 = false;
							break;
						}
					}
				}

				if (encoding.IsSJis != false)
				{
					foreach (var c in sjis)
					{
						var supportedFamilies = FontFamiliesSupportingChar(fontFamilies, c);
						if (supportedFamilies.Contains(msMincho)) continue;
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

		private void StartMonitor()
		{
			while (true)
			{
				if (ViewModel == null || Removed || MonitorLoop()) break;
				Thread.Sleep(1000);
			}
			_monitorPairs.Clear();
			if (IsDisplay) OnPropertyChanged(nameof(Text));
			_monitorThread = null;
		}

		/// <summary>
		/// true to break from loop
		/// </summary>
		private bool MonitorLoop()
		{
			if (IsPaused) return true;
			if (_monitorPairs.Count > 20) _monitorPairs.Clear();
			_monitorPairs[DateTime.UtcNow] = Bytes.AggregateCount + CurrentBytes.Count;
			if (_monitorPairs.Count < 5) return false;
			var list = _monitorPairs as IEnumerable<KeyValuePair<DateTime, int>>;
			DateTime startTime = default;
			int startLength = 0;
			var lpsList = new List<double>();
			foreach (var pair in list)
			{
				if (ViewModel == null || Removed || IsPaused) return true;
				if (startTime == default)
				{
					startTime = pair.Key;
					startLength = pair.Value;
					continue;
				}
				var timePassed = pair.Key - startTime;
				lpsList.Add((pair.Value - startLength) / timePassed.TotalSeconds);
				startTime = pair.Key;
				startLength = pair.Value;
			}
			var lengthPerSecond = lpsList.Average();
			if (lengthPerSecond < 500) return false;
			IsPaused = true;
			return true;
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

		private void SetTextThreadUnicodeStatus(ProcessRecord processRecord, uint hook)
		{
			if (IsUnicodeHook(processRecord, hook) != 0) Status |= (uint)HookParamType.USING_UNICODE;
		}

		private unsafe uint IsUnicodeHook(ProcessRecord processRecord, uint hookAddr)
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

		private unsafe bool GetHookParam(uint pid, uint hookAddr, out HookParam hp)
		{
			hp = new HookParam();
			if (pid == 0) return false;
			if (ProcessRecordPtr == IntPtr.Zero) return false;
			var processRecord = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			bool result = false;
			WinAPI.WaitForSingleObject(processRecord.hookman_mutex, 0);
			for (int i = 0; i < MaxHook; i++)
			{
				var newPtr = IntPtr.Add(processRecord.hookman_map, i * sizeof(Hook));
				var hook = Marshal.PtrToStructure<Hook>(newPtr);
				var hookAddress = hook.Address();
				if (hookAddress != hookAddr) continue;
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
			return GetHookParam(ProcessId, Addr, out HookParam hp) ? GetCode(hp, ProcessId) : null;
		}

		private string GetCode(HookParam hp, uint pid)
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
			if (pid != 0)
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

		private unsafe IntPtr GetAllocationBase(IntPtr addr)
		{
			if (ProcessRecordPtr == IntPtr.Zero) return IntPtr.Zero;
			var pr = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			if (WinAPI.VirtualQueryEx(pr.process_handle, addr, out var info, (uint)sizeof(WinAPI.MEMORY_BASIC_INFORMATION)) == 0) return IntPtr.Zero;
			return (info.Type & 0x1000000) != 0 ? info.AllocationBase : IntPtr.Zero;
		}

		private string GetModuleFileNameAsString(IntPtr allocationBase)
		{
			if (ProcessRecordPtr == IntPtr.Zero) return "";
			var pr = Marshal.PtrToStructure<ProcessRecord>(ProcessRecordPtr);
			var path = new StringBuilder(512);
			return WinAPI.GetModuleFileNameEx(pr.process_handle, allocationBase, path, 512) != 0 ? path.ToString() : "";
		}

		private static string ToHexString(uint number) => $"{number:X}";
		private static string ToHexString(long number) => $"{(int)number:X}";



		private class EncodingBools
		{
			public List<bool?> Bools { get; } = new List<bool?> { null, null, null };

			public bool? IsSJis
			{
				get => Bools[0];
				set => Bools[0] = value;
			}
			public bool? IsUtf8
			{
				get => Bools[1];
				set => Bools[1] = value;
			}
			public bool? IsUtf16
			{
				get => Bools[2];
				set => Bools[2] = value;
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

		public event PropertyChangedEventHandler PropertyChanged;

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public class TextOutputEventArgs
	{
		public string Text { get; set; }
		public DateTime Time { get; set; }
		public string Source { get; set; }
		public bool FromClipboard { get; set; }
		public bool FromInternal { get; set; }
		public TextThread TextThread { get; set; }

		public override string ToString()
		{
			var source = Source;
			if (TextThread != null)
			{
				if (TextThread.IsConsole) source += "/Console";
				else source += "/" + TextThread.EntryString.Substring(0, 4);
			}
			return $"{Time:HH:mm:ss:fff} {source} {Text.Substring(0, Math.Min(50, Text.Length))}";
		}

		public TextOutputEventArgs(TextThread thread, string text, string source, bool clipboard)
		{
			TextThread = thread;
			if (text.StartsWith("\r\n")) text = text.Substring(2);
			if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
			Text = text.Trim();
			Time = DateTime.UtcNow;
			Source = source;
			if (clipboard) FromClipboard = true;
			else FromInternal = true;
		}
	}
}
