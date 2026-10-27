#pragma once
#include <windows.h>
#include <shlobj.h>
#include <filesystem>
#include <functional>

namespace MenuResearch
{
    int Run(HWND owner, IFolderView2* filteredView, IShellView* desktopView,
        const std::filesystem::path& fixture, const std::filesystem::path& output,
        const std::function<void(HWND, const std::filesystem::path&)>& capture,
        const std::function<void(unsigned)>& pump);
    int RunCommandHelper(const std::filesystem::path& marker);
}
