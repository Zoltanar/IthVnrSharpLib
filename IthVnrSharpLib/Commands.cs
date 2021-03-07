using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace IthVnrSharpLib
{
	public class Commands
	{
		private readonly HookManagerWrapper _hookManager;
		private readonly IthVnrViewModel _viewModel;
		private readonly VNR _vnrHost;
		private Regex ProcessNameRegex { get; } = new ("/pn(.+)", RegexOptions.IgnoreCase);
		private Regex ProcessRegex { get; } = new ("/p(.+)", RegexOptions.IgnoreCase);
		private Regex HookRegex { get; } = new ("/h(.+)", RegexOptions.IgnoreCase);
		private Regex LinkRegex { get; } = new (":l(.+)", RegexOptions.IgnoreCase);
		private Regex UnlinkRegex { get; } = new (":u(.+)", RegexOptions.IgnoreCase);
		private Regex HelpRegex { get; } = new ("(:h|:help)", RegexOptions.IgnoreCase);
		private Regex SearchRegex { get; } = new("(:s|:search) (.+)", RegexOptions.IgnoreCase);
		private Regex SearchAllRegex { get; } = new("(:sa|:searchall) (.+)", RegexOptions.IgnoreCase);

		public Commands(IthVnrViewModel viewModel)
		{
			_viewModel = viewModel;
			_hookManager = viewModel.HookManager;
			_vnrHost = viewModel.VnrHost;
		}

		public void ProcessCommand(string cmd, int pid)
		{
			_hookManager.ConsoleOutput($"Processing command '{cmd}'...", true);
			Match searchMatch;
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
			else if ((searchMatch = SearchRegex.Match(cmd)).Success) _hookManager.FindThreadWithText(searchMatch.Groups[2].Value, false);
			else if ((searchMatch = SearchAllRegex.Match(cmd)).Success) _hookManager.FindThreadWithText(searchMatch.Groups[2].Value, true);
			else _hookManager.ConsoleOutput("Unrecognized command.", true);
		}

		public const string CommandUsage =
@"Available commands:
:h or :help - display this mesage.
:s or :search [search term] - search threads for search term in their set encoding.
:sa or :searchall [search term] - search threads for search term in all encodings (Unicode, Shift-JIS, UTF-8).
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
		private readonly List<object> _stopGarbageCollection = new();

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
		}

		private unsafe bool AddHookCode(string cmd, int pid)
		{
			var hp = new HookParam();
			_stopGarbageCollection.Add(hp);
			try
			{
				if (!HookParam.Parse(cmd.Substring(2), ref hp)) return false;
			}
			catch (Exception ex)
			{
				_hookManager.ConsoleOutput($"Exception while parsing hook code: {ex}", true);
				return false;
			}
			var hookParamPointer = (IntPtr) (&hp);
			_hookManager.ConsoleOutput($"Parsed code to: {hp}", true);
			var commandHandle = _viewModel.PipeAndRecordMap.GetCommandHandle(pid);
			if (commandHandle == IntPtr.Zero) return false;
			var result = _vnrHost.Host_InsertHook(hookParamPointer, null, commandHandle);
			return result == 0;
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
				var success = _vnrHost.Host_InjectByPID((uint)process.Id,out var errorMessage);
				_hookManager.ConsoleOutput(success ? $"Injected into {process.ProcessName} ({process.Id})" : errorMessage, true);
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
					var success = _vnrHost.Host_InjectByPID((uint)processes[0].Id, out var errorMessage);
					_hookManager.ConsoleOutput(success ? $"Injected into {processes[0].ProcessName} ({processes[0].Id})" : errorMessage, true);
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