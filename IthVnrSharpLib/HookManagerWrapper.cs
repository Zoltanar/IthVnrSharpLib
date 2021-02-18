using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using IthVnrSharpLib.Properties;

namespace IthVnrSharpLib
{
	public class HookManagerWrapper : MarshalByRefObject, IDisposable, INotifyPropertyChanged
	{
		public override object InitializeLifetimeService() => null;

		// ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
		private VNR.ThreadOutputFilterCallback _threadOutput;
		private VNR.ThreadEventCallback _threadCreate;
		private VNR.ProcessEventCallback _registerProcessList;
		private VNR.ProcessEventCallback _removeProcessList;
		private VNR.ThreadEventCallback _threadRemove;
		private VNR.ThreadEventCallback _threadReset;
		// ReSharper restore PrivateFieldCanBeConvertedToLocalVariable
		
		private readonly IthVnrViewModel _viewModel;
		public readonly VNR VnrProxy;
		public readonly IntPtr HookManager;
		private readonly ThreadTableWrapper _threadTable;

		private bool _mergeByHookCode;
		public readonly ConsoleThread ConsoleThread;
		public ConcurrentDictionary<IntPtr, TextThread> Threads { get; } = new();
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
				if (ConsoleThread == null) return;
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
			foreach (var thread in Threads.Values)
			{
				unmergedThreads.Add(thread);
				foreach (var mergedThread in thread.MergedThreads.Values) unmergedThreads.Add(mergedThread);
				thread.MergedThreads.Clear();
			}
			Threads.Clear();
			Threads[ConsoleThread.Id] = ConsoleThread;
			foreach (var thread in unmergedThreads) Threads[thread.Id] = thread;
			ResetDisplayCollection();
		}

		private void ResetDisplayCollection()
		{
			_viewModel.ClearThreadDisplayCollection();
			foreach (var textThread in Threads.Values) _viewModel.AddNewThreadToDisplayCollection(textThread);
		}

		private void MergeThreads()
		{
			var mergedGroups = Threads.Values.OrderBy(x => x.Number).GroupBy(x => x.HookCode);
			var mergedThreads = new List<TextThread>();
			foreach (var mergedGroup in mergedGroups)
			{
				var enumerator = mergedGroup.GetEnumerator();
				enumerator.MoveNext();
				var masterThread = enumerator.Current;
				mergedThreads.Add(masterThread);
				// ReSharper disable PossibleNullReferenceException
				while (enumerator.MoveNext()) masterThread.MergedThreads[enumerator.Current.Id] = enumerator.Current;
				// ReSharper restore PossibleNullReferenceException
				enumerator.Dispose();
			}
			Threads.Clear();
			Threads[ConsoleThread.Id] = ConsoleThread;
			foreach (var thread in mergedThreads) Threads[thread.Id] = thread;
			ResetDisplayCollection();
		}

		public HookManagerWrapper(IthVnrViewModel propertyChangedNotifier, TextOutputEvent updateDisplayText, VNR vnrProxy)
		{
			VnrProxy = vnrProxy;
			TextThread.UpdateDisplay = updateDisplayText;
			TextThread.VnrProxy = vnrProxy;
			TextThread.CopyToClipboardFunc = () => _viewModel.Settings.ClipboardFlag;
			_viewModel = propertyChangedNotifier;
			TextThread.ViewModel = _viewModel;
			//save callbacks so they dont get GC'd
			_threadOutput = ThreadOutput;
			_threadCreate = ThreadCreate;
			_registerProcessList = RegisterProcessList;
			_removeProcessList = RemoveProcessList;
			_threadRemove = ThreadRemove;
			_threadReset = ThreadReset;
			//end of callback section
			Host_GetHookManager(ref HookManager);
			_threadTable = new ThreadTableWrapper(this, VnrProxy.HookManager_GetThreadTable(HookManager));
			HookManager_RegisterThreadCreateCallback(_threadCreate);
			HookManager_RegisterThreadRemoveCallback(_threadRemove);
			HookManager_RegisterThreadResetCallback(_threadReset);
			IntPtr console = _threadTable.FindThread(0);
			ConsoleThread = new ConsoleThread { Id = console };
			Threads[console] = ConsoleThread;
			_viewModel.AddNewThreadToDisplayCollection(ConsoleThread);
			TextThread_RegisterOutputCallBack(console, _threadOutput, IntPtr.Zero);
			HookManager_RegisterProcessAttachCallback(_registerProcessList);
			HookManager_RegisterProcessDetachCallback(_removeProcessList);
			VnrProxy.Host_Start();
			ConsoleOutput(StaticHelpers.VersionInfo, true);
		}

		private void HookManager_RegisterProcessAttachCallback(VNR.ProcessEventCallback callback) => VnrProxy.HookManager_RegisterProcessAttachCallback(HookManager, callback);

