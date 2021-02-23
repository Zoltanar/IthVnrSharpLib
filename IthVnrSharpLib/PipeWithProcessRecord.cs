using System;

namespace IthVnrSharpLib
{
		public class PipeWithProcessRecord
		{
			public IntPtr Text;
			public IntPtr Cmd;
			public IntPtr RecvThread;
			public ProcessRecord Process;
			public bool HasPipe => Text != IntPtr.Zero || Cmd != IntPtr.Zero || RecvThread != IntPtr.Zero;
			public bool HasProcess => Process.pid_register != 0;

			/// <summary>
			/// Initialise with pipe parameters
			/// </summary>
			public PipeWithProcessRecord(IntPtr text, IntPtr cmd, IntPtr thread)
			{
				Text = text;
				Cmd = cmd;
				RecvThread = thread;
			}

			/// <summary>
			/// Initialise with process record parameter
			/// </summary>
			/// <param name="processRecord"></param>
			public PipeWithProcessRecord(ProcessRecord processRecord)
			{
				Process = processRecord;
			}

			public void AddPipe(IntPtr text, IntPtr cmd, IntPtr thread)
			{
				Text = text;
				Cmd = cmd;
				RecvThread = thread;
			}
		}
	}