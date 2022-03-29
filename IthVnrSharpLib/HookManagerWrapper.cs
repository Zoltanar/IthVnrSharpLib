using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using IthVnrSharpLib.Properties;

namespace IthVnrSharpLib
{
	public class HookManagerWrapper : MarshalByRefObject, IDisposable, INotifyPropertyChanged
	{
		public override object InitializeLifetimeService() => null;
		private readonly IthVnrViewModel _viewModel;
		private VNR VnrHost => _viewModel.VnrHost;
		private IntPtr _hookManager;
		private readonly ThreadTableWrapper _threadTable;

		private bool _mergeByHookCode;
		//todo do not allow editing threads collection directly
		/// <summary>
		/// Does not include system threads like Console and Clipboard
		/// </summary>
		public ConcurrentDictionary<IntPtr, TextThread> TextThreads => _threadTable.Map;
		public ConcurrentDictionary<int, ProcessInfo> Processes { get; } = new();
		public bool Paused { get; set; }
		public bool ShowLatestThread { get; set; }
		public bool IgnoreOtherThreads { get; set; }
		public bool MergeByHookCode
		{
			get => _mergeByHookCode;
			set
			{
				if (value == _mergeByHookCode) return;
				_mergeByHookCode = value;
				if (_viewModel == null) return;
				var selectedThreadPointer = _viewModel.SelectedTextThread?.Id ?? IntPtr.Zero;
				if (_mergeByHookCode) MergeThreads();
				else UnmergeThreads();
				if (selectedThreadPointer != IntPtr.Zero)
				{
					GetOrCreateThread(selectedThreadPointer, out var selectedThread);
					UpdateDisplayThread(selectedThread);
				}
				_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
				_viewModel.OnPropertyChanged(nameof(_viewModel.HookManager));
			}
		}

		private void UnmergeThreads()
		{
			var unmergedThreads = new List<TextThread>();
			foreach (var thread in TextThreads.Values)
			{
				unmergedThreads.Add(thread);
				foreach (var mergedThread in thread.MergedThreads.Values) unmergedThreads.Add(mergedThread);
				thread.MergedThreads.Clear();
			}

			_threadTable.ClearAll(false);
			//todo do not allow editing threads collection directly
			foreach (var thread in unmergedThreads) TextThreads[thread.Id] = thread;
			ResetDisplayCollection();
		}

		private void ResetDisplayCollection()
		{
			_viewModel.ClearThreadDisplayCollection();
			foreach (var textThread in _threadTable.AllThreads)
			{
				_viewModel.AddNewThreadToDisplayCollection(textThread);
			}
		}

		private void MergeThreads()
		{
			var mergedGroups = TextThreads.Values.OrderBy(x => x.Number).GroupBy(x => x.MergeProperty);
			var mergedThreads = new List<TextThread>();
			foreach (var mergedGroup in mergedGroups)
			{
				if (mergedGroup.Key == null) continue;
				var enumerator = mergedGroup.GetEnumerator();
				enumerator.MoveNext();
				var masterThread = enumerator.Current;
				mergedThreads.Add(masterThread);
				// ReSharper disable PossibleNullReferenceException
				while (enumerator.MoveNext()) masterThread.MergedThreads[enumerator.Current.Id] = enumerator.Current;
				// ReSharper restore PossibleNullReferenceException
				enumerator.Dispose();
			}
			_threadTable.ClearAll(false);
			//todo do not allow editing threads collection directly
			foreach (var thread in mergedThreads) TextThreads[thread.Id] = thread;
			ResetDisplayCollection();
		}

		public HookManagerWrapper(IthVnrViewModel propertyChangedNotifier, TextOutputEvent updateDisplayText, ThreadTableWrapper threadTable)
		{
			TextThread.UpdateDisplay = updateDisplayText;
			TextThread.CopyToClipboardFunc = () => _viewModel.Settings.ClipboardFlag;
			_viewModel = propertyChangedNotifier;
			_threadTable = threadTable;
			var consoleThread = _threadTable.CreateConsoleThread();
			_viewModel.AddNewThreadToDisplayCollection(consoleThread);
			ConsoleOutput(StaticHelpers.VersionInfo, true);
		}

		private void HookManager_RegisterProcessAttachCallback(VNR.ProcessEventCallback callback) => VnrHost.HookManager_RegisterProcessAttachCallback(_hookManager, callback);

		private void HookManager_RegisterProcessDetachCallback(VNR.ProcessEventCallback callback) => VnrHost.HookManager_RegisterProcessDetachCallback(_hookManager, callback);

