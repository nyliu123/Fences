#include "../Fences.Native/DesktopView.h"
#include "../Fences.Native/DesktopDragDrop.h"
#include <commctrl.h>
#include <propkey.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <iostream>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;
namespace
{
    void Check(bool value, const char* message)
    {
        if (!value) throw std::runtime_error(message);
        std::cout << "PASS: " << message << std::endl;
    }
    void Pump(int milliseconds = 400)
    {
        const auto end = GetTickCount64() + milliseconds;
        do
        {
            MSG message;
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
            Sleep(10);
        } while (GetTickCount64() < end);
    }
    struct View
    {
        HWND host = nullptr, list = nullptr;
        void* context = nullptr;
        bool ownedHost = true;
        View(const wchar_t* folder)
        {
            if (folder)
                host = CreateWindowExW(0, L"STATIC", L"Owned Shell transfer test", WS_OVERLAPPEDWINDOW,
                    40, 40, 1000, 650, nullptr, nullptr, nullptr, nullptr);
            else
            {
                ownedHost = false;
                EnumWindows([](HWND window, LPARAM data) -> BOOL
                {
                    wchar_t name[64]; GetClassNameW(window, name, ARRAYSIZE(name));
                    if (wcscmp(name, L"Progman") && wcscmp(name, L"WorkerW")) return TRUE;
                    const auto child = FindWindowExW(window, nullptr, L"SHELLDLL_DefView", nullptr);
                    if (child) { *reinterpret_cast<HWND*>(data) = child; return FALSE; }
                    return TRUE;
                }, reinterpret_cast<LPARAM>(&host));
            }
            Check(SUCCEEDED(FencesCreateView(host, folder, FALSE, nullptr, 0, &context)), "create owned production Shell view");
            Pump();
            EnumChildWindows(FencesViewWindow(context), [](HWND child, LPARAM data) -> BOOL
            {
                wchar_t name[64]; GetClassNameW(child, name, ARRAYSIZE(name));
                if (!wcscmp(name, WC_LISTVIEWW)) *reinterpret_cast<HWND*>(data) = child;
                return TRUE;
            }, reinterpret_cast<LPARAM>(&list));
            Check(list != nullptr, "find native item list");
            if (ownedHost) { ShowWindow(host, SW_SHOWNOACTIVATE); ShowWindow(FencesViewWindow(context), SW_SHOWNOACTIVATE); Pump(); }
        }
        ~View() { if (context) FencesDestroyView(context); if (host && ownedHost) DestroyWindow(host); }
        ComPtr<IDropTarget> Target()
        {
            ComPtr<IDropTarget> target = static_cast<IDropTarget*>(GetPropW(list, L"OleDropTargetInterface"));
            Check(target != nullptr, "Shell registers OLE drop target while desktop menu flags remain enabled");
            DesktopPreferences prefs = { sizeof(prefs) };
            Check(SUCCEEDED(FencesGetPreferences(context, &prefs)) && (prefs.flags & FWF_DESKTOP), "view retains desktop behavior");
            return target;
        }
        POINT Position(const std::wstring& path)
        {
            wchar_t name[32768]; POINT point = {};
            for (int index = 0; index < FencesVisibleCount(context); ++index)
                if (SUCCEEDED(FencesItemPosition(context, index, name, ARRAYSIZE(name), &point)))
                {
                    if (path == name) return point;
                }
            throw std::runtime_error("fixture missing from view");
        }
    };
    ComPtr<IDataObject> Data(const std::wstring& path)
    {
        ComPtr<IShellItem> item;
        Check(SUCCEEDED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(&item))), "bind owned file");
        ComPtr<IDataObject> data;
        Check(SUCCEEDED(item->BindToHandler(nullptr, BHID_DataObject, IID_PPV_ARGS(&data))), "obtain real Shell data object");
        return data;
    }
    void Move(View& view, const std::wstring& source, const std::wstring& destination, IDataObject* sourceData = nullptr)
    {
        auto target = view.Target(); ComPtr<IDataObject> data = sourceData;
        if (!data) data = Data(source);
        POINT point = { 700, 450 }; ClientToScreen(view.list, &point);
        DWORD effect = DROPEFFECT_MOVE | DROPEFFECT_COPY | DROPEFFECT_LINK;
        Check(SUCCEEDED(target->DragEnter(data.Get(), MK_SHIFT | MK_LBUTTON, { point.x, point.y }, &effect)) && (effect & DROPEFFECT_MOVE), "native background accepts incoming move");
        Pump(100);
        effect = DROPEFFECT_MOVE | DROPEFFECT_COPY | DROPEFFECT_LINK;
        Check(SUCCEEDED(target->DragOver(MK_SHIFT | MK_LBUTTON, { point.x, point.y }, &effect)) && (effect & DROPEFFECT_MOVE), "native background retains move feedback");
        effect = DROPEFFECT_MOVE | DROPEFFECT_COPY | DROPEFFECT_LINK;
        Check(SUCCEEDED(target->Drop(data.Get(), MK_SHIFT, { point.x, point.y }, &effect)), "native Shell drop completes");
        Pump(800);
        Check(GetFileAttributesW(source.c_str()) == INVALID_FILE_ATTRIBUTES && GetFileAttributesW(destination.c_str()) != INVALID_FILE_ATTRIBUTES,
            "move changes the actual path without leaving a duplicate");
    }
    void Write(const std::wstring& path)
    {
        auto file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        Check(file != INVALID_HANDLE_VALUE, "create owned fixture"); CloseHandle(file);
    }
    ComPtr<IDataObject> SourceData(const std::wstring& folder, const std::wstring& path)
    {
        auto host = CreateWindowExW(0, L"STATIC", L"Owned Shell source", WS_OVERLAPPEDWINDOW,
            40, 40, 700, 500, nullptr, nullptr, nullptr, nullptr);
        ComPtr<IExplorerBrowser> browser;
        Check(SUCCEEDED(CoCreateInstance(CLSID_ExplorerBrowser, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&browser))), "create independent desktop-mode drag source");
        RECT bounds = { 0, 0, 700, 500 };
        FOLDERSETTINGS settings = { FVM_ICON, FWF_DESKTOP | FWF_AUTOARRANGE | FWF_NOBROWSERVIEWSTATE };
        Check(SUCCEEDED(browser->Initialize(host, &bounds, &settings)), "initialize real Shell source");
        PIDLIST_ABSOLUTE location = nullptr;
        SHParseDisplayName(folder.c_str(), nullptr, &location, 0, nullptr);
        browser->BrowseToIDList(location, SBSP_ABSOLUTE); CoTaskMemFree(location); Pump(800);
        ComPtr<IFolderView2> view;
        Check(SUCCEEDED(browser->GetCurrentView(IID_PPV_ARGS(&view))), "obtain real source selection interface");
        ComPtr<IDataObject> data; DWORD allowed = 0;
        const auto empty = GetDesktopDragData(view.Get(), &data, &allowed);
        Check(empty != S_OK && !data, "empty selection never starts a drag");
        int count = 0, selected = -1; view->ItemCount(SVGIO_ALLVIEW, &count);
        for (int index = 0; index < count; ++index)
        {
            ComPtr<IShellItem> item; PWSTR name = nullptr;
            if (SUCCEEDED(view->GetItem(index, IID_PPV_ARGS(&item))) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &name)))
            {
                if (path == name) selected = index;
                CoTaskMemFree(name);
            }
        }
        Check(selected >= 0 && SUCCEEDED(view->SelectItem(selected, SVSI_SELECT | SVSI_FOCUSED)), "select owned outgoing fixture"); Pump();
        Check(GetDesktopDragData(view.Get(), &data, &allowed) == S_OK && (allowed & DROPEFFECT_MOVE), "production drag source exports native data and permitted move effect");
        FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL }; STGMEDIUM medium = {};
        Check(SUCCEEDED(data->GetData(&format, &medium)), "outgoing native data supplies filesystem paths");
        wchar_t name[32768]; DragQueryFileW(reinterpret_cast<HDROP>(medium.hGlobal), 0, name, ARRAYSIZE(name));
        ReleaseStgMedium(&medium);
        Check(path == name, "outgoing data preserves exact original path");
        view.Reset(); browser->Destroy(); browser.Reset(); DestroyWindow(host);
        return data;
    }
}
int wmain()
{
    if (FAILED(OleInitialize(nullptr))) return 2;
    InitCommonControls();
    wchar_t temp[MAX_PATH]; GetTempPathW(ARRAYSIZE(temp), temp);
    GUID id; CoCreateGuid(&id); wchar_t guid[40]; StringFromGUID2(id, guid, ARRAYSIZE(guid));
    const std::wstring root = std::wstring(temp) + L"Fences-transfer-" + guid;
    const auto desktop = root + L"\\desktop", external = root + L"\\external";
    CreateDirectoryW(root.c_str(), nullptr); CreateDirectoryW(desktop.c_str(), nullptr); CreateDirectoryW(external.c_str(), nullptr);
    int result = 0;
    try
    {
        const auto file = L"\\round.trip.txt";
        Write(external + file);
        {
            View view(desktop.c_str());
            Move(view, external + file, desktop + file);
            auto overlay = CreateWindowExW(WS_EX_TRANSPARENT, L"STATIC", L"Owned caption layer", WS_CHILD,
                0, 0, 200, 200, FencesViewWindow(view.context), nullptr, nullptr, nullptr);
            Check(SUCCEEDED(FencesSetCaptionWindow(view.context, overlay)) && GetPropW(overlay, L"OleDropTargetInterface"),
                "caption layer forwards OLE drops to the native Shell target");
            Check(SUCCEEDED(FencesSetCaptionWindow(view.context, nullptr)) && !GetPropW(overlay, L"OleDropTargetInterface"),
                "detaching caption layer revokes its owned OLE registration");
            DestroyWindow(overlay);
            DesktopPreferences prefs = { sizeof(prefs) }; FencesGetPreferences(view.context, &prefs);
            prefs.sortFormat = PKEY_ItemNameDisplay.fmtid; prefs.sortProperty = PKEY_ItemNameDisplay.pid; prefs.sortDirection = SORT_DESCENDING;
            Check(SUCCEEDED(FencesSetPreferences(view.context, &prefs)), "select descending name sort"); Pump();
            ListView_SetItemState(view.list, 0, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
            Write(desktop + L"\\zzz.new.txt"); Pump(3500);
            const auto first = view.Position(desktop + L"\\zzz.new.txt"), second = view.Position(desktop + file);
            Check(first.x < second.x || (first.x == second.x && first.y < second.y), "new arrival participates in selected descending sort");
            wchar_t selectedName[32768]; POINT selectedPoint = {};
            const auto selectedIndex = ListView_GetNextItem(view.list, -1, LVNI_SELECTED);
            Check(selectedIndex >= 0 && SUCCEEDED(FencesItemPosition(view.context, selectedIndex, selectedName, ARRAYSIZE(selectedName), &selectedPoint)) &&
                desktop + file == selectedName, "automatic arrival sort preserves the previously selected file");
            prefs.sortDirection = SORT_ASCENDING; FencesSetPreferences(view.context, &prefs); Pump();
            Write(desktop + L"\\aaa.new.txt"); Pump(3500);
            const auto ascending = view.Position(desktop + L"\\aaa.new.txt"), middle = view.Position(desktop + file);
            Check(ascending.x < middle.x || (ascending.x == middle.x && ascending.y < middle.y), "new arrival participates in selected ascending sort");
            prefs.flags &= ~FWF_AUTOARRANGE; FencesSetPreferences(view.context, &prefs); Pump();
            const auto manual = view.Position(desktop + file);
            Write(desktop + L"\\manual.new.txt"); Pump(3500);
            const auto retained = view.Position(desktop + file);
            Check(manual.x == retained.x && manual.y == retained.y, "new arrivals preserve existing positions when automatic arrangement is disabled");
        }
        {
            auto data = SourceData(desktop, desktop + file);
            View view(external.c_str()); Move(view, desktop + file, external + file, data.Get());
        }
        // The actual desktop is only read. Hovering an owned file over its bin
        // must advertise Move; do not drop or modify any existing bin entries.
        {
            View view(nullptr); auto target = view.Target(); auto data = Data(external + file);
            const auto bin = view.Position(L"::{645FF040-5081-101B-9F08-00AA002F954E}");
            POINT point = { bin.x + 16, bin.y + 16 }; ClientToScreen(view.list, &point);
            DWORD effect = DROPEFFECT_COPY | DROPEFFECT_MOVE | DROPEFFECT_LINK;
            Check(SUCCEEDED(target->DragEnter(data.Get(), MK_LBUTTON, { point.x, point.y }, &effect)) && (effect & DROPEFFECT_MOVE), "Recycle Bin advertises native recycling for an owned file");
            target->DragLeave();
        }
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << std::endl; result = 1; }
    for (const auto& path : { desktop + L"\\round.trip.txt", external + L"\\round.trip.txt", desktop + L"\\zzz.new.txt", desktop + L"\\aaa.new.txt", desktop + L"\\manual.new.txt" }) DeleteFileW(path.c_str());
    RemoveDirectoryW(desktop.c_str()); RemoveDirectoryW(external.c_str()); RemoveDirectoryW(root.c_str());
    OleUninitialize(); return result;
}
