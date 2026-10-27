#pragma once
#include <commctrl.h>
#include <windowsx.h>

// Keep the Shell's native drag, multi-select and rename editor. The fallback
// timer only repairs a slow label click that did not open the native editor.
class DesktopInteraction
{
    static constexpr UINT_PTR renameTimer = 0xF317;
    int renameItem = -1;
    POINT pressed = {};
    bool labelPress = false;
    bool blankPress = false;
    bool blankDragged = false;
    UINT_PTR selectionEpoch = 0;
    bool clearPending = false;
    void Cancel(HWND list)
    {
        KillTimer(list, renameTimer); renameItem = -1; labelPress = false;
    }
    static bool Modified()
    {
        return (GetKeyState(VK_CONTROL) | GetKeyState(VK_SHIFT) | GetKeyState(VK_MENU)) & 0x8000;
    }
public:
    static void Deselect(HWND list, IFolderView2* view, int index = -1, bool clearHot = true)
    {
        IShellView* shell = nullptr;
        if (view && SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&shell))))
        {
            PITEMID_CHILD child = nullptr;
            if (index < 0 || SUCCEEDED(view->Item(index, &child)))
                shell->SelectItem(child, (index < 0 ? SVSI_DESELECTOTHERS : SVSI_DESELECT) | SVSI_NOTAKEFOCUS);
            CoTaskMemFree(child); shell->Release();
        }
        ListView_SetItemState(list, index, 0, LVIS_SELECTED | LVIS_FOCUSED | LVIS_DROPHILITED);
        if (index < 0 || ListView_GetSelectionMark(list) == index) ListView_SetSelectionMark(list, -1);
        if (clearHot && (index < 0 || ListView_GetHotItem(list) == index)) ListView_SetHotItem(list, -1);
    }
    UINT_PTR SelectionEpoch() const { return selectionEpoch; }
    bool FlushSelection(HWND list, IFolderView2* view, UINT_PTR epoch)
    {
        if (!clearPending || epoch != selectionEpoch) return false;
        clearPending = false; Deselect(list, view, -1, false); return true;
    }
    void Close(HWND list) { if (list) Cancel(list); }
    bool Before(HWND list, UINT message, WPARAM wParam, LPARAM lParam, IFolderView2* view)
    {
        if (message == WM_LBUTTONDOWN || message == WM_LBUTTONDBLCLK || message == WM_RBUTTONDOWN || message == WM_KEYDOWN)
        { ++selectionEpoch; clearPending = false; }
        if (message == WM_RBUTTONDOWN || message == WM_LBUTTONDBLCLK || message == WM_KEYDOWN || message == WM_CANCELMODE)
            blankPress = false;
        if (message == WM_TIMER && wParam == renameTimer)
        {
            const int item = renameItem;
            Cancel(list);
            if (item >= 0 && GetFocus() == list && !Modified() && !(GetAsyncKeyState(VK_LBUTTON) & 0x8000) && !ListView_GetEditControl(list) &&
                ListView_GetSelectedCount(list) == 1 &&
                ListView_GetItemState(list, item, LVIS_SELECTED | LVIS_FOCUSED) == (LVIS_SELECTED | LVIS_FOCUSED))
                view->SelectItem(item, SVSI_EDIT);
            return true;
        }
        if (message == WM_LBUTTONDBLCLK || message == WM_RBUTTONDOWN || message == WM_KEYDOWN ||
            message == WM_KILLFOCUS || message == WM_CANCELMODE) Cancel(list);
        if (message == WM_MOUSEMOVE && labelPress &&
            (std::abs(GET_X_LPARAM(lParam) - pressed.x) > GetSystemMetrics(SM_CXDRAG) / 2 ||
             std::abs(GET_Y_LPARAM(lParam) - pressed.y) > GetSystemMetrics(SM_CYDRAG) / 2)) Cancel(list);
        if (message == WM_MOUSEMOVE && blankPress &&
            (std::abs(GET_X_LPARAM(lParam) - pressed.x) > GetSystemMetrics(SM_CXDRAG) / 2 ||
             std::abs(GET_Y_LPARAM(lParam) - pressed.y) > GetSystemMetrics(SM_CYDRAG) / 2)) blankDragged = true;
        if (message == WM_LBUTTONDOWN)
        {
            Cancel(list);
            pressed = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
            LVHITTESTINFO hit = {}; hit.pt = pressed;
            const int item = ListView_HitTest(list, &hit);
            blankPress = item < 0 && !Modified();
            blankDragged = false;
            // A single label click may start rename even when the icon was not
            // previously selected. Native selection still runs before the timer.
            if (item >= 0 && (hit.flags & LVHT_ONITEMLABEL) && !Modified())
            {
                renameItem = item; labelPress = true;
            }
        }
        return false;
    }
    bool After(HWND list, UINT message, LPARAM lParam, IFolderView2* view)
    {
        bool requestClear = false;
        // DefView can restore the previously opened item while activating the
        // desktop. Apply a plain background click after its activation handler.
        if ((message == WM_LBUTTONDOWN || message == WM_LBUTTONUP) && blankPress)
        {
            POINT released = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
            const bool completed = message == WM_LBUTTONUP || !(GetAsyncKeyState(VK_LBUTTON) & 0x8000);
            if (message == WM_LBUTTONDOWN && completed) { GetCursorPos(&released); ScreenToClient(list, &released); }
            if (completed && !blankDragged && std::abs(released.x - pressed.x) <= GetSystemMetrics(SM_CXDRAG) / 2 &&
                std::abs(released.y - pressed.y) <= GetSystemMetrics(SM_CYDRAG) / 2)
            {
                Deselect(list, view); clearPending = requestClear = true;
            }
            if (completed) blankPress = false;
        }
        // ListView's modal mouse loop can consume BUTTONUP internally.
        if ((message == WM_LBUTTONDOWN || message == WM_LBUTTONUP) && labelPress && renameItem >= 0)
        {
            SetTimer(list, renameTimer, GetDoubleClickTime() + 50, nullptr);
        }
        return requestClear;
    }
};