		public void ConsoleOutput2(string text) => ConsoleOutput(text, true);

		public void ConsoleOutput(string text, bool show)
		{
			_threadTable.ConsoleThread.AddText(text);
			if (show) UpdateDisplayThread(_threadTable.ConsoleThread);
		}

		private static void UpdateDisplayThread(TextThread thread)
		{
			if (thread.IsDisplay) thread.OnPropertyChanged(nameof(TextThread.Text));
		}

		private IntPtr TextThread_GetThreadParameter(IntPtr thread) => VnrHost.TextThread_GetThreadParameter(thread);

		private void HookManager_RegisterThreadCreateCallback(VNR.ThreadEventCallback threadCreate) => VnrHost.HookManager_RegisterThreadCreateCallback(_hookManager, threadCreate);
		private void HookManager_RegisterThreadRemoveCallback(VNR.ThreadEventCallback threadRemove) => VnrHost.HookManager_RegisterThreadRemoveCallback(_hookManager, threadRemove);
		private void HookManager_RegisterThreadResetCallback(VNR.ThreadEventCallback threadReset) => VnrHost.HookManager_RegisterThreadResetCallback(_hookManager, threadReset);

		private void TextThread_RegisterOutputCallBack(IntPtr textThread, VNR.ThreadOutputFilterCallback callback, IntPtr data) => VnrHost.TextThread_RegisterOutputCallBack(textThread, callback, data);
		private void Host_GetHookManager(ref IntPtr hookManager) => VnrHost.Host_GetHookManager(ref hookManager);

		private int ThreadReset(IntPtr threadPointer)
		{
			if (Paused) return 0;
			GetOrCreateThread(threadPointer, out TextThread thread);
			thread.Clear(false);
			return 0;
		}

		private int ThreadRemove(IntPtr thread)
		{
			_threadTable.RemoveThread(thread, out var removedTextThread);
			if (removedTextThread != null)
			{
				_viewModel.RemoveThreadFromDisplayCollection(removedTextThread);
				TextThread_RegisterOutputCallBack(thread, null, IntPtr.Zero);
			}
			//set as removed instead of removing
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			return 0;
		}

