namespace IthVnrSharpLib
{

	public class IthVnrSettings : SettingsJsonFile
	{
		private bool _clipboardFlag;

		/// <summary>
		/// Default constructor, sets all values to default.
		/// </summary>
		public IthVnrSettings()
		{
			ClipboardFlag = false;
		}

		public bool ClipboardFlag
		{
			get => _clipboardFlag;
			set
			{
				if (_clipboardFlag == value) return;
				_clipboardFlag = value;
				if (Loaded) Save();
			}
		}
	}
}