using System;
using System.Text;
using System.Timers;

namespace IthVnrSharpLib.Engine
{
	internal class EmbedTextThread : TextThread
	{
		private readonly StringBuilder _textBuffer = new(1000);
		public TextRole Role { get; }
		protected override uint Status
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

		public override void Clear(bool _)
		{
			_textBuffer.Clear();
		}

		public override void AddText(object value)
		{
			var text = value as string ?? (value is byte[] bArray
				? Encoding.Unicode.GetString(bArray)
				: throw new NotSupportedException($"Text as object of type {value.GetType()} is not supported by {nameof(ConsoleThread)}"));
			_textBuffer.Append(text);
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs _)
		{
			try
			{
				if(IsPosting) UpdateDisplay(this, new TextOutputEventArgs(this, Text, "Internal", false));
			}
			finally
			{
				Timer?.Close();
				Timer = null;
			}
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