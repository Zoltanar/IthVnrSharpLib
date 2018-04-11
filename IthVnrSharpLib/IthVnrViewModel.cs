using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using IthVnrSharpLib.Properties;
using System.Windows;
using System.Windows.Input;

namespace IthVnrSharpLib
{
	/// <summary>
	/// You must call Initialize on Window Load
	/// </summary>
	[Serializable]
	public class IthVnrViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		public HookManagerWrapper HookManager { get; protected set; }
		public Commands Commands { get; protected set; }
		public List<TextThread> DisplayThreads => HookManager?.Threads.Values.OrderBy(x => x.EntryString).ToList();
		public TextThread SelectedTextThread
		{
			get => _selectedTextThread;
			set
			{
				if (_selectedTextThread == value) return;
				if (_selectedTextThread != null) _selectedTextThread.IsDisplay = false; //set old thread as no longer displayed
				_selectedTextThread = value;
				if (value == null) return;
				_selectedTextThread.IsDisplay = true; //set new thread as displayed
				if (!value.EncodingDefined) value.SetEncoding();
				_selectedTextThread.CloseByteSection(this, null);
				_selectedTextThread = value;
				OnPropertyChanged();
			}
		}
		public List<ProcessInfo> DisplayProcesses => HookManager?.Processes.Values.OrderBy(x => x.Id).ToList();
		public ProcessInfo SelectedProcess { get; set; }
		public static Encoding[] Encodings { get; } = { Encoding.GetEncoding("SHIFT-JIS"), Encoding.UTF8, Encoding.Unicode };
		public VNR VnrProxy { get; private set; }
		public static AppDomain IthVnrDomain { get; private set; }
		public virtual bool MergeByHookCode
		{
			get => HookManager?.MergeByHookCode ?? false;
			set
			{
				HookManager.MergeByHookCode = value;
				OnPropertyChanged();
			}
		}
		public virtual Encoding PrefEncoding
		{
			get => SelectedTextThread?.PrefEncoding ?? Encoding.Unicode;
			set
			{
				SelectedTextThread.PrefEncoding = value;
				OnPropertyChanged();
			}
		}
		public IthVnrSettings Settings => StaticHelpers.CSettings;
		public ICommand PauseOtherThreadsCommand { get; }
		public ICommand UnpauseOtherThreadsCommand { get; }
		public ICommand ClearThreadCommand { get; }
		public ICommand ClearOtherThreadsCommand { get; }
		public ICommand DontPostOthersCommand { get; }

		protected bool Finalized;
		private TextOutputEvent _updateDisplayText;
		private GetPreferredHookEvent _getPreferredHook;
		private TextThread _selectedTextThread;

		public IthVnrViewModel()
		{
			PauseOtherThreadsCommand = new IthCommandHandler(PauseOtherThreads);
			UnpauseOtherThreadsCommand = new IthCommandHandler(UnpauseOtherThreads);
			ClearThreadCommand = new IthCommandHandler(ClearThread);
			ClearOtherThreadsCommand = new IthCommandHandler(ClearOtherThreads);
			DontPostOthersCommand = new IthCommandHandler(DontPostOthers);
		}

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Initializes ITHVNR, pass method to be called when display text should be updated.
		/// </summary>
		public void Initialize(TextOutputEvent updateDisplayText, GetPreferredHookEvent getPreferredHook)
		{
			InitVnrProxy();
			_updateDisplayText = updateDisplayText;
			_getPreferredHook = getPreferredHook;
			if (!VnrProxy.Host_IthInitSystemService()) Process.GetCurrentProcess().Kill();
			if (VnrProxy.Host_Open())
			{
				HookManager = new HookManagerWrapper(this, updateDisplayText, VnrProxy, getPreferredHook);
				Application.Current.Exit += Finalize;
				Commands = new Commands(HookManager, VnrProxy);
			}
			else
			{
				Finalize(null, null);
				Finalized = true;
			}
			OnPropertyChanged(nameof(HookManager));
			OnPropertyChanged(nameof(DisplayThreads));
			OnPropertyChanged(nameof(DisplayProcesses));
		}

		public void ReInitialize()
		{
			Initialize(_updateDisplayText, _getPreferredHook);
			Finalized = false;
		}

		public void Finalize(object sender, ExitEventArgs e)
		{
			if (Finalized) return;
			var exitWatch = Stopwatch.StartNew();
			Debug.WriteLine("(IthVnrViewModel) Starting exit procedures...");
			try
			{
				Settings.Save();
				HookManager?.Dispose();
				VnrProxy?.Exit();
				if (IthVnrDomain != null) AppDomain.Unload(IthVnrDomain);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"IthVnrViewModel.Finalize {ex.Message}");
			}
			finally
			{
				HookManager = null;
				Commands = null;
				SelectedTextThread = null;
				SelectedProcess = null;
				VnrProxy = null;
				OnPropertyChanged(nameof(SelectedTextThread));
				OnPropertyChanged(nameof(SelectedProcess));
				Finalized = true;
				Debug.WriteLine($"(IthVnrViewModel) Completed exit procedures, took {exitWatch.Elapsed}");
			}
			GC.Collect();
		}

		public void OutputSelectedText(string text)
		{
			_updateDisplayText(this, new TextOutputEventArgs(SelectedTextThread, text, "Selected Text", false));
		}

		public void DontPostOthers()
		{
			foreach (var textThread in DisplayThreads)
			{
				if (textThread.IsDisplay) continue;
				textThread.IsPosting = false;
			}
			OnPropertyChanged(nameof(SelectedTextThread));
		}

		public void PauseOtherThreads()
		{
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread.IsDisplay) continue;
				thread.IsPaused = true;
			}
		}

		public void UnpauseOtherThreads()
		{
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread.IsDisplay) continue;
				thread.IsPaused = false;
			}
		}

		public void ClearThread()
		{
			SelectedTextThread.Bytes.Clear();
			OnPropertyChanged(nameof(SelectedTextThread));
		}

		public void ClearOtherThreads()
		{
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread.IsDisplay) continue;
				thread.Bytes.Clear();
			}
		}

		public void InitVnrProxy()
		{
			var path = Path.GetFullPath(@"IthVnrSharpLib.dll");
			var assembly = Assembly.LoadFile(path);
			IthVnrDomain = AppDomain.CreateDomain(@"StaticMethodsIthVnrAppDomain");
			VnrProxy = (VNR)IthVnrDomain.CreateInstanceAndUnwrap(assembly.FullName, @"IthVnrSharpLib.VNR");
		}

	}
}
