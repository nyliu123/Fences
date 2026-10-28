#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <propkey.h>
#include <propsys.h>
#include <wrl/client.h>
#include <string>

using Microsoft::WRL::ComPtr;

// Operates only on the exact caller-created fixture. No existing bin entry is
// deleted, restored or enumerated through a user-visible Explorer window.
extern "C" __declspec(dllexport) HRESULT __cdecl RecycleRestoreFixture(HWND list, const wchar_t* path)
{
    if (!IsWindow(list) || !path || !*path) return E_INVALIDARG;
    const std::wstring source(path);
    const auto separator = source.find_last_of(L"\\/");
    if (separator == std::wstring::npos || GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) return E_INVALIDARG;
    PROPERTYKEY deletedFrom = {};
    const auto property = PSGetPropertyKeyFromName(L"System.Recycle.DeletedFrom", &deletedFrom);
    if (FAILED(property)) return property;
    auto from = source; from.push_back(L'\0');
    SHFILEOPSTRUCTW recycle = {};
    recycle.hwnd = list; recycle.wFunc = FO_DELETE; recycle.pFrom = from.c_str();
    recycle.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT;
    const auto deleted = SHFileOperationW(&recycle);
    if (deleted || recycle.fAnyOperationsAborted) return HRESULT_FROM_WIN32(deleted ? deleted : ERROR_CANCELLED);
    PIDLIST_ABSOLUTE binIdentity = nullptr;
    auto result = SHGetKnownFolderIDList(FOLDERID_RecycleBinFolder, 0, nullptr, &binIdentity);
    if (FAILED(result)) return result;
    ComPtr<IShellFolder> desktop, bin;
    result = SHGetDesktopFolder(&desktop);
    if (SUCCEEDED(result)) result = desktop->BindToObject(binIdentity, nullptr, IID_PPV_ARGS(&bin));
    ComPtr<IEnumIDList> entries;
    if (SUCCEEDED(result)) result = bin->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, &entries);
    if (SUCCEEDED(result) && entries)
    {
        result = HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        PITEMID_CHILD child = nullptr;
        while (entries->Next(1, &child, nullptr) == S_OK)
        {
            ComPtr<IShellItem2> item; PWSTR originalFolder = nullptr, originalName = nullptr;
            bool matches = false;
            if (SUCCEEDED(SHCreateItemWithParent(binIdentity, bin.Get(), child, IID_PPV_ARGS(&item))) &&
                SUCCEEDED(item->GetString(deletedFrom, &originalFolder)) &&
                SUCCEEDED(item->GetString(PKEY_ItemNameDisplay, &originalName)))
                matches = !_wcsicmp(originalFolder, source.substr(0, separator).c_str()) && !_wcsicmp(originalName, source.c_str() + separator + 1);
            CoTaskMemFree(originalFolder); CoTaskMemFree(originalName);
            if (matches)
            {
                ComPtr<IDataObject> data;
                PCUITEMID_CHILD selection[] = { child };
                result = bin->GetUIObjectOf(list, 1, selection, IID_IDataObject, nullptr, reinterpret_cast<void**>(data.GetAddressOf()));
                ComPtr<IDropTarget> target = static_cast<IDropTarget*>(GetPropW(list, L"OleDropTargetInterface"));
                if (SUCCEEDED(result) && target)
                {
                    POINT point = { 350, 350 }; ClientToScreen(list, &point);
                    DWORD effect = DROPEFFECT_MOVE;
                    result = target->DragEnter(data.Get(), MK_LBUTTON, { point.x, point.y }, &effect);
                    if (SUCCEEDED(result))
                    {
                        effect = DROPEFFECT_MOVE;
                        result = target->DragOver(MK_LBUTTON, { point.x, point.y }, &effect);
                        if (SUCCEEDED(result))
                        {
                            effect = DROPEFFECT_MOVE;
                            result = target->Drop(data.Get(), 0, { point.x, point.y }, &effect);
                        }
                        else target->DragLeave();
                    }
                }
                else if (!target) result = E_NOINTERFACE;
                const auto deadline = GetTickCount64() + 3000;
                while (SUCCEEDED(result) && GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES && GetTickCount64() < deadline)
                {
                    MSG message;
                    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
                    Sleep(10);
                }
                // Recover our fixture if the tested drop failed; never leave
                // synthetic entries behind in the user's real Recycle Bin.
                if (GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES)
                {
                    if (SUCCEEDED(result)) result = E_FAIL;
                    ComPtr<IContextMenu> menu;
                    if (SUCCEEDED(bin->GetUIObjectOf(list, 1, selection, IID_IContextMenu, nullptr, reinterpret_cast<void**>(menu.GetAddressOf()))))
                    {
                        CMINVOKECOMMANDINFO invoke = { sizeof(invoke) }; invoke.hwnd = list; invoke.lpVerb = "undelete"; invoke.nShow = SW_HIDE;
                        menu->InvokeCommand(&invoke);
                    }
                }
                CoTaskMemFree(child); break;
            }
            CoTaskMemFree(child); child = nullptr;
        }
    }
    CoTaskMemFree(binIdentity); return result;
}
