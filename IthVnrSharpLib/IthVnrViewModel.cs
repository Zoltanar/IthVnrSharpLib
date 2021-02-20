using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
		public event PropertyChangedEventHandler PropertyChanged;
		public HookManagerWrapper HookManager { get; protected set; }
		public Commands Commands { get; protected set; }
		public ObservableCollection<FrameworkElement> DisplayThreads { get; } = new();
		public virtual TextThread SelectedTextThread { get; set; }
		public List<ProcessInfo> DisplayProcesses => HookManager?.Processes.Values.OrderBy(x => x.Id).ToList();
		public ProcessInfo SelectedProcess { get; set; }
		public static Encoding[] Encodings { get; } = { Encoding.GetEncoding("SHIFT-JIS"), Encoding.UTF8, Encoding.Unicode };
		public VNR VnrProxy { get; private set; }
		public static AppDomain IthVnrDomain { get; private set; }
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

		private TextOutputEvent _updateDisplayText;
		private bool _finalized;
		private ThreadTableWrapper _threadTable;

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
			ClearThreadDisplayCollection();
			InitVnrProxy();
			_updateDisplayText = updateDisplayText;
			if (!VnrProxy.Host_IthInitSystemService()) Process.GetCurrentProcess().Kill();
			_threadTable = new ThreadTableWrapper();
			_threadTableSetThread = _threadTable.SetThread;
			VnrProxy.SaveObject(_threadTable);
			if (VnrProxy.Host_Open(_threadTableSetThread, out errorMessage))
			{
				HookManager = new HookManagerWrapper(this, updateDisplayText, VnrProxy, _threadTable);
				Application.Current.Exit += Finalize;
				Commands = new Commands(HookManager, VnrProxy);
			}
			else
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
			if (Finalized) return;
			Application.Current.Dispatcher.Invoke(()=> DisplayThreads.Clear());
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
				HookManager?.Dispose();
				VnrProxy?.Exit();
				if (IthVnrDomain != null) AppDomain.Unload(IthVnrDomain);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"{nameof(IthVnrViewModel)}.{nameof(Finalize)} {ex.Message}");
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
				Debug.WriteLine($"[{nameof(IthVnrViewModel)}] Completed exit procedures, took {exitWatch.Elapsed}");
			}
			GC.Collect();
		}

		protected virtual void SaveGameTextThreads()
		{
			//can be overridden to save game text threads to persistant data storage
		}

		public void OutputSelectedText(string text)
		{
			_updateDisplayText(this, new TextOutputEventArgs(SelectedTextThread, text, "Selected Text", false));
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
			OnPropertyChanged(nameof(SelectedTextThread));
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

		public void InitVnrProxy()
		{
			var path = Path.GetFullPath($@"{nameof(IthVnrSharpLib)}.dll");
			var assembly = Assembly.LoadFile(path);
			IthVnrDomain = AppDomain.CreateDomain(@"StaticMethodsIthVnrAppDomain");
			VnrProxy = (VNR)IthVnrDomain.CreateInstanceAndUnwrap(assembly.FullName, $@"{nameof(IthVnrSharpLib)}.{nameof(VNR)}");
		}

		public virtual void AddGameThread(GameTextThread gameTextThread)
		{
			//can be overridden to save a new game text thread to persistent data storage
		}
	}
}
