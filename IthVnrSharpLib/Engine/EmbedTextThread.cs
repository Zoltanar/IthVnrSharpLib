using System;
using System.Text;

namespace IthVnrSharpLib.Engine
{
	internal class EmbedTextThread : TextThread
	{
		public TextRole Role { get; }
		public override uint Status
		{
			get => 1;
			set => throw new NotSupportedException();
		}

		public EmbedTextThread(EngineText message , string engineName, int processId)
		{
			Role = message.Role;
			HookCode = HookNameless = HookFull = ThreadString = $"{engineName} ({message.Signature}, {Role})";
			Id = message.Signature;
			ProcessId = processId;
			SetEncoding(Encoding.Unicode);
		}
	}

	internal enum TextRole
	{
		UnknownRole = 0,
		ScenarioRole,
		NameRole,
		OtherRole,
		ChoiceRole = OtherRole,
		HistoryRole = OtherRole
	}
}