using System.Text;

namespace IthVnrSharpLib
{
	class ConsoleThread : TextThread
	{
		public override bool IsConsole => true;
		public override bool IsPaused => false;
		public override bool IsPosting => false;
		public override bool EncodingDefined => true;
		public override Encoding PrefEncoding => Encoding.Unicode;
		private readonly StringBuilder _textBuffer = new StringBuilder(1000);
		public override string Text => _textBuffer.ToString();
		public override string ToString() => "Console";

		public void AddText(string text)
		{
			_textBuffer.Append(text);
			_textBuffer.AppendLine();
		}
	}
}
