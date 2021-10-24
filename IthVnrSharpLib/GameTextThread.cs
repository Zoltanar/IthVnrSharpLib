namespace IthVnrSharpLib
{
	public class GameTextThread
	{
		public long GameId { get; set; }
		public bool IsDisplay { get; set; } 
		public bool IsPaused { get; set; }
		public bool IsPosting { get; set; }
		public string Identifier { get; set; }
		public uint RetnRight { get; set; }
		public uint Spl { get; set; }
		public string Encoding { get; set; }
		public string Label { get; set; }

		public GameTextThread()
		{
		}

		public GameTextThread(TextThread thread)
		{
			Identifier = thread.PersistentIdentifier;
			if (thread is HookTextThread hookThread)
			{
				RetnRight = hookThread.Parameter.retn & 0xFFFF;
				Spl = hookThread.Parameter.spl;
			}
			IsDisplay = true;
			Encoding = System.Text.Encoding.Unicode.WebName;
		}

		public string Options => $"'{Label}' Display: {IsDisplay.ToEmoji()}, Posting: {IsPosting.ToEmoji()}, Paused: {IsPaused.ToEmoji()}";

		public override string ToString() => $"[{GameId}] {Identifier} {Label}";
	}
}
