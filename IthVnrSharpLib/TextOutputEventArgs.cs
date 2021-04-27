using System;

namespace IthVnrSharpLib
{
	public delegate bool TextOutputEvent(object sender, TextOutputEventArgs e);

	public class TextOutputEventArgs
	{
		public string Text { get; set; }
		public DateTime Time { get; set; }
		public string Source { get; set; }
		public bool FromClipboard { get; set; }
		public bool FromInternal { get; set; }
		public TextThread TextThread { get; set; }

		public override string ToString()
		{
			var source = Source;
			if (TextThread != null) source += $"/{TextThread.Number:0000}";
			return $"{Time:HH:mm:ss:fff} {source} {Text.Substring(0, Math.Min(50, Text.Length))}";
		}

		public TextOutputEventArgs(TextThread thread, string text, string source, bool clipboard)
		{
			TextThread = thread;
			if (text.StartsWith("\r\n")) text = text.Substring(2);
			if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
			Text = text.Trim();
			Time = DateTime.UtcNow;
			Source = source;
			if (clipboard) FromClipboard = true;
			else FromInternal = true;
		}
	}
}