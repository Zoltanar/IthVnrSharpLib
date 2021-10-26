using System;
using System.Linq;
using System.Text;
using System.Timers;

namespace IthVnrSharpLib
{
	public class ClipboardThread : TextThread
	{
		public override Encoding PrefEncoding
		{
			get => Encoding.Unicode;
			set => throw new NotSupportedException();
		}
		public override bool EncodingCanChange { get; } = false;
		public override bool IsSystem { get; } = true;
		private readonly StringBuilder _textBuffer = new(1000);

		public ClipboardThread()
		{
			DisplayName = "Clipboard";
			ProcessId = -1;
		}

		public override void Clear(bool _)
		{
			_textBuffer.Clear();
		}

		protected override int GetCharacterCount() => _textBuffer.Length;

		public override string SearchForText(string searchTerm, bool searchAllEncodings)
		{
			var textLines = Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			var firstLineWith = textLines.FirstOrDefault(l => l.Contains(searchTerm));
			return firstLineWith;
		}

		public override object MergeProperty => throw new NotSupportedException();
		public override string PersistentIdentifier => "Clipboard";

		public override void AddText(object value)
		{
			//if (value is not string sValue) throw new NotSupportedException($"Text as object of type {value.GetType()} is not supported by {nameof(ClipboardThread)}");
			if(value is not ValueTuple<string, System.Diagnostics.Process, string> tuple) throw new NotSupportedException($"Text as object of type {value.GetType()} is not supported by {nameof(ClipboardThread)}");
			(string text, System.Diagnostics.Process clipboardOwner, string name) = tuple;
			var tName = !string.IsNullOrWhiteSpace(name) ? name : clipboardOwner != null ? clipboardOwner.ProcessName : "Unknown";
			_textBuffer.AppendLine($"[{tName}] {text.Trim()}");
			if (_textBuffer.Length > TextTrimAt) _textBuffer.Remove(0, TextTrimCount);
			if (IsDisplay) OnPropertyChanged(nameof(Text));
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs _)
		{
			//ignored
		}

		public override string Text => _textBuffer.ToString();
	}
}
