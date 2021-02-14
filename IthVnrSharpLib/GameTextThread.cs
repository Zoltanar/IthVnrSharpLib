namespace IthVnrSharpLib
{
	public class GameTextThread
	{
		public long Id { get; set; }
		public long GameId { get; set; }
		public bool IsDisplay { get; set; } 
		public bool IsPaused { get; set; }
		public bool IsPosting { get; set; }
		public string HookCode { get; set; }
		public string HookNameless { get; set; }
		public string HookFull { get; set; }
		public string Encoding { get; set; }

		public GameTextThread()
		{
			
		}

		public GameTextThread(TextThread thread)
		{
			HookCode = thread.HookCode;
			HookNameless = thread.HookNameless;
			HookFull = thread.HookFull;
		}

		public string Options => $"Display: {IsDisplay}, Posting: {IsPosting}, Paused: {IsPaused}";

		public override string ToString() => $"{HookCode} ({HookFull})";
	}
}
