#include "DesktopView.h"
#include "DesktopSource.h"
#include "OpaquePaintBuffer.h"
#include "DesktopLabels.h"
#include "DesktopInteraction.h"
#include "DesktopWallpaper.h"
#include "DesktopSelection.h"
#include "RecycleBinIcon.h"
#include "DesktopItemRenderer.h"
#include "DesktopRenameTarget.h"
#include "DesktopSortSelection.h"
#include "DesktopDragDrop.h"
#include <commctrl.h>
#include <windowsx.h>
#include <shlguid.h>
#include <shellapi.h>
#include <propkey.h>
#include <wrl/client.h>
#include <algorithm>
#include <atomic>
#include <cwctype>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_set>
#include <unordered_map>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace
{
    constexpr UINT layoutMessage = WM_APP + 51;
    constexpr UINT labelsMessage = WM_APP + 52;
    constexpr UINT shellChangeMessage = WM_APP + 53;
    constexpr UINT imagesMessage = WM_APP + 54;
    constexpr UINT renameBoundsMessage = WM_APP + 55;
    constexpr UINT clearSelectionMessage = WM_APP + 56;
    constexpr UINT sortChangeMessage = WM_APP + 57;
    constexpr UINT_PTR iconsTimer = 0xF318;
    constexpr UINT_PTR sortTimer = 0xF319;
    const wchar_t* windowClass = L"FencesDesktopView17";
    std::wstring Normalize(std::wstring value)
    {
        while (value.size() > 3 && (value.back() == L'\\' || value.back() == L'/')) value.pop_back();
        std::transform(value.begin(), value.end(), value.begin(), [](wchar_t item) { return static_cast<wchar_t>(towlower(item)); });
        return value;
    }
    class DesktopView final : public IFolderFilter, public IExplorerBrowserEvents,
        public IServiceProvider, public ICommDlgBrowser3
    {
        LONG references = 1;
        std::shared_ptr<const std::unordered_set<std::wstring>> excluded = std::make_shared<const std::unordered_set<std::wstring>>();
        ComPtr<IExplorerBrowser> browser;
        ComPtr<IFolderFilterSite> filterSite;
        ComPtr<IFolderView2> view;
        DWORD cookie = 0;
        HWND shellWindow = nullptr;
        HWND listWindow = nullptr;
        HWND captionWindow = nullptr;
        std::vector<HWND> dropWindows;
        HWND notificationWindow;
        UINT notificationMessage;
        bool wallpaper;
        bool desktop = false;
        DesktopSource original;
        OpaquePaintBuffer paintBuffer;
        DesktopItemRenderer itemsRenderer;
        std::unique_ptr<DesktopRenameTarget> pendingRename;
        HWND pendingNativeEditor = nullptr;
        DesktopRenameTarget* activeRename = nullptr;
        DesktopInteraction interaction;
        DesktopSortSelection sortSelection;
        SORTCOLUMN primarySort = { PKEY_ItemNameDisplay, SORT_ASCENDING };
        int layoutItemCount = -1;
        bool suppressStationaryHot = false;
        POINT hoverResetPoint = {};
        ULONG shellChanges = 0;
        ULONG binChanges = 0;
        std::vector<ULONG> sortChanges;
        RecycleBinIcon recycleBin;
        bool refreshAllIcons = false;
        UINT_PTR wallpaperLoads = 0;
        UINT_PTR wallpaperInvalidations = 0;
        bool painting = false;
        bool dragging = false;
        UINT_PTR paintedFrames = 0;
        UINT_PTR presentationRevision = 0;
        RECT captionDirty = {};
        struct CachedCaption { std::wstring text; std::vector<BYTE> identity; };
        std::unordered_map<int, CachedCaption> captionCache;
        UINT_PTR paintedCaptions = 0;
        UINT_PTR iconRefreshes = 0;
        UINT_PTR enumerationStarts = 0;
        UINT_PTR viewRefreshes = 0;
        bool initialized = false;
        bool closing = false;
        bool layoutPosted = false;
        bool labelsPosted = false;
        bool imagesPosted = false;
        bool imagesNeeded = false;
        UINT_PTR imagePreparations = 0;
        int preparedItemCount = -1;
        HRESULT status = E_PENDING;
        std::vector<HWND> subclassed;
        HBITMAP wallpaperBitmap = nullptr;
        RECT wallpaperBounds = {};
        void ClearWallpaper()
        {
            if (wallpaperBitmap) DeleteObject(wallpaperBitmap);
            wallpaperBitmap = nullptr;
        }
        void PaintBackground(HWND target, HDC dc)
        {
            RECT bounds; GetClientRect(target, &bounds);
            if (wallpaper)
            {
                // PaintDesktop uses the supplied DC's clipping/buffering geometry.
                // Calling it for each icon's temporary DC repeats shifted wallpaper
                // fragments. Render once at full desktop size, then copy in client
                // coordinates without changing the caller's viewport/window origin.
                const RECT screen = { GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                    GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
                    GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN) };
                if (!EqualRect(&screen, &wallpaperBounds)) ClearWallpaper();
                const auto memory = CreateCompatibleDC(dc);
                if (!memory)
                {
                    FillRect(dc, &bounds, GetSysColorBrush(COLOR_DESKTOP)); Notify(3); return;
                }
                if (!wallpaperBitmap)
                {
                    ++wallpaperLoads;
                    BITMAPINFO info = {};
                    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
                    info.bmiHeader.biWidth = screen.right - screen.left;
                    info.bmiHeader.biHeight = -(screen.bottom - screen.top);
                    info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
                    void* pixels = nullptr;
                    wallpaperBitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
                    if (wallpaperBitmap)
                    {
                        const auto previous = SelectObject(memory, wallpaperBitmap);
                        SetWindowOrgEx(memory, screen.left, screen.top, nullptr);
                        const bool painted = PaintCurrentDesktopWallpaper(memory, screen) || PaintDesktop(memory) != FALSE;
                        // GDI wallpaper drawing leaves reserved alpha bytes at zero.
                        // Transparent desktop text is composited over this bitmap, so
                        // its background must be opaque before copying into item DCs.
                        GdiFlush();
                        auto colors = static_cast<DWORD*>(pixels);
                        const auto count = static_cast<size_t>(info.bmiHeader.biWidth) * -info.bmiHeader.biHeight;
                        for (size_t index = 0; index < count; ++index) colors[index] |= 0xff000000u;
                        SetWindowOrgEx(memory, 0, 0, nullptr);
                        SelectObject(memory, previous);
                        if (painted) wallpaperBounds = screen;
                        else ClearWallpaper();
                    }
                }
                if (wallpaperBitmap)
                {
                    const auto previous = SelectObject(memory, wallpaperBitmap);
                    POINT origin = {}; ClientToScreen(target, &origin);
                    const bool painted = BitBlt(dc, 0, 0, bounds.right, bounds.bottom, memory,
                        origin.x - wallpaperBounds.left, origin.y - wallpaperBounds.top, SRCCOPY);
                    SelectObject(memory, previous);
                    if (!painted) Notify(3);
                }
                else { FillRect(dc, &bounds, GetSysColorBrush(COLOR_DESKTOP)); Notify(3); }
                DeleteDC(memory);
            }
            else
            {
                const auto brush = CreateSolidBrush(RGB(30, 40, 52));
                FillRect(dc, &bounds, brush); DeleteObject(brush);
            }
        }
        void Notify(WPARAM kind)
        {
            if (!closing && notificationWindow) PostMessageW(notificationWindow, notificationMessage, kind, reinterpret_cast<LPARAM>(window));
        }
        bool IsExcluded(PCUITEMID_CHILD child)
        {
            if (!view) return false;
            ComPtr<IShellFolder> folder;
            if (FAILED(view->GetFolder(IID_PPV_ARGS(&folder)))) return false;
            return ShouldShow(folder.Get(), nullptr, child) == S_FALSE;
        }
        void QueueLayout()
        {
            if (!layoutPosted && window) { layoutPosted = true; PostMessageW(window, layoutMessage, 0, 0); }
        }
        void QueueLabels()
        {
            ++presentationRevision;
            if (!labelsPosted && window && !closing) { labelsPosted = true; PostMessageW(window, labelsMessage, 0, 0); }
        }
        void QueueImages()
        {
            imagesNeeded = true;
            if (!imagesPosted && window && !closing) { imagesPosted = true; PostMessageW(window, imagesMessage, 0, 0); }
        }
        void PrepareImages()
        {
            imagesPosted = false;
            if (!listWindow || closing || !IsWindowVisible(listWindow)) return;
            imagesNeeded = false;
            RECT bounds; GetClientRect(listWindow, &bounds);
            const auto dc = GetDC(listWindow);
            OpaquePaintBuffer scratch;
            if (dc && scratch.Ensure(dc, bounds.right, bounds.bottom))
            {
                // Native painting schedules Shell's deferred icon/thumbnail
                // providers. Run after redraw resumes, outside WM_PAINT; the
                // native text/background stays in this disposable surface.
                ++imagePreparations; preparedItemCount = ListView_GetItemCount(listWindow);
                SendMessageW(listWindow, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(scratch.DC()), PRF_CLIENT | PRF_ERASEBKGND);
            }
            if (dc) ReleaseDC(listWindow, dc);
        }
        void DirtyAllCaptions()
        {
            if (window) GetClientRect(window, &captionDirty);
        }
        void ResetHoverUntilMovement()
        {
            suppressStationaryHot = GetCursorPos(&hoverResetPoint) != FALSE;
        }
        void CaptureRenameBounds(HWND editor)
        {
            if (!pendingRename || !editor) return;
            RECT bounds, client;
            if (GetWindowRect(editor, &bounds) && GetClientRect(listWindow, &client) && !IsRectEmpty(&bounds) &&
                bounds.right - bounds.left <= client.right && bounds.bottom - bounds.top <= client.bottom)
                pendingRename->bounds = bounds;
        }
        void FinishRenameBounds()
        {
            const auto editor = pendingNativeEditor;
            if (!editor || !pendingRename) return;
            CaptureRenameBounds(editor);
            pendingNativeEditor = nullptr;
            if (listWindow && ListView_GetEditControl(listWindow) == editor)
                SendMessageW(listWindow, LVM_CANCELEDITLABEL, 0, 0);
            Notify(8);
        }
        void InvalidateItem(int index)
        {
            if (!listWindow || index < 0) return;
            RECT bounds;
            if (itemsRenderer.DirtyBounds(listWindow, index, bounds))
            {
                InflateRect(&bounds, 2, 2); InvalidateRect(listWindow, &bounds, FALSE);
            }
        }
        bool Caption(int index, std::wstring& text)
        {
            if (!view) return false;
            PITEMID_CHILD child = nullptr;
            if (FAILED(view->Item(index, &child)) || !child) return false;
            const auto bytes = ILGetSize(child);
            const auto cached = captionCache.find(index);
            if (cached != captionCache.end() && cached->second.identity.size() == bytes &&
                std::memcmp(cached->second.identity.data(), child, bytes) == 0)
            {
                text = cached->second.text; CoTaskMemFree(child); return true;
            }
            const auto data = reinterpret_cast<const BYTE*>(child);
            std::vector<BYTE> identity(data, data + bytes);
            const bool hidden = IsExcluded(child);
            ComPtr<IShellFolder> folder;
            ComPtr<IShellItem> item;
            auto result = hidden ? S_FALSE : view->GetFolder(IID_PPV_ARGS(&folder));
            if (result == S_OK) result = SHCreateItemWithParent(nullptr, folder.Get(), child, IID_PPV_ARGS(&item));
            CoTaskMemFree(child);
            PWSTR name = nullptr;
            if (result == S_OK) result = item->GetDisplayName(SIGDN_NORMALDISPLAY, &name);
            if (result != S_OK) return false;
            text = name; CoTaskMemFree(name); captionCache[index] = { text, std::move(identity) }; ++paintedCaptions; return true;
        }
        bool IsRenaming(int index)
        {
            if (!activeRename || !view) return false;
            PITEMID_CHILD child = nullptr;
            if (FAILED(view->Item(index, &child)) || !child) return false;
            const bool editing = activeRename->Matches(child); CoTaskMemFree(child); return editing;
        }
        static void OpaqueItemText(HDC dc, RECT bounds)
        {
            // GDI can clear reserved alpha while drawing white glyphs. Fix the
            // completed item buffer before the desktop compositor consumes it,
            // rather than relying on a later screenshot/whole-window repaint.
            DIBSECTION bitmap = {};
            const auto selected = GetCurrentObject(dc, OBJ_BITMAP);
            if (!selected || GetObjectW(selected, sizeof(bitmap), &bitmap) != sizeof(bitmap) ||
                !bitmap.dsBm.bmBits || bitmap.dsBm.bmBitsPixel != 32) return;
            if (!LPtoDP(dc, reinterpret_cast<POINT*>(&bounds), 2)) return;
            GdiFlush();
            const int height = std::abs(bitmap.dsBm.bmHeight);
            const int left = (std::max)(0L, bounds.left), right = (std::min)(bitmap.dsBm.bmWidth, bounds.right);
            const int top = (std::max)(0L, bounds.top), bottom = (std::min)(static_cast<LONG>(height), bounds.bottom);
            for (int y = top; y < bottom; ++y)
            {
                const int row = bitmap.dsBmih.biHeight < 0 ? y : height - 1 - y;
                auto pixels = reinterpret_cast<DWORD*>(static_cast<BYTE*>(bitmap.dsBm.bmBits) + row * bitmap.dsBm.bmWidthBytes);
                for (int x = left; x < right; ++x) pixels[x] |= 0xff000000u;
            }
        }
        static LRESULT CALLBACK ChildProcedure(HWND child, UINT message, WPARAM wParam, LPARAM lParam,
            UINT_PTR id, DWORD_PTR data)
        {
            auto self = reinterpret_cast<DesktopView*>(data);
            try
            {
                if (message == WM_NCDESTROY)
                {
                    self->UnregisterDrop(child); RemoveWindowSubclass(child, ChildProcedure, id);
                }
                if (child == self->pendingNativeEditor)
                {
                    if (message == WM_WINDOWPOSCHANGING && lParam)
                        reinterpret_cast<WINDOWPOS*>(lParam)->flags &= ~SWP_SHOWWINDOW;
                    if (message == WM_WINDOWPOSCHANGED) self->CaptureRenameBounds(child);
                }
                if (child == self->captionWindow) return DefSubclassProc(child, message, wParam, lParam);
                if (child == self->listWindow && (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN || message == WM_KEYDOWN))
                    self->suppressStationaryHot = false;
                if (child == self->listWindow && (message == LVM_SORTITEMS || message == LVM_SORTITEMSEX || message == LVM_DELETEALLITEMS))
                    self->captionCache.clear();
                if (child == self->listWindow && (message == LVM_SETITEMW || message == LVM_SETITEMA) && lParam)
                {
                    const auto item = reinterpret_cast<const LVITEMW*>(lParam);
                    if (item->mask & (LVIF_TEXT | LVIF_PARAM)) self->captionCache.erase(item->iItem);
                    if (item->mask & LVIF_IMAGE) self->QueueImages();
                }
                if (child == self->listWindow && (message == LVM_REDRAWITEMS || message == LVM_SETIMAGELIST ||
                    message == LVM_SETITEMCOUNT || (message == WM_SETREDRAW && wParam)))
                    self->QueueImages();
                if (child == self->listWindow && self->interaction.Before(child, message, wParam, lParam, self->view.Get())) return 0;
                const bool needsEditor = child != self->listWindow || message == WM_PAINT || message == WM_CTLCOLOREDIT;
                const auto editor = needsEditor && self->listWindow && message != LVM_GETEDITCONTROL ? ListView_GetEditControl(self->listWindow) : nullptr;
                if (child == editor)
                {
                    if (message == WM_SETFOCUS || message == WM_KILLFOCUS || message == WM_WINDOWPOSCHANGED || message == WM_SHOWWINDOW)
                        self->QueueLabels();
                    return DefSubclassProc(child, message, wParam, lParam);
                }
                if (message == WM_CTLCOLOREDIT && editor && reinterpret_cast<HWND>(lParam) == editor)
                {
                    const auto dc = reinterpret_cast<HDC>(wParam);
                    SetTextColor(dc, GetSysColor(COLOR_WINDOWTEXT)); SetBkColor(dc, GetSysColor(COLOR_WINDOW));
                    return reinterpret_cast<LRESULT>(GetSysColorBrush(COLOR_WINDOW));
                }
                if ((message == WM_PAINT && child != self->listWindow) || message == WM_WINDOWPOSCHANGED ||
                    message == WM_SETFOCUS || message == WM_KILLFOCUS || message == WM_KEYUP || message == WM_LBUTTONUP ||
                    message == WM_HSCROLL || message == WM_VSCROLL) self->QueueLabels();
                if (child == self->listWindow && (message == WM_MOUSEMOVE || message == WM_MOUSELEAVE))
                {
                    POINT cursor;
                    if (self->suppressStationaryHot && GetCursorPos(&cursor) &&
                        (cursor.x != self->hoverResetPoint.x || cursor.y != self->hoverResetPoint.y))
                    {
                        self->suppressStationaryHot = false;
                        self->InvalidateItem(ListView_GetHotItem(child)); self->QueueLabels();
                    }
                    LVHITTESTINFO hit = {}; hit.pt = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                    const int hot = message == WM_MOUSELEAVE ? -1 : ListView_HitTest(child, &hit);
                    const int oldHot = ListView_GetHotItem(child);
                    if (hot != oldHot)
                    {
                        self->InvalidateItem(oldHot);
                        ListView_SetHotItem(child, hot);
                        self->InvalidateItem(hot);
                    }
                    if (message == WM_MOUSEMOVE) { TRACKMOUSEEVENT track = { sizeof(track), TME_LEAVE, child, 0 }; TrackMouseEvent(&track); }
                }
                if (child == self->listWindow && (message == WM_SETFOCUS || message == WM_KILLFOCUS))
                    InvalidateRect(child, nullptr, FALSE);
                if (child == self->listWindow && message == WM_SETFONT) self->itemsRenderer.ResetFont();
                if (message == WM_PAINT && child == self->listWindow && !wParam && !self->painting)
                {
                    if (self->layoutItemCount != ListView_GetItemCount(child)) self->QueueLayout();
                    if (self->imagesNeeded || self->preparedItemCount != ListView_GetItemCount(child)) self->QueueImages();
                    // Shell enumeration may complete after the first layout.
                    // Discover the virtual bin before painting that later frame.
                    if (self->desktop && self->recycleBin.index < 0)
                    {
                        self->recycleBin.Locate(self->view.Get());
                        self->recycleBin.Refresh(child, false);
                    }
                    ++self->paintedFrames;
                    ScopedWindowPaint paint(child);
                    const auto target = paint.DC();
                    if (!target) return 0;
                    RECT bounds; GetClientRect(child, &bounds);
                    const auto& update = paint.State().rcPaint;
                    if (!IsRectEmpty(&update))
                    {
                        if (self->paintBuffer.Ensure(target, bounds.right, bounds.bottom))
                        {
                            const auto memory = self->paintBuffer.DC();
                            const int saved = SaveDC(memory);
                            IntersectClipRect(memory, update.left, update.top, update.right, update.bottom);
                            self->PaintBackground(child, memory);
                            const bool rendered = self->itemsRenderer.Draw(child, memory, update,
                                [self](int index, std::wstring& text) { return self->Caption(index, text); }, self->recycleBin.index,
                                [self](int index) { return self->IsRenaming(index); }, !self->suppressStationaryHot);
                            self->recycleBin.Draw(child, memory, update);
                            self->paintBuffer.MakeOpaque(update);
                            RECT dirty = update; MapWindowPoints(child, self->window, reinterpret_cast<POINT*>(&dirty), 2);
                            UnionRect(&self->captionDirty, &self->captionDirty, &dirty);
                            const int targetState = SaveDC(target);
                            if (editor && IsWindowVisible(editor))
                            {
                                RECT edit; GetWindowRect(editor, &edit); MapWindowPoints(nullptr, child, reinterpret_cast<POINT*>(&edit), 2);
                                ExcludeClipRect(target, edit.left, edit.top, edit.right, edit.bottom);
                            }
                            const bool copied = BitBlt(target, update.left, update.top,
                                update.right - update.left, update.bottom - update.top,
                                memory, update.left, update.top, SRCCOPY) != FALSE;
                            RestoreDC(target, targetState);
                            RestoreDC(memory, saved);
                            if (!rendered || !copied) self->Notify(3);
                        }
                        else self->Notify(3);
                    }
                    self->QueueLabels();
                    return 0;
                }
                if (child == self->listWindow && message == LVM_SETEXTENDEDLISTVIEWSTYLE)
                    lParam &= ~static_cast<LPARAM>(LVS_EX_TRANSPARENTSHADOWTEXT);
                // Transparent ListView items request their background from the parent via
                // WM_PRINTCLIENT, including during partial/buffered item repaints. The
                // Shell's default handler uses different wallpaper coordinates here.
                // Use the same screen-anchored painter as the surrounding background.
                if ((message == WM_ERASEBKGND && child != self->listWindow) ||
                    (message == WM_PRINTCLIENT && self->listWindow && child == GetParent(self->listWindow)))
                {
                    self->PaintBackground(child, reinterpret_cast<HDC>(wParam));
                    return TRUE;
                }
                if (message == WM_CONTEXTMENU && self->view)
                {
                    POINT point = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                    bool background = false;
                    if (point.x == -1 && point.y == -1)
                    {
                        int selected = 0; self->view->ItemCount(SVGIO_SELECTION, &selected);
                        background = selected == 0;
                        if (background) GetCursorPos(&point);
                    }
                    else background = self->BackgroundPoint(point, false);
                    if (background)
                    {
                        bool changed = false;
                        if (ShowDesktopMenu(self->window, self->view.Get(), point, &changed)) self->Notify(7);
                        if (changed) { self->QueueLayout(); self->Notify(1); }
                        return 0;
                    }
                }
                if (message == WM_NOTIFY)
                {
                    const auto notification = reinterpret_cast<NMHDR*>(lParam);
                    if (notification->hwndFrom == self->listWindow)
                    {
                        if (notification->code == LVN_BEGINDRAG || notification->code == LVN_BEGINRDRAG)
                        {
                            self->interaction.Close(self->listWindow);
                            ComPtr<IDataObject> dragData; DWORD allowed = DROPEFFECT_NONE;
                            if (GetDesktopDragData(self->view.Get(), &dragData, &allowed) == S_OK)
                            {
                                self->dragging = true;
                                DWORD effect = DROPEFFECT_NONE;
                                SHDoDragDrop(self->listWindow, dragData.Get(), nullptr, allowed, &effect);
                                self->dragging = false; self->QueueLayout(); self->Notify(1);
                            }
                            return 0;
                        }
                        if (notification->code == LVN_BEGINLABELEDITW)
                        {
                            if (self->pendingNativeEditor) return TRUE;
                            self->interaction.Close(self->listWindow);
                            const int index = reinterpret_cast<NMLVDISPINFOW*>(lParam)->item.iItem;
                            auto target = std::make_unique<DesktopRenameTarget>();
                            const auto opened = target->Open(self->view.Get(), index);
                            if (SUCCEEDED(opened) && ListView_GetItemRect(self->listWindow, index, &target->bounds, LVIR_LABEL))
                            {
                                MapWindowPoints(self->listWindow, nullptr, reinterpret_cast<POINT*>(&target->bounds), 2);
                                self->pendingRename = std::move(target);
                                const auto nativeEditor = ListView_GetEditControl(self->listWindow);
                                if (nativeEditor && SetWindowSubclass(nativeEditor, ChildProcedure, reinterpret_cast<UINT_PTR>(self), reinterpret_cast<DWORD_PTR>(self)))
                                {
                                    // BEGIN arrives before the control's final layout.
                                    // Suppress showing it, allow layout to finish, then
                                    // cancel it and pass its real bounds to the editor.
                                    self->pendingNativeEditor = nativeEditor;
                                    PostMessageW(self->window, renameBoundsMessage, 0, 0);
                                    return FALSE;
                                }
                                self->Notify(8);
                            }
                            // The desktop-hosted native EDIT is invisible on some
                            // Explorer compositions. Use a normal top-level editor.
                            return TRUE;
                        }
                        if (notification->code == LVN_ITEMCHANGED &&
                            (reinterpret_cast<NMLISTVIEW*>(lParam)->uChanged & (LVIF_STATE | LVIF_IMAGE)))
                        {
                            if (reinterpret_cast<NMLISTVIEW*>(lParam)->uChanged & LVIF_IMAGE) self->QueueImages();
                            const int index = reinterpret_cast<NMLISTVIEW*>(lParam)->iItem;
                            if (index < 0) InvalidateRect(self->listWindow, nullptr, FALSE); else self->InvalidateItem(index);
                        }
                        if (notification->code == LVN_ITEMCHANGED && (reinterpret_cast<NMLISTVIEW*>(lParam)->uChanged & LVIF_TEXT))
                        {
                            const int index = reinterpret_cast<NMLISTVIEW*>(lParam)->iItem;
                            if (index < 0) self->captionCache.clear(); else self->captionCache.erase(index);
                        }
                        if (notification->code == LVN_INSERTITEM || notification->code == LVN_DELETEITEM)
                            self->captionCache.clear();
                        if (notification->code == LVN_BEGINLABELEDITW || notification->code == LVN_BEGINDRAG ||
                            notification->code == LVN_BEGINRDRAG || notification->code == NM_DBLCLK ||
                            notification->code == LVN_INSERTITEM || notification->code == LVN_DELETEITEM)
                            self->interaction.Close(self->listWindow);
                        if (notification->code == LVN_ITEMCHANGED || notification->code == LVN_BEGINLABELEDITW || notification->code == LVN_ENDLABELEDITW)
                            self->QueueLabels();
                        if (notification->code == LVN_INSERTITEM || notification->code == LVN_DELETEITEM) self->QueueLayout();
                        if (notification->code == NM_CUSTOMDRAW)
                        {
                            const auto draw = reinterpret_cast<NMLVCUSTOMDRAW*>(lParam);
                            if (draw->nmcd.dwDrawStage == CDDS_PREPAINT)
                                return CDRF_NOTIFYITEMDRAW;
                            if (draw->nmcd.dwDrawStage == CDDS_ITEMPREPAINT)
                            {
                                PITEMID_CHILD item = nullptr;
                                if (SUCCEEDED(self->view->Item(static_cast<int>(draw->nmcd.dwItemSpec), &item)))
                                {
                                    const bool hide = self->IsExcluded(item);
                                    CoTaskMemFree(item);
                                    if (hide) return CDRF_SKIPDEFAULT;
                                }
                                // The Shell desktop's custom-draw handler owns its
                                // own composited label pipeline. Hosting that pipeline
                                // under another process can punch out glyphs even
                                // though screenshots look correct. Let the ListView
                                // draw native icons/labels itself instead.
                                draw->clrText = RGB(255, 255, 255);
                                draw->clrTextBk = CLR_NONE;
                                return CDRF_DODEFAULT | CDRF_NOTIFYPOSTPAINT;
                            }
                            if (draw->nmcd.dwDrawStage == CDDS_ITEMPOSTPAINT)
                            {
                                OpaqueItemText(draw->nmcd.hdc, draw->nmcd.rc);
                                return CDRF_DODEFAULT;
                            }
                        }
                    }
                }
                if (message == WM_LBUTTONUP || message == WM_KEYUP) self->Notify(1);
            }
            catch (...) { self->Notify(3); }
            const auto result = DefSubclassProc(child, message, wParam, lParam);
            if (child == self->listWindow && self->interaction.After(child, message, lParam, self->view.Get()))
            {
                self->ResetHoverUntilMovement();
                PostMessageW(self->window, clearSelectionMessage, self->interaction.SelectionEpoch(), 0);
            }
            return result;
        }
        HRESULT RegisterDrop(HWND targetWindow)
        {
            if (std::find(dropWindows.begin(), dropWindows.end(), targetWindow) != dropWindows.end()) return S_OK;
            ComPtr<IDropTarget> target;
            auto result = view.As(&target);
            if (FAILED(result)) return result;
            result = RegisterDragDrop(targetWindow, target.Get());
            if (result == S_OK) dropWindows.push_back(targetWindow);
            return result == DRAGDROP_E_ALREADYREGISTERED ? S_OK : result;
        }
        void UnregisterDrop(HWND targetWindow)
        {
            const auto found = std::find(dropWindows.begin(), dropWindows.end(), targetWindow);
            if (found == dropWindows.end()) return;
            RevokeDragDrop(targetWindow); dropWindows.erase(found);
        }
        void AttachChildren()
        {
            if (!window || !view) return;
            ComPtr<IShellView> shell;
            if (FAILED(view.As(&shell))) return;
            shell->GetWindow(&shellWindow);
            EnumChildWindows(window, [](HWND child, LPARAM parameter) -> BOOL
            {
                auto self = reinterpret_cast<DesktopView*>(parameter);
                if (child == self->pendingNativeEditor) return TRUE;
                if (self->captionWindow && (child == self->captionWindow || IsChild(self->captionWindow, child))) return TRUE;
                wchar_t name[128]; GetClassNameW(child, name, ARRAYSIZE(name));
                if (wcscmp(name, WC_LISTVIEWW) == 0) self->listWindow = child;
                if (std::find(self->subclassed.begin(), self->subclassed.end(), child) == self->subclassed.end() &&
                    SetWindowSubclass(child, ChildProcedure, reinterpret_cast<UINT_PTR>(self), reinterpret_cast<DWORD_PTR>(self)))
                    self->subclassed.push_back(child);
                return TRUE;
            }, reinterpret_cast<LPARAM>(this));
            if (listWindow)
            {
                // Desktop-mode DefView supplies its native IDropTarget but
                // leaves registration to its host. Keep native item/background
                // routing, including virtual targets such as the Recycle Bin.
                if (FAILED(RegisterDrop(listWindow))) { Notify(3); return; }
                ListView_SetBkColor(listWindow, CLR_NONE);
                ListView_SetTextBkColor(listWindow, CLR_NONE);
                ListView_SetTextColor(listWindow, RGB(255, 255, 255));
                // Shell's alpha-shadow text path can lose foreground coverage on
                // the real composited desktop while screenshots appear normal.
                ListView_SetExtendedListViewStyleEx(listWindow,
                    LVS_EX_TRANSPARENTBKGND | LVS_EX_TRANSPARENTSHADOWTEXT, LVS_EX_TRANSPARENTBKGND);
                if (desktop)
                {
                    FOLDERVIEWMODE mode; int size = original.iconSize;
                    view->GetViewModeAndIconSize(&mode, &size);
                    const int delta = size - original.iconSize;
                    ListView_SetIconSpacing(listWindow, original.spacing.x + delta, original.spacing.y + delta);
                }
            }
        }
        void Layout()
        {
            captionCache.clear(); itemsRenderer.ResetLayout(); DirtyAllCaptions();
            layoutPosted = false;
            AttachChildren();
            if (!listWindow) return;
            layoutItemCount = ListView_GetItemCount(listWindow);
            if (desktop)
            {
                recycleBin.Locate(view.Get());
                recycleBin.Refresh(listWindow, false);
            }
            std::vector<RECT> areas;
            const auto root = GetAncestor(window, GA_ROOT);
            wchar_t rootClass[128]; GetClassNameW(root, rootClass, ARRAYSIZE(rootClass));
            const bool onDesktop = wcscmp(rootClass, L"Progman") == 0 || wcscmp(rootClass, L"WorkerW") == 0;
            if (onDesktop)
            {
                EnumDisplayMonitors(nullptr, nullptr, [](HMONITOR monitor, HDC, LPRECT, LPARAM parameter) -> BOOL
                {
                    auto areas = reinterpret_cast<std::vector<RECT>*>(parameter);
                    MONITORINFO info = { sizeof(info) };
                    if (GetMonitorInfoW(monitor, &info))
                    {
                        if (info.dwFlags & MONITORINFOF_PRIMARY) areas->insert(areas->begin(), info.rcWork);
                        else areas->push_back(info.rcWork);
                    }
                    return TRUE;
                }, reinterpret_cast<LPARAM>(&areas));
                for (auto& area : areas) MapWindowPoints(nullptr, listWindow, reinterpret_cast<POINT*>(&area), 2);
            }
            else
            {
                RECT area; GetClientRect(listWindow, &area); areas.push_back(area);
            }
            if (!areas.empty()) ListView_SetWorkAreas(listWindow, static_cast<int>(areas.size()), areas.data());
            DWORD flags = 0;
            view->GetCurrentFolderFlags(&flags);
            // Recycle Bin drops can clear all Shell sort columns. Reinstate
            // the configured rule for automatic layout, keeping manual layout.
            int sortCount = 0;
            if ((flags & FWF_AUTOARRANGE) && SUCCEEDED(view->GetSortColumnCount(&sortCount)) && sortCount == 0)
                view->SetSortColumns(&primarySort, 1);
            if (flags & FWF_AUTOARRANGE) ListView_Arrange(listWindow, LVA_DEFAULT);
            sortSelection.Restore(view.Get(), interaction.SelectionEpoch());
            RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
            Notify(4);
            QueueImages();
            QueueLabels();
        }
        static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
        {
            auto self = reinterpret_cast<DesktopView*>(GetWindowLongPtrW(window, GWLP_USERDATA));
            if (message == WM_NCCREATE)
            {
                self = static_cast<DesktopView*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
                SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
                self->window = window;
            }
            if (!self) return DefWindowProcW(window, message, wParam, lParam);
            // Numeric, read-only live diagnostics; querying does not trigger paint.
            if (message == WM_APP + 178) return wParam == 7 ? self->imagePreparations : wParam == 6 ? self->viewRefreshes : wParam == 5 ? self->enumerationStarts : wParam == 4 ? self->wallpaperInvalidations : wParam == 3 ? self->wallpaperLoads : wParam == 2 ? self->iconRefreshes : wParam == 1 ? self->paintedCaptions : self->paintedFrames;
            if (message == WM_APP + 179)
            {
                const auto bounds = self->recycleBin.Bounds(self->listWindow);
                switch (wParam)
                {
                case 0: return bounds.left; case 1: return bounds.top;
                case 2: return bounds.right; case 3: return bounds.bottom;
                case 4: return self->recycleBin.Full(); case 6: return self->Count();
                case 7: return self->recycleBin.identity != nullptr; default: return self->recycleBin.index;
                }
            }
            if (message == WM_APP + 180)
            {
                // Read the actual icon pixels last copied by WM_PAINT. This
                // diagnostic never causes a repaint or changes selection.
                const auto bounds = self->recycleBin.Bounds(self->listWindow);
                const int x = bounds.left + LOWORD(wParam), y = bounds.top + HIWORD(wParam);
                DIBSECTION image = {};
                const auto bitmap = GetCurrentObject(self->paintBuffer.DC(), OBJ_BITMAP);
                if (!bitmap || GetObjectW(bitmap, sizeof(image), &image) != sizeof(image) || !image.dsBm.bmBits ||
                    x < 0 || y < 0 || x >= image.dsBm.bmWidth || y >= std::abs(image.dsBm.bmHeight)) return 0;
                const auto row = reinterpret_cast<const DWORD*>(static_cast<const BYTE*>(image.dsBm.bmBits) + y * image.dsBm.bmWidthBytes);
                return row[x] | 0xff000000u;
            }
            try
            {
                if (message == WM_ERASEBKGND) { self->PaintBackground(window, reinterpret_cast<HDC>(wParam)); return TRUE; }
                if (message == WM_SIZE && self->browser && self->initialized)
                {
                    RECT bounds; GetClientRect(window, &bounds);
                    self->browser->SetRect(nullptr, bounds); self->QueueLayout();
                }
                if (message == layoutMessage) { self->Layout(); return 0; }
                if (message == labelsMessage)
                {
                    self->labelsPosted = false;
                    if (self->view) self->sortSelection.Restore(self->view.Get(), self->interaction.SelectionEpoch());
                    self->Notify(5); return 0;
                }
                if (message == imagesMessage) { self->PrepareImages(); return 0; }
                if (message == renameBoundsMessage) { self->FinishRenameBounds(); return 0; }
                if (message == clearSelectionMessage)
                {
                    if (self->interaction.FlushSelection(self->listWindow, self->view.Get(), wParam)) self->QueueLabels();
                    return 0;
                }
                if (message == sortChangeMessage)
                {
                    PIDLIST_ABSOLUTE* items = nullptr; LONG event = 0;
                    const auto locked = SHChangeNotification_Lock(reinterpret_cast<HANDLE>(wParam), static_cast<DWORD>(lParam), &items, &event);
                    if (locked)
                    {
                        SHChangeNotification_Unlock(locked);
                        SetTimer(window, sortTimer, 250, nullptr);
                    }
                    return 0;
                }
                if (message == WM_TIMER && wParam == sortTimer)
                {
                    // Batch filesystem arrivals and wait for active interactions.
                    // Refresh re-sorts the Shell model; setting identical sort
                    // columns or arranging ListView cells leaves arrivals appended.
                    if (self->dragging || self->activeRename || (self->listWindow && (ListView_GetEditControl(self->listWindow) || GetCapture() ||
                        ((GetAsyncKeyState(VK_LBUTTON) | GetAsyncKeyState(VK_RBUTTON)) & 0x8000)))) return 0;
                    KillTimer(window, sortTimer);
                    DWORD flags = 0;
                    if (self->view && SUCCEEDED(self->view->GetCurrentFolderFlags(&flags)) && (flags & FWF_AUTOARRANGE))
                    {
                        self->sortSelection.Capture(self->view.Get(), self->interaction.SelectionEpoch());
                        self->Refresh(false);
                    }
                    return 0;
                }
                if (message == shellChangeMessage)
                {
                    PIDLIST_ABSOLUTE* items = nullptr; LONG event = 0;
                    const auto locked = SHChangeNotification_Lock(reinterpret_cast<HANDLE>(wParam), static_cast<DWORD>(lParam), &items, &event);
                    if (locked)
                    {
                        SHChangeNotification_Unlock(locked);
                        if (event & SHCNE_ASSOCCHANGED) self->refreshAllIcons = true;
                        SetTimer(window, iconsTimer, 100, nullptr);
                    }
                    return 0;
                }
                if (message == WM_TIMER && wParam == iconsTimer)
                {
                    // Refreshing during a native edit/drag would destroy its editor.
                    if (self->dragging || self->activeRename || (self->listWindow && (ListView_GetEditControl(self->listWindow) || GetCapture() ||
                        ((GetAsyncKeyState(VK_LBUTTON) | GetAsyncKeyState(VK_RBUTTON)) & 0x8000)))) return 0;
                    KillTimer(window, iconsTimer);
                    if (self->desktop && !self->refreshAllIcons)
                    {
                        if (self->recycleBin.Refresh(self->listWindow, false) == S_OK)
                        {
                            ++self->iconRefreshes;
                            RedrawWindow(self->listWindow, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
                        }
                    }
                    else
                    {
                        ComPtr<IShellView> shell;
                        if (self->view && SUCCEEDED(self->view.As(&shell)))
                        {
                            if (SUCCEEDED(shell->Refresh())) ++self->iconRefreshes;
                            self->QueueLayout();
                        }
                    }
                    self->refreshAllIcons = false;
                    return 0;
                }
                if (message == WM_CONTEXTMENU && self->view)
                {
                    POINT point = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                    if (point.x == -1 && point.y == -1) GetCursorPos(&point);
                    if (self->BackgroundPoint(point, false))
                    {
                        bool changed = false;
                        if (ShowDesktopMenu(window, self->view.Get(), point, &changed)) self->Notify(7);
                        if (changed) { self->QueueLayout(); self->Notify(1); }
                        return 0;
                    }
                }
                if (message == WM_NCDESTROY) { self->Notify(2); self->window = nullptr; }
            }
            catch (...) { self->Notify(3); }
            return DefWindowProcW(window, message, wParam, lParam);
        }
    public:
        HWND window = nullptr;
        DesktopView(bool paintWallpaper, HWND notify, UINT message) : notificationWindow(notify), notificationMessage(message), wallpaper(paintWallpaper) {}
        HRESULT Open(HWND parent, const wchar_t* folder)
        {
            desktop = !folder || !*folder;
            if (desktop)
            {
                const auto result = ReadDesktopSource(parent, original);
                if (FAILED(result)) return result;
            }
            HMODULE module = nullptr;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(WindowProcedure), &module);
            WNDCLASSW cls = {}; cls.lpfnWndProc = WindowProcedure; cls.hInstance = module; cls.lpszClassName = windowClass;
            cls.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            RegisterClassW(&cls);
            RECT bounds; GetClientRect(parent, &bounds);
            window = CreateWindowExW(0, windowClass, L"Fences 桌面", WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, bounds.right, bounds.bottom, parent, nullptr, module, this);
            if (!window) return HRESULT_FROM_WIN32(GetLastError());
            SHChangeNotifyEntry entry = { nullptr, TRUE };
            shellChanges = SHChangeNotifyRegister(window, SHCNRF_ShellLevel | SHCNRF_NewDelivery,
                SHCNE_UPDATEIMAGE | SHCNE_ASSOCCHANGED | SHCNE_DELETE | SHCNE_RMDIR, shellChangeMessage, 1, &entry);
            if (!shellChanges) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            const std::vector<std::wstring> folders = desktop ? original.folders : std::vector<std::wstring>{ folder };
            for (const auto& path : folders)
            {
                PIDLIST_ABSOLUTE location = nullptr;
                auto watched = SHParseDisplayName(path.c_str(), nullptr, &location, 0, nullptr);
                if (FAILED(watched)) return watched;
                SHChangeNotifyEntry watchedEntry = { location, FALSE };
                const auto registration = SHChangeNotifyRegister(window,
                    SHCNRF_InterruptLevel | SHCNRF_ShellLevel | SHCNRF_NewDelivery,
                    SHCNE_CREATE | SHCNE_MKDIR | SHCNE_DELETE | SHCNE_RMDIR,
                    sortChangeMessage, 1, &watchedEntry);
                CoTaskMemFree(location);
                if (!registration) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
                sortChanges.push_back(registration);
            }
            if (desktop)
            {
                const auto binResult = recycleBin.Open();
                if (FAILED(binResult)) return binResult;
                SHChangeNotifyEntry binEntry = { recycleBin.identity, TRUE };
                binChanges = SHChangeNotifyRegister(window, SHCNRF_ShellLevel | SHCNRF_NewDelivery,
                    SHCNE_ALLEVENTS, shellChangeMessage, 1, &binEntry);
                if (!binChanges) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            }
            auto result = CoCreateInstance(CLSID_ExplorerBrowser, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&browser));
            if (FAILED(result)) return result;
            result = browser->SetOptions(static_cast<EXPLORER_BROWSER_OPTIONS>(EBO_NAVIGATEONCE | EBO_NOBORDER | EBO_NOTRAVELLOG | EBO_NOPERSISTVIEWSTATE));
            if (FAILED(result)) return result;
            ComPtr<IObjectWithSite> site;
            result = browser.As(&site);
            if (FAILED(result)) return result;
            result = site->SetSite(static_cast<IServiceProvider*>(this));
            if (FAILED(result)) return result;
            FOLDERSETTINGS settings = { FVM_ICON, FWF_AUTOARRANGE | FWF_DESKTOP | FWF_TRANSPARENT | FWF_ALIGNLEFT | FWF_NOBROWSERVIEWSTATE };
            result = browser->Initialize(window, &bounds, &settings);
            if (FAILED(result)) return result;
            initialized = true;
            result = browser.As(&filterSite);
            if (FAILED(result)) return result;
            result = filterSite->SetFilter(static_cast<IFolderFilter*>(this));
            if (FAILED(result)) return result;
            result = browser->Advise(static_cast<IExplorerBrowserEvents*>(this), &cookie);
            if (FAILED(result)) return result;
            PIDLIST_ABSOLUTE location = nullptr;
            result = folder && *folder ? SHParseDisplayName(folder, nullptr, &location, 0, nullptr)
                : SHGetSpecialFolderLocation(nullptr, CSIDL_DESKTOP, &location);
            if (FAILED(result)) return result;
            result = browser->BrowseToIDList(location, SBSP_ABSOLUTE);
            CoTaskMemFree(location);
            return result;
        }
        void Close()
        {
            closing = true;
            if (pendingNativeEditor && IsWindow(pendingNativeEditor))
                RemoveWindowSubclass(pendingNativeEditor, ChildProcedure, reinterpret_cast<UINT_PTR>(this));
            pendingNativeEditor = nullptr; pendingRename.reset();
            interaction.Close(listWindow);
            sortSelection.Clear();
            while (!dropWindows.empty()) UnregisterDrop(dropWindows.back());
            if (shellChanges) SHChangeNotifyDeregister(shellChanges);
            if (binChanges) SHChangeNotifyDeregister(binChanges);
            for (const auto registration : sortChanges) SHChangeNotifyDeregister(registration);
            sortChanges.clear();
            shellChanges = 0;
            binChanges = 0;
            if (window) KillTimer(window, iconsTimer);
            if (window) KillTimer(window, sortTimer);
            if (browser && cookie) browser->Unadvise(cookie);
            cookie = 0;
            for (const auto child : subclassed) if (IsWindow(child)) RemoveWindowSubclass(child, ChildProcedure, reinterpret_cast<UINT_PTR>(this));
            subclassed.clear();
            view.Reset();
            if (filterSite) filterSite->SetFilter(nullptr);
            filterSite.Reset();
            if (browser)
            {
                ComPtr<IObjectWithSite> site;
                if (SUCCEEDED(browser.As(&site))) site->SetSite(nullptr);
                if (initialized) browser->Destroy();
                browser.Reset();
            }
            initialized = false;
            if (window) DestroyWindow(window);
            window = nullptr;
            ClearWallpaper();
            paintBuffer.Close();
        }
        HRESULT Status() const { return status; }
        HRESULT SetCaptionWindow(HWND value)
        {
            if (value && (!window || !IsChild(window, value))) return E_INVALIDARG;
            if (captionWindow != value) UnregisterDrop(captionWindow);
            if (value)
            {
                const auto result = RegisterDrop(value);
                if (FAILED(result)) return result;
            }
            captionWindow = value; return S_OK;
        }
        HFONT CaptionMetrics(RECT* bounds, int* height)
        {
            if (!view || !window || !listWindow || !bounds || !height) return nullptr;
            DWORD flags = 0; view->GetCurrentFolderFlags(&flags);
            if (flags & FWF_NOICONS) return nullptr;
            GetClientRect(window, bounds);
            const auto font = reinterpret_cast<HFONT>(SendMessageW(listWindow, WM_GETFONT, 0, 0));
            LOGFONTW info = {};
            if (!font || !GetObjectW(font, sizeof(info), &info)) return nullptr;
            *height = std::abs(info.lfHeight); return font;
        }
        HRESULT CaptionBackground(HDC dc)
        {
            if (!dc || !window) return E_INVALIDARG;
            PaintBackground(window, dc); return S_OK;
        }
        int CaptionLineHeight()
        {
            if (!listWindow) return 0;
            const auto dc = GetDC(listWindow);
            if (!dc) return 0;
            const auto font = reinterpret_cast<HFONT>(SendMessageW(listWindow, WM_GETFONT, 0, 0));
            const auto previous = font ? SelectObject(dc, font) : nullptr;
            TEXTMETRICW metrics = {};
            const bool measured = GetTextMetricsW(dc, &metrics) != FALSE;
            if (previous) SelectObject(dc, previous);
            ReleaseDC(listWindow, dc); return measured ? metrics.tmHeight : 0;
        }
        HWND CaptionEditor(RECT* bounds)
        {
            if (!bounds) return nullptr;
            SetRectEmpty(bounds);
            if (!listWindow || !window) return nullptr;
            const auto editor = ListView_GetEditControl(listWindow);
            if (!editor || !IsWindowVisible(editor)) return nullptr;
            GetWindowRect(editor, bounds); MapWindowPoints(nullptr, window, reinterpret_cast<POINT*>(bounds), 2);
            return editor;
        }
        HRESULT CaptionAt(int index, wchar_t* name, UINT capacity, RECT* bounds, DWORD* state)
        {
            if (!name || capacity < 2 || !bounds || !state || !view || !listWindow) return E_INVALIDARG;
            const auto editor = ListView_GetEditControl(listWindow);
            if (editor && IsWindowVisible(editor) && ListView_GetNextItem(listWindow, -1, LVNI_FOCUSED) == index) return S_FALSE;
            if (IsRenaming(index)) return S_FALSE;
            std::wstring text;
            if (!Caption(index, text) || !itemsRenderer.CaptionBounds(listWindow, index, *bounds)) return S_FALSE;
            if (text.size() >= capacity) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
            wcscpy_s(name, capacity, text.c_str());
            MapWindowPoints(listWindow, window, reinterpret_cast<POINT*>(bounds), 2);
            *state = ListView_GetItemState(listWindow, index, LVIS_SELECTED); return S_OK;
        }
        HRESULT CaptionDecoration(int index, RECT* bounds, int* state)
        {
            if (!bounds || !state || !listWindow) return E_INVALIDARG;
            if (!itemsRenderer.ItemBounds(listWindow, index, *bounds)) return S_FALSE;
            MapWindowPoints(listWindow, window, reinterpret_cast<POINT*>(bounds), 2);
            *state = IsRenaming(index) ? LISS_NORMAL : DesktopItemVisualState(listWindow, index, !suppressStationaryHot); return S_OK;
        }
        HRESULT DrawCaptionDecoration(int state, HDC dc, const RECT* bounds)
        {
            if (!listWindow || !dc || !bounds) return E_INVALIDARG;
            DrawDesktopItemBackground(listWindow, state, dc, *bounds); return S_OK;
        }
        HRESULT CaptionSurface(HDC dc)
        {
            if (!dc || !window || !listWindow) return E_INVALIDARG;
            if (!paintBuffer.DC()) { PaintBackground(window, dc); return S_FALSE; }
            RECT bounds; GetClientRect(listWindow, &bounds);
            POINT origin = {}; MapWindowPoints(listWindow, window, &origin, 1);
            return BitBlt(dc, origin.x, origin.y, bounds.right, bounds.bottom, paintBuffer.DC(), 0, 0, SRCCOPY) ? S_OK : E_FAIL;
        }
        UINT_PTR CaptionRevision() const { return presentationRevision; }
        HRESULT CaptionDirty(RECT* bounds)
        {
            if (!bounds) return E_POINTER;
            *bounds = captionDirty; return S_OK;
        }
        void AcknowledgeCaption(UINT_PTR revision)
        {
            if (revision == presentationRevision) SetRectEmpty(&captionDirty);
        }
        HRESULT CaptionSurfaceRegion(HDC dc, const RECT* dirty)
        {
            if (!dc || !dirty || !window || !listWindow) return E_INVALIDARG;
            if (!paintBuffer.DC()) return CaptionSurface(dc);
            POINT origin = {}; MapWindowPoints(listWindow, window, &origin, 1);
            return BitBlt(dc, dirty->left, dirty->top, dirty->right - dirty->left, dirty->bottom - dirty->top,
                paintBuffer.DC(), dirty->left - origin.x, dirty->top - origin.y, SRCCOPY) ? S_OK : E_FAIL;
        }
        HRESULT TakeRename(wchar_t* name, UINT capacity, RECT* bounds, BOOL* file, void** target)
        {
            if (!name || !bounds || !file || !target) return E_POINTER;
            *target = nullptr;
            if (!pendingRename) return S_FALSE;
            if (pendingRename->name.size() >= capacity) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
            wcscpy_s(name, capacity, pendingRename->name.c_str());
            *bounds = pendingRename->bounds; *file = pendingRename->file;
            *target = pendingRename.release(); return S_OK;
        }
        void ActivateRename(DesktopRenameTarget* target)
        {
            suppressStationaryHot = false;
            activeRename = target; DirtyAllCaptions();
            if (listWindow) RedrawWindow(listWindow, nullptr, nullptr, RDW_INVALIDATE | RDW_UPDATENOW);
            QueueLabels();
        }
        void EndRename(BOOL restoreFocus)
        {
            if (!restoreFocus) ResetHoverUntilMovement();
            if (!restoreFocus && listWindow && view)
                for (int index = 0, count = ListView_GetItemCount(listWindow); index < count; ++index)
                    if (IsRenaming(index)) { DesktopInteraction::Deselect(listWindow, view.Get(), index); break; }
            activeRename = nullptr;
            if (restoreFocus && listWindow) SetFocus(listWindow);
            QueueLayout(); QueueLabels();
        }
        HRESULT OriginalMetrics(int* size, POINT* spacing)
        {
            if (!size || !spacing) return E_POINTER;
            if (!desktop) return S_FALSE;
            *size = original.iconSize; *spacing = original.spacing; return S_OK;
        }
        HRESULT TranslateKey(HWND target, UINT message, WPARAM wParam, LPARAM lParam)
        {
            if (closing || !view || !window || (target != window && !IsChild(window, target)) ||
                message < WM_KEYFIRST || message > WM_KEYLAST) return S_FALSE;
            ComPtr<IShellView> shell;
            if (FAILED(view.As(&shell))) return S_FALSE;
            const auto position = GetMessagePos();
            MSG input = { target, message, wParam, lParam, static_cast<DWORD>(GetMessageTime()),
                { GET_X_LPARAM(position), GET_Y_LPARAM(position) } };
            const auto result = shell->TranslateAccelerator(&input);
            // F5 is normally supplied by the Explorer frame rather than its view.
            if (result == S_FALSE && message == WM_KEYDOWN && wParam == VK_F5 &&
                !(GetKeyState(VK_CONTROL) & 0x8000) && !(GetKeyState(VK_MENU) & 0x8000) && !(GetKeyState(VK_SHIFT) & 0x8000))
                return Refresh();
            return result;
        }
        HRESULT Refresh(bool readSource = true)
        {
            captionCache.clear();
            if (!view) return E_PENDING;
            if (desktop && readSource)
            {
                DesktopSource updated;
                const auto result = ReadDesktopSource(GetParent(window), updated);
                if (FAILED(result)) return result;
                original = std::move(updated);
            }
            ComPtr<IShellView> shell; view.As(&shell);
            ++viewRefreshes;
            const auto result = shell->Refresh(); QueueLayout();
            if (listWindow) RedrawWindow(listWindow, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
            return result;
        }
        HRESULT RefreshBackground()
        {
            ++wallpaperInvalidations;
            ClearWallpaper(); Notify(6);
            if (window) RedrawWindow(window, nullptr, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN);
            return S_OK;
        }
        HRESULT Exclude(const wchar_t* paths, UINT characters)
        {
            if (!paths || characters < 2) return E_INVALIDARG;
            auto entries = std::make_shared<std::unordered_set<std::wstring>>();
            UINT offset = 0;
            while (offset < characters && paths[offset])
            {
                UINT end = offset;
                while (end < characters && paths[end]) ++end;
                if (end == characters) return E_INVALIDARG;
                entries->insert(Normalize(std::wstring(paths + offset, end - offset)));
                offset = end + 1;
            }
            std::shared_ptr<const std::unordered_set<std::wstring>> snapshot = entries;
            if (*std::atomic_load(&excluded) == *entries) return S_OK;
            std::atomic_store(&excluded, snapshot);
            // Membership changes do not require another cross-process snapshot
            // of Explorer. Explicit Refresh still updates namespace visibility.
            return view ? Refresh(false) : S_OK;
        }
        HRESULT Resize()
        {
            if (!window || !IsWindow(GetParent(window))) return E_HANDLE;
            RECT bounds; GetClientRect(GetParent(window), &bounds);
            if (!SetWindowPos(window, HWND_BOTTOM, 0, 0, bounds.right, bounds.bottom, SWP_NOACTIVATE))
                return HRESULT_FROM_WIN32(GetLastError());
            QueueLayout(); return S_OK;
        }
        bool BackgroundPoint(POINT screen, bool requireWindow)
        {
            if (!listWindow || !IsWindowVisible(window)) return false;
            DWORD flags = 0; view->GetCurrentFolderFlags(&flags);
            if (flags & FWF_NOICONS)
            {
                RECT bounds; GetWindowRect(window, &bounds);
                const auto hit = WindowFromPoint(screen);
                return PtInRect(&bounds, screen) && (!requireWindow || hit == window || IsChild(window, hit));
            }
            if (requireWindow)
            {
                const auto hit = WindowFromPoint(screen);
                if (hit != listWindow && !IsChild(listWindow, hit) && hit != captionWindow) return false;
            }
            RECT bounds; GetWindowRect(listWindow, &bounds);
            if (!PtInRect(&bounds, screen)) return false;
            ScreenToClient(listWindow, &screen);
            LVHITTESTINFO hit = {}; hit.pt = screen;
            return ListView_HitTest(listWindow, &hit) < 0;
        }
        HRESULT GetPreferences(DesktopPreferences* prefs)
        {
            if (!prefs || prefs->size != sizeof(*prefs) || !view) return E_INVALIDARG;
            FOLDERVIEWMODE mode;
            auto result = view->GetViewModeAndIconSize(&mode, &prefs->iconSize);
            if (FAILED(result)) return result;
            result = view->GetCurrentFolderFlags(&prefs->flags);
            if (FAILED(result)) return result;
            int count = 0;
            result = view->GetSortColumnCount(&count);
            if (FAILED(result)) return result;
            if (count > 0)
            {
                std::vector<SORTCOLUMN> columns(count);
                result = view->GetSortColumns(columns.data(), count);
                if (FAILED(result)) return result;
                primarySort = columns.front();
            }
            // Zero columns is a valid post-drop state, not an invalid argument.
            // Keep the last configured rule for persistence and layout repair.
            prefs->sortFormat = primarySort.propkey.fmtid; prefs->sortProperty = primarySort.propkey.pid; prefs->sortDirection = primarySort.direction;
            return S_OK;
        }
        HRESULT SetPreferences(const DesktopPreferences* prefs)
        {
            if (!prefs || prefs->size != sizeof(*prefs) || !view || prefs->iconSize < 16 || prefs->iconSize > 256) return E_INVALIDARG;
            const DWORD mask = FWF_AUTOARRANGE | FWF_SNAPTOGRID | FWF_NOICONS;
            auto result = view->SetCurrentFolderFlags(mask, prefs->flags & mask);
            if (FAILED(result)) return result;
            result = view->SetViewModeAndIconSize(FVM_ICON, prefs->iconSize);
            if (FAILED(result)) return result;
            SORTCOLUMN sort = { { prefs->sortFormat, prefs->sortProperty }, prefs->sortDirection == SORT_DESCENDING ? SORT_DESCENDING : SORT_ASCENDING };
            result = view->SetSortColumns(&sort, 1);
            if (SUCCEEDED(result)) primarySort = sort;
            QueueLayout(); return result;
        }
        int Count() { int count = -1; if (view) view->ItemCount(SVGIO_ALLVIEW, &count); return count; }
        HRESULT ItemPosition(int index, wchar_t* name, UINT capacity, POINT* point)
        {
            if (!view || !name || capacity < 2 || !point) return E_INVALIDARG;
            PITEMID_CHILD child = nullptr;
            auto result = view->Item(index, &child);
            if (FAILED(result)) return result;
            ComPtr<IShellFolder> folder; view->GetFolder(IID_PPV_ARGS(&folder));
            ComPtr<IShellItem> item;
            result = SHCreateItemWithParent(nullptr, folder.Get(), child, IID_PPV_ARGS(&item));
            PWSTR fullName = nullptr;
            if (SUCCEEDED(result)) result = item->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &fullName);
            if (SUCCEEDED(result))
            {
                result = wcslen(fullName) < capacity ? S_OK : HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
                if (SUCCEEDED(result)) wcscpy_s(name, capacity, fullName);
                CoTaskMemFree(fullName);
            }
            if (SUCCEEDED(result)) result = view->GetItemPosition(child, point);
            CoTaskMemFree(child); return result;
        }
        HRESULT SetItemPosition(const wchar_t* name, POINT point)
        {
            if (!name || !view || !listWindow) return E_INVALIDARG;
            UINT areas = 0; ListView_GetNumberOfWorkAreas(listWindow, &areas);
            std::vector<RECT> bounds(areas);
            if (areas) ListView_GetWorkAreas(listWindow, areas, bounds.data());
            if (areas && std::none_of(bounds.begin(), bounds.end(), [&](const RECT& area) { return PtInRect(&area, point) != FALSE; })) return S_FALSE;
            const auto count = Count();
            std::vector<wchar_t> candidate(32768);
            for (int index = 0; index < count; ++index)
            {
                POINT current;
                if (FAILED(ItemPosition(index, candidate.data(), static_cast<UINT>(candidate.size()), &current)) || _wcsicmp(candidate.data(), name) != 0) continue;
                if (current.x == point.x && current.y == point.y) return S_OK;
                PITEMID_CHILD child = nullptr;
                const auto found = view->Item(index, &child);
                if (FAILED(found)) return found;
                PCUITEMID_CHILD children[] = { child };
                const auto result = view->SelectAndPositionItems(1, children, &point, SVSI_POSITIONITEM);
                CoTaskMemFree(child); return result;
            }
            return S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid == IID_IUnknown || iid == IID_IFolderFilter) *result = static_cast<IFolderFilter*>(this);
            else if (iid == IID_IExplorerBrowserEvents) *result = static_cast<IExplorerBrowserEvents*>(this);
            else if (iid == IID_IServiceProvider) *result = static_cast<IServiceProvider*>(this);
            else if (iid == IID_ICommDlgBrowser || iid == IID_ICommDlgBrowser2 || iid == IID_ICommDlgBrowser3) *result = static_cast<ICommDlgBrowser3*>(this);
            else return E_NOINTERFACE;
            AddRef(); return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
        ULONG STDMETHODCALLTYPE Release() override { const auto value = InterlockedDecrement(&references); if (!value) delete this; return value; }
        HRESULT STDMETHODCALLTYPE ShouldShow(IShellFolder* folder, PCIDLIST_ABSOLUTE, PCUITEMID_CHILD child) override
        {
            try
            {
                ComPtr<IShellItem> item;
                if (FAILED(SHCreateItemWithParent(nullptr, folder, child, IID_PPV_ARGS(&item)))) return S_OK;
                PWSTR path = nullptr;
                const bool file = SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path));
                const auto normalized = file ? Normalize(path) : std::wstring(); CoTaskMemFree(path);
                if (desktop && !original.Allows(item.Get(), normalized)) return S_FALSE;
                if (!file) return S_OK;
                const auto snapshot = std::atomic_load(&excluded);
                return snapshot->count(normalized) ? S_FALSE : S_OK;
            }
            catch (...) { return S_OK; }
        }
        HRESULT STDMETHODCALLTYPE GetEnumFlags(IShellFolder*, PCIDLIST_ABSOLUTE, HWND*, DWORD* flags) override
        {
            if (!flags) return E_POINTER;
            SHELLSTATE state = {}; SHGetSetSettings(&state, SSF_SHOWALLOBJECTS | SSF_SHOWSUPERHIDDEN, FALSE);
            *flags = SHCONTF_FOLDERS | SHCONTF_NONFOLDERS;
            if (state.fShowAllObjects) *flags |= SHCONTF_INCLUDEHIDDEN;
            if (state.fShowSuperHidden) *flags |= SHCONTF_INCLUDESUPERHIDDEN;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE OnNavigationPending(PCIDLIST_ABSOLUTE) override { ++enumerationStarts; return S_OK; }
        HRESULT STDMETHODCALLTYPE OnViewCreated(IShellView* created) override
        {
            created->QueryInterface(IID_PPV_ARGS(&view)); AttachChildren(); QueueLayout(); return S_OK;
        }
        HRESULT STDMETHODCALLTYPE OnNavigationComplete(PCIDLIST_ABSOLUTE) override
        {
            if (!view) browser->GetCurrentView(IID_PPV_ARGS(&view));
            AttachChildren(); status = listWindow ? S_OK : E_NOINTERFACE; QueueLayout(); Notify(0); return S_OK;
        }
        HRESULT STDMETHODCALLTYPE OnNavigationFailed(PCIDLIST_ABSOLUTE) override { status = E_FAIL; Notify(3); return S_OK; }
        HRESULT STDMETHODCALLTYPE QueryService(REFGUID service, REFIID iid, void** result) override
        {
            if (service == SID_SExplorerBrowserFrame) return QueryInterface(iid, result);
            if (result) *result = nullptr; return E_NOINTERFACE;
        }
        HRESULT STDMETHODCALLTYPE OnDefaultCommand(IShellView*) override
        {
            if (!view) return E_FAIL;
            ComPtr<IShellItemArray> items;
            if (FAILED(view->GetSelection(FALSE, &items))) return S_FALSE;
            DWORD count = 0; items->GetCount(&count);
            for (DWORD index = 0; index < count; ++index)
            {
                ComPtr<IShellItem> item; items->GetItemAt(index, &item);
                PIDLIST_ABSOLUTE pidl = nullptr;
                if (!item || FAILED(SHGetIDListFromObject(item.Get(), &pidl))) continue;
                SHELLEXECUTEINFOW execute = { sizeof(execute) };
                execute.fMask = SEE_MASK_IDLIST; execute.hwnd = window; execute.lpIDList = pidl; execute.nShow = SW_SHOWNORMAL;
                ShellExecuteExW(&execute); CoTaskMemFree(pidl);
            }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE OnStateChange(IShellView*, ULONG change) override
        {
            if (change == CDBOSC_RENAME) { captionCache.clear(); QueueLayout(); }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE IncludeObject(IShellView*, PCUITEMID_CHILD) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE Notify(IShellView*, DWORD) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE GetDefaultMenuText(IShellView*, LPWSTR, int) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE GetViewFlags(DWORD* flags) override { if (!flags) return E_POINTER; *flags = 0; return S_OK; }
        HRESULT STDMETHODCALLTYPE OnColumnClicked(IShellView*, int) override { return S_OK; }
        HRESULT STDMETHODCALLTYPE GetCurrentFilter(LPWSTR text, int count) override { if (text && count > 0) text[0] = 0; return S_OK; }
        HRESULT STDMETHODCALLTYPE OnPreViewCreated(IShellView*) override { return S_OK; }
    };
    template<class Action> HRESULT Guard(Action action)
    {
        try { return action(); }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        catch (...) { return E_FAIL; }
    }
}

HRESULT __cdecl FencesCreateView(HWND parent, const wchar_t* folder, BOOL wallpaper, HWND notify, UINT message, void** result)
{
    return FencesCreateFilteredView(parent, folder, wallpaper, notify, message, nullptr, 0, result);
}
HRESULT __cdecl FencesCreateFilteredView(HWND parent, const wchar_t* folder, BOOL wallpaper, HWND notify, UINT message,
    const wchar_t* excluded, UINT characters, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (!IsWindow(parent)) return E_INVALIDARG;
    return Guard([&]() -> HRESULT
    {
        auto initialized = OleInitialize(nullptr);
        if (FAILED(initialized)) return initialized;
        struct OleScope
        {
            bool retained = false;
            ~OleScope() { if (!retained) OleUninitialize(); }
        } apartment;
        struct ViewCloser
        {
            void operator()(DesktopView* value) const { value->Close(); value->Release(); }
        };
        std::unique_ptr<DesktopView, ViewCloser> context(new DesktopView(wallpaper != FALSE, notify, message));
        if (excluded || characters)
        {
            const auto filtered = context->Exclude(excluded, characters);
            if (FAILED(filtered)) return filtered;
        }
        const auto opened = context->Open(parent, folder);
        if (FAILED(opened)) return opened;
        *result = context.release(); apartment.retained = true; return S_OK;
    });
}
void __cdecl FencesDestroyView(void* context)
{
    if (!context) return;
    auto view = static_cast<DesktopView*>(context);
    view->Close(); view->Release(); OleUninitialize();
}
HWND __cdecl FencesViewWindow(void* context) { return context ? static_cast<DesktopView*>(context)->window : nullptr; }
HRESULT __cdecl FencesViewStatus(void* context) { return context ? static_cast<DesktopView*>(context)->Status() : E_POINTER; }
HRESULT __cdecl FencesExcludePaths(void* context, const wchar_t* paths, UINT characters)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->Exclude(paths, characters); }) : E_POINTER; }
HRESULT __cdecl FencesRefreshView(void* context)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->Refresh(); }) : E_POINTER; }
HRESULT __cdecl FencesRefreshBackground(void* context)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->RefreshBackground(); }) : E_POINTER; }
HRESULT __cdecl FencesTranslateKey(void* context, HWND target, UINT message, WPARAM wParam, LPARAM lParam)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->TranslateKey(target, message, wParam, lParam); }) : S_FALSE; }
HRESULT __cdecl FencesResizeView(void* context)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->Resize(); }) : E_POINTER; }
HRESULT __cdecl FencesGetPreferences(void* context, DesktopPreferences* prefs)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->GetPreferences(prefs); }) : E_POINTER; }
HRESULT __cdecl FencesSetPreferences(void* context, const DesktopPreferences* prefs)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->SetPreferences(prefs); }) : E_POINTER; }
BOOL __cdecl FencesIsBackgroundPoint(void* context, POINT point)
{ return context && static_cast<DesktopView*>(context)->BackgroundPoint(point, true); }
int __cdecl FencesVisibleCount(void* context) { return context ? static_cast<DesktopView*>(context)->Count() : -1; }
HRESULT __cdecl FencesItemPosition(void* context, int index, wchar_t* name, UINT capacity, POINT* point)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->ItemPosition(index, name, capacity, point); }) : E_POINTER; }
HRESULT __cdecl FencesSetItemPosition(void* context, const wchar_t* name, POINT point)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->SetItemPosition(name, point); }) : E_POINTER; }
HRESULT __cdecl FencesOriginalMetrics(void* context, int* size, POINT* spacing)
{ return context ? static_cast<DesktopView*>(context)->OriginalMetrics(size, spacing) : E_POINTER; }
HRESULT __cdecl FencesSetCaptionWindow(void* context, HWND window)
{ return context ? static_cast<DesktopView*>(context)->SetCaptionWindow(window) : E_POINTER; }
HFONT __cdecl FencesCaptionMetrics(void* context, RECT* bounds, int* height)
{ return context ? static_cast<DesktopView*>(context)->CaptionMetrics(bounds, height) : nullptr; }
HRESULT __cdecl FencesCaptionAt(void* context, int index, wchar_t* name, UINT capacity, RECT* bounds, DWORD* state)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->CaptionAt(index, name, capacity, bounds, state); }) : E_POINTER; }
HRESULT __cdecl FencesCaptionBackground(void* context, HDC dc)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->CaptionBackground(dc); }) : E_POINTER; }
int __cdecl FencesCaptionLineHeight(void* context)
{ return context ? static_cast<DesktopView*>(context)->CaptionLineHeight() : 0; }
HWND __cdecl FencesCaptionEditor(void* context, RECT* bounds)
{ return context ? static_cast<DesktopView*>(context)->CaptionEditor(bounds) : nullptr; }
HRESULT __cdecl FencesCaptionDecoration(void* context, int index, RECT* bounds, int* state)
{ return context ? static_cast<DesktopView*>(context)->CaptionDecoration(index, bounds, state) : E_POINTER; }
HRESULT __cdecl FencesDrawCaptionDecoration(void* context, int state, HDC dc, const RECT* bounds)
{ return context ? static_cast<DesktopView*>(context)->DrawCaptionDecoration(state, dc, bounds) : E_POINTER; }
HRESULT __cdecl FencesCaptionSurface(void* context, HDC dc)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->CaptionSurface(dc); }) : E_POINTER; }
UINT_PTR __cdecl FencesCaptionRevision(void* context)
{ return context ? static_cast<DesktopView*>(context)->CaptionRevision() : 0; }
HRESULT __cdecl FencesCaptionDirty(void* context, RECT* bounds)
{ return context ? static_cast<DesktopView*>(context)->CaptionDirty(bounds) : E_POINTER; }
void __cdecl FencesAcknowledgeCaption(void* context, UINT_PTR revision)
{ if (context) static_cast<DesktopView*>(context)->AcknowledgeCaption(revision); }
HRESULT __cdecl FencesCaptionSurfaceRegion(void* context, HDC dc, const RECT* bounds)
{ return context ? static_cast<DesktopView*>(context)->CaptionSurfaceRegion(dc, bounds) : E_POINTER; }
HRESULT __cdecl FencesTakeRename(void* context, wchar_t* name, UINT capacity, RECT* bounds, BOOL* file, void** target)
{ return context ? Guard([&] { return static_cast<DesktopView*>(context)->TakeRename(name, capacity, bounds, file, target); }) : E_POINTER; }
HRESULT __cdecl FencesCommitRename(void* target, HWND owner, const wchar_t* name)
{
    if (!target) return E_POINTER;
    auto session = static_cast<DesktopRenameTarget*>(target); session->AddRef();
    const auto result = Guard([&] { return session->Commit(owner, name); }); session->Release(); return result;
}
void __cdecl FencesActivateRename(void* context, void* target)
{ if (context) static_cast<DesktopView*>(context)->ActivateRename(static_cast<DesktopRenameTarget*>(target)); }
void __cdecl FencesEndRename(void* context, BOOL restoreFocus)
{ if (context) static_cast<DesktopView*>(context)->EndRename(restoreFocus); }
void __cdecl FencesReleaseRename(void* target) { if (target) static_cast<DesktopRenameTarget*>(target)->Release(); }
