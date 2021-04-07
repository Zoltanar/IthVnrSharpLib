using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace IthVnrSharpLib
{
	public class ThreadTableWrapper : MarshalByRefObject, IDisposable
	{
		public override object InitializeLifetimeService() => null;

		private HookManagerWrapper _hookManager;

		private readonly Dictionary<uint, IntPtr> _textThreadMap = new();

		public ConcurrentDictionary<IntPtr, TextThread> Map { get; } = new();

		public void Initialize(HookManagerWrapper hookManager)
		{
			_hookManager = hookManager;
		}

		public IntPtr FindThread(uint number)
		{
			if (_textThreadMap.TryGetValue(number, out var threadPointer) && Map.TryGetValue(threadPointer, out var thread)) return thread.Id;
			return IntPtr.Zero;
		}

		public void SetThread(uint num, IntPtr textThreadPointer)
		{
			if (_textThreadMap.ContainsKey(num)) { }

			if (!Map.TryGetValue(textThreadPointer, out _))
			{
				var thread = new TextThread { Id = textThreadPointer };
				_hookManager?.InitThread(thread);
				Map[textThreadPointer] = thread;
			}
			_textThreadMap[num] = textThreadPointer;
		}

		public void SetThread(uint num, TextThread textThread)
		{
			Map[textThread.Id] = textThread;
			_textThreadMap[num] = textThread.Id;
		}

		public void Dispose()
		{
			_hookManager?.Dispose();
			_textThreadMap.Clear();
			Map.Clear();
		}
	}
}
