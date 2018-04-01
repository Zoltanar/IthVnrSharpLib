﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IthVnrSharpLib
{
	public class HookManagerWrapper : MarshalByRefObject, IDisposable
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
		private readonly VNR _vnrProxy;
		public readonly IntPtr HookManager;
		private bool _mergeByHookCode;
		private readonly ConsoleThread _consoleThread;
		public ConcurrentDictionary<IntPtr, TextThread> Threads { get; } = new ConcurrentDictionary<IntPtr, TextThread>();
		public ConcurrentDictionary<int, ProcessInfo> Processes { get; } = new ConcurrentDictionary<int, ProcessInfo>();
		public bool Paused { get; set; }
		public bool ShowLatestThread { get; set; }
		public bool IgnoreOtherThreads { get; set; }
		public bool FollowPreferredHook { get; set; }
		public bool MergeByHookCode
		{
			get => _mergeByHookCode;
			set
			{
				if (value == _mergeByHookCode) return;
				_mergeByHookCode = value;
				if (_consoleThread == null) return;
				Threads.Clear();
				Threads[_consoleThread.Id] = _consoleThread;
				_viewModel.SelectedTextThread = _consoleThread;
				_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
				_viewModel.OnPropertyChanged(nameof(_viewModel.SelectedTextThread));
				_viewModel.OnPropertyChanged(nameof(_viewModel.HookManager));
			}
		}

		public HookManagerWrapper(IthVnrViewModel propertyChangedNotifier, TextOutputEvent updateDisplayText, VNR vnrProxy, GetPreferredHookEvent getPreferredHook)
		{
			_vnrProxy = vnrProxy;
			TextThread.GetPreferredHook = getPreferredHook;
			TextThread.UpdateDisplay = updateDisplayText;
			TextThread.VnrProxy = vnrProxy;
			TextThread.CopyToClipboardFunc = () => _viewModel.Settings.ClipboardFlag;
			_viewModel = propertyChangedNotifier;
			TextThread.ViewModel = _viewModel;
			TextThread.HookManager = HookManager;
			//save callbacks so they dont get GC'd
			_threadOutput = ThreadOutput;
			_threadCreate = ThreadCreate;
			_registerProcessList = RegisterProcessList;
			_removeProcessList = RemoveProcessList;
			_threadRemove = ThreadRemove;
			_threadReset = ThreadReset;
			//end of callback section
			Host_GetHookManager(ref HookManager);
			HookManager_RegisterThreadCreateCallback(_threadCreate);
			HookManager_RegisterThreadRemoveCallback(_threadRemove);
			HookManager_RegisterThreadResetCallback(_threadReset);
			IntPtr console = HookManager_FindSingle(0);
			_consoleThread = new ConsoleThread { Id = console };
			Threads[console] = _consoleThread;
			TextThread_RegisterOutputCallBack(console, _threadOutput, IntPtr.Zero);
			HookManager_RegisterProcessAttachCallback(_registerProcessList);
			HookManager_RegisterProcessDetachCallback(_removeProcessList);
			_vnrProxy.Host_Start();
			ConsoleOutput(StaticHelpers.VersionInfo, true);
		}

		private void HookManager_RegisterProcessAttachCallback(VNR.ProcessEventCallback callback) => _vnrProxy.HookManager_RegisterProcessAttachCallback(HookManager, callback);

		private void HookManager_RegisterProcessDetachCallback(VNR.ProcessEventCallback callback) => _vnrProxy.HookManager_RegisterProcessDetachCallback(HookManager, callback);
		
		public void ConsoleOutput(string text, bool show)
		{
			_consoleThread.AddText(text);
			if (show) _viewModel.SelectedTextThread = _consoleThread;
			if (_consoleThread.IsDisplay) UpdateDisplayThread();
		}

		private void UpdateDisplayThread()
		{
			_viewModel.OnPropertyChanged(nameof(_viewModel.SelectedTextThread));
			_viewModel.OnPropertyChanged(nameof(_viewModel.PrefEncoding));
		}

		private IntPtr TextThread_GetThreadParameter(IntPtr thread) => _vnrProxy.TextThread_GetThreadParameter(thread);

		private void HookManager_RegisterThreadCreateCallback(VNR.ThreadEventCallback threadCreate) => _vnrProxy.HookManager_RegisterThreadCreateCallback(HookManager, threadCreate);
		private void HookManager_RegisterThreadRemoveCallback(VNR.ThreadEventCallback threadRemove) => _vnrProxy.HookManager_RegisterThreadRemoveCallback(HookManager, threadRemove);
		private void HookManager_RegisterThreadResetCallback(VNR.ThreadEventCallback threadReset) => _vnrProxy.HookManager_RegisterThreadResetCallback(HookManager, threadReset);

		private IntPtr HookManager_FindSingle(int number) => _vnrProxy.HookManager_FindSingle(HookManager, number);
		private void TextThread_RegisterOutputCallBack(IntPtr textThread, VNR.ThreadOutputFilterCallback callback, IntPtr data) => _vnrProxy.TextThread_RegisterOutputCallBack(textThread, callback, data);
		private void Host_GetHookManager(ref IntPtr hookManager) => _vnrProxy.Host_GetHookManager(ref hookManager);
		private bool ConsoleOrNoThreadSelected => _viewModel.SelectedTextThread?.IsConsole ?? true;

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
			if(removedTextThread != null) removedTextThread.Removed = true;
			//set as removed instead of removing
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			if (_viewModel.SelectedTextThread != null) return 0;
			_viewModel.SelectedTextThread = _consoleThread;
			_viewModel.OnPropertyChanged(nameof(_viewModel.SelectedTextThread));
			return 0;
		}
		
		private int RemoveProcessList(int pid)
		{
			Processes.TryRemove(pid, out _);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayProcesses));
			//set as removed instead of removing
			return 0;
		}

		private int RegisterProcessList(int pid)
		{
			var process = Process.GetProcessById(pid);
			Processes[pid] = new ProcessInfo(process, true);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayProcesses));
			_viewModel.SelectedProcess = Processes[pid];
			_viewModel.OnPropertyChanged(nameof(_viewModel.SelectedProcess));
			return 0;
		}

		private int ThreadCreate(IntPtr threadPointer)
		{
			if (Paused) return 0;
			if (threadPointer != _viewModel.SelectedTextThread?.Id && IgnoreOtherThreads) return 0;
			GetOrCreateThread(threadPointer, out var thread);
			TextThread_RegisterOutputCallBack(threadPointer, _threadOutput, IntPtr.Zero);
			_viewModel.OnPropertyChanged(nameof(_viewModel.DisplayThreads));
			//select this thread if none/console selected, or if its preferred hook code or if its filepath hook and there exists no preferred hook
			var b1 = thread.IsPreferredHookCode;
			var b2 = ThreadIsFilePath(thread) && !thread.HasPreferredHook;
			if (ConsoleOrNoThreadSelected && (b1 || b2) )
			{
				if (b1)
				{
					thread.SetEncoding();
				}
				thread.IsPosting = true;
				_viewModel.SelectedTextThread = thread;
				UpdateDisplayThread();
			}
			return 0;
		}

		private bool ThreadIsFilePath(TextThread thread)
		{
			return Processes.Count == 1 && thread.HookCode.EndsWith(Processes.Values.First().MainFileName);
		}

		private void InitThread(TextThread thread)
		{
			thread.Parameter = Marshal.PtrToStructure<ThreadParameter>(TextThread_GetThreadParameter(thread.Id));
			IntPtr pr = _vnrProxy.HookManager_GetProcessRecord(HookManager,thread.Parameter.pid);
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
				var existingMerged = Threads.FirstOrDefault(x => x.Value.MergedThreads.Contains(threadPointer)).Value;
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
					existingMaster.MergedThreads.Add(threadPointer);
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
		}

		private int ThreadOutput(IntPtr threadPointer, byte[] value, int len, bool newLine, IntPtr data, bool space)
		{
			if (Paused || len == 0) return len;
			GetOrCreateThread(threadPointer, out var thread);
			if (thread.Status == 0 || thread.IsPaused || IgnoreOtherThreads && !thread.IsDisplay) return len;
			if (thread.IsPaused) return len;
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
			if (ShowLatestThread || (FollowPreferredHook && thread.IsPreferredHookCode))
			{
				_viewModel.SelectedTextThread = thread;
				thread.IsPosting = true;
			}
			if (thread.IsDisplay) UpdateDisplayThread();
			return len;
		}

		public void Dispose()
		{
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
			fromThread.LinkTo = fromThread;
			ConsoleOutput($"Linked thread {fromThread} to {toThread}", true);
		}
	}

	public class ProcessInfo
	{
		public Process Process { get; set; }
		public string DisplayString { get; set; }
		public string MainFileName { get; set; }
		public string FullMainFilePath { get; set; }
		public bool Attached { get; set; }
		public string Status => Attached ? "Attached" : "";

		public override string ToString() => DisplayString;

		public ProcessInfo(Process process, bool attached)
		{
			Process = process;
			DisplayString = $"[{process.Id}] {process.ProcessName}";
			Attached = attached;
			FullMainFilePath = process.MainModule.FileName;
			MainFileName = Path.GetFileName(process.MainModule.FileName);
		}

		public ProcessInfo() { }
	}
}
