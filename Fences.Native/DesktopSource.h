#pragma once
#include "DesktopView.h"
#include <string>
#include <vector>

// Read-only snapshot of Explorer's actual desktop, never of the broader Desktop namespace.
struct DesktopSource
{
    int iconSize = 0;
    POINT spacing = {};
    std::vector<std::wstring> items;
    std::vector<std::wstring> folders;
    bool Allows(IShellItem* item, const std::wstring& path) const;
};
HRESULT ReadDesktopSource(HWND expectedHost, DesktopSource& result);
