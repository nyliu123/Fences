#include "DesktopSource.h"
#include <exdisp.h>
#include <shlguid.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;
namespace
{
    bool Equal(const std::wstring& first, const std::wstring& second)
    {
        return CompareStringOrdinal(first.c_str(), -1, second.c_str(), -1, TRUE) == CSTR_EQUAL;
    }
}
bool DesktopSource::Allows(IShellItem* item, const std::wstring& path) const
{
    // Real items in either Desktop folder must appear even before Explorer's
    // asynchronous enumeration catches up after a create/paste/rename.
    const auto separator = path.find_last_of(L"\\/");
    if (separator != std::wstring::npos)
        for (const auto& folder : folders) if (Equal(path.substr(0, separator), folder)) return true;
    PWSTR name = nullptr;
    if (FAILED(item->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &name))) return false;
    bool visible = false;
    for (const auto& original : items) if (Equal(original, name)) { visible = true; break; }
    CoTaskMemFree(name); return visible;
}
HRESULT ReadDesktopSource(HWND expectedHost, DesktopSource& result)
{
    ComPtr<IShellWindows> windows;
    auto status = CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&windows));
    if (FAILED(status)) return status;
    VARIANT empty; VariantInit(&empty);
    long handle = 0;
    ComPtr<IDispatch> dispatch;
    status = windows->FindWindowSW(&empty, &empty, SWC_DESKTOP, &handle, SWFO_NEEDDISPATCH, &dispatch);
    if (FAILED(status) || !dispatch) return FAILED(status) ? status : E_NOINTERFACE;
    ComPtr<IServiceProvider> services; status = dispatch.As(&services);
    if (FAILED(status)) return status;
    ComPtr<IShellBrowser> browser;
    status = services->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser));
    if (FAILED(status)) return status;
    ComPtr<IShellView> shell; status = browser->QueryActiveShellView(&shell);
    if (FAILED(status)) return status;
    HWND host = nullptr; shell->GetWindow(&host);
    if (expectedHost && expectedHost != host) return E_HANDLE;
    ComPtr<IFolderView2> view; status = shell.As(&view);
    if (FAILED(status)) return status;
    DesktopSource snapshot;
    FOLDERVIEWMODE mode;
    status = view->GetViewModeAndIconSize(&mode, &snapshot.iconSize);
    if (FAILED(status)) return status;
    status = view->GetSpacing(&snapshot.spacing);
    if (FAILED(status)) return status;
    if (snapshot.iconSize < 16 || snapshot.iconSize > 256 || snapshot.spacing.x <= 0 || snapshot.spacing.y <= 0 ||
        snapshot.spacing.x > 32767 || snapshot.spacing.y > 32767) return E_INVALIDARG;
    ComPtr<IShellFolder> folder; status = view->GetFolder(IID_PPV_ARGS(&folder));
    if (FAILED(status)) return status;
    int count = 0; status = view->ItemCount(SVGIO_ALLVIEW, &count);
    if (FAILED(status)) return status;
    for (int index = 0; index < count; ++index)
    {
        PITEMID_CHILD child = nullptr; status = view->Item(index, &child);
        if (FAILED(status)) return status;
        ComPtr<IShellItem> item; status = SHCreateItemWithParent(nullptr, folder.Get(), child, IID_PPV_ARGS(&item));
        CoTaskMemFree(child);
        if (FAILED(status)) return status;
        PWSTR name = nullptr; status = item->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &name);
        if (FAILED(status)) return status;
        snapshot.items.emplace_back(name); CoTaskMemFree(name);
    }
    for (const auto& id : { FOLDERID_Desktop, FOLDERID_PublicDesktop })
    {
        PWSTR path = nullptr;
        if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &path)))
        {
            std::wstring value(path); CoTaskMemFree(path);
            while (value.size() > 3 && value.back() == L'\\') value.pop_back();
            snapshot.folders.push_back(value);
        }
    }
    result = std::move(snapshot); return S_OK;
}
