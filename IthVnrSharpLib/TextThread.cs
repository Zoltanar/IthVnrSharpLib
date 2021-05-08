using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Timers;
using IthVnrSharpLib.Properties;
using Timer = System.Timers.Timer;

namespace IthVnrSharpLib
{
	public abstract class TextThread : INotifyPropertyChanged
	{
		public static TextOutputEvent UpdateDisplay;
		public static VNR VnrProxy;
		public static Func<bool> CopyToClipboardFunc;
		public static IthVnrViewModel ViewModel;
		public static Encoding[] AllEncodings => IthVnrViewModel.Encodings;
		protected static bool CopyToClipboard => CopyToClipboardFunc();

		public TextThread LinkTo { get; set; }
		private readonly Dictionary<DateTime, int> _monitorPairs = new();
		protected readonly object TimerTickLock = new();
		protected Timer Timer;
		private bool _isDisplay = true;
		private bool _isPaused;
		private bool _isPosting;
		protected DateTime LastUpdateTime = DateTime.MinValue;
		private Thread _monitorThread;

		protected abstract void OnTimerEnd(object sender, ElapsedEventArgs e);

		public abstract string Text { get; }
		public abstract Encoding PrefEncoding { get; set; }
		public abstract bool EncodingCanChange { get; }

		public string DisplayName { get; set; }
		public IntPtr Id { get; }
		public Dictionary<IntPtr, TextThread> MergedThreads { get; } = new();
		private string MonitorThreadName => $"{nameof(IthVnrSharpLib)}.{nameof(TextThread)}.{nameof(StartMonitor)}:{Id}";
		public GameTextThread GameThread { get; set; }

		public bool IsDisplay
		{
			get => _isDisplay;
			set
			{
				if (_isDisplay == value) return;
				_isDisplay = value;
				OnPropertyChanged();
				if (GameThread != null) GameThread.IsDisplay = _isDisplay;
			}
		}

		public virtual bool IsPaused
		{
			get => _isPaused;
			set
			{
				if (_isPaused == value) return;
				_isPaused = value;
				if (GameThread != null) GameThread.IsPaused = _isPaused;
				OnPropertyChanged();
				if (_isPaused || _monitorThread != null) return;
				_monitorThread = new Thread(StartMonitor) {IsBackground = true, Name = MonitorThreadName};
				_monitorThread.Start();
			}
		}

		public virtual bool IsPosting
		{
			get => _isPosting;
			set
			{
				_isPosting = value;
				if (GameThread != null) GameThread.IsPosting = _isPosting;
				if (_isPosting) OnTimerEnd(this, null);
				OnPropertyChanged();
			}
		}

		public ushort Number { get; set; }
		public int ProcessId { get; set; }
		public bool Removed { get; set; }
		public abstract object MergeProperty { get; }
		public abstract string PersistentIdentifier { get; }
		public const int TextTrimAt = 2048;
		public const int TextTrimCount = 512;

		protected TextThread(IntPtr id)
		{
			Id = id;
			_monitorThread = new Thread(StartMonitor) {IsBackground = true, Name = MonitorThreadName};
			_monitorThread.Start();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public abstract void AddText(object value);

		public abstract void Clear(bool clearCurrent);

		protected abstract int GetCharacterCount();

		public void StartTimer()
		{
			lock (TimerTickLock)
			{
				if (Timer == null)
				{
					Timer = new Timer
					{
						AutoReset = false,
						Enabled = true,
						Interval = 100 //todo make this a setting (splitting interval)
					};
					Timer.Elapsed += OnTimerEnd;
				}

				Timer.Stop();
				Timer.Start();
			}
		}

		[NotifyPropertyChangedInvocator]
		public void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// true to break from loop
		/// </summary>
		private bool MonitorLoop()
		{
			if (IsPaused) return true;
			if (_monitorPairs.Count > 20) _monitorPairs.Clear();
			_monitorPairs[DateTime.UtcNow] = GetCharacterCount();
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

		public abstract string SearchForText(string searchTerm, bool searchAllEncodings);
	}
}