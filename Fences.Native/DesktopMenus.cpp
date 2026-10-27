#include "DesktopView.h"
#include <commctrl.h>
#include <wrl/client.h>
#include <algorithm>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace
{
    constexpr UINT firstCommand = 0x1000;
    void Require(HRESULT result)
    {
        if (FAILED(result)) throw std::runtime_error("Native context menu operation failed");
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
        std::vector<Row> rows;
        bool enumerated = false;
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
        const std::vector<Row>& Rows()
        {
            // Providers may populate expensive dynamic submenus here. Each
            // popup instance has one snapshot; command lookups reuse it.
            if (!enumerated) { Enumerate(handle, 0, rows); enumerated = true; }
            return rows;
        }
        UINT FindVerb(const wchar_t* verb)
        {
            for (const auto& row : Rows()) if (_wcsicmp(row.verb.c_str(), verb) == 0) return row.id;
            return 0;
        }
        void Invoke(HWND owner, UINT command)
        {
            if (command < commandBase || command >= commandBase + 0x3000) throw std::runtime_error("Invalid context-menu command ID");
            CMINVOKECOMMANDINFO info = { sizeof(info) };
            info.hwnd = owner;
            info.lpVerb = MAKEINTRESOURCEA(command - commandBase);
            info.nShow = SW_SHOWNORMAL;
            Require(context->InvokeCommand(&info));
        }
        UINT Track(HWND owner, POINT point, HMENU alternative, Menu* secondary)
        {
            Router router = { this, secondary };
            if (!SetWindowSubclass(owner, Route, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(&router)))
                throw std::runtime_error("Cannot route native menu messages");
            const auto command = TrackPopupMenuEx(alternative ? alternative : handle, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                point.x, point.y, owner, nullptr);
            RemoveWindowSubclass(owner, Route, reinterpret_cast<UINT_PTR>(this));
            PostMessageW(owner, WM_NULL, 0, 0);
            return command;
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
                for (const auto verb : { L"paste", L"pastelink", L"undo" })
                {
                    const auto command = view.FindVerb(verb);
                    if (command) MoveCommand(view, command);
                }
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
}

bool ShowDesktopMenu(HWND owner, IFolderView2* view, POINT point, bool* viewChanged)
{
    if (viewChanged) *viewChanged = false;
    ComPtr<IShellView> shell;
    Require(view->QueryInterface(IID_PPV_ARGS(&shell)));
    ComPtr<IContextMenu> viewContext;
    Require(shell->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&viewContext)));
    Menu viewMenu(viewContext.Get());
    ComPtr<IShellFolder> desktop;
    Require(SHGetDesktopFolder(&desktop));
    ComPtr<IContextMenu> desktopContext;
    Require(desktop->CreateViewObject(owner, IID_PPV_ARGS(&desktopContext)));
    Menu desktopMenu(desktopContext.Get(), 0x4000);
    const auto toggle = viewMenu.FindVerb(L"showdesktopicons");
    const auto nextBackground = desktopMenu.FindVerb(L"NextDesktopBackground");
    const auto nextSpotlight = desktopMenu.FindVerb(L".SpotlightNextImage");
    CompositeMenu combined(viewMenu, desktopMenu);
    const auto command = viewMenu.Track(owner, point, combined.Handle(), &desktopMenu);
    if (!command) return false;
    if (viewChanged) *viewChanged = command >= firstCommand && command < 0x4000;
    if (command == toggle)
    {
        // Scope visibility to this hosted view; do not rewrite Explorer's saved preference.
        DWORD flags = 0;
        Require(view->GetCurrentFolderFlags(&flags));
        Require(view->SetCurrentFolderFlags(FWF_NOICONS, (flags ^ FWF_NOICONS) & FWF_NOICONS));
    }
    else combined.Invoke(owner, command);
    return (nextBackground && command == nextBackground) || (nextSpotlight && command == nextSpotlight);
}