		public int RemoveProcessList(int pid)
		{
			Processes.TryRemove(pid, out _);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayProcesses));
			var associatedThreads = TextThreads.Values.Where(x => x.ProcessId == pid).ToList();
			foreach (var associatedThread in associatedThreads)
			{
				_threadTable.RemoveThread(associatedThread.Id, out _);
				_viewModel.RemoveThreadFromDisplayCollection(associatedThread);
			}
			_viewModel.PipeAndRecordMap?.RemoveProcess(pid);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			_viewModel.UserGameInitialized = false;
			_viewModel.SaveGameTextThreads();
			return 0;
		}

		private int RegisterProcessList(int pid)
		{
			using (var process = Process.GetProcessById(pid))
			{
				Processes[pid] = new ProcessInfo(process, true, false);
			}
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayProcesses));
			_viewModel.SelectedProcess = Processes[pid];
			_viewModel.OnPropertyChanged(nameof(_viewModel.SelectedProcess));
			return 0;
		}

		private int ThreadCreate(IntPtr threadPointer)
		{
			if (threadPointer != _viewModel.SelectedTextThread?.Id && IgnoreOtherThreads) return 0;
			GetOrCreateThread(threadPointer, out var thread);
			TextThread_RegisterOutputCallBack(threadPointer, ThreadOutput, IntPtr.Zero);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			SetOptionsToNewThread(thread);
			return 0;
		}

		public void SetOptionsToNewThread(TextThread thread)
		{
			if (_viewModel.GameHookCodes.Length > 0)
			{
				SetOptionsToNewThreadHookCodeOnly(thread);
				return;
			}

			var savedThread = thread.FindSaved(_viewModel.GameTextThreads);
			if (savedThread != null)
			{
				ConsoleOutput($"Thread number {thread.Number:0000} (saved), '{thread.DisplayIdentifier}': {savedThread.Options}", true);
				thread.GameThread = savedThread;
				thread.IsDisplay = savedThread.IsDisplay;
				thread.IsPaused = savedThread.IsPaused;
				thread.IsPosting = savedThread.IsPosting;
				if (thread is not HookTextThread hookTextThread) return;
				Encoding prefEncoding;
				try
				{
					prefEncoding = Encoding.GetEncoding(savedThread.Encoding);
				}
				catch (ArgumentException)
				{
					prefEncoding = _viewModel.PrefEncoding;
				}
				hookTextThread.SetEncoding(prefEncoding);
				return;
			}
			var gameTextThread = new GameTextThread(thread);
			thread.GameThread = gameTextThread;
			_viewModel.AddGameThread(gameTextThread);
			if (thread is HookTextThread hookTextThread1) hookTextThread1.SetEncoding(_viewModel.PrefEncoding);
			thread.IsDisplay = gameTextThread.IsDisplay;
			thread.IsPaused = gameTextThread.IsPaused;
			thread.IsPosting = ShowLatestThread;
			if (thread.IsPosting && !Paused) UpdateDisplayThread(thread);
			ConsoleOutput($"Thread number {thread.Number:0000} (new) '{thread.DisplayIdentifier}': {gameTextThread.Options}", true);
		}

		private void SetOptionsToNewThreadHookCodeOnly(TextThread thread)
		{
			var hookTextThread = thread as HookTextThread;
			bool matchesHookCode = hookTextThread != null && _viewModel.GameHookCodes.Contains(hookTextThread.HookCode, StringComparer.OrdinalIgnoreCase);
			var gameTextThread = new GameTextThread(thread);
			thread.GameThread = gameTextThread;
			thread.IsDisplay = matchesHookCode;
			thread.IsPaused = !matchesHookCode;
			thread.IsPosting = matchesHookCode || ShowLatestThread;
			hookTextThread?.SetEncoding(_viewModel.PrefEncoding);
			if (thread.IsPosting && !Paused) UpdateDisplayThread(thread);
			ConsoleOutput($"Thread number {thread.Number:0000} (hook code {(matchesHookCode ? "match" : "mismatch")}) '{thread.DisplayIdentifier}': {gameTextThread.Options}", true);
		}

		public void InitHookThread(HookTextThread thread)
		{
			thread.Parameter = Marshal.PtrToStructure<ThreadParameter>(TextThread_GetThreadParameter(thread.Id));
			thread.ProcessId = (int)thread.Parameter.pid;
			var pr = VnrHost.HookManager_GetProcessRecord(_hookManager, thread.Parameter.pid);
			thread.ProcessRecordPtr = pr;
			thread.SetUnicodeStatus(pr, thread.Parameter.hook);
			thread.SetEntryString();
			_viewModel.AddNewThreadToDisplayCollection(thread);
		}

		private void GetOrCreateThread(IntPtr threadPointer, out TextThread thread)
		{
			_viewModel.InitializeUserGame();
			if (TextThreads.TryGetValue(threadPointer, out thread))
			{
				if (thread.LinkTo != null) thread = thread.LinkTo;
				return;
			}
			if (MergeByHookCode)
			{
				//if this pointer is already in another thread's merged collection
				var existingMerged = TextThreads.FirstOrDefault(x => x.Value.MergedThreads.Keys.Contains(threadPointer)).Value;
				if (existingMerged != null)
				{
					thread = existingMerged;
					return;
				}
				var hookThread = new HookTextThread(threadPointer);
				thread = hookThread;
				InitHookThread(hookThread);
				var threadMergeProperty = thread.MergeProperty;
				//if another thread exists with same hook code (the master thread)
				var existingMaster = TextThreads.Values.FirstOrDefault(x => x.MergeProperty == threadMergeProperty);
				if (existingMaster != null)
				{
					existingMaster.MergedThreads[threadPointer] = thread;
					thread = existingMaster;
					return;
				}
				//if no master exists, add to threads collection.
			}
			else
			{
				var hookThread = new HookTextThread(threadPointer);
				thread = hookThread;
				InitHookThread(hookThread);
			}
			//todo do not allow editing threads collection directly
			TextThreads[threadPointer] = thread;
		}

		public int AddTextToThread(IntPtr threadPointer, object textObject, int len, bool newLine)
		{
			if (_viewModel.Finalized || Paused || len == 0 || _viewModel.IsPaused) return len;
			GetOrCreateThread(threadPointer, out var thread);
			return AddTextToThread(thread, textObject, len, newLine);
		}

		public int AddTextToThread(TextThread thread, object textObject, int len, bool newLine)
		{
			if (_viewModel.Finalized || Paused || len == 0 || _viewModel.IsPaused) return len;
			if (!ShowLatestThread && (thread.IsPaused || (IgnoreOtherThreads && !thread.IsDisplay))) return len;
			if (newLine) return len;
			if (ShowLatestThread) thread.IsDisplay = true;
			thread.AddText(textObject);
			if (thread.IsPosting || thread.IsDisplay) thread.StartTimer();
			return len;
		}

		private int ThreadOutput(IntPtr threadPointer, byte[] value, int len, bool newLine, IntPtr data, bool space)
			=> AddTextToThread(threadPointer, value, len, newLine);

		public void Dispose()
		{
			FinaliseVnrHost(null);
			foreach (var textThread in TextThreads)
			{
				textThread.Value.Clear(true);
			}
			_threadTable.ClearAll(true);
			_viewModel.ClearThreadDisplayCollection();
		}


		public void AddLink(uint fromThreadNumber, uint toThreadNumber)
		{
			var fromThread = TextThreads.Values.FirstOrDefault(x => x.Number == fromThreadNumber);
			if (fromThread == null)
			{
				ConsoleOutput($"Thread with number {fromThreadNumber:X} not found.", true);
				return;
			}
			var toThread = TextThreads.Values.FirstOrDefault(x => x.Number == toThreadNumber);
			if (toThread == null)
			{
				ConsoleOutput($"Thread with number {fromThreadNumber:X} not found.", true);
				return;
			}
			fromThread.LinkTo = toThread;
			ConsoleOutput($"Linked thread {fromThread} to {toThread}", true);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public void FindThreadWithText(string searchTerm, bool searchAllEncodings)
		{
			foreach (var thread in TextThreads.Values.OrderBy(t => t.Number))
			{
				var lineFound = thread.SearchForText(searchTerm, searchAllEncodings);
				if (lineFound != null) ConsoleOutput($"Found text in thread {thread.DisplayName}: {lineFound}", true);
			}
			ConsoleOutput(@"Text search complete.", true);
		}

		public void InitialiseVnrHost(VNR vnrProxy)
		{
			TextThread.VnrProxy = vnrProxy;
			Host_GetHookManager(ref _hookManager);
			_threadTable.Initialize(this);
			VnrHost.ThreadTable_RegisterGetThread(_hookManager, _threadTable.FindThread);
			HookManager_RegisterThreadCreateCallback(ThreadCreate);
			HookManager_RegisterThreadRemoveCallback(ThreadRemove);
			HookManager_RegisterThreadResetCallback(ThreadReset);
			VnrHost.HookManager_RegisterConsoleCallback(_hookManager, ConsoleOutput2);
			HookManager_RegisterProcessAttachCallback(RegisterProcessList);
			HookManager_RegisterProcessDetachCallback(RemoveProcessList);
			VnrHost.Host_Start();
		}

		public void FinaliseVnrHost(int? delayMilliseconds)
		{
			if (VnrHost == null) return;
			StaticHelpers.LogToDebug($"{nameof(FinaliseVnrHost)} Entry, Delay: {delayMilliseconds ?? 0}");
			try
			{
				_threadTable.Finalise();
				HookManager_RegisterThreadCreateCallback(null);
				HookManager_RegisterThreadRemoveCallback(null);
				HookManager_RegisterThreadResetCallback(null);
				HookManager_RegisterProcessAttachCallback(null);
				HookManager_RegisterProcessDetachCallback(null);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				if (delayMilliseconds.HasValue)
				{
					Thread.Sleep(delayMilliseconds.Value);
					VnrHost?.Dispose();
				}
				else VnrHost?.Dispose();
				TextThread.VnrProxy = null;
				StaticHelpers.LogToDebug($"{nameof(FinaliseVnrHost)} Exit");
			}
		}

		public void ClipboardOutput(string text, Process clipboardOwner, string name)
		{
			var clipboardThread = _threadTable.GetClipboardThread(out var created, out var index);
			if (created)
			{
				_viewModel.AddNewThreadToDisplayCollection(clipboardThread);
			}
			clipboardThread.AddText((text,clipboardOwner,name));
		}

        public void SwitchEncoding(string value)
        {
            Encoding encoding;
            try
            {
                encoding = Encoding.GetEncoding(value);
            }
            catch (Exception ex)
            {
                ConsoleOutput($"Error getting encoding for '{value}': {ex}", true);
                return;
            }
            if (!IthVnrViewModel.Encodings.Contains(encoding))

			{
                ConsoleOutput($"Encoding not supported: '{encoding}'", true);
                return;
            }
            ConsoleOutput($"Setting encoding of all panels to: '{encoding.EncodingName}'", true);
            foreach (var textThread in TextThreads)
            {
                textThread.Value.PrefEncoding = encoding;
            }
        }
    }
}
