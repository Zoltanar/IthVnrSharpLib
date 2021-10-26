using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
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
		public static Encoding[] AllEncodings => IthVnrViewModel.Encodings;
		protected static bool CopyToClipboard => CopyToClipboardFunc();

		public TextThread LinkTo { get; set; }
		protected readonly object TimerTickLock = new();
		protected Timer Timer;
		private bool _isDisplay = true;
		private bool _isPaused;
		private bool _isPosting;
		protected DateTime LastCopyTime = DateTime.MinValue;

		protected abstract void OnTimerEnd(object sender, ElapsedEventArgs e);

		public abstract string Text { get; }
		public abstract Encoding PrefEncoding { get; set; }
		public abstract bool EncodingCanChange { get; }
		public bool CanSaveHookCode => this is HookTextThread;
		public abstract bool IsSystem { get; }

		public string DisplayName { get; set; }
		public IntPtr Id { get; protected set; }
		public Dictionary<IntPtr, TextThread> MergedThreads { get; } = new();
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
		public abstract object MergeProperty { get; }
		public abstract string PersistentIdentifier { get; }
		public virtual string DisplayIdentifier => PersistentIdentifier;

		protected const int TextTrimAt = 2048;
		protected const int TextTrimCount = 512;

		protected TextThread(IntPtr id)
		{
			Id = id;
		}

		/// <summary>
		/// Constructor for console thread.
		/// </summary>
		protected TextThread()
		{

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
		
		public abstract string SearchForText(string searchTerm, bool searchAllEncodings);

		public virtual GameTextThread FindSaved(ConcurrentList<GameTextThread> gameTextThreads)
		{
			var savedThread = gameTextThreads.FirstOrDefault(t => string.Equals(t.Identifier, PersistentIdentifier, StringComparison.OrdinalIgnoreCase));
			return savedThread;
		}
	}
}