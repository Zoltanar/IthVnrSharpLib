namespace IthVnrSharpLib
{

	public class IthVnrSettings : SettingsJsonFile
	{
		
		/// <summary>
		/// Default constructor, sets all values to default.
		/// </summary>
		public IthVnrSettings()
		{
				SplitTime = 200;
				ProcessTime = 50;
				InjectDelay = 3000;
				InsertDelay = 500;
				AutoInject = true;
				AutoInsert = true;
				ClipboardFlag= false;
				CyclicRemove = false;
				GlobalFilter = 0;
		}
		
		public long SplitTime { get; set; }
		public bool CyclicRemove { get; set; }
		public long GlobalFilter { get; set; }
		public long ProcessTime { get; set; }
		public long InjectDelay { get; set; }
		public long InsertDelay { get; set; }
		public bool AutoInject { get; set; }
		public bool AutoInsert { get; set; }
		public bool ClipboardFlag { get; set; }
		
	}
}