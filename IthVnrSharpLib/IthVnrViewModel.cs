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
using IthVnrSharpLib.Engine;

namespace IthVnrSharpLib
{
	/// <summary>
	/// You must call Initialize on Window Load
	/// </summary>
	[Serializable]
	public class IthVnrViewModel : INotifyPropertyChanged, IDisposable
	{
		static IthVnrViewModel()
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			Encodings = new[] { Encoding.GetEncoding("Shift-JIS"), Encoding.UTF8, Encoding.Unicode };
        }

        private VNR.SetThreadCallback _threadTableSetThread;
		private VNR.RegisterPipeCallback _registerPipe;
		private VNR.RegisterProcessRecordCallback _registerProcessRecord;
		private bool _finalized;
		private bool _disposed;
        private bool _userGameInitialized;
        private bool _userGameInitializing;
        private readonly object _disposeLock = new();
		/// <summary>
		/// Argument is Process Id.
		/// </summary>
		protected Action<int> InitializeUserGameAction;

		public event PropertyChangedEventHandler PropertyChanged;
		public HookManagerWrapper HookManager { get; protected set; }
		public ThreadTableWrapper ThreadTable { get; protected set; }
		public Commands Commands { get; protected set; }
		public ObservableCollection<FrameworkElement> DisplayThreads { get; } = new();
		public virtual TextThread SelectedTextThread { get; set; }
		public List<ProcessInfo> DisplayProcesses => HookManager?.Processes.Values.OrderBy(x => x.Id).ToList();
		public ProcessInfo SelectedProcess { get; set; }
		public static Encoding[] Encodings { get; }
		public EmbedHost EmbedHost { get; private set; }
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
		public IthVnrSettings Settings => StaticHelpers.Settings;
		public Brush MainTextBoxBackground => Finalized ? Brushes.DarkRed : Brushes.White;
		public ICommand TogglePauseOthersCommand { get; }
		public ICommand ToggleDisplayOthersCommand { get; }
        public ICommand ClearOtherThreadsCommand { get; }
        public ICommand TogglePostOthersCommand { get; }
        public ICommand StopHideOthersCommand { get; }
        public ConcurrentList<GameTextThread> GameTextThreads { get; set; } = new();

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

