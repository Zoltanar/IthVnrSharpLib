using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using IthVnrSharpLib.Properties;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace IthVnrSharpLib
{
	/// <summary>
	/// You must call Initialize on Window Load
	/// </summary>
	[Serializable]
	public class IthVnrViewModel : INotifyPropertyChanged
	{
		private VNR.SetThreadCallback _threadTableSetThread;
		private VNR.RegisterPipeCallback _registerPipe;
		private VNR.RegisterProcessRecordCallback _registerProcessRecord;
		private TextOutputEvent _updateDisplayText;
		private bool _finalized;
		private ThreadTableWrapper _threadTable;

		public event PropertyChangedEventHandler PropertyChanged;
		public HookManagerWrapper HookManager { get; protected set; }
		public Commands Commands { get; protected set; }
		public ObservableCollection<FrameworkElement> DisplayThreads { get; } = new();
		public virtual TextThread SelectedTextThread { get; set; }
		public List<ProcessInfo> DisplayProcesses => HookManager?.Processes.Values.OrderBy(x => x.Id).ToList();
		public ProcessInfo SelectedProcess { get; set; }
		public static Encoding[] Encodings { get; } = { Encoding.GetEncoding("SHIFT-JIS"), Encoding.UTF8, Encoding.Unicode };
		public VNR VnrHost { get; private set; }
		public virtual bool IsPaused => false;
		public virtual bool MergeByHookCode
		{
			get => HookManager?.MergeByHookCode ?? false;
			set
			{
				HookManager.MergeByHookCode = value;
				OnPropertyChanged();
			}
		}
		public Encoding PrefEncoding { get; set; }
		public IthVnrSettings Settings => StaticHelpers.CSettings;
		public Brush MainTextBoxBackground => Finalized ? Brushes.DarkRed : Brushes.White;
		public ICommand TogglePauseOthersCommand { get; }
		public ICommand ToggleDisplayOthersCommand { get; }
		public ICommand ClearThreadCommand { get; }
		public ICommand ClearOtherThreadsCommand { get; }
		public ICommand TogglePostOthersCommand { get; }
		public GameTextThread[] GameTextThreads { get; set; } = new GameTextThread[0];

		public bool Finalized
		{
			get => _finalized;
			protected set
			{
				_finalized = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(MainTextBoxBackground));

			}
		}

		public PipeAndProcessRecordMap PipeAndRecordMap { get; set; }

		public IthVnrViewModel()
		{
			TogglePauseOthersCommand = new IthCommandHandler(TogglePauseOtherThreads);
			ToggleDisplayOthersCommand = new IthCommandHandler(ToggleDisplayOtherThreads);
			ClearThreadCommand = new IthCommandHandler(ClearThread);
			ClearOtherThreadsCommand = new IthCommandHandler(ClearOtherThreads);
			TogglePostOthersCommand = new IthCommandHandler(TogglePostOtherThreads);
		}

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Initializes ITHVNR, pass method to be called when display text should be updated.
		/// </summary>
		public void Initialize(TextOutputEvent updateDisplayText, out string errorMessage)
		{
			bool result;
			ClearThreadDisplayCollection();
			try
			{
				VnrHost = new VNR();
				result = true;
				errorMessage = string.Empty;
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				errorMessage = ex.ToString();
				result = false;
			}
			if (result)
			{
				_updateDisplayText = updateDisplayText;
				_threadTable = new ThreadTableWrapper();
				_threadTableSetThread = _threadTable.SetThread;
				PipeAndRecordMap = new PipeAndProcessRecordMap();
				_registerPipe = PipeAndRecordMap.RegisterPipe;
				_registerProcessRecord = PipeAndRecordMap.RegisterProcessRecord;
				result = VnrHost.Host_Open2(_threadTableSetThread, _registerPipe, _registerProcessRecord, out errorMessage);
			}
			if (result)
			{
				HookManager = new HookManagerWrapper(this, updateDisplayText, VnrHost, _threadTable);
				PipeAndRecordMap.HookManager = HookManager;
				Application.Current.Exit += Finalize;
				Commands = new Commands(this);
			}
			if (!result)
			{
				Finalize(null, null);
				Finalized = true;
			}
			OnPropertyChanged(nameof(HookManager));
			OnPropertyChanged(nameof(DisplayProcesses));
		}

		public void ReInitialize(out string errorMessage)
		{
			Initialize(_updateDisplayText, out errorMessage);
			Finalized = false;
		}

		/// <summary>
		/// Can be overridden for specific logic when clearing thread display collection.
		/// </summary>
		public virtual void ClearThreadDisplayCollection()
		{
			var dispatcher = Application.Current?.Dispatcher;
			if (Finalized || dispatcher is null) return;
			dispatcher.Invoke(() => DisplayThreads.Clear());
		}

		/// <summary>
		/// Can be overridden to convert thread to a display object and add it to a collection
		/// </summary>
		public virtual void AddNewThreadToDisplayCollection(TextThread textThread)
		{
			Application.Current.Dispatcher.Invoke(() =>
			{
				var displayThread = new TextBlock(new Run(textThread.EntryString)) { Tag = textThread };
				DisplayThreads.Add(displayThread);
			});
		}

		/// <summary>
		/// Can be overridden to convert thread to a display object and add it to a collection
		/// </summary>
		/// <param name="textThread"></param>
		public virtual void RemoveThreadFromDisplayCollection(TextThread textThread)
		{
			Application.Current.Dispatcher.Invoke(() => DisplayThreads.Remove(DisplayThreads.FirstOrDefault(x => x.Tag == textThread)));
		}

		public void Finalize(object sender, ExitEventArgs e)
		{
			if (Finalized) return;
			var exitWatch = Stopwatch.StartNew();
			Debug.WriteLine($"[{nameof(IthVnrViewModel)}] Starting exit procedures...");
			try
			{
				Settings.Save();
				SaveGameTextThreads();
				ClearThreadDisplayCollection();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"{nameof(IthVnrViewModel)}.{nameof(Finalize)} {ex.Message}");
			}
			finally
			{
				HookManager?.Dispose();
				VnrHost?.Dispose();
				HookManager = null;
				Commands = null;
				SelectedProcess = null;
				VnrHost = null;
				OnPropertyChanged(nameof(SelectedProcess));
				Finalized = true;
				Debug.WriteLine($"[{nameof(IthVnrViewModel)}] Completed exit procedures, took {exitWatch.Elapsed}");
			}
			GC.Collect();
		}

		protected virtual void SaveGameTextThreads()
		{
			//can be overridden to save game text threads to persistent data storage
		}

		public void TogglePostOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread == selected) continue;
				thread.IsPosting = toggleValue ??= !thread.IsPosting;
			}
		}

		public void TogglePauseOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread == selected) continue;
				thread.IsPaused = toggleValue ??= !thread.IsPaused;
			}
		}

		public void ToggleDisplayOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread == selected) continue;
				thread.IsDisplay = toggleValue ??= !thread.IsDisplay;
			}
		}

		public void ClearThread()
		{
			var thread = SelectedTextThread;
			if (thread == null) return;
			thread.Bytes.Clear();
			thread.OnPropertyChanged(nameof(thread.Text));
		}

		public void ClearOtherThreads()
		{
			var selected = SelectedTextThread;
			foreach (var thread in HookManager.Threads.Values)
			{
				if (thread == selected) continue;
				thread.Bytes.Clear();
				thread.OnPropertyChanged(nameof(thread.Text));
			}
		}
		
		public virtual void AddGameThread(GameTextThread gameTextThread)
		{
			//can be overridden to save a new game text thread to persistent data storage
		}
	}
}
