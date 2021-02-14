using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IthVnrSharpLib
{
	public class Commands
	{
		private readonly HookManagerWrapper _hookManager;
		private readonly VNR _vnrProxy;
		private Regex ProcessNameRegex { get; } = new Regex("/pn(.+)", RegexOptions.IgnoreCase);
		private Regex ProcessRegex { get; } = new Regex("/p(.+)", RegexOptions.IgnoreCase);
		private Regex HookRegex { get; } = new Regex("/h(.+)", RegexOptions.IgnoreCase);
		private Regex LinkRegex { get; } = new Regex(":l(.+)", RegexOptions.IgnoreCase);
		private Regex UnlinkRegex { get; } = new Regex(":u(.+)", RegexOptions.IgnoreCase);
		private Regex HelpRegex { get; } = new Regex("(:h|:help)", RegexOptions.IgnoreCase);

		public Commands(HookManagerWrapper hookManager, VNR vnrProxy)
		{
			_hookManager = hookManager;
			_vnrProxy = vnrProxy;
		}

		public void ProcessCommand(string cmd, int pid)
		{
			_hookManager.ConsoleOutput($"Processing command '{cmd}'...", true);
			if (ProcessNameRegex.IsMatch(cmd)) AttachWithProcessName(cmd);
			else if (ProcessRegex.IsMatch(cmd)) AttachWithProcessId(cmd);
			else if (HookRegex.IsMatch(cmd))
			{
				var result = AddHookCode(cmd, pid);
				if (!result) _hookManager.ConsoleOutput($"Failed to add hook code '{cmd}'", true);
			}
			else if (LinkRegex.IsMatch(cmd)) LinkThreads(cmd);
			else if (UnlinkRegex.IsMatch(cmd)) UnlinkThread(cmd);
			else if (HelpRegex.IsMatch(cmd)) _hookManager.ConsoleOutput(CommandUsage, true);
			else _hookManager.ConsoleOutput("Unrecognized command.", true);
		}

		public const string CommandUsage =
@"Available commands:
:h or :help - display this mesage.

:L[from]-[to] - link from thread [from] to thread [to],
    [from] and [to] are hexadecimal thread numbers, which is the first number in the drop down menu above.
    This will put text received in [from] thread in front of text in [to] thread.'

/P[pid] - Attach to process with specified PID.

/PN[name] - Attach to process with specified name.

/H[X]{A|B|W|S|Q}[N][data_offset[*drdo]][:sub_offset[*drso]]@addr[:module[:{name|#ordinal}]]
    Add specified H-Code
    All numbers except ordinal are hexadecimal without any prefixes.
";
		// ReSharper disable once CollectionNeverQueried.Local
		private readonly List<object> _stopGarbageCollection = new List<object>();

		private void UnlinkThread(string cmd)
		{
			if (!int.TryParse(cmd.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var threadId))
			{
				_hookManager.ConsoleOutput($"Failed to parse '{cmd.Substring(3)}' as a hexadecimal number.", true);
				return;
			}
			var thread = _hookManager.Threads.Values.FirstOrDefault(x => x.Number == threadId);
			if (thread == null) _hookManager.ConsoleOutput($"Failed to find thread with Id '{threadId}'.", true);
			else
			{
				thread.LinkTo = null;
				_hookManager.ConsoleOutput($"Thread '{threadId}' is no longer linked.", true);
			}
		}

		private void LinkThreads(string cmd)
		{
			var parts = cmd.Substring(2).Split('-');
			if (parts.Length != 2 ||
				!uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fromThread) ||
				!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var toThread))
			{
				_hookManager.ConsoleOutput("Two hexadecimal numbers separated by \'-\' required (eg. :L1-1A", true);
				return;
			}
			_hookManager.AddLink(fromThread, toThread);
			//_vnrProxy.Host_AddLink(fromThread, toThread);
		}

		private unsafe bool AddHookCode(string cmd, int pid)
		{
			var hp = new HookParam();
			_stopGarbageCollection.Add(hp);
			if (!HookParam.Parse(cmd.Substring(2), ref hp)) return false;
			var hookParamPointer = (IntPtr) (&hp);
			var insertHookTask = Task.Run(()=> _vnrProxy.Host_InsertHook(pid, hookParamPointer, null));
			var success = insertHookTask.Wait(5000);
			return success;
		}

		private void AttachWithProcessId(string cmd)
		{
			if (!int.TryParse(cmd.Substring(2), out var processId))
			{
				_hookManager.ConsoleOutput($"Failed to parse '{cmd.Substring(3)}'.", true);
				return;
			}

			try
			{
				var process = System.Diagnostics.Process.GetProcessById(processId);
				_vnrProxy.Host_InjectByPID((uint)process.Id);
				_hookManager.ConsoleOutput($"Injected into {process.ProcessName} ({process.Id})", true);
			}
			catch (Exception ex)
			{
				_hookManager.ConsoleOutput(ex.Message, true);//$"Failed to find process with PID '{processId}'.");
			}
		}

		private void AttachWithProcessName(string cmd)
		{
			var processName = cmd.Substring(3);
			var processes = System.Diagnostics.Process.GetProcessesByName(processName);
			switch (processes.Length)
			{
				case 0:
					_hookManager.ConsoleOutput($"No processes found with name '{processName}'", true);
					return;
				case 1:
					_vnrProxy.Host_InjectByPID((uint)processes[0].Id);
					_hookManager.ConsoleOutput($"Injected into {processes[0].ProcessName} ({processes[0].Id}", true);
					return;
				default:
					_hookManager.ConsoleOutput($"Multiple processes found with name '{processName}', use PID:", true);
					foreach (var process in processes)
					{
						_hookManager.ConsoleOutput($"Name: {process.ProcessName}, PID:{process.Id}", true);
					}
					return;
			}
		}

	}
}