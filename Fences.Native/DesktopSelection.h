#pragma once
#include <commctrl.h>
#include <uxtheme.h>
#include <vsstyle.h>

inline int DesktopItemVisualState(HWND list, int index, bool allowHot = true)
{
    const bool selected = (ListView_GetItemState(list, index, LVIS_SELECTED | LVIS_DROPHILITED) & (LVIS_SELECTED | LVIS_DROPHILITED)) != 0;
    const bool hot = allowHot && ListView_GetHotItem(list) == index;
    if (!selected) return hot ? LISS_HOT : LISS_NORMAL;
    const auto focus = GetFocus();
    if (focus != list && !IsChild(list, focus)) return LISS_SELECTEDNOTFOCUS;
    return hot ? LISS_HOTSELECTED : LISS_SELECTED;
}

inline void DrawDesktopItemBackground(HWND list, int state, HDC dc, const RECT& bounds)
{
    if (state == LISS_NORMAL) return;
    const auto theme = GetWindowTheme(list);
    if (theme && SUCCEEDED(DrawThemeBackground(theme, dc, LVP_LISTITEM, state, &bounds, nullptr))) return;
    if (state == LISS_SELECTED || state == LISS_SELECTEDNOTFOCUS || state == LISS_HOTSELECTED)
        FillRect(dc, &bounds, GetSysColorBrush(COLOR_HIGHLIGHT));
}
