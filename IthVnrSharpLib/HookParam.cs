using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

namespace IthVnrSharpLib
{
	[StructLayout(LayoutKind.Sequential, Size = 60)]
	public unsafe struct HookParam
	{
		// ReSharper disable once InconsistentNaming
		// ReSharper disable once UnusedMember.Global
		// jichi 8/24/2013: For special hooks.
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void* text_fun_t(uint esp, HookParam* hp, byte index, uint* data, uint* split, uint* len);

		// jichi 10/24/2014: Add filter function. Return the if skip the text
		//public delegate bool *filter_fun_t(IntPtr str, int* len, HookParam* hp, byte index);

		// jichi 10/24/2014: Add generic hook function, return false if stop execution.
		//public delegate bool *hook_fun_t(int esp, HookParam* hp);

		public uint address;    // absolute or relative address

		public uint offset,     // offset of the data in the memory
			index,      // ?
			split,      // esp offset of the split character = pusha offset - 4
			split_index; // ?

		public uint module, // hash of the module
			function;
		public IntPtr text_fun;
		public IntPtr filter_fun;
		public IntPtr hook_fun;
		public uint type;   // flags
		public ushort length_offset; // index of the string length

		public byte hook_len; // ?
		public byte recover_len; // ?

		// 2/2/2015: jichi number of times - 1 to run the hook
		public byte extra_text_count;
		private byte _unused; // jichi 2/2/2015: add a BYTE type to make to total sizeof(HookParam) even.

		// 7/20/2014: jichi additional parameters for PSP games
		public uint user_flags, user_value;

		/// <summary>
		/// Ported from profile\misc.cpp
		/// </summary>
		public static bool Parse(string cmd, ref HookParam hp)
		{
			unchecked
			{
				// /H[X]{A|B|W|S|Q}[N][data_offset[*drdo]][:sub_offset[*drso]]@addr[:[module[:{name|#ordinal}]]]
				var rx = new Regex("^X?([ABWSQ])X?(N)?", RegexOptions.IgnoreCase);
				var m1 = rx.Matches(cmd);
				var result = m1.Count != 0;
				if (!result) return false;
				var m = m1[0].Groups;
				var start = cmd.Substring(m[0].Value.Length);
				if (m[2].Success) hp.type |= (uint)HookParamType.NO_CONTEXT;
				if (m[1].Value.Length == 0) hp.type |= (uint)HookParamType.STRING_LAST_CHAR;
				else
				{
					switch (char.ToUpperInvariant(m[1].Value[0]))
					{
						case 'S':
							hp.type |= (uint)HookParamType.USING_STRING;
							break;
						case 'E':
							hp.type |= (uint)HookParamType.STRING_LAST_CHAR;
							goto case 'A';
						case 'A':
							hp.type |= (uint)HookParamType.BIG_ENDIAN;
							hp.length_offset = 1;
							break;
						case 'B':
							hp.length_offset = 1;
							break;
						case 'H':
							hp.type |= (uint)HookParamType.PRINT_DWORD;
							goto case 'Q';
						case 'Q':
							hp.type |= (uint)HookParamType.USING_STRING | (uint)HookParamType.USING_UNICODE;
							break;
						case 'L':
							hp.type |= (uint)HookParamType.STRING_LAST_CHAR;
							goto case 'W';
						case 'W':
							hp.type |= (uint)HookParamType.USING_UNICODE;
							hp.length_offset = 1;
							break;
					}
				}

				// [data_offset[*drdo]]
				const string data_offset = "(-?[A-Fa-f0-9]+)";
				const string drdo = "(\\*-?[A-Fa-f0-9]+)?";
				rx = new Regex("^" + data_offset + drdo, RegexOptions.IgnoreCase);
				m1 = rx.Matches(start);
				result = m1.Count != 0;
				if (result)
				{
					m = m1[0].Groups;
					start = start.Substring(m[0].Value.Length);
					hp.offset = GetUIntFromHexValue(m[1].Value);
					if (m[2].Success)
					{
						hp.type |= (uint)HookParamType.DATA_INDIRECT;
						hp.index = uint.Parse(m[2].Value.Substring(1), NumberStyles.HexNumber);
					}
				}
				// [:sub_offset[*drso]]
				const string sub_offset = "(-?[A-Fa-f0-9]+)";
				const string drso = "(\\*-?[A-Fa-f0-9]+)?";
				rx = new Regex($"^:{sub_offset}{drso}", RegexOptions.IgnoreCase);
				m1 = rx.Matches(start);
				result = m1.Count != 0;
				if (result)
				{
					m = m1[0].Groups;
					start = start.Substring(m[0].Value.Length);
					hp.type |= (uint)HookParamType.USING_SPLIT;
					hp.split = GetUIntFromHexValue(m[1].Value);
					if (m[2].Success)
					{
						hp.type |= (uint)HookParamType.SPLIT_INDIRECT;
						hp.split_index = GetUIntFromHexValue(m[2].Value.Substring(1));
					}
				}
				// @addr
				rx = new Regex("^@[A-Fa-f0-9]+", RegexOptions.IgnoreCase);
				m1 = rx.Matches(start);
				result = m1.Count != 0;
				if (!result) return false;
				m = m1[0].Groups;
				start = start.Substring(m[0].Value.Length);
				hp.address = GetUIntFromHexValue(m[0].Value.Substring(1));
				if ((hp.offset & 0x80000000) != 0)
					hp.offset -= 4;
				if ((hp.split & 0x80000000) != 0)
					hp.split -= 4;

				// [:[module[:{name|#ordinal}]]]
				// ":"               -> module == NULL &% function == NULL
				// ""                -> MODULE_OFFSET && module == NULL && function == addr
				// ":GDI.dll"        -> MODULE_OFFSET && module != NULL
				// ":GDI.dll:strlen" -> MODULE_OFFSET | FUNCTION_OFFSET && module != NULL && function != NULL
				// ":GDI.dll:#123"   -> MODULE_OFFSET | FUNCTION_OFFSET && module != NULL && function != NULL
				string module = @"([^:;]+)";
				const string nameRegexPart = @"[^:;]+";
				const string ordinalRegexPart = "\\d+";
				rx = new Regex($"^:({module}(:{nameRegexPart}|#{ordinalRegexPart})?)?$", RegexOptions.IgnoreCase);
				m1 = rx.Matches(start);
				result = m1.Count != 0;
				if (result) // :[module[:{name|#ordinal}]]
				{
					m = m1[0].Groups;
					if (m[1].Success) // module
					{
						hp.type |= (uint)HookParamType.MODULE_OFFSET;
						module = m[2].Value;
						module = module.ToLowerInvariant();
						hp.module = Hash(module);
						if (m[3].Success) // :name|#ordinal
						{
							hp.type |= (uint)HookParamType.FUNCTION_OFFSET;
							hp.function = Hash(m[3].Value.Substring(1));
						}
					}
				}
				else
				{
					rx = new Regex("^!([A-Fa-f0-9]+)(!([A-Fa-f0-9]+))?$", RegexOptions.IgnoreCase);
					m1 = rx.Matches(start);
					result = m1.Count != 0;
					if (result)
					{
						m = m1[0].Groups;
						hp.type |= (uint)HookParamType.MODULE_OFFSET;
						hp.module = GetUIntFromHexValue(m[1].Value);
						if (m[2].Success)
						{
							hp.type |= (uint)HookParamType.FUNCTION_OFFSET;
							hp.function = GetUIntFromHexValue(m[2].Value.Substring(1));
						}
					}
					else
					{
						// Hook is relative to the executable. Store the original address in function.
						// hp.module == NULL && hp.function != NULL
						hp.type |= (uint)HookParamType.MODULE_OFFSET;
						hp.function = hp.address;
					}
				}
				return true;
			}
		}
		
