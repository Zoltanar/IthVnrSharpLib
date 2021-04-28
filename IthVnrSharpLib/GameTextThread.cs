namespace IthVnrSharpLib
{
	public class GameTextThread
	{
		public long GameId { get; set; }
		public bool IsDisplay { get; set; } 
		public bool IsPaused { get; set; }
		public bool IsPosting { get; set; }
		public string Identifier { get; set; }
		public string Encoding { get; set; }
		public string Label { get; set; }

		public GameTextThread()
		{
		}

		public GameTextThread(TextThread thread)
		{
			Identifier = thread.PersistentIdentifier;
			IsDisplay = true;
			Encoding = System.Text.Encoding.Unicode.WebName;
		}

		public string Options => $"'{Label}' Display: {IsDisplay}, Posting: {IsPosting}, Paused: {IsPaused}";

		public override string ToString() => $"[{GameId}] {Identifier} {Label}";
	}
}
