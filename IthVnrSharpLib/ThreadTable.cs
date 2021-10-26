using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IthVnrSharpLib
{
	public class ThreadTableWrapper : MarshalByRefObject, IDisposable
	{
		public override object InitializeLifetimeService() => null;

		private readonly object _syncObject = new();

		private HookManagerWrapper _hookManager;

		/// <summary>
		/// This object exists solely for fast execution of <see cref="FindThread"/>.
		/// </summary>
		private readonly Dictionary<uint, IntPtr> _textThreadMap = new();

		public TextThread ConsoleThread { get; private set; }
		private TextThread _clipboardThread;
		private ConcurrentList<TextThread> List { get; } = new();
		public ConcurrentDictionary<IntPtr, TextThread> Map { get; } = new();
		public IEnumerable<TextThread> AllThreads => List.Concat(Map.Values.OrderBy(t => t.Number));

		public void Initialize(HookManagerWrapper hookManager)
		{
			_hookManager = hookManager;
		}

		public void RemoveThread(IntPtr threadId, out TextThread thread)
		{
			lock (_syncObject)
			{
				if (!Map.TryRemove(threadId, out thread)) return;
				_textThreadMap.Remove(thread.Number);
			}
		}

		public TextThread CreateConsoleThread()
		{
			ConsoleThread = new ConsoleThread();
			List.Add(ConsoleThread);
			return ConsoleThread;
		}

		public TextThread GetClipboardThread(out bool justCreated, out int index)
		{
			justCreated = _clipboardThread == null;
			if (justCreated)
			{
				_clipboardThread = new ClipboardThread();
				List.Add(_clipboardThread);
				index = List.Count - 1;
			}
			else index = -1;
			return _clipboardThread;
		}

		public IntPtr FindThread(uint number)
		{
			// ReSharper disable once InconsistentlySynchronizedField
			if (_textThreadMap.TryGetValue(number, out var threadPointer)) return threadPointer;
			return IntPtr.Zero;
		}

		public void SetHookThread(uint num, IntPtr textThreadPointer)
		{
			//if hook manager is still null, this is console thread, ignore
			if (_hookManager == null) return;
			lock (_syncObject)
			{
				if (!Map.TryGetValue(textThreadPointer, out _))
				{
					var thread = new HookTextThread(textThreadPointer);
					_hookManager.InitHookThread(thread);
					Map[textThreadPointer] = thread;
				}
				_textThreadMap[num] = textThreadPointer;
			}
		}

		public void CreateThread(TextThread textThread)
		{
			lock (_syncObject)
			{
				Map[textThread.Id] = textThread;
				var num = (uint)_textThreadMap.Count;
				textThread.Number = (ushort)num;
				textThread.DisplayName = $@"{num:0000} {textThread.DisplayName}";
				_textThreadMap[num] = textThread.Id;
			}
		}
		
		public void Dispose()
		{
			_hookManager?.Dispose();
			_textThreadMap.Clear();
			Map.Clear();
		}
		
		public void Finalise()
		{
			_hookManager = null;
		}

		public void ClearAll(bool includeSystem)
		{
			List<TextThread> threads;
			lock (_syncObject)
			{
				threads = (includeSystem ? AllThreads : Map.Values).ToList();
			}
			foreach (var thread in threads)
			{
				RemoveThread(thread.Id, out _);
			}
		}
	}
}
