#pragma once
#include "DesktopSelection.h"
#include <gdiplus.h>
#include <commoncontrols.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <functional>
#include <string>
#include <unordered_map>

// One background pass covers each icon and its caption. The caption layer
// copies these completed pixels; it never interprets the theme independently.
class DesktopItemRenderer
{
    ULONG_PTR token = 0;
    HFONT sourceFont = nullptr;
    float lineHeight = 0;
    int nativeLineHeight = 0;
    UINT fontDpi = 0;
    std::unordered_map<int, RECT> previousBounds;
public:
    DesktopItemRenderer() { Gdiplus::GdiplusStartupInput input; Gdiplus::GdiplusStartup(&token, &input, nullptr); }
    ~DesktopItemRenderer() { if (token) Gdiplus::GdiplusShutdown(token); }
    void ResetLayout() { previousBounds.clear(); }
    void ResetFont() { sourceFont = nullptr; lineHeight = 0; nativeLineHeight = 0; }
    bool CaptionBounds(HWND list, int index, RECT& bounds)
    {
        if (!ListView_GetItemRect(list, index, &bounds, LVIR_LABEL)) return false;
        const auto font = reinterpret_cast<HFONT>(SendMessageW(list, WM_GETFONT, 0, 0));
        const auto dpi = GetDpiForWindow(list);
        if (sourceFont != font || lineHeight <= 0 || dpi != fontDpi)
        {
            const auto dc = GetDC(list);
            const auto previous = SelectObject(dc, font);
            TEXTMETRICW metrics = {}; GetTextMetricsW(dc, &metrics);
            LOGFONTW value = {}; GetObjectW(font, sizeof(value), &value);
            const int style = (value.lfWeight >= FW_BOLD ? Gdiplus::FontStyleBold : 0) |
                (value.lfItalic ? Gdiplus::FontStyleItalic : 0) | (value.lfUnderline ? Gdiplus::FontStyleUnderline : 0) |
                (value.lfStrikeOut ? Gdiplus::FontStyleStrikeout : 0);
            Gdiplus::Font drawing(value.lfFaceName, static_cast<Gdiplus::REAL>(std::abs(value.lfHeight)), style, Gdiplus::UnitPixel);
            lineHeight = drawing.GetLastStatus() == Gdiplus::Ok ? drawing.GetHeight(static_cast<Gdiplus::REAL>(GetDeviceCaps(dc, LOGPIXELSY))) : static_cast<float>(metrics.tmHeight);
            sourceFont = font;
            nativeLineHeight = (std::max)(1L, metrics.tmHeight); fontDpi = dpi;
            SelectObject(dc, previous); ReleaseDC(list, dc);
        }
        const int lines = (std::max)(1, static_cast<int>(std::round(static_cast<double>(bounds.bottom - bounds.top) / nativeLineHeight)));
        bounds.bottom = (std::max)(bounds.bottom, bounds.top + static_cast<LONG>(std::ceil(lineHeight * lines)));
        return true;
    }
    bool DirtyBounds(HWND list, int index, RECT& bounds)
    {
        const bool found = ItemBounds(list, index, bounds);
        const auto previous = previousBounds.find(index);
        if (previous != previousBounds.end())
        {
            if (found) UnionRect(&bounds, &bounds, &previous->second); else bounds = previous->second;
        }
        return found || previous != previousBounds.end();
    }
    bool ItemBounds(HWND list, int index, RECT& bounds)
    {
        RECT label;
        if (!ListView_GetItemRect(list, index, &bounds, LVIR_BOUNDS) || !CaptionBounds(list, index, label)) return false;
        bounds.left = (std::min)(bounds.left, label.left); bounds.right = (std::max)(bounds.right, label.right);
        bounds.bottom = (std::max)(bounds.bottom, label.bottom + 2); return true;
    }
    bool Draw(HWND list, HDC dc, const RECT& update, const std::function<bool(int, std::wstring&)>& caption, int replacedIcon,
        const std::function<bool(int)>& isEditing, bool allowHot = true)
    {
        const auto images = ListView_GetImageList(list, LVSIL_NORMAL);
        int width = 0, height = 0;
        if (!images || !ImageList_GetIconSize(images, &width, &height)) return false;
        Microsoft::WRL::ComPtr<IImageList2> sizedImages;
        // Keep the existing export usable by hosts without a v6 activation
        // context. Fences itself declares v6 and uses original image geometry.
        using QueryImages = HRESULT(WINAPI*)(HIMAGELIST, REFIID, void**);
        static const auto queryImages = []() -> QueryImages
        {
            HMODULE module = nullptr;
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(ImageList_GetIconSize), &module)) return nullptr;
            return reinterpret_cast<QueryImages>(GetProcAddress(module, "HIMAGELIST_QueryInterface"));
        }();
        if (queryImages) queryImages(images, IID_PPV_ARGS(&sizedImages));
        for (int index = 0; index < ListView_GetItemCount(list); ++index)
        {
            RECT item, visible;
            if (!ItemBounds(list, index, item) || !IntersectRect(&visible, &item, &update)) continue;
            previousBounds[index] = item;
            std::wstring text;
            if (!caption(index, text)) continue;
            const bool editing = isEditing(index);
            if (!editing) DrawDesktopItemBackground(list, DesktopItemVisualState(list, index, allowHot), dc, item);
            if (index == replacedIcon) continue;
            LVITEMW value = {}; value.mask = LVIF_IMAGE | LVIF_STATE; value.iItem = index;
            value.stateMask = LVIS_OVERLAYMASK | LVIS_CUT | LVIS_FOCUSED;
            RECT icon;
            if (!ListView_GetItem(list, &value) || !ListView_GetItemRect(list, index, &icon, LVIR_ICON)) continue;
            UINT flags = ILD_TRANSPARENT | (value.state & LVIS_OVERLAYMASK);
            if (value.state & LVIS_CUT) flags |= ILD_BLEND50;
            int cx = width, cy = height;
            if (sizedImages)
            {
                int originalWidth = 0, originalHeight = 0;
                if (SUCCEEDED(sizedImages->GetOriginalSize(value.iImage, ILGOS_FROMSTANDBY, &originalWidth, &originalHeight)) &&
                    originalWidth > 0 && originalHeight > 0)
                {
                    const auto scale = (std::min)(1.0, (std::min)(static_cast<double>(width) / originalWidth, static_cast<double>(height) / originalHeight));
                    cx = (std::max)(1, static_cast<int>(std::round(originalWidth * scale)));
                    cy = (std::max)(1, static_cast<int>(std::round(originalHeight * scale)));
                }
            }
            const int verticalInset = (std::max)(0L, (icon.bottom - icon.top - height) / 2);
            ImageList_DrawEx(images, value.iImage, dc, icon.left + (icon.right - icon.left - cx) / 2, icon.bottom - cy - verticalInset,
                cx, cy, CLR_NONE, CLR_NONE, flags | ILD_SCALE);
            if (!editing && (value.state & LVIS_FOCUSED) && GetFocus() == list && !(SendMessageW(list, WM_QUERYUISTATE, 0, 0) & UISF_HIDEFOCUS))
                DrawFocusRect(dc, &item);
        }
        return true;
    }
};
