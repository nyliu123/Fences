#pragma once
#include <windows.h>
#include <commctrl.h>
#include <functional>
#include <string>

// The native list still owns icon images, layout, hit testing and editing.
// Caption ink is presented by a separate layered surface. Erase native ink
// here so normal/hover text cannot bleed through or double up beneath it.
inline void PaintDesktopLabelBackgrounds(HWND list, HDC dc, const RECT& update,
    const std::function<void(HDC)>& background,
    const std::function<bool(int, std::wstring&)>& caption)
{
    const auto editor = ListView_GetEditControl(list);
    const int editing = editor && IsWindowVisible(editor) ? ListView_GetNextItem(list, -1, LVNI_FOCUSED) : -1;
    const int saved = SaveDC(dc);
    for (int index = 0; index < ListView_GetItemCount(list); ++index)
    {
        if (index == editing) continue;
        RECT label, visible;
        if (!ListView_GetItemRect(list, index, &label, LVIR_LABEL) || !IntersectRect(&visible, &label, &update)) continue;
        std::wstring text;
        if (!caption(index, text) || text.empty()) continue;
        const int clipped = SaveDC(dc);
        IntersectClipRect(dc, label.left, label.top, label.right, label.bottom);
        background(dc);
        RestoreDC(dc, clipped);
    }
    RestoreDC(dc, saved);
}
