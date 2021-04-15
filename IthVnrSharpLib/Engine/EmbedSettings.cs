namespace IthVnrSharpLib.Engine
{
	/// <summary>
	/// Holds Settings object used in RPC communication, must match 'vnragent.dll'
	/// </summary>
	public class EmbedSettings
	{
		// ReSharper disable InconsistentNaming
		// ReSharper disable UnusedMember.Global
		// ReSharper disable IdentifierTypo
		public bool embeddedScenarioTranscodingEnabled { get; set; }
		public bool embeddedFontCharSetEnabled { get; set; } = true;
		public int embeddedTranslationWaitTime { get; set; } = 2000;
		public bool embeddedOtherTranscodingEnabled { get; set; }
		public string embeddedSpacePolicyEncoding { get; set; } = string.Empty;
		public bool windowTranslationEnabled { get; set; }
		public bool windowTextVisible { get; set; } = true;
		public bool embeddedNameTranscodingEnabled { get; set; }
		public string gameEncoding { get; set; } //= "shift-jis";
		public bool embeddedOtherTranslationEnabled { get; set; }
		public bool embeddedSpaceSmartInserted { get; set; }
		public int embeddedFontCharSet { get; set; } = 0;
		public int embeddedScenarioWidth { get; set; } = 0;
		public bool embeddedScenarioTextVisible { get; set; } = true;
		public bool windowTranscodingEnabled { get; set; }
		public int nameSignature { get; set; } = 0;
		public bool embeddedScenarioTranslationEnabled { get; set; }
		public bool embeddedScenarioVisible { get; set; } = true;
		public int embeddedFontScale { get; set; }
		public bool embeddedAllTextsExtracted { get; set; }
		public bool embeddedOtherVisible { get; set; } = true;
		public string embeddedFontFamily { get; set; }
		public bool embeddedTextEnabled { get; set; } = true;
		public int scenarioSignature { get; set; } //=30661;
		public bool embeddedOtherTextVisible { get; set; } = true;
		public bool embeddedNameTextVisible { get; set; } = true;
		public bool embeddedSpaceAlwaysInserted { get; set; }
		public bool embeddedNameTranslationEnabled { get; set; }
		public bool debug { get; set; } //=true;
		public bool embeddedNameVisible { get; set; } = true;
		public int embeddedFontWeight { get; set; } = 0;
		// ReSharper restore IdentifierTypo
		// ReSharper restore UnusedMember.Global
		// ReSharper restore InconsistentNaming
	}
}