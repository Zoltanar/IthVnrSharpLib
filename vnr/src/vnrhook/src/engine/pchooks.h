#pragma once

#include <string>
// pchooks.h
// 8/1/2014 jichi

namespace PcHooks {

void hookGDIFunctions();
void hookGDIPlusFunctions();
void hookOtherPcFunctions();
void hookLstrFunctions();
void hookWcharFunctions();
void hookCharNextFunctions();

} // namespace PcHooks

// EOF
