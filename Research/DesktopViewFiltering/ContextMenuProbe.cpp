#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include "ContextMenuProbe.h"
#include <commctrl.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdio>
#include <exception>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace MenuResearch
{
    using Microsoft::WRL::ComPtr;
    namespace fs = std::filesystem;
    constexpr UINT firstCommand = 0x1000;
    static void Require(HRESULT result)
    {
        if (FAILED(result))
        {
            char message[96];
            std::snprintf(message, sizeof(message), "Context menu operation failed: 0x%08lX", result);
            throw std::runtime_error(message);
        }
    }
    static void Check(bool result, const char* message, int& assertions)
    {
        if (!result) throw std::runtime_error(message);
        ++assertions;
        std::printf("PASS: %s\n", message);
        std::fflush(stdout);
    }
    static std::string Utf8(const std::wstring& text)
    {
        const auto length = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        std::string result(length, '\0');
        if (length) WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), length, nullptr, nullptr);
        return result;
    }
    struct Row
    {
        UINT id;
        std::wstring label;
        std::wstring verb;
        bool enabled;
        int depth;
    };
    class Menu
    {
        ComPtr<IContextMenu> context;
        ComPtr<IContextMenu2> context2;
        ComPtr<IContextMenu3> context3;
        HMENU handle = nullptr;
        const UINT commandBase;
        std::vector<HMENU> submenus;
        struct Router { Menu* first; Menu* second; };
        bool Recognizes(UINT message, WPARAM wParam, LPARAM lParam) const
        {
            if (message == WM_INITMENUPOPUP || message == WM_MENUCHAR)
            {
                const auto menu = reinterpret_cast<HMENU>(message == WM_INITMENUPOPUP ? wParam : lParam);
                return menu == handle || std::find(submenus.begin(), submenus.end(), menu) != submenus.end();
            }
            UINT command = 0;
            if (message == WM_MEASUREITEM) command = reinterpret_cast<MEASUREITEMSTRUCT*>(lParam)->itemID;
            if (message == WM_DRAWITEM) command = reinterpret_cast<DRAWITEMSTRUCT*>(lParam)->itemID;
            return command >= commandBase && command < commandBase + 0x3000;
        }
        static LRESULT CALLBACK Route(HWND window, UINT message, WPARAM wParam, LPARAM lParam,
            UINT_PTR, DWORD_PTR data)
        {
            auto router = reinterpret_cast<Router*>(data);
            if (message == WM_INITMENUPOPUP || message == WM_DRAWITEM || message == WM_MEASUREITEM || message == WM_MENUCHAR)
            {
                auto self = router->first;
                if (router->second && router->second->Recognizes(message, wParam, lParam)) self = router->second;
                else if (!self->Recognizes(message, wParam, lParam)) return DefSubclassProc(window, message, wParam, lParam);
                LRESULT result = 0;
                if (self->context3 && SUCCEEDED(self->context3->HandleMenuMsg2(message, wParam, lParam, &result))) return result;
                if (self->context2 && SUCCEEDED(self->context2->HandleMenuMsg(message, wParam, lParam))) return 0;
            }
            return DefSubclassProc(window, message, wParam, lParam);
        }
        void Enumerate(HMENU menu, int depth, std::vector<Row>& result)
        {
            for (int index = 0; index < GetMenuItemCount(menu); ++index)
            {
                wchar_t text[1024] = {};
                MENUITEMINFOW info = { sizeof(info) };
                info.fMask = MIIM_ID | MIIM_STRING | MIIM_FTYPE | MIIM_SUBMENU | MIIM_STATE;
                info.dwTypeData = text; info.cch = ARRAYSIZE(text);
                if (!GetMenuItemInfoW(menu, index, TRUE, &info) || (info.fType & MFT_SEPARATOR)) continue;
                wchar_t verb[512] = {};
                if (info.wID >= commandBase && info.wID < commandBase + 0x3000)
                    context->GetCommandString(info.wID - commandBase, GCS_VERBW, nullptr, reinterpret_cast<LPSTR>(verb), ARRAYSIZE(verb));
                result.push_back({ info.wID, text, verb, !(info.fState & (MFS_GRAYED | MFS_DISABLED)), depth });
                if (info.hSubMenu && depth < 2)
                {
                    if (std::find(submenus.begin(), submenus.end(), info.hSubMenu) == submenus.end()) submenus.push_back(info.hSubMenu);
                    LRESULT ignored;
                    if (context3) context3->HandleMenuMsg2(WM_INITMENUPOPUP, reinterpret_cast<WPARAM>(info.hSubMenu), MAKELPARAM(index, FALSE), &ignored);
                    else if (context2) context2->HandleMenuMsg(WM_INITMENUPOPUP, reinterpret_cast<WPARAM>(info.hSubMenu), MAKELPARAM(index, FALSE));
                    Enumerate(info.hSubMenu, depth + 1, result);
                }
            }
        }
    public:
        explicit Menu(IContextMenu* value, UINT base = firstCommand) : context(value), commandBase(base)
        {
            context.As(&context2); context.As(&context3);
            handle = CreatePopupMenu();
            if (!handle) throw std::runtime_error("Cannot create native popup menu");
            const auto result = context->QueryContextMenu(handle, 0, commandBase, commandBase + 0x2fff, CMF_NORMAL | CMF_CANRENAME);
            if (FAILED(result)) { DestroyMenu(handle); handle = nullptr; Require(result); }
        }
        ~Menu() { if (handle) DestroyMenu(handle); }
        HMENU Handle() const { return handle; }
        HMENU Submenu(UINT command) const
        {
            for (int index = 0; index < GetMenuItemCount(handle); ++index)
            {
                MENUITEMINFOW info = { sizeof(info) }; info.fMask = MIIM_ID | MIIM_SUBMENU;
                if (GetMenuItemInfoW(handle, index, TRUE, &info) && info.wID == command) return info.hSubMenu;
            }
            return nullptr;
        }
        std::vector<Row> Rows()
        {
            std::vector<Row> result;
            Enumerate(handle, 0, result);
            return result;
        }
        UINT FindVerb(const wchar_t* verb)
        {
            for (const auto& row : Rows()) if (row.enabled && _wcsicmp(row.verb.c_str(), verb) == 0) return row.id;
            return 0;
        }
        void Invoke(HWND owner, UINT command)
        {
            if (command < commandBase || command >= commandBase + 0x3000) throw std::runtime_error("Invalid context-menu command ID");
            CMINVOKECOMMANDINFO info = { sizeof(info) };
            info.hwnd = owner;
            info.lpVerb = MAKEINTRESOURCEA(command - commandBase);
            info.nShow = SW_HIDE;
            Require(context->InvokeCommand(&info));
        }
        void Dump(const fs::path& destination)
        {
            std::ofstream stream(destination, std::ios::binary);
            stream << "Native classic Shell context menu: depth | label | canonical verb | enabled\n";
            for (const auto& row : Rows())
                stream << row.depth << " | " << Utf8(row.label) << " | " << Utf8(row.verb) << " | " << row.enabled << '\n';
            if (!stream) throw std::runtime_error("Cannot save menu inventory");
        }
        void Preview(HWND owner, const fs::path& destination,
            const std::function<void(HWND, const fs::path&)>& capture, HMENU alternative = nullptr, Menu* secondary = nullptr)
        {
            struct PreviewState
            {
                const fs::path& destination;
                const std::function<void(HWND, const fs::path&)>& capture;
                bool captured = false;
                std::exception_ptr error;
            } state = { destination, capture };
            const UINT_PTR timer = SetTimer(owner, reinterpret_cast<UINT_PTR>(&state), 500,
                [](HWND ownerWindow, UINT, UINT_PTR id, DWORD)
            {
                KillTimer(ownerWindow, id);
                auto state = reinterpret_cast<PreviewState*>(id);
                EnumThreadWindows(GetCurrentThreadId(), [](HWND window, LPARAM data) -> BOOL
                {
                    wchar_t name[64]; GetClassNameW(window, name, ARRAYSIZE(name));
                    if (wcscmp(name, L"#32768") != 0 || !IsWindowVisible(window)) return TRUE;
                    auto state = reinterpret_cast<PreviewState*>(data);
                    try { state->capture(window, state->destination); state->captured = true; }
                    catch (...) { state->error = std::current_exception(); }
                    return FALSE;
                }, reinterpret_cast<LPARAM>(state));
                EndMenu();
            });
            if (!timer) throw std::runtime_error("Cannot establish menu preview timer");
            Router router = { this, secondary };
            if (!SetWindowSubclass(owner, Route, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(&router)))
            {
                KillTimer(owner, timer);
                throw std::runtime_error("Cannot route native menu messages");
            }
            RECT bounds; GetWindowRect(owner, &bounds);
            SetForegroundWindow(owner);
            TrackPopupMenuEx(alternative ? alternative : handle, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
                bounds.left + 360, bounds.top + 90, owner, nullptr);
            KillTimer(owner, timer);
            RemoveWindowSubclass(owner, Route, reinterpret_cast<UINT_PTR>(this));
            if (state.error) std::rethrow_exception(state.error);
            if (!state.captured) throw std::runtime_error("Native context menu screenshot was not captured");
        }
    };
    class CompositeMenu
    {
        HMENU handle = CreatePopupMenu();
        Menu& viewMenu;
        Menu& desktopMenu;
        static void Move(HMENU source, int position, HMENU destination)
        {
            wchar_t text[1024] = {};
            MENUITEMINFOW info = { sizeof(info) };
            info.fMask = MIIM_ID | MIIM_STRING | MIIM_SUBMENU | MIIM_STATE | MIIM_FTYPE | MIIM_DATA | MIIM_BITMAP | MIIM_CHECKMARKS;
            info.dwTypeData = text; info.cch = ARRAYSIZE(text);
            if (!GetMenuItemInfoW(source, position, TRUE, &info) || !RemoveMenu(source, position, MF_BYPOSITION))
                throw std::runtime_error("Cannot transfer native context-menu item");
            if (!InsertMenuItemW(destination, GetMenuItemCount(destination), TRUE, &info))
            {
                InsertMenuItemW(source, position, TRUE, &info);
                throw std::runtime_error("Cannot compose native context menu");
            }
        }
        void MoveCommand(Menu& source, UINT command)
        {
            for (int index = 0; index < GetMenuItemCount(source.Handle()); ++index)
            {
                MENUITEMINFOW info = { sizeof(info) }; info.fMask = MIIM_ID;
                GetMenuItemInfoW(source.Handle(), index, TRUE, &info);
                if (info.wID == command) { Move(source.Handle(), index, handle); return; }
            }
            throw std::runtime_error("Cannot find required native menu row");
        }
    public:
        CompositeMenu(Menu& view, Menu& desktop) : viewMenu(view), desktopMenu(desktop)
        {
            if (!handle) throw std::runtime_error("Cannot create composed menu");
            try
            {
                const auto viewCommand = view.FindVerb(L"view"), sort = view.FindVerb(L"arrange"), refresh = view.FindVerb(L"refresh");
                const auto ownNew = view.FindVerb(L"New"), desktopNew = desktop.FindVerb(L"New");
                if (!viewCommand || !sort || !refresh || !ownNew || !desktopNew) throw std::runtime_error("Required menu groups unavailable");
                MoveCommand(view, viewCommand); MoveCommand(view, sort); MoveCommand(view, refresh);
                AppendMenuW(handle, MF_SEPARATOR, 0, nullptr);
                while (GetMenuItemCount(desktop.Handle()) > 0)
                {
                    MENUITEMINFOW info = { sizeof(info) }; info.fMask = MIIM_ID;
                    GetMenuItemInfoW(desktop.Handle(), 0, TRUE, &info);
                    if (info.wID == desktopNew)
                    {
                        // Keep New bound to the isolated folder, never the user's Desktop.
                        MoveCommand(view, ownNew);
                        DeleteMenu(desktop.Handle(), 0, MF_BYPOSITION);
                    }
                    else Move(desktop.Handle(), 0, handle);
                }
            }
            catch (...) { DestroyMenu(handle); handle = nullptr; throw; }
        }
        ~CompositeMenu() { if (handle) DestroyMenu(handle); }
        HMENU Handle() const { return handle; }
        void Invoke(HWND owner, UINT command)
        {
            if (command >= 0x4000 && command < 0x7000) desktopMenu.Invoke(owner, command);
            else viewMenu.Invoke(owner, command);
        }
        HMENU Submenu(UINT command) const
        {
            for (int index = 0; index < GetMenuItemCount(handle); ++index)
            {
                MENUITEMINFOW info = { sizeof(info) }; info.fMask = MIIM_ID | MIIM_SUBMENU;
                if (GetMenuItemInfoW(handle, index, TRUE, &info) && info.wID == command) return info.hSubMenu;
            }
            return nullptr;
        }
    };
    static ComPtr<IContextMenu> Background(IShellView* view)
    {
        ComPtr<IContextMenu> result;
        Require(view->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&result)));
        return result;
    }
    static ComPtr<IContextMenu> Item(HWND owner, IFolderView2* view, const wchar_t* name)
    {
        ComPtr<IShellFolder> folder;
        Require(view->GetFolder(IID_PPV_ARGS(&folder)));
        std::wstring mutableName(name);
        PIDLIST_RELATIVE child = nullptr;
        ULONG eaten = 0;
        Require(folder->ParseDisplayName(owner, nullptr, mutableName.data(), &eaten, &child, nullptr));
        ComPtr<IContextMenu> context;
        PCUITEMID_CHILD children[] = { child };
        const auto result = folder->GetUIObjectOf(owner, 1, children, IID_IContextMenu, nullptr,
            reinterpret_cast<void**>(context.GetAddressOf()));
        CoTaskMemFree(child);
        Require(result);
        return context;
    }
    int RunCommandHelper(const fs::path& marker)
    {
        std::error_code error;
        const auto directory = marker.parent_path();
        if (marker.filename() != L"menu-command-result.txt" ||
            directory.filename().wstring().find(L"Fences-ViewFilter-") != 0 ||
            !fs::equivalent(directory.parent_path(), fs::temp_directory_path(), error) || error) return 2;
        const auto handle = CreateFileW(marker.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE) return 3;
        const auto process = GetCurrentProcessId();
        DWORD written = 0;
        const bool succeeded = WriteFile(handle, &process, sizeof(process), &written, nullptr) != FALSE && written == sizeof(process);
        CloseHandle(handle);
        return succeeded ? 0 : 4;
    }
    int Run(HWND owner, IFolderView2* filteredView, IShellView* desktopView,
        const fs::path& fixture, const fs::path& output,
        const std::function<void(HWND, const fs::path&)>& capture,
        const std::function<void(unsigned)>& pump)
    {
        int assertions = 0;
        ComPtr<IShellView> ownView;
        Require(filteredView->QueryInterface(IID_PPV_ARGS(&ownView)));
        Menu file(Item(owner, filteredView, L"01-before.txt").Get());
        file.Dump(output / L"file-context-menu.txt");
        Check(file.FindVerb(L"open") && file.FindVerb(L"rename") && file.FindVerb(L"delete") && file.FindVerb(L"properties"),
            "filtered native view retains standard file context-menu commands", assertions);
        file.Preview(owner, output / L"file-context-menu.png", capture);
        const auto sevenZip = file.FindVerb(L"SevenZip");
        if (sevenZip)
        {
            const auto submenu = file.Submenu(sevenZip);
            Check(submenu && GetMenuItemCount(submenu) > 0, "installed 7-Zip context-menu extension provides its submenu", assertions);
            file.Preview(owner, output / L"file-7zip-submenu.png", capture, submenu);
        }

        Menu background(Background(ownView.Get()).Get());
        background.Dump(output / L"folder-background-menu.txt");
        background.Preview(owner, output / L"folder-background-menu.png", capture);
        const auto refresh = background.FindVerb(L"refresh");
        Check(refresh != 0, "hosted view exposes an enabled native Refresh command", assertions);
        background.Invoke(owner, refresh);
        pump(250);
        int visible = 0;
        Require(filteredView->ItemCount(SVGIO_ALLVIEW, &visible));
        Check(visible == 4, "native hosted-view Refresh executes and retains the four visible icons", assertions);
        FOLDERVIEWMODE originalMode; int originalIconSize;
        Require(filteredView->GetViewModeAndIconSize(&originalMode, &originalIconSize));
        const auto smallIcons = background.FindVerb(L"viewlogicaliconssmall");
        Check(smallIcons != 0, "native View submenu exposes Small icons", assertions);
        background.Invoke(owner, smallIcons); pump(100);
        FOLDERVIEWMODE smallMode; int smallSize;
        Require(filteredView->GetViewModeAndIconSize(&smallMode, &smallSize));
        Check(smallSize < originalIconSize || smallMode != originalMode,
            "native Small icons command changes the hosted view", assertions);
        Require(filteredView->SetViewModeAndIconSize(originalMode, originalIconSize));
        SORTCOLUMN originalSort;
        Require(filteredView->GetSortColumns(&originalSort, 1));
        const auto descending = background.FindVerb(L"sortdescending");
        Check(descending != 0, "native Sort submenu exposes descending order", assertions);
        background.Invoke(owner, descending); pump(100);
        SORTCOLUMN changedSort;
        Require(filteredView->GetSortColumns(&changedSort, 1));
        Check(changedSort.direction == SORT_DESCENDING, "native Sort command changes the hosted sort order", assertions);
        Require(filteredView->SetSortColumns(&originalSort, 1));

        // Obtain/display the actual desktop menu without executing any desktop command.
        ComPtr<IContextMenu> desktopContext;
        const auto desktopResult = desktopView->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&desktopContext));
        std::printf("OBSERVATION: live desktop GetItemObject context-menu result=0x%08lX\n", desktopResult);
        if (SUCCEEDED(desktopResult))
        {
            const auto remoteMenu = CreatePopupMenu();
            const auto populated = desktopContext->QueryContextMenu(remoteMenu, 0, firstCommand, 0x7ffe, CMF_NORMAL);
            std::printf("OBSERVATION: live desktop remote QueryContextMenu result=0x%08lX\n", populated);
            DestroyMenu(remoteMenu);
        }
        desktopContext.Reset();
        ComPtr<IShellFolder> desktopFolder;
        Require(SHGetDesktopFolder(&desktopFolder));
        Require(desktopFolder->CreateViewObject(owner, IID_PPV_ARGS(&desktopContext)));
        std::printf("OBSERVATION: locally creating the desktop-folder context menu\n"); std::fflush(stdout);
        Menu desktop(desktopContext.Get(), 0x4000);
        desktop.Dump(output / L"desktop-background-menu.txt");
        desktop.Preview(owner, output / L"desktop-background-menu.png", capture);
        Check(!desktop.Rows().empty(), "a native desktop-folder background menu can be displayed in the test host", assertions);
        Check(desktop.FindVerb(L"Display") && desktop.FindVerb(L"Personalize"),
            "desktop-specific Display settings and Personalize commands are present", assertions);
        const auto newFolder = background.FindVerb(L"NewFolder");
        const auto viewGroup = background.FindVerb(L"view"), newGroup = background.FindVerb(L"New");
        {
            CompositeMenu combined(background, desktop);
            background.Preview(owner, output / L"combined-desktop-menu.png", capture, combined.Handle(), &desktop);
            Check(GetMenuItemCount(combined.Handle()) > 3, "combined native menu contains view controls and desktop-specific entries", assertions);
            Check(combined.Submenu(viewGroup) != nullptr, "combined View submenu is retained", assertions);
            background.Preview(owner, output / L"combined-view-submenu.png", capture, combined.Submenu(viewGroup), &desktop);
            Check(combined.Submenu(newGroup) != nullptr, "combined New submenu is retained", assertions);
            background.Preview(owner, output / L"combined-new-submenu.png", capture, combined.Submenu(newGroup), &desktop);
            Check(newFolder != 0, "composed New group retains the native New folder command", assertions);
            combined.Invoke(owner, newFolder);
            const auto deadline = GetTickCount64() + 8000;
            fs::path created;
            while (created.empty() && GetTickCount64() < deadline)
            {
                for (const auto& entry : fs::directory_iterator(fixture)) if (entry.is_directory()) { created = entry.path(); break; }
                pump(20);
            }
            Check(!created.empty() && created.parent_path() == fixture,
                "composed New folder command creates inside the isolated folder", assertions);
            Require(ownView->UIActivate(SVUIA_DEACTIVATE));
            Require(ownView->Refresh()); pump(100);
            Check(fs::remove(created), "the folder created through the native menu is cleaned up", assertions);
        }

        const auto shortcut = fixture / L"menu-command.lnk", marker = fixture / L"menu-command-result.txt";
        wchar_t executable[32768];
        if (!GetModuleFileNameW(nullptr, executable, ARRAYSIZE(executable))) throw std::runtime_error("Cannot locate own test helper");
        ComPtr<IShellLinkW> link;
        Require(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link)));
        Require(link->SetPath(executable));
        const auto arguments = std::wstring(L"--context-menu-command \"") + marker.wstring() + L"\"";
        Require(link->SetArguments(arguments.c_str()));
        Require(link->SetShowCmd(SW_HIDE));
        ComPtr<IPersistFile> persistent;
        Require(link.As(&persistent));
        Require(persistent->Save(shortcut.c_str(), TRUE));
        Menu shortcutMenu(Item(owner, filteredView, L"menu-command.lnk").Get());
        const auto open = shortcutMenu.FindVerb(L"open");
        Check(open != 0, "shortcut native context menu retains its Open command", assertions);
        shortcutMenu.Invoke(owner, open);
        const auto deadline = GetTickCount64() + 8000;
        while ((!fs::exists(marker) || fs::file_size(marker) < sizeof(DWORD)) && GetTickCount64() < deadline) pump(20);
        DWORD childId = 0;
        std::ifstream result(marker, std::ios::binary);
        result.read(reinterpret_cast<char*>(&childId), sizeof(childId));
        Check(result.gcount() == sizeof(childId) && childId != GetCurrentProcessId(),
            "invoking the shortcut menu Open command launches the actual target with its arguments", assertions);
        result.close();
        const auto child = OpenProcess(SYNCHRONIZE, FALSE, childId);
        if (child)
        {
            const auto exited = WaitForSingleObject(child, 5000);
            CloseHandle(child);
            Check(exited == WAIT_OBJECT_0, "context-menu helper process exits", assertions);
        }
        else Check(GetLastError() == ERROR_INVALID_PARAMETER, "context-menu helper already exited", assertions);
        Check(fs::remove(shortcut) && fs::remove(marker), "context-menu command fixtures are cleaned up", assertions);
        return assertions;
    }
}
