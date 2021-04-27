using System;
using System.Text;
using System.Timers;

namespace IthVnrSharpLib
{
	public class ConsoleThread : TextThread
	{
		private const string Name = @"Console";
		public override object MergeProperty => null;
		public override string PersistentIdentifier { get; } = Name;

		public override bool IsPaused
		{
			get => false;
			set {/*ignore*/ }
		}

		public override bool IsPosting { get; set; } = false;
		private readonly StringBuilder _textBuffer = new(1000);
		public override string Text => _textBuffer.ToString();
		public override Encoding PrefEncoding
		{
			get => Encoding.Unicode;
			set => throw new NotSupportedException();
		}
		public override bool EncodingCanChange { get; } = false;
		public override string ToString() => Name;

		public ConsoleThread(IntPtr id) : base(id)
		{
			DisplayName = Name;
		}

		public override void Clear(bool _)
		{
			_textBuffer.Clear();
		}

		protected override int GetCharacterCount()
		{
			return _textBuffer.Length;
		}

		public override string SearchForText(string searchTerm, bool searchAllEncodings)
		{
			return null;
		}

		public override void AddText(object value)
		{
			var text = value as string ?? (value is byte[] bArray
				? Encoding.Unicode.GetString(bArray)
				: throw new NotSupportedException($"Text as object of type {value.GetType()} is not supported by {nameof(ConsoleThread)}"));
			_textBuffer.AppendLine(text);
		}

		protected override void OnTimerEnd(object sender, ElapsedEventArgs _)
		{
			Timer?.Close();
			Timer = null;
			if (IsDisplay) OnPropertyChanged(nameof(Text));
		}

	}
}
