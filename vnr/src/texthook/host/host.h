#pragma once

// host.h
// 8/23/2013 jichi
// Branch: ITH/IHF.h, rev 105

//#include "host/settings.h"
#include "config.h"
#include "host/hookman.h"
#include <psapi.h>

struct Settings;
struct HookParam;

extern "C" IHFSERVICE void IHFAPI Host_Init();
extern "C" IHFSERVICE void IHFAPI Host_Destroy();

extern "C" IHFSERVICE DWORD IHFAPI Host_Start();
extern "C" IHFSERVICE BOOL IHFAPI Host_Open();
extern "C" IHFSERVICE bool IHFAPI Host_Close();
extern "C" IHFSERVICE DWORD IHFAPI Host_GetHookManager(HookManager **hookman);
extern "C" IHFSERVICE bool IHFAPI Host_GetSettings(Settings **settings);
extern "C" IHFSERVICE DWORD IHFAPI Host_GetPIDByName(LPCWSTR pwcTarget);
extern "C" IHFSERVICE bool IHFAPI Host_InjectByPID(DWORD pid);
extern "C" IHFSERVICE bool IHFAPI Host_ActiveDetachProcess(DWORD pid);
extern "C" IHFSERVICE bool IHFAPI Host_HijackProcess(DWORD pid);
extern "C" IHFSERVICE DWORD IHFAPI Host_InsertHook(DWORD pid, HookParam *hp, LPCSTR name = nullptr);
extern "C" IHFSERVICE DWORD IHFAPI Host_ModifyHook(DWORD pid, HookParam *hp);
extern "C" IHFSERVICE DWORD IHFAPI Host_RemoveHook(DWORD pid, DWORD addr);
extern "C" IHFSERVICE DWORD IHFAPI Host_AddLink(DWORD from, DWORD to);
extern "C" IHFSERVICE DWORD IHFAPI Host_UnLink(DWORD from);
extern "C" IHFSERVICE DWORD IHFAPI Host_UnLinkAll(DWORD from);
extern "C" IHFSERVICE bool IHFAPI IsUnicodeHook(const ProcessRecord& pr, DWORD hook);

// EOF
