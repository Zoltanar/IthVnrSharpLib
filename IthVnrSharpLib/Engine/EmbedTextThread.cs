using System;
using System.Linq;
using System.Text;
using System.Timers;

namespace IthVnrSharpLib.Engine
{
	internal sealed class EmbedTextThread : TextThread
	{
		public override Encoding PrefEncoding
		{
			get => Encoding.Unicode;
			set => throw new NotSupportedException();
		}
		public override bool EncodingCanChange { get; } = false;
		private readonly StringBuilder _currentTextBuffer = new(1000);
		private readonly StringBuilder _textBuffer = new(1000);
		public TextRole Role { get; }

		public EmbedTextThread(EngineText message , string engineName, int processId) : base(message.Signature)
		{
			Role = message.Role;
			DisplayName = $"{engineName} ({message.Signature}, {Role})";
			ProcessId = processId;
		}

		public override void Clear(bool _)
		{
			_textBuffer.Clear();
		}

		protected override int GetCharacterCount() =>_textBuffer.Length;

		public override string SearchForText(string searchTerm, bool searchAllEncodings)
		{
			var textLines = Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
			var firstLineWith = textLines.FirstOrDefault(l => l.Contains(searchTerm));
			return firstLineWith;
		}

		public override object MergeProperty => (Id, Role);
		public override string PersistentIdentifier => $"{(uint)Id:X8},{(int)Role:00}";

		public override void AddText(object value)
		{
			if(value is not string sValue) throw new NotSupportedException($"Text as object of type {value.GetType()} is not supported by {nameof(EmbedTextThread)}");
			_currentTextBuffer.AppendLine(sValue);
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs _)
		{
			Timer?.Close();
			Timer = null;
				var text = _currentTextBuffer.ToString();
				_currentTextBuffer.Clear();
				_textBuffer.Append(text);
				if (_textBuffer.Length > TextTrimAt) _textBuffer.Remove(0, TextTrimCount);
				if (IsPosting) UpdateDisplay(this, new TextOutputEventArgs(this, text, "Internal", false));
				if(IsDisplay) OnPropertyChanged(nameof(Text));
		}

		public override string Text => _textBuffer.ToString();
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