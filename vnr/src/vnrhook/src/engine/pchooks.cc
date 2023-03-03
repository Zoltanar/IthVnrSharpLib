// pchooks.cc
// 8/1/2014 jichi

#include "src/engine/pchooks.h"
#include "src/main.h"
//#include <gdiplus.h>

enum { STRING = 12, MESSAGE_SIZE = 500, PIPE_BUFFER_SIZE = 50000, SHIFT_JIS = 932, MAX_MODULE_SIZE = 120, PATTERN_SIZE = 30, HOOK_NAME_SIZE = 60,  };
#define DEBUG "vnrcli"
#define DPRINT(cstr) ConsoleOutput(DEBUG ":" __FUNCTION__ ":" cstr) // defined in vnrcli

DWORD Hash(const std::wstring& module, int length = -1)
{
    DWORD hash = 0;
    auto end = (length < 0 || static_cast<size_t>(length) > module.length()) ?
        module.end() :
        module.begin() + length;
    for (auto it = module.begin(); it != end; ++it)
        hash = _rotr(hash, 7) + *it;
    return hash;
}

static std::wstring charToWString(const char* text)
{
    const size_t size = std::strlen(text);
    std::wstring wstr;
    if (size > 0) {
        wstr.resize(size);
        std::mbstowcs(&wstr[0], text, size);
    }
    return wstr;
}


// 8/1/2014 jichi: Split is not used.
// Although split is specified, USING_SPLIT is not assigned.

