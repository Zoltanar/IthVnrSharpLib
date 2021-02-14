using System;

namespace IthVnrSharpLib
{
	public class ThreadTableWrapper
	{
		private readonly HookManagerWrapper _hookManager;
		private readonly IntPtr _threadTable;

		public ThreadTableWrapper(HookManagerWrapper hookManager, IntPtr threadTablePointer)
		{
			_hookManager = hookManager;
			_threadTable = threadTablePointer;
		}

		public IntPtr FindThread(uint number) => _hookManager.VnrProxy.ThreadTable_FindTextThread(_threadTable, number);
		
		/*
		 class ThreadTable : public MyVector<TextThread *, 0x40>
{
public:
  virtual void SetThread(DWORD number, TextThread *ptr);
  virtual TextThread *FindThread(DWORD number);
};
		 */
	}
}
