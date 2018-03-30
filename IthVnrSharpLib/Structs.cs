using System;
using System.Runtime.InteropServices;
// ReSharper disable UnusedMember.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable InconsistentNaming

namespace IthVnrSharpLib
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessRecord
    {
        public int pid_register;
        public int hookman_register;
        public int module_register;
        //DWORD engine_register; // jichi 10/19/2014: removed
        public IntPtr process_handle;
        public IntPtr hookman_mutex;
        public IntPtr hookman_section;
        public IntPtr hookman_map;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct ThreadParameter
    {
        public uint pid; // jichi: 5/11/2014: The process ID
        public uint hook;
        public uint retn; // jichi 5/11/2014: The return address of the hook
        public uint spl;  // jichi 5/11/2014: the processed split value of the hook parameter
    };

	internal enum HookParamType : ulong
    {
        USING_STRING = 0x1,     // type(data) is char* or wchar_t* and has length
        USING_UTF8 = USING_STRING, // jichi 10/21/2014: temporarily handled the same way as USING_STRING
        USING_UNICODE = 0x2,     // type(data) is wchar_t or wchar_t*
        BIG_ENDIAN = 0x4,    // type(data) is char
        DATA_INDIRECT = 0x8,
        USING_SPLIT = 0x10,    // aware of split time?
        SPLIT_INDIRECT = 0x20,
        MODULE_OFFSET = 0x40,    // do hash module, and the address is relative to module
        FUNCTION_OFFSET = 0x80,    // do hash function, and the address is relative to funccion
        PRINT_DWORD = 0x100, // jichi 12/7/2014: Removed
        NO_ASCII = 0x100,   // jichi 1l/22/2015: Skip ascii characters
        STRING_LAST_CHAR = 0x200,
        NO_CONTEXT = 0x400,
        HOOK_EMPTY = 0x800,
        FIXING_SPLIT = 0x1000,
        // HOOK_AUXILIARY    = 0x2000,  // jichi 12/13/2013: None of known hooks are auxiliary
        RELATIVE_SPLIT = 0x2000, // jichi 10/24/2014: relative split return address
        HOOK_ENGINE = 0x4000,
        HOOK_ADDITIONAL = 0x8000,
    };

	[StructLayout(LayoutKind.Sequential, Size = 128)]
	public unsafe struct Hook
	{ // size: 0x80
		public HookParam hp;
		public IntPtr hook_name;
		public int name_length;
		public fixed byte recover[44];
		public fixed byte original[16];
		public uint Address() { return hp.address; }
		public uint Type() { return hp.type; }
		public ushort Length() { return hp.hook_len; }
		public string Name() { return Marshal.PtrToStringAuto(hook_name, name_length); }
		public int NameLength() { return name_length; }
	};
}
