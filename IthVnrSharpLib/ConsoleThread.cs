using System.Text;

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

		public void AddText(string text)
		{
			_textBuffer.Append(text);
			_textBuffer.AppendLine();
		}
	}
}