// Use LPASTE to convert to wchar_t
// http://bytes.com/topic/c/answers/135834-defining-wide-character-strings-macros
//#define LPASTE(s) L##s
//#define L(s) LPASTE(s)
#define NEW_HOOK_AT(_addr, _fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \
  { \
    HookParam hp = {}; \
    hp.address = _addr; \
    hp.offset = _data; \
    hp.index = _data_ind; \
    hp.split = _split_off; \
    hp.split_index = _split_ind; \
    hp.type = _type; \
    hp.length_offset = _len_off; \
    NewHook(hp, #_fun); \
  }

// Static hook
#define NEW_HOOK(_fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \
  NEW_HOOK_AT((DWORD)_fun, _fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \

#define NEW_MODULE_HOOK(_module, _fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \
  { \
    if (DWORD addr = (DWORD)::GetProcAddress(_module, #_fun)) \
      NEW_HOOK_AT(addr, _fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \
  }
#define NEW_HOOK_DLL_NAME(_dll, _fun, _data, _data_ind, _split_off, _split_ind, _type, _len_off) \
  { \
    HookParam hp = {}; \
    wchar_t moduleName[MAX_MODULE_SIZE]; \
    char functionName[MAX_MODULE_SIZE]; \
	wcsncpy_s(moduleName, _dll, MAX_MODULE_SIZE - 1); \
	strncpy_s(functionName, #_fun, MAX_MODULE_SIZE - 1); \
    hp.module = Hash(moduleName); \
    hp.function = Hash(charToWString(functionName)); \
    hp.offset = _data; \
    hp.index = _data_ind; \
    hp.split = _split_off; \
    hp.split_index = _split_ind; \
    hp.type = _type | MODULE_OFFSET | FUNCTION_OFFSET; \
    hp.length_offset = _len_off; \
    NewHook(hp, #_fun); \
  }
#ifndef _WIN64
enum args {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    , s_arg2 = 4 * 2 // 0x8
    , s_arg3 = 4 * 3 // 0xc
    , s_arg4 = 4 * 4 // 0x10
    , s_arg5 = 4 * 5 // 0x14
    , s_arg6 = 4 * 6 // 0x18
    , s_arg7 = 4 * 7
};
#else // _WIN32
enum args {
    s_retaddr = 0x0,
    s_arg1 = -0x20,
    s_arg2 = -0x28,
    s_arg3 = -0x50,
    s_arg4 = -0x58,
    s_arg5 = 0x8,
    s_arg6 = 0x10,
    s_arg7 = 0x18
};
#endif // _WIN64

constexpr short arg_sz = (short)sizeof(void*);

// jichi 7/17/2014: Renamed from InitDefaultHook
void PcHooks::hookGDIFunctions()
{
  DPRINT("enter");
  // int TextHook::InitHook(LPVOID addr, DWORD data, DWORD data_ind, DWORD split_off, DWORD split_ind, WORD type, DWORD len_off)
  //
  // jichi 9/8/2013: Guessed meaning
  // - data(off): 4 * the n-th (base 1) parameter representing the data of the string
  // - len_off:
  //   - the n-th (base 1) parameter representing the length of the string
  //   - or 1 if is char
  //   - or 0 if detect on run time
  // - type: USING_STRING if len_off != 1 else BIG_ENDIAN or USING_UNICODE
  //
  // Examples:
  // int WINAPI lstrlenA(LPCSTR lpString)
  // - data: 4 * 1 = 4, as lpString is the first
  // - len_off: 0, as no parameter representing string length
  // - type: BIG_ENDIAN, since len_off == 1
  // BOOL GetTextExtentPoint32(HDC hdc, LPCTSTR lpString, int c, LPSIZE lpSize);
  // - data: 4 * 2 = 0x8, as lpString is the second
  // - len_off: 3, as nCount is the 3rd parameter
  // - type: USING_STRING, since len_off != 1
  //
  // Note: All functions does not have NO_CONTEXT attribute and will be filtered.

  enum stack {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    , s_arg2 = 4 * 2 // 0x8
    , s_arg3 = 4 * 3 // 0xc
    , s_arg4 = 4 * 4 // 0x10
    , s_arg5 = 4 * 5 // 0x14
    , s_arg6 = 4 * 6 // 0x18
  };

//#define _(Name, ...) \
//  hookman[HF_##Name].InitHook(Name, __VA_ARGS__); \
//  hookman[HF_##Name].SetHookName(names[HF_##Name]);

  // Always use s_arg1 = hDC as split_off
  // 7/26/2014 jichi: Why there is no USING_SPLIT type?

  // gdi32.dll
  NEW_HOOK(GetTextExtentPoint32A,  s_arg2, 0,s_arg1,0, USING_STRING,  3) // BOOL GetTextExtentPoint32(HDC hdc, LPCTSTR lpString, int c, LPSIZE lpSize);
  NEW_HOOK(GetTextExtentExPointA,  s_arg2, 0,s_arg1,0, USING_STRING,  3) // BOOL GetTextExtentExPoint(HDC hdc, LPCTSTR lpszStr, int cchString, int nMaxExtent, LPINT lpnFit, LPINT alpDx, LPSIZE lpSize);
  NEW_HOOK(GetTabbedTextExtentA,   s_arg2, 0,s_arg1,0, USING_STRING,  3) // DWORD GetTabbedTextExtent(HDC hDC, LPCTSTR lpString, int nCount, int nTabPositions, const LPINT lpnTabStopPositions);
  NEW_HOOK(GetCharacterPlacementA, s_arg2, 0,s_arg1,0, USING_STRING,  3) //  DWORD GetCharacterPlacement(HDC hdc, LPCTSTR lpString, int nCount, int nMaxExtent, LPGCP_RESULTS lpResults, DWORD dwFlags);
  NEW_HOOK(GetGlyphIndicesA,       s_arg2, 0,s_arg1,0, USING_STRING,  3) // DWORD GetGlyphIndices( HDC hdc, LPCTSTR lpstr, int c, LPWORD pgi, DWORD fl);
  NEW_HOOK(GetGlyphOutlineA,       s_arg2, 0,s_arg1,0, BIG_ENDIAN,    1) // DWORD GetGlyphOutline(HDC hdc,  UINT uChar,  UINT uFormat, LPGLYPHMETRICS lpgm, DWORD cbBuffer, LPVOID lpvBuffer, const MAT2 *lpmat2);
  NEW_HOOK(ExtTextOutA,            s_arg6, 0,s_arg1,0, USING_STRING,  7) // BOOL ExtTextOut(HDC hdc, int X, int Y, UINT fuOptions, const RECT *lprc, LPCTSTR lpString, UINT cbCount, const INT *lpDx);
  NEW_HOOK(TextOutA,               s_arg4, 0,s_arg1,0, USING_STRING,  5) // BOOL TextOut(HDC hdc, int nXStart, int nYStart, LPCTSTR lpString, int cchString);
  NEW_HOOK(TabbedTextOutA,         s_arg4, 0,s_arg1,0, USING_STRING,  5) // LONG TabbedTextOut(HDC hDC, int X, int Y, LPCTSTR lpString, int nCount, int nTabPositions, const LPINT lpnTabStopPositions, int nTabOrigin);
  NEW_HOOK(GetCharABCWidthsA,      s_arg2, 0,s_arg1,0, BIG_ENDIAN,    1) // BOOL GetCharABCWidths(HDC hdc, UINT uFirstChar, UINT uLastChar,  LPABC lpabc);
  NEW_HOOK(GetCharABCWidthsFloatA, s_arg2, 0,s_arg1,0, BIG_ENDIAN,    1) // BOOL GetCharABCWidthsFloat(HDC hdc, UINT iFirstChar, UINT iLastChar, LPABCFLOAT lpABCF);
  NEW_HOOK(GetCharWidth32A,        s_arg2, 0,s_arg1,0, BIG_ENDIAN,    1) // BOOL GetCharWidth32(HDC hdc, UINT iFirstChar, UINT iLastChar, LPINT lpBuffer);
  NEW_HOOK(GetCharWidthFloatA,     s_arg2, 0,s_arg1,0, BIG_ENDIAN,    1) // BOOL GetCharWidthFloat(HDC hdc, UINT iFirstChar, UINT iLastChar, PFLOAT pxBuffer);

  NEW_HOOK(GetTextExtentPoint32W,  s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(GetTextExtentExPointW,  s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(GetTabbedTextExtentW,   s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(GetCharacterPlacementW, s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(GetGlyphIndicesW,       s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(GetGlyphOutlineW,       s_arg2, 0,s_arg1,0, USING_UNICODE, 1)
  NEW_HOOK(ExtTextOutW,            s_arg6, 0,s_arg1,0, USING_UNICODE|USING_STRING, 7)
  NEW_HOOK(TextOutW,               s_arg4, 0,s_arg1,0, USING_UNICODE|USING_STRING, 5)
  NEW_HOOK(TabbedTextOutW,         s_arg4, 0,s_arg1,0, USING_UNICODE|USING_STRING, 5)
  NEW_HOOK(GetCharABCWidthsW,      s_arg2, 0,s_arg1,0, USING_UNICODE, 1)
  NEW_HOOK(GetCharABCWidthsFloatW, s_arg2, 0,s_arg1,0, USING_UNICODE, 1)
  NEW_HOOK(GetCharWidth32A,        s_arg2, 0,s_arg1,0, USING_UNICODE, 1)
  NEW_HOOK(GetCharWidthFloatA,     s_arg2, 0,s_arg1,0, USING_UNICODE, 1)

  // user32.dll
  NEW_HOOK(DrawTextA,              s_arg2, 0,s_arg1,0, USING_STRING,  3) // int DrawText(HDC hDC, LPCTSTR lpchText, int nCount, LPRECT lpRect, UINT uFormat);
  NEW_HOOK(DrawTextExA,            s_arg2, 0,s_arg1,0, USING_STRING,  3) // int DrawTextEx(HDC hdc, LPTSTR lpchText,int cchText, LPRECT lprc, UINT dwDTFormat, LPDRAWTEXTPARAMS lpDTParams);
  NEW_HOOK(DrawTextW,              s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)
  NEW_HOOK(DrawTextExW,            s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3)

  DPRINT("leave");
}

// jichi 6/18/2015: GDI+ functions
void PcHooks::hookGDIPlusFunctions()
{
  HMODULE hModule = ::GetModuleHandleA("gdiplus.dll");
  if (!hModule) {
    DPRINT("not loaded");
    return;
  }

  DPRINT("enter");
  enum stack {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    , s_arg2 = 4 * 2 // 0x8
    , s_arg3 = 4 * 3 // 0xc
    , s_arg4 = 4 * 4 // 0x10
    , s_arg5 = 4 * 5 // 0x14
    , s_arg6 = 4 * 6 // 0x18
  };

  // gdiplus.dll
  // https://msdn.microsoft.com/en-us/library/windows/desktop/ms534053%28v=vs.85%29.aspx
  // https://msdn.microsoft.com/en-us/library/windows/desktop/ms534052%28v=vs.85%29.aspx
  // https://msdn.microsoft.com/en-us/library/windows/desktop/ms534039%28v=vs.85%29.aspx
  // Use arg1 pionter to GpGraphics as split
  //using namespace Gdiplus::DllExports;
  // Use arg5 style as split
  NEW_MODULE_HOOK(hModule, GdipAddPathString,           s_arg2, 0,s_arg5,0, USING_UNICODE|USING_STRING, 3) // GpStatus WINGDIPAPI GdipAddPathString(GpPath *path, GDIPCONST WCHAR *string, INT length, GDIPCONST GpFontFamily *family, INT style, REAL emSize, GDIPCONST RectF *layoutRect, GDIPCONST GpStringFormat *format)
  NEW_MODULE_HOOK(hModule, GdipAddPathStringl,          s_arg2, 0,s_arg5,0, USING_UNICODE|USING_STRING, 3) // GpStatus WINGDIPAPI GdipAddPathStringI(GpPath *path, GDIPCONST WCHAR *string, INT length, GDIPCONST GpFontFamily *family, INT style, REAL emSize, GDIPCONST Rect *layoutRect, GDIPCONST GpStringFormat *format)
  //NEW_MODULE_HOOK(hModule, GdipMeasureCharacterRanges,  s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3) // GpStatus WINGDIPAPI GdipMeasureCharacterRanges(GpGraphics *graphics, GDIPCONST WCHAR *string, INT length, GDIPCONST GpFont *font, GDIPCONST RectF &layoutRect, GDIPCONST GpStringFormat *stringFormat, INT regionCount, GpRegion **regions)
  NEW_MODULE_HOOK(hModule, GdipDrawString,              s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3) // GpStatus WINGDIPAPI GdipDrawString(GpGraphics *graphics, GDIPCONST WCHAR *string, INT length, GDIPCONST GpFont *font, GDIPCONST RectF *layoutRect, GDIPCONST GpStringFormat *stringFormat, GDIPCONST GpBrush *brush);
  NEW_MODULE_HOOK(hModule, GdipMeasureString,           s_arg2, 0,s_arg1,0, USING_UNICODE|USING_STRING, 3) // GpStatus WINGDIPAPI GdipMeasureString(GpGraphics *graphics, GDIPCONST WCHAR *string, INT length, GDIPCONST GpFont *font, GDIPCONST RectF *layoutRect, GDIPCONST GpStringFormat *stringFormat, RectF *boundingBox, INT *codepointsFitted, INT *linesFilled )

  DPRINT("leave");
}

/**
 * Copied from Textractor 
 * https://github.com/Artikash/Textractor/blob/aa0c0e0047685b502934fe7ba855b7a7be0a5836/texthook/engine/native/pchooks.cc
 */
void PcHooks::hookOtherPcFunctions()
{
// int TextHook::InitHook(LPVOID addr, DWORD data, DWORD data_ind, DWORD split_off, DWORD split_ind, WORD type, DWORD len_off)

  // http://msdn.microsoft.com/en-us/library/78zh94ax.aspx
  // int WINAPI lstrlen(LPCTSTR lpString);
  // Lstr functions usually extracts rubbish, and might crash certain games like 「Magical Marriage Lunatics!!」
  // Needed by Gift
  // Use arg1 address for both split and data
    NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrlenA, s_arg1, 0, s_arg1, 0, USING_STRING, 0) // 9/8/2013 jichi: int WINAPI lstrlen(LPCTSTR lpString);
        NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrcpyA, s_arg2, 0, 0, 0, USING_STRING, 0)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrcpynA, s_arg2, 0, 0, 0, USING_STRING, 0)

        NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrlenW, s_arg1, 0, s_arg1, 0, USING_UNICODE | USING_STRING, 0) // 9/8/2013 jichi: add lstrlen
        NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrcpyW, s_arg2, 0, 0, 0, USING_UNICODE | USING_STRING, 0)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", lstrcpynW, s_arg2, 0, 0, 0, USING_UNICODE | USING_STRING, 0)

    // size_t strlen(const char *str);
    // size_t strlen_l(const char *str, _locale_t locale);
    // size_t wcslen(const wchar_t *str);
    // size_t wcslen_l(const wchar_t *str, _locale_t locale);
    // size_t _mbslen(const unsigned char *str);
    // size_t _mbslen_l(const unsigned char *str, _locale_t locale);
    // size_t _mbstrlen(const char *str);
    // size_t _mbstrlen_l(const char *str, _locale_t locale);

    // http://msdn.microsoft.com/en-us/library/ex0hs2ad.aspx
    // Needed by 娘姉妹
    //
    // <tchar.h>
    // char *_strinc(const char *current, _locale_t locale);
    // wchar_t *_wcsinc(const wchar_t *current, _locale_t locale);
    // <mbstring.h>
    // unsigned char *_mbsinc(const unsigned char *current);
    // unsigned char *_mbsinc_l(const unsigned char *current, _locale_t locale);
    //_(L"_strinc", _strinc, 4,  0,4,0, USING_STRING, 0) // 12/13/2013 jichi
    //_(L"_wcsinc", _wcsinc, 4,  0,4,0, USING_UNICODE|USING_STRING, 0)

    // 12/1/2013 jichi:
    // AlterEgo
    // http://tieba.baidu.com/p/2736475133
    // http://www.hongfire.com/forum/showthread.php/36807-AGTH-text-extraction-tool-for-games-translation/page355
    //
    // MultiByteToWideChar
    // http://blgames.proboards.com/thread/265
    //
    // WideCharToMultiByte
    // http://www.hongfire.com/forum/showthread.php/36807-AGTH-text-extraction-tool-for-games-translation/page156
    //
    // int MultiByteToWideChar(
    //   _In_       UINT CodePage,
    //   _In_       DWORD dwFlags,
    //   _In_       LPCSTR lpMultiByteStr, // hook here
    //   _In_       int cbMultiByte,
    //   _Out_opt_  LPWSTR lpWideCharStr,
    //   _In_       int cchWideChar
    // );
    // int WideCharToMultiByte(
    //   _In_       UINT CodePage,
    //   _In_       DWORD dwFlags,
    //   _In_       LPCWSTR lpWideCharStr,
    //   _In_       int cchWideChar,
    //   _Out_opt_  LPSTR lpMultiByteStr,
    //   _In_       int cbMultiByte,
    //   _In_opt_   LPCSTR lpDefaultChar,
    //   _Out_opt_  LPBOOL lpUsedDefaultChar
    // );

    // 2/29/2020 Artikash: TODO: Sort out what to do for string comparison functions
    // http://sakuradite.com/topic/159
        NEW_HOOK_DLL_NAME(L"kernel32.dll", MultiByteToWideChar, s_arg3, 0, 4, 0, USING_STRING, s_arg4 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", WideCharToMultiByte, s_arg3, 0, 4, 0, USING_UNICODE | USING_STRING, s_arg4 / arg_sz)

        NEW_HOOK_DLL_NAME(L"kernel32.dll", GetStringTypeA, s_arg3, 0, 0, 0, USING_STRING, s_arg4 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", GetStringTypeExA, s_arg3, 0, 0, 0, USING_STRING, s_arg4 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", FoldStringA, s_arg2, 0, 0, 0, USING_STRING, s_arg3 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", GetStringTypeW, s_arg2, 0, 0, 0, USING_UNICODE | USING_STRING, s_arg3 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", GetStringTypeExW, s_arg3, 0, 0, 0, USING_UNICODE | USING_STRING, s_arg4 / arg_sz)
        NEW_HOOK_DLL_NAME(L"kernel32.dll", FoldStringW, s_arg2, 0, 0, 0, USING_UNICODE | USING_STRING, s_arg3 / arg_sz)

        NEW_HOOK_DLL_NAME(L"user32.dll", CharNextA, s_arg1, 0, 0, 0, USING_STRING | DATA_INDIRECT, 1) // LPTSTR WINAPI CharNext(_In_ LPCTSTR lpsz);
        NEW_HOOK_DLL_NAME(L"user32.dll", CharNextW, s_arg1, 0, 0, 0, USING_UNICODE | DATA_INDIRECT, 1)
        NEW_HOOK_DLL_NAME(L"user32.dll", CharPrevA, s_arg1, 0, 0, 0, USING_STRING | DATA_INDIRECT, 1) // LPTSTR WINAPI CharPrev(_In_ LPCTSTR lpszStart, _In_ LPCTSTR lpszCurrent);
        NEW_HOOK_DLL_NAME(L"user32.dll", CharPrevW, s_arg1, 0, 0, 0, USING_UNICODE | DATA_INDIRECT, 1)
        NEW_HOOK_DLL_NAME(L"user32.dll", CharNextExA, s_arg2, 0, 0, 0, USING_STRING | DATA_INDIRECT, 1) // LPSTR WINAPI CharNextExA(_In_ WORD   CodePage, _In_ LPCSTR lpCurrentChar, _In_ DWORD  dwFlags);
        NEW_HOOK_DLL_NAME(L"user32.dll", CharPrevExA, s_arg2, 0, 0, 0, USING_UNICODE | DATA_INDIRECT, 1)
    if (HMODULE module = GetModuleHandleW(L"OLEAUT32.dll"))
    {
        NEW_MODULE_HOOK(module, SysAllocString, s_arg1, 0, 0, 0, USING_UNICODE | USING_STRING, 0)
            NEW_MODULE_HOOK(module, SysAllocStringLen, s_arg1, 0, 0, 0, USING_UNICODE | USING_STRING | KNOWN_UNSTABLE, s_arg2 / arg_sz)
    }
}

// jichi 10/2/2013
// Note: All functions does not have NO_CONTEXT attribute and will be filtered.
void PcHooks::hookLstrFunctions()
{
  DPRINT("enter");
  // int TextHook::InitHook(LPVOID addr, DWORD data, DWORD data_ind, DWORD split_off, DWORD split_ind, WORD type, DWORD len_off)

  enum stack {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    //, s_arg2 = 4 * 2 // 0x8
    //, s_arg3 = 4 * 3 // 0xc
    //, s_arg4 = 4 * 4 // 0x10
    //, s_arg5 = 4 * 5 // 0x14
    //, s_arg6 = 4 * 6 // 0x18
  };

  // http://msdn.microsoft.com/en-us/library/78zh94ax.aspx
  // int WINAPI lstrlen(LPCTSTR lpString);
  // Lstr functions usually extracts rubbish, and might crash certain games like 「Magical Marriage Lunatics!!」
  // Needed by Gift
  // Use arg1 address for both split and data
  NEW_HOOK(lstrlenA, s_arg1, 0,s_arg1,0, USING_STRING, 0) // 9/8/2013 jichi: int WINAPI lstrlen(LPCTSTR lpString);
  NEW_HOOK(lstrlenW, s_arg1, 0,s_arg1,0, USING_UNICODE|USING_STRING, 0) // 9/8/2013 jichi: add lstrlen

  // size_t strlen(const char *str);
  // size_t strlen_l(const char *str, _locale_t locale);
  // size_t wcslen(const wchar_t *str);
  // size_t wcslen_l(const wchar_t *str, _locale_t locale);
  // size_t _mbslen(const unsigned char *str);
  // size_t _mbslen_l(const unsigned char *str, _locale_t locale);
  // size_t _mbstrlen(const char *str);
  // size_t _mbstrlen_l(const char *str, _locale_t locale);

  // http://msdn.microsoft.com/en-us/library/ex0hs2ad.aspx
  // Needed by 娘姉妹
  //
  // <tchar.h>
  // char *_strinc(const char *current, _locale_t locale);
  // wchar_t *_wcsinc(const wchar_t *current, _locale_t locale);
  // <mbstring.h>
  // unsigned char *_mbsinc(const unsigned char *current);
  // unsigned char *_mbsinc_l(const unsigned char *current, _locale_t locale);
  //_(L"_strinc", _strinc, 4,  0,4,0, USING_STRING, 0) // 12/13/2013 jichi
  //_(L"_wcsinc", _wcsinc, 4,  0,4,0, USING_UNICODE|USING_STRING, 0)
  DPRINT("leave");
}

void PcHooks::hookWcharFunctions()
{
  DPRINT("enter");
  // 12/1/2013 jichi:
  // AlterEgo
  // http://tieba.baidu.com/p/2736475133
  // http://www.hongfire.com/forum/showthread.php/36807-AGTH-text-extraction-tool-for-games-translation/page355
  //
  // MultiByteToWideChar
  // http://blgames.proboards.com/thread/265
  //
  // WideCharToMultiByte
  // http://www.hongfire.com/forum/showthread.php/36807-AGTH-text-extraction-tool-for-games-translation/page156
  //
  // int MultiByteToWideChar(
  //   _In_       UINT CodePage,
  //   _In_       DWORD dwFlags,
  //   _In_       LPCSTR lpMultiByteStr, // hook here
  //   _In_       int cbMultiByte,
  //   _Out_opt_  LPWSTR lpWideCharStr,
  //   _In_       int cchWideChar
  // );
  // int WideCharToMultiByte(
  //   _In_       UINT CodePage,
  //   _In_       DWORD dwFlags,
  //   _In_       LPCWSTR lpWideCharStr,
  //   _In_       int cchWideChar,
  //   _Out_opt_  LPSTR lpMultiByteStr,
  //   _In_       int cbMultiByte,
  //   _In_opt_   LPCSTR lpDefaultChar,
  //   _Out_opt_  LPBOOL lpUsedDefaultChar
  // );

  enum stack {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    //, s_arg2 = 4 * 2 // 0x8
    , s_arg3 = 4 * 3 // 0xc
    //, s_arg4 = 4 * 4 // 0x10
    //, s_arg5 = 4 * 5 // 0x14
    //, s_arg6 = 4 * 6 // 0x18
  };

  // 3/17/2014 jichi: Temporarily disabled
  // http://sakuradite.com/topic/159
  NEW_HOOK(MultiByteToWideChar, s_arg3, 0,4,0, USING_STRING, 4)
  NEW_HOOK(WideCharToMultiByte, s_arg3, 0,4,0, USING_UNICODE|USING_STRING, 4)
  DPRINT("leave");
}

void PcHooks::hookCharNextFunctions()
{
  enum stack {
    s_retaddr = 0
    , s_arg1 = 4 * 1 // 0x4
    , s_arg2 = 4 * 2 // 0x8
    //, s_arg3 = 4 * 3 // 0xc
    //, s_arg4 = 4 * 4 // 0x10
    //, s_arg5 = 4 * 5 // 0x14
    //, s_arg6 = 4 * 6 // 0x18
  };

  DPRINT("enter");
  NEW_HOOK(CharNextA, s_arg1, 0,0,0, USING_STRING|DATA_INDIRECT, 1) // LPTSTR WINAPI CharNext(_In_ LPCTSTR lpsz);
  NEW_HOOK(CharNextW, s_arg1, 0,0,0, USING_UNICODE|DATA_INDIRECT, 1)
  NEW_HOOK(CharPrevA, s_arg1, 0,0,0, USING_STRING|DATA_INDIRECT, 1) // LPTSTR WINAPI CharPrev(_In_ LPCTSTR lpszStart, _In_ LPCTSTR lpszCurrent);
  NEW_HOOK(CharPrevW, s_arg1, 0,0,0, USING_UNICODE|DATA_INDIRECT, 1)
  //NEW_HOOK(CharNextExA, s_arg2, 0,0,0, USING_STRING|DATA_INDIRECT, 1) // LPSTR WINAPI CharNextExA(_In_ WORD   CodePage, _In_ LPCSTR lpCurrentChar, _In_ DWORD  dwFlags);
  //NEW_HOOK(CharNextExW, s_arg2, 0,0,0, USING_UNICODE|DATA_INDIRECT, 1)
  DPRINT("leave");
}

// EOF