		public bool UserGameInitialized
		{
			get => _userGameInitialized;
			set
			{
				_userGameInitialized = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Contains Hook Codes set to game when game has option to capture by hook code.
		/// Indicates that game text threads should not be saved, if this array is not empty.
		/// </summary>
		public string[] GameHookCodes { get; set; } = Array.Empty<string>();

		public void InitializeUserGame(int processId)
		{
			if (UserGameInitialized || _userGameInitializing) return;
			_userGameInitializing = true;
			try
            {
                InitializeUserGameAction?.Invoke(processId);
            }
			finally
			{
				_userGameInitializing = false;
                UserGameInitialized = true;
            }
		}

		public IthVnrViewModel()
		{
			TogglePauseOthersCommand = new IthCommandHandler(TogglePauseOtherThreads);
			ToggleDisplayOthersCommand = new IthCommandHandler(ToggleDisplayOtherThreads);
			ClearOtherThreadsCommand = new IthCommandHandler(ClearOtherThreads);
			TogglePostOthersCommand = new IthCommandHandler(TogglePostOtherThreads);
			StopHideOthersCommand = new IthCommandHandler(StopHideOtherThreads);
		}

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Initializes ITHVNR, pass method to be called when display text should be updated.
		/// </summary>
		public void Initialize(TextOutputEvent updateDisplayText)
		{
			ClearThreadDisplayCollection();
			EmbedHost = new EmbedHost(this);
			ThreadTable = new ThreadTableWrapper();
			HookManager = new HookManagerWrapper(this, updateDisplayText, ThreadTable);
			Commands = new Commands(this);
			OnPropertyChanged(nameof(HookManager));
			OnPropertyChanged(nameof(DisplayProcesses));
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
				var displayThread = new TextBlock(new Run(textThread.DisplayName)) { Tag = textThread };
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

		/// <summary>
		/// Called when a process is detached and/or when application closes.
		/// Should clear GameTextThreads collection.
		/// </summary>
		public virtual void SaveGameTextThreads()
		{
			//can be overridden to save game text threads to persistent data storage
		}

		public void TogglePostOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.TextThreads.Values)
			{
				if (thread == selected) continue;
				thread.IsPosting = toggleValue ??= !thread.IsPosting;
			}
		}

		public void TogglePauseOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.TextThreads.Values)
			{
				if (thread == selected) continue;
				thread.IsPaused = toggleValue ??= !thread.IsPaused;
			}
		}

		public void ToggleDisplayOtherThreads()
		{
			var selected = SelectedTextThread;
			bool? toggleValue = null;
			foreach (var thread in HookManager.TextThreads.Values)
			{
				if (thread == selected) continue;
				thread.IsDisplay = toggleValue ??= !thread.IsDisplay;
			}
		}

		public void ClearOtherThreads()
		{
			var selected = SelectedTextThread;
			foreach (var thread in HookManager.TextThreads.Values)
			{
				if (thread == selected) continue;
				thread.Clear(false);
				thread.OnPropertyChanged(nameof(thread.Text));
			}
		}
		public void StopHideOtherThreads()
		{
			var selected = SelectedTextThread;
			foreach (var thread in HookManager.TextThreads.Values)
			{
				if (thread == selected) continue;
				thread.IsDisplay = false;
				thread.IsPosting = false;
				thread.IsPaused = true;
				thread.OnPropertyChanged(nameof(thread.Text));
			}
		}


        public virtual void AddGameThread(GameTextThread gameTextThread, int processId)
		{
			//can be overridden to save a new game text thread to persistent data storage
		}

		public bool InitialiseVnrHost()
		{
			try
			{
				VnrHost = new VNR();
				_threadTableSetThread = ThreadTable.SetHookThread;
				PipeAndRecordMap = new PipeAndProcessRecordMap { HookManager = HookManager };
				_registerPipe = PipeAndRecordMap.RegisterPipe;
				_registerProcessRecord = PipeAndRecordMap.RegisterProcessRecord;
				var result = VnrHost.Host_Open2(_threadTableSetThread, _registerPipe, _registerProcessRecord, out var errorMessage);
				if (!result)
				{
					HookManager.ConsoleOutput($"Failed to initialise VNR Host: {errorMessage}", true);
					return false;
				}
				HookManager.InitialiseVnrHost(VnrHost);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				HookManager.ConsoleOutput($"Failed to initialise VNR Host: {ex}", true);
				return false;
			}
			HookManager.ConsoleOutput($"Initialised VNR Host", true);
			return true;
		}


		public void FinaliseVnrHost(int? delayMilliseconds)
		{
			try
			{
				HookManager.FinaliseVnrHost(delayMilliseconds);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}
			finally
			{
				_threadTableSetThread = null;
				PipeAndRecordMap = null;
				VnrHost = null;
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			lock (_disposeLock)
			{
				if (_disposed) return;
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
					Debug.WriteLine($"{nameof(IthVnrViewModel)}.{nameof(Dispose)} {ex.Message}");
				}
				finally
				{
					FinaliseVnrHost(null);
					HookManager?.Dispose();
					ThreadTable?.Dispose();
					EmbedHost?.Dispose();
					HookManager = null;
					ThreadTable = null;
					Commands = null;
					SelectedProcess = null;
					VnrHost = null;
					EmbedHost = null;
					_disposed = true;
					Debug.WriteLine($"[{nameof(IthVnrViewModel)}] Completed exit procedures, took {exitWatch.Elapsed}");
				}
				GC.Collect();
			}
		}

		public virtual void SaveHookCode(TextThread thread)
		{
			//override to save hook code to game object
		}
	}
}
