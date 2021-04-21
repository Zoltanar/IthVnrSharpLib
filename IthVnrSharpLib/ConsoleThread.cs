using System;
using System.Text;
using System.Timers;

namespace IthVnrSharpLib
{
	public class ConsoleThread : TextThread
	{
		public override bool IsConsole => true;
		public override bool IsPaused { get; set; } =false;
		public override bool IsPosting { get; set; } = false;
		public override bool EncodingDefined => true;
		public override Encoding PrefEncoding { get; set; } = Encoding.Unicode;
		private readonly StringBuilder _textBuffer = new (1000);
		public override string Text => _textBuffer.ToString();
		public override string ToString() => "Console";

		public ConsoleThread()
		{
			ThreadString = "Console";
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
			_textBuffer.AppendLine();
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs _)
		{
			try
			{
				//ignore
			}
			finally
			{
				Timer?.Close();
				Timer = null;
			}
		}

	}
}