		private void HookManager_RegisterProcessDetachCallback(VNR.ProcessEventCallback callback) => VnrProxy.HookManager_RegisterProcessDetachCallback(HookManager, callback);

		public void ConsoleOutput(string text, bool show)
		{
			ConsoleThread.AddText(text);
			if (show) UpdateDisplayThread(ConsoleThread);
		}

		private static void UpdateDisplayThread(TextThread thread)
		{
			if (thread.IsDisplay) thread.OnPropertyChanged(nameof(TextThread.Text));
		}

		private IntPtr TextThread_GetThreadParameter(IntPtr thread) => VnrProxy.TextThread_GetThreadParameter(thread);

		private void HookManager_RegisterThreadCreateCallback(VNR.ThreadEventCallback threadCreate) => VnrProxy.HookManager_RegisterThreadCreateCallback(HookManager, threadCreate);
		private void HookManager_RegisterThreadRemoveCallback(VNR.ThreadEventCallback threadRemove) => VnrProxy.HookManager_RegisterThreadRemoveCallback(HookManager, threadRemove);
		private void HookManager_RegisterThreadResetCallback(VNR.ThreadEventCallback threadReset) => VnrProxy.HookManager_RegisterThreadResetCallback(HookManager, threadReset);
		
		private void TextThread_RegisterOutputCallBack(IntPtr textThread, VNR.ThreadOutputFilterCallback callback, IntPtr data) => VnrProxy.TextThread_RegisterOutputCallBack(textThread, callback, data);
		private void Host_GetHookManager(ref IntPtr hookManager) => VnrProxy.Host_GetHookManager(ref hookManager);

		private int ThreadReset(IntPtr threadPointer)
		{
			if (Paused) return 0;
			GetOrCreateThread(threadPointer, out TextThread thread);
			thread.Bytes.Clear();
			thread.CurrentBytes.Clear();
			return 0;
		}

		private int ThreadRemove(IntPtr thread)
		{
			Threads.TryRemove(thread, out var removedTextThread);
			if (removedTextThread != null)
			{
				_viewModel.RemoveThreadFromDisplayCollection(removedTextThread);
				removedTextThread.Removed = true;
				TextThread_RegisterOutputCallBack(thread, null, IntPtr.Zero);
			}
			//set as removed instead of removing
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			if (_viewModel.SelectedTextThread != null) return 0;
			UpdateDisplayThread(ConsoleThread);
			return 0;
		}

		private int RemoveProcessList(int pid)
		{
			Processes.TryRemove(pid, out _); //todo check the selected thread throughout this method, ensure it ends in something
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayProcesses));
			var associatedThreads = Threads.Values.Where(x => x.ProcessId == pid).ToList();
			foreach (var associatedThread in associatedThreads)
			{
				Threads.TryRemove(associatedThread.Id, out _);
				_viewModel.RemoveThreadFromDisplayCollection(associatedThread);
			}
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			if (Threads.Values.Contains(_viewModel.SelectedTextThread)) return 0;
			UpdateDisplayThread(ConsoleThread);
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
			TextThread_RegisterOutputCallBack(threadPointer, _threadOutput, IntPtr.Zero);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			InitialiseThread(thread);
			return 0;
		}

		private void InitialiseThread(TextThread thread)
		{
			var savedThread =
				_viewModel.GameTextThreads.FirstOrDefault(t => string.Equals(t.HookFull,thread.HookFull, StringComparison.OrdinalIgnoreCase)) ??
				_viewModel.GameTextThreads.FirstOrDefault(t => string.Equals(t.HookNameless, thread.HookNameless, StringComparison.OrdinalIgnoreCase));
			if (savedThread != null)
			{
				ConsoleOutput($"Found saved thread '{thread.HookFull}': {savedThread.Options}", true);
				thread.GameThread = savedThread;
				thread.IsDisplay = savedThread.IsDisplay;
				thread.IsPaused = savedThread.IsPaused;
				thread.IsPosting = savedThread.IsPosting;
				Encoding prefEncoding;
				try
				{
					prefEncoding = Encoding.GetEncoding(savedThread.Encoding);
				}
				catch (ArgumentException)
				{
					prefEncoding = Encoding.Unicode;
				}
				thread.SetEncoding(prefEncoding);
				return;
			}
			var gameTextThread = new GameTextThread(thread);
			thread.GameThread = gameTextThread;
			_viewModel.AddGameThread(gameTextThread);
			thread.SetEncoding(_viewModel.PrefEncoding);
			thread.IsPosting = ShowLatestThread;
			if(thread.IsPosting && !Paused) UpdateDisplayThread(thread);
			ConsoleOutput($"Found new thread '{thread.HookFull}': {gameTextThread.Options}", true);
		}
		
		private void InitThread(TextThread thread)
		{
			thread.Parameter = Marshal.PtrToStructure<ThreadParameter>(TextThread_GetThreadParameter(thread.Id));
			var pr = VnrProxy.HookManager_GetProcessRecord(HookManager, thread.Parameter.pid);
			thread.ProcessRecordPtr = pr;
			thread.SetUnicodeStatus(pr, thread.Parameter.hook);
			thread.SetEntryString();
		}

		private void GetOrCreateThread(IntPtr threadPointer, out TextThread thread)
		{
			if (Threads.TryGetValue(threadPointer, out thread))
			{
				if (thread.LinkTo != null) thread = thread.LinkTo;
				return;
			}
			if (MergeByHookCode)
			{
				//if this pointer is already in another thread's merged collection
				var existingMerged = Threads.FirstOrDefault(x => x.Value.MergedThreads.Keys.Contains(threadPointer)).Value;
				if (existingMerged != null)
				{
					thread = existingMerged;
					return;
				}
				thread = new TextThread { Id = threadPointer };
				InitThread(thread);
				string threadHook = thread.HookCode;
				//if another thread exists with same hook code (the master thread)
				var existingMaster = Threads.FirstOrDefault(x => x.Value.HookCode == threadHook).Value;
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
				thread = new TextThread { Id = threadPointer };
				InitThread(thread);
			}
			Threads[threadPointer] = thread;
			_viewModel.AddNewThreadToDisplayCollection(thread);
		}

		private int ThreadOutput(IntPtr threadPointer, byte[] value, int len, bool newLine, IntPtr data, bool space)
		{
			if (_viewModel.Finalized || Paused || len == 0 || _viewModel.IsPaused) return len;
			GetOrCreateThread(threadPointer, out var thread);
			if (!ShowLatestThread && (thread.Status == 0 || thread.IsPaused || IgnoreOtherThreads && !thread.IsDisplay)) return len;
			if (newLine)
			{
				//thread.CloseByteSection(this, null);
				return len;
			}
			if (thread.IsPosting || thread.IsDisplay)
			{
				thread.CurrentBytes.AddRange(value);
				thread.StartTimer();
			}
			else thread.CurrentBytes.AddRange(value);
			if (ShowLatestThread)
			{
				_viewModel.SelectedTextThread = thread;
				thread.IsDisplay = true;
			}
			UpdateDisplayThread(thread);
			return len;
		}

		public void Dispose()
		{
			foreach (var textThread in Threads)
			{
				textThread.Value.CurrentBytes.Clear();
				textThread.Value.Bytes.Clear();
			}
			Threads.Clear();
			_viewModel.ClearThreadDisplayCollection();
			HookManager_RegisterThreadCreateCallback(null);
			HookManager_RegisterThreadRemoveCallback(null);
			HookManager_RegisterThreadResetCallback(null);
			HookManager_RegisterProcessAttachCallback(null);
			HookManager_RegisterProcessDetachCallback(null);
			_threadOutput = null;
			_threadCreate = null;
			_registerProcessList = null;
			_removeProcessList = null;
			_threadRemove = null;
			_threadReset = null;
		}

		public void AddLink(uint fromThreadNumber, uint toThreadNumber)
		{
			var fromThread = Threads.Values.FirstOrDefault(x => x.Number == fromThreadNumber);
			if (fromThread == null)
			{
				ConsoleOutput($"Thread with number {fromThreadNumber:X} not found.", true);
				return;
			}
			var toThread = Threads.Values.FirstOrDefault(x => x.Number == toThreadNumber);
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

		public void FindThreadWithText(string searchTerm)
		{
			foreach (var thread in Threads.Values)
			{
				if (thread.IsConsole) continue;
				var textLines = thread.Text.Split(new [] {Environment.NewLine}, StringSplitOptions.None);
				var firstLineWith = textLines.FirstOrDefault(l => l.Contains(searchTerm));
				if (firstLineWith != null) ConsoleOutput($"Found text in thread {thread.EntryString}: {firstLineWith}", true);
			}
		}
	}

	public class ProcessInfo
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public string DisplayString { get; set; }
		public string MainFileName { get; set; }
		public string FullMainFilePath { get; set; }
		public bool Attached { get; set; }
		public string Status => Attached ? "Attached" : "";

		public override string ToString() => DisplayString;

		public ProcessInfo([NotNull] Process process, bool attached, bool dispose)
		{
			Id = process.Id;
			Name = process.ProcessName;
			DisplayString = $"[{Id}] {Name}";
			Attached = attached;
			if (process.MainModule == null) throw new InvalidOperationException($"Main Module of Process [{process.Id}:{process.MainWindowTitle}] was null.");
			FullMainFilePath = process.MainModule.FileName;
			MainFileName = Path.GetFileName(FullMainFilePath);
			if (dispose) process.Dispose();
		}

		public ProcessInfo() { }
	}
}
