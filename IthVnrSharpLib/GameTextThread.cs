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
		public string HookFull { get; set; }
		public string Encoding { get; set; }

		public override string ToString() => $"{HookCode} ({HookFull})";
	}
}
