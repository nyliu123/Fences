#pragma once
#include "DesktopView.h"
#include <wrl/client.h>
#include <string>

// Holds the exact Shell identity while a top-level editor owns keyboard focus.
// A view index alone is unsafe if other files arrive during editing.
class DesktopRenameTarget
{
    Microsoft::WRL::ComPtr<IShellFolder> folder;
    PITEMID_CHILD child = nullptr;
    PITEMID_CHILD renamedChild = nullptr;
    bool filesystem = false;
    std::wstring path;
    LONG references = 1;
public:
    std::wstring name;
    bool file = false;
    RECT bounds = {};
    ~DesktopRenameTarget() { CoTaskMemFree(child); CoTaskMemFree(renamedChild); }
    void AddRef() { InterlockedIncrement(&references); }
    void Release() { if (!InterlockedDecrement(&references)) delete this; }
    HRESULT Open(IFolderView2* view, int index)
    {
        auto result = view->GetFolder(IID_PPV_ARGS(&folder));
        if (SUCCEEDED(result)) result = view->Item(index, &child);
        if (FAILED(result)) return result;
        PCUITEMID_CHILD children[] = { child };
        SFGAOF attributes = SFGAO_CANRENAME | SFGAO_FILESYSTEM | SFGAO_FOLDER;
        result = folder->GetAttributesOf(1, children, &attributes);
        if (FAILED(result) || !(attributes & SFGAO_CANRENAME)) return E_ACCESSDENIED;
        filesystem = (attributes & SFGAO_FILESYSTEM) != 0;
        file = filesystem && !(attributes & SFGAO_FOLDER);
        Microsoft::WRL::ComPtr<IShellItem> item;
        result = SHCreateItemWithParent(nullptr, folder.Get(), child, IID_PPV_ARGS(&item));
        PWSTR text = nullptr;
        if (SUCCEEDED(result)) result = item->GetDisplayName(SIGDN_PARENTRELATIVEEDITING, &text);
        if (SUCCEEDED(result)) name = text;
        CoTaskMemFree(text);
        text = nullptr;
        if (filesystem && item && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &text))) path = text;
        CoTaskMemFree(text); return result;
    }
    bool Matches(PCUITEMID_CHILD candidate) const
    {
        if (!child) return false;
        if (ILIsEqual(child, candidate) || (renamedChild && ILIsEqual(renamedChild, candidate))) return true;
        const auto compared = folder->CompareIDs(SHCIDS_CANONICALONLY, child, candidate);
        return SUCCEEDED(compared) && static_cast<short>(HRESULT_CODE(compared)) == 0;
    }
    HRESULT Commit(HWND owner, const wchar_t* value)
    {
        if (!value || !*value || (filesystem && (wcschr(value, L'\\') || wcschr(value, L'/')))) return E_INVALIDARG;
        if (name == value) return S_OK;
        const auto separator = path.find_last_of(L"\\/");
        // For ordinary filesystem editing names, reject an existing sibling
        // before Shell opens a collision dialog or pumps owner focus messages.
        // Alias/hidden-extension names remain delegated to the Shell contract.
        if (separator != std::wstring::npos && _wcsicmp(path.c_str() + separator + 1, name.c_str()) == 0)
        {
            const auto destination = path.substr(0, separator + 1) + value;
            if (_wcsicmp(destination.c_str(), path.c_str()) != 0 && GetFileAttributesW(destination.c_str()) != INVALID_FILE_ATTRIBUTES)
                return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
        }
        PITEMID_CHILD renamed = nullptr;
        const auto result = folder->SetNameOf(owner, child, value, SHGDN_INFOLDER, &renamed);
        if (SUCCEEDED(result)) { CoTaskMemFree(renamedChild); renamedChild = renamed; }
        else CoTaskMemFree(renamed);
        return result;
    }
};
