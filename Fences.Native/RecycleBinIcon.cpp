#include "RecycleBinIcon.h"
#include "DesktopSelection.h"
#include <shlwapi.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace
{
    constexpr auto binClass = L"{645FF040-5081-101B-9F08-00AA002F954E}";
    std::wstring IconLocation(bool full)
    {
        const auto value = full ? L"full" : L"empty";
        const std::wstring explorer = std::wstring(L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\") + binClass + L"\\DefaultIcon";
        const std::wstring classes = std::wstring(L"Software\\Classes\\CLSID\\") + binClass + L"\\DefaultIcon";
        const std::wstring shared = std::wstring(L"CLSID\\") + binClass + L"\\DefaultIcon";
        const HKEY roots[] = { HKEY_CURRENT_USER, HKEY_CURRENT_USER, HKEY_CLASSES_ROOT };
        const wchar_t* keys[] = { explorer.c_str(), classes.c_str(), shared.c_str() };
        for (int i = 0; i < 3; ++i)
        {
            wchar_t path[32768] = {}; DWORD bytes = sizeof(path);
            if (RegGetValueW(roots[i], keys[i], value, RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ, nullptr, path, &bytes) == ERROR_SUCCESS && *path)
                return path;
        }
        return {};
    }
}

RecycleBinIcon::~RecycleBinIcon()
{
    if (icon) DestroyIcon(icon);
    CoTaskMemFree(identity);
}
HRESULT RecycleBinIcon::Open()
{
    return SHGetKnownFolderIDList(FOLDERID_RecycleBinFolder, 0, nullptr, &identity);
}
void RecycleBinIcon::Locate(IFolderView2* view)
{
    index = -1;
    if (!view || !identity) return;
    int count = 0; view->ItemCount(SVGIO_ALLVIEW, &count);
    ComPtr<IShellFolder> folder;
    view->GetFolder(IID_PPV_ARGS(&folder));
    for (int i = 0; i < count; ++i)
    {
        PITEMID_CHILD child = nullptr;
        if (FAILED(view->Item(i, &child))) continue;
        bool match = ILIsEqual(child, identity) != FALSE;
        if (!match && folder)
        {
            ComPtr<IShellItem> item;
            PWSTR name = nullptr;
            if (SUCCEEDED(SHCreateItemWithParent(nullptr, folder.Get(), child, IID_PPV_ARGS(&item))) &&
                SUCCEEDED(item->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &name)))
                match = _wcsicmp(name, L"::{645FF040-5081-101B-9F08-00AA002F954E}") == 0;
            CoTaskMemFree(name);
        }
        CoTaskMemFree(child);
        if (match) { index = i; return; }
    }
}
HRESULT RecycleBinIcon::Refresh(HWND list, bool force)
{
    if (index < 0 || !list) return S_FALSE;
    SHQUERYRBINFO info = { sizeof(info) };
    auto result = SHQueryRecycleBinW(nullptr, &info);
    if (FAILED(result)) return result;
    int width = 0, height = 0;
    ImageList_GetIconSize(ListView_GetImageList(list, LVSIL_NORMAL), &width, &height);
    if (width < 1) return E_FAIL;
    const bool nextFull = info.i64NumItems != 0;
    if (icon && !force && full == nextFull && size == width) return S_FALSE;
    auto path = IconLocation(nextFull);
    int resource = 0;
    if (!path.empty()) resource = PathParseIconLocationW(&path[0]);
    else
    {
        ComPtr<IShellItem> item; ComPtr<IExtractIconW> extract;
        result = SHCreateItemFromIDList(identity, IID_PPV_ARGS(&item));
        if (SUCCEEDED(result)) result = item->BindToHandler(nullptr, BHID_SFUIObject, IID_PPV_ARGS(&extract));
        wchar_t location[32768] = {}; UINT flags = 0;
        if (SUCCEEDED(result)) result = extract->GetIconLocation(GIL_FORSHELL, location, ARRAYSIZE(location), &resource, &flags);
        if (FAILED(result) || (flags & GIL_NOTFILENAME)) return E_FAIL;
        path = location;
    }
    HICON next = nullptr;
    result = SHDefExtractIconW(path.c_str(), resource, 0, &next, nullptr, MAKELONG(width, width));
    if (FAILED(result) || !next) return FAILED(result) ? result : E_FAIL;
    if (icon) DestroyIcon(icon);
    icon = next; size = width; full = nextFull; return S_OK;
}
RECT RecycleBinIcon::Bounds(HWND list) const
{
    RECT bounds = {};
    if (index >= 0 && list) ListView_GetItemRect(list, index, &bounds, LVIR_ICON);
    return bounds;
}
void RecycleBinIcon::Draw(HWND list, HDC dc, const RECT& update)
{
    if (!icon || index < 0) return;
    const auto bounds = Bounds(list);
    RECT visible;
    if (!IntersectRect(&visible, &bounds, &update)) return;
    const int saved = SaveDC(dc);
    IntersectClipRect(dc, bounds.left, bounds.top, bounds.right, bounds.bottom);
    DrawIconEx(dc, bounds.left + (bounds.right - bounds.left - size) / 2, bounds.top, icon, size, size, 0, nullptr, DI_NORMAL);
    RestoreDC(dc, saved);
}
