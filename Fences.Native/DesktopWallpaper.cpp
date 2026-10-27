#include "DesktopView.h"
#include "DesktopWallpaper.h"
#include <gdiplus.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <memory>

using Microsoft::WRL::ComPtr;
namespace
{
    struct DrawingSession
    {
        ULONG_PTR token = 0;
        DrawingSession() { Gdiplus::GdiplusStartupInput input; Gdiplus::GdiplusStartup(&token, &input, nullptr); }
        ~DrawingSession() { if (token) Gdiplus::GdiplusShutdown(token); }
    };
    bool DrawImage(HDC dc, const wchar_t* path, const RECT& bounds, DESKTOP_WALLPAPER_POSITION position)
    {
        Gdiplus::Bitmap image(path);
        if (image.GetLastStatus() != Gdiplus::Ok || !image.GetWidth() || !image.GetHeight()) return false;
        const int width = bounds.right - bounds.left, height = bounds.bottom - bounds.top;
        if (width <= 0 || height <= 0) return false;
        Gdiplus::Graphics graphics(dc);
        graphics.SetClip(Gdiplus::Rect(bounds.left, bounds.top, width, height));
        graphics.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
        graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
        Gdiplus::ImageAttributes attributes;
        attributes.SetWrapMode(Gdiplus::WrapModeTileFlipXY);
        const int sourceWidth = static_cast<int>(image.GetWidth()), sourceHeight = static_cast<int>(image.GetHeight());
        auto draw = [&](int x, int y, int w, int h)
        {
            return graphics.DrawImage(&image, Gdiplus::Rect(x, y, w, h), 0, 0,
                sourceWidth, sourceHeight, Gdiplus::UnitPixel, &attributes) == Gdiplus::Ok;
        };
        if (position == DWPOS_TILE)
        {
            const int startX = bounds.left - ((bounds.left % sourceWidth + sourceWidth) % sourceWidth);
            const int startY = bounds.top - ((bounds.top % sourceHeight + sourceHeight) % sourceHeight);
            for (int y = startY; y < bounds.bottom; y += sourceHeight)
                for (int x = startX; x < bounds.right; x += sourceWidth)
                    if (!draw(x, y, sourceWidth, sourceHeight)) return false;
            return true;
        }
        if (position == DWPOS_STRETCH) return draw(bounds.left, bounds.top, width, height);
        double scale = 1;
        if (position != DWPOS_CENTER)
        {
            const double horizontal = static_cast<double>(width) / sourceWidth, vertical = static_cast<double>(height) / sourceHeight;
            scale = position == DWPOS_FIT ? (std::min)(horizontal, vertical) : (std::max)(horizontal, vertical);
        }
        const int scaledWidth = static_cast<int>(std::round(sourceWidth * scale)), scaledHeight = static_cast<int>(std::round(sourceHeight * scale));
        return draw(bounds.left + (width - scaledWidth) / 2, bounds.top + (height - scaledHeight) / 2, scaledWidth, scaledHeight);
    }
}

bool PaintCurrentDesktopWallpaper(HDC dc, const RECT& virtualScreen)
{
    ComPtr<IDesktopWallpaper> desktop;
    if (FAILED(CoCreateInstance(CLSID_DesktopWallpaper, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&desktop)))) return false;
    DESKTOP_WALLPAPER_POSITION position;
    COLORREF color;
    UINT monitors = 0;
    if (FAILED(desktop->GetPosition(&position)) || FAILED(desktop->GetBackgroundColor(&color)) ||
        FAILED(desktop->GetMonitorDevicePathCount(&monitors))) return false;
    const auto brush = CreateSolidBrush(color);
    if (!brush) return false;
    FillRect(dc, &virtualScreen, brush); DeleteObject(brush);
    DrawingSession session;
    if (!session.token) return false;
    for (UINT index = 0; index < monitors; ++index)
    {
        PWSTR monitor = nullptr;
        if (FAILED(desktop->GetMonitorDevicePathAt(index, &monitor))) return false;
        RECT bounds = {};
        PWSTR path = nullptr;
        auto result = desktop->GetMonitorRECT(monitor, &bounds);
        if (SUCCEEDED(result)) result = desktop->GetWallpaper(monitor, &path);
        CoTaskMemFree(monitor);
        if (FAILED(result)) { CoTaskMemFree(path); return false; }
        // An empty path means the user's solid desktop background.
        const bool drawn = !path || !*path || DrawImage(dc, path,
            position == DWPOS_SPAN || position == DWPOS_TILE ? virtualScreen : bounds, position);
        CoTaskMemFree(path);
        if (!drawn) return false;
        if (position == DWPOS_SPAN || position == DWPOS_TILE) break;
    }
    return true;
}