        /// <summary>
        /// Copied from Textractor
        /// https://github.com/Artikash/Textractor/blob/15db478e62eb955d2299484dbf2c9c7e707bb4cb/host/hookcode.cpp
        /// </summary>
        public static bool ParseR(string RCode, ref HookParam hp)
        {
            hp.type |= (uint)HookParamType.DIRECT_READ;

            // {S|Q|V|M}
            switch (RCode[0])
            {
                case 'S':
                    break;
                case 'Q':
                    hp.type |= (uint)HookParamType.USING_UNICODE;
                    break;
                case 'V':
                    hp.type |= (uint)HookParamType.USING_UTF8;
                    break;
                case 'M':
                    hp.type |= (uint)HookParamType.USING_UNICODE | (uint)HookParamType.HEX_DUMP;
                    break;
                default:
                    return false;
            }
            RCode = RCode.Substring(1);

            // [null_length<]
            var pattern = new Regex("^([0-9]+)<");
            var match = pattern.Match(RCode);
            if (match.Success) //std::regex_search(RCode, match, std::wregex("^([0-9]+)<"))))
            {
                //todo
                //hp.null_length = int.Parse(match.Groups[1].Value);
                RCode = RCode.Substring(match.Groups[0].Length);
            }

            // [codepage#]
            pattern = new Regex("^([0-9]+)#");
            match = pattern.Match(RCode);
            if (match.Success)
            {
				//todo
                //hp.codepage = int.Parse(match.Groups[1].Value);
                RCode = RCode.Substring(match.Groups[0].Length);
            }

            // @addr
            pattern = new Regex("@([0-9A-F]+)");
			match = pattern.Match(RCode);
            if (!match.Success) return false;
            hp.address = Convert.ToUInt32(match.Groups[1].Value, 16);
            return true;
        }

        private static uint GetUIntFromHexValue(string text)
		{
			unchecked
			{
				if (text.StartsWith("-")) return (uint)-uint.Parse(text.Substring(1), NumberStyles.HexNumber);
				return uint.Parse(text, NumberStyles.HexNumber);
			}
		}

		private static uint Hash(string module)
		{
			uint hash = 0;
			using var it = module.GetEnumerator();
			while (it.MoveNext())
			{
				hash = _rotr(hash, 7) + it.Current;
			}
			return hash;
		}

		private static uint _rotr(uint value, int count)
		{
			return (value >> count) | (value << (32 - count));
		}

		public override string ToString() => 
			$"addr: {address}, text_fun: {text_fun}, function: {function}, hook_len: {hook_len}, ind: {index}, length_offset: {length_offset}, module: {module}, off: {offset}, recover_len: {recover_len}, split: {split}, split_ind: {split_index}, type: {type}";
	};
}