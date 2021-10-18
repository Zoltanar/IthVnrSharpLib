using System;
using System.Runtime.CompilerServices;

namespace IthVnrSharpLib
{
	public static class StaticHelpers
	{
		private const string BuildDate = "15 - Apr - 2021";
		internal const string Version = "2.5.0";
		public static readonly string VersionInfo = $"{nameof(IthVnrSharpLib)} {Version} (VNR DLLs Forked from 3.5640.1) {BuildDate}";

		public static IthVnrSettings Settings { get; private set; }
		private static Action<string> _logToDebugAction;
		private static Action<string[]> _logToFileAction;
		private static Action<Exception, string> _logExceptionToFileAction;

		public static void Initialise(string settingsFilePath, Action<string[]> logToFile, Action<string> logToDebug, Action<Exception, string> logExceptionToFile)
		{
			_logToFileAction = logToFile;
			_logToDebugAction = logToDebug;
			_logExceptionToFileAction = logExceptionToFile;
			 Settings = SettingsJsonFile.Load<IthVnrSettings>(settingsFilePath);
		}

		internal static void LogToDebug(string text) => _logToDebugAction?.Invoke(text);

		internal static void LogToFile(string text) => _logToFileAction?.Invoke(new[] { text });

		internal static void LogToFile(Exception exception, [CallerMemberName] string source = null) => _logExceptionToFileAction?.Invoke(exception, source);

		internal static string ToEmoji(this bool value) => value ? "✔" : "❌";
	}
}
