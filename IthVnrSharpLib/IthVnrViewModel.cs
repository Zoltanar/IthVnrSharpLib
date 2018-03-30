using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using IthVnrSharpLib.Properties;
using System.Windows;

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
			}
		}
		public List<ProcessInfo> DisplayProcesses => HookManager?.Processes.Values.OrderBy(x => x.Process.Id).ToList();
		public ProcessInfo SelectedProcess { get; set; }
		public static Encoding[] Encodings { get; } = { Encoding.GetEncoding("SHIFT-JIS"), Encoding.UTF8, Encoding.Unicode };
		public VNR VnrProxy { get; private set; }
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

		protected bool Finalized;
		private TextOutputEvent _updateDisplayText;
		private GetPreferredHookCodeEvent _getPreferredHookCode;
		private AppDomain _ithVnrAppDomain;
		private TextThread _selectedTextThread;

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Initializes ITHVNR, pass method to be called when display text should be updated.
		/// </summary>
		public void Initialize(TextOutputEvent updateDisplayText, VNR vnrProxy, AppDomain ithVnrAppDomain, GetPreferredHookCodeEvent getPreferredHookCode)
		{
			_ithVnrAppDomain = ithVnrAppDomain;
			VnrProxy = vnrProxy;
			_updateDisplayText = updateDisplayText;
			_getPreferredHookCode = getPreferredHookCode;
			if (!VnrProxy.Host_IthInitSystemService()) Process.GetCurrentProcess().Kill();
			if (VnrProxy.Host_Open())
			{
				HookManager = new HookManagerWrapper(this, updateDisplayText, vnrProxy, getPreferredHookCode);
				Application.Current.Exit += Finalize;
				Commands = new Commands(HookManager, vnrProxy);
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

		public void ReInitialize(VNR vnrProxy, AppDomain ithVnrAppDomain)
		{
			Initialize(_updateDisplayText, vnrProxy, ithVnrAppDomain, _getPreferredHookCode);
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
				VnrProxy?.Host_Close();
				VnrProxy?.Host_IthCloseSystemService();
				if (_ithVnrAppDomain != null) AppDomain.Unload(_ithVnrAppDomain);
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
				OnPropertyChanged(nameof(SelectedTextThread));
				OnPropertyChanged(nameof(SelectedProcess));
				Finalized = true;
				Debug.WriteLine($"(IthVnrViewModel) Completed exit procedures, took {exitWatch.Elapsed}");
			}
		}

		public void OutputSelectedText(string text)
		{
			_updateDisplayText(this, new TextOutputEventArgs(SelectedTextThread, text, "Selected Text", false));
		}

		public void PostDisplayedOnly()
		{
			foreach (var textThread in DisplayThreads)
			{
				textThread.IsPosting = textThread == SelectedTextThread;
			}
			OnPropertyChanged(nameof(SelectedTextThread));
		}

		public void PauseOtherThreads()
		{
			foreach (var thread in HookManager.Threads.Values) thread.IsPaused = !thread.IsDisplay;
		}

	}
}
