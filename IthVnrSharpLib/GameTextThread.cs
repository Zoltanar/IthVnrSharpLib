using IthVnrSharpLib.Properties;
using System.Text;

namespace IthVnrSharpLib
{
	public class GameTextThread
	{
		// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global Object created by Entity Framework
		public long Id { get; set; }
		public long GameId { get; set; }
		public bool IsDisplay { get; set; } 
		public bool IsPaused { get; set; }
		public bool IsPosting { get; set; }
		public string Identifier { get; set; }
		public string Encoding { get; set; }
		// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global

		[UsedImplicitly]
		public GameTextThread()
		{
			
		}

		public GameTextThread(TextThread thread)
		{
			Identifier = thread.PersistentIdentifier;
			IsDisplay = true;
			Encoding = System.Text.Encoding.Unicode.WebName;
		}

		public string Options => $"Display: {IsDisplay}, Posting: {IsPosting}, Paused: {IsPaused}";

		public override string ToString() => $"[{Id}] {Identifier}";
	}
}
