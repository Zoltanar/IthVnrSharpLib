using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IthVnrSharpLib
{
	public static class StaticHelpers
	{
		private const string BuildDate = "15 - Apr - 2021";
		private const string LibVersion = "1.1.0";
		public static readonly string VersionInfo = $"{nameof(IthVnrSharpLib)} {LibVersion} (VNR DLLs Forked from 3.5640.1) {BuildDate}";

		static StaticHelpers()
		{
			Directory.CreateDirectory(StoredDataFolder);
			CSettings = SettingsJsonFile.Load<IthVnrSettings>(CoreSettingsJson);
		}
		public const string StoredDataFolder = @"Stored Data\"; //this is in order to use same folder for all builds (32/64 and debug/release)
		public const string LogFile = StoredDataFolder + "message.log";
		public const string CoreSettingsJson = StoredDataFolder + "settings.json";
		public static readonly IthVnrSettings CSettings;


		/// <summary>
		/// Print message to Debug and write it to log file.
		/// </summary>
		/// <param name="message">Message to be written</param>
		public static void LogToFile(string message)
		{
			Debug.Print(message);
			int counter = 0;
			while (IsFileLocked(new FileInfo(LogFile)))
			{
				counter++;
				if (counter > 5) return;//throw new IOException("Logfile is locked!");
				Thread.Sleep(25);
			}
			using var writer = new StreamWriter(LogFile, true);
			writer.WriteLine(message);
		}

		[Conditional("DEBUG")]
		public static void LogToDebug(string text)
		{
			Debug.WriteLine(text);
		}

		/// <summary>
		/// Print exception to Debug and write it to log file.
		/// </summary>
		/// <param name="exception">Exception to be written to file</param>
		/// <param name="source">Source of error, CallerMemberName by default</param>
		public static void LogToFile(Exception exception, [CallerMemberName] string source = null)
		{
			Debug.Print($"Source: {source}");
			Debug.Print(exception.Message);
			Debug.Print(exception.StackTrace);
			int counter = 0;
			while (IsFileLocked(new FileInfo(LogFile)))
			{
				counter++;
				if (counter > 5) return;//throw new IOException("Logfile is locked!");
				Thread.Sleep(25);
			}
			using var writer = new StreamWriter(LogFile, true);
			writer.WriteLine($"Source: {source}");
			writer.WriteLine(exception.Message);
			writer.WriteLine(exception.StackTrace);
		}

		/// <summary>
		/// Check if file is locked,
		/// </summary>
		/// <param name="file">File to be checked</param>
		/// <returns>Whether file is locked</returns>
		public static bool IsFileLocked(FileInfo file)
		{
			FileStream stream = null;

			try
			{
				stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException)
			{
				return true;
			}
			finally
			{
				stream?.Close();
			}
			return false;
		}
	}
}
