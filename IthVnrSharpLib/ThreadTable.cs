using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace IthVnrSharpLib
{
	public class ThreadTableWrapper : MarshalByRefObject, IDisposable
	{
		public override object InitializeLifetimeService() => null;

		private readonly object _syncObject = new();

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

		public void SetHookThread(uint num, IntPtr textThreadPointer)
		{
			lock (_syncObject)
			{
				if (!Map.TryGetValue(textThreadPointer, out _))
				{
					var thread = new HookTextThread(textThreadPointer);
					_hookManager?.InitHookThread(thread);
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
	}
}
