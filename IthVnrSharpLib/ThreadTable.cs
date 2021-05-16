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

		private readonly Dictionary<uint, IntPtr> _textThreadMap = new();

		public ConcurrentDictionary<IntPtr, TextThread> Map { get; } = new();

		public void Initialize(HookManagerWrapper hookManager)
		{
			_hookManager = hookManager;
		}

		public void RemoveThread(IntPtr threadId, out TextThread thread)
		{
			lock (_syncObject)
			{
				if(!Map.TryRemove(threadId, out thread)) return;
				_textThreadMap.Remove(thread.Number);
			}
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

		public void ChangeId(TextThread thread)
		{
			lock (_syncObject)
			{
				var pair = Map.FirstOrDefault(t => t.Value == thread);
				if (pair.Value != default) 
				{
					var pointer = pair.Key;
					var pair2 = _textThreadMap.FirstOrDefault(p => p.Value == pointer);
					_textThreadMap.Remove(pair2.Key);
					Map.TryRemove(pointer, out _);
				}
				CreateThread(thread);
			}
		}

		public void Finalise()
		{
			_hookManager = null;
		}

		public void ClearAll(bool includeConsole)
		{
			List<TextThread> threads;
			lock (_syncObject)
			{
				threads = includeConsole ? Map.Values.ToList() : Map.Values.Where(t => t is not ConsoleThread).ToList();
			}
			foreach (var thread in threads)
			{
				RemoveThread(thread.Id, out _);
			}
		}
	}
}
