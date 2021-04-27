using System;
using System.Diagnostics;
using System.IO;
using IthVnrSharpLib.Properties;

namespace IthVnrSharpLib
{
	public class ProcessInfo
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public string DisplayString { get; set; }
		public string MainFileName { get; set; }
		public string FullMainFilePath { get; set; }
		public bool Attached { get; set; }
		public string Status => Attached ? "Attached" : "";

		public override string ToString() => DisplayString;

		public ProcessInfo([NotNull] Process process, bool attached, bool dispose)
		{
			Id = process.Id;
			Name = process.ProcessName;
			DisplayString = $"[{Id}] {Name}";
			Attached = attached;
			if (process.MainModule == null) throw new InvalidOperationException($"Main Module of Process [{process.Id}:{process.MainWindowTitle}] was null.");
			FullMainFilePath = process.MainModule.FileName;
			MainFileName = Path.GetFileName(FullMainFilePath);
			if (dispose) process.Dispose();
		}

		public ProcessInfo() { }
	}
}