#include "../Fences.Native/DesktopWallpaper.cpp"
#include "../Fences.Native/OpaquePaintBuffer.h"
#include <iostream>
#include <stdexcept>
#include <vector>

namespace
{
    void Check(bool value, const char* message)
    {
        if (!value) throw std::runtime_error(message);
        std::cout << "PASS: " << message << std::endl;
    }
    CLSID PngEncoder()
    {
        UINT count = 0, bytes = 0;
        Gdiplus::GetImageEncodersSize(&count, &bytes);
        std::vector<BYTE> buffer(bytes);
        auto codecs = reinterpret_cast<Gdiplus::ImageCodecInfo*>(buffer.data());
        Gdiplus::GetImageEncoders(count, bytes, codecs);
        for (UINT i = 0; i < count; ++i) if (wcscmp(codecs[i].MimeType, L"image/png") == 0) return codecs[i].Clsid;
        throw std::runtime_error("PNG encoder unavailable");
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const auto apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(apartment)) return 2;
    const auto display = GetDC(nullptr);
    wchar_t temporary[MAX_PATH], sourcePath[MAX_PATH];
    GetTempPathW(MAX_PATH, temporary); GetTempFileNameW(temporary, L"Fnc", 0, sourcePath);
    int result = 1;
    try
    {
        DrawingSession session;
        auto png = PngEncoder();
        {
            Gdiplus::Bitmap source(80, 40, PixelFormat32bppARGB);
            Gdiplus::Graphics graphics(&source);
            Gdiplus::SolidBrush red(Gdiplus::Color(255, 220, 40, 40)), blue(Gdiplus::Color(255, 35, 90, 220));
            graphics.FillRectangle(&red, 0, 0, 40, 40); graphics.FillRectangle(&blue, 40, 0, 40, 40);
            Check(source.Save(sourcePath, &png) == Gdiplus::Ok, "create owned wallpaper fixture");
        }
        OpaquePaintBuffer surface;
        Check(surface.Ensure(display, 660, 360), "allocate wallpaper layout surface");
        const auto dc = surface.DC();
        const COLORREF background = RGB(30, 40, 52);
        RECT full = { 0, 0, 660, 360 };
        const auto brush = CreateSolidBrush(background); FillRect(dc, &full, brush); DeleteObject(brush);
        const DESKTOP_WALLPAPER_POSITION positions[] = { DWPOS_CENTER, DWPOS_FIT, DWPOS_FILL, DWPOS_STRETCH, DWPOS_TILE, DWPOS_SPAN };
        const wchar_t* names[] = { L"Center", L"Fit", L"Fill", L"Stretch", L"Tile", L"Span" };
        for (int index = 0; index < 6; ++index)
        {
            const int x = 10 + (index % 3) * 220, y = 30 + (index / 3) * 180;
            RECT bounds = { x, y, x + 200, y + 140 };
            Check(DrawImage(dc, sourcePath, bounds, positions[index]), "render configured wallpaper placement");
            const auto top = GetPixel(dc, x + 20, y + 3);
            if (positions[index] == DWPOS_FIT || positions[index] == DWPOS_CENTER)
                Check(top == background, "fit and center preserve background margins");
            else Check(top != background, "fill, stretch, tile and span cover the full monitor");
            if (positions[index] == DWPOS_TILE)
                Check(GetRValue(GetPixel(dc, x + (80 - x % 80) % 80 + 5, y + 40)) > 150, "tile remains anchored to desktop coordinates");
            else
            {
                Check(GetRValue(GetPixel(dc, x + 90, y + 70)) > 150, "wallpaper left half keeps source colors");
                Check(GetBValue(GetPixel(dc, x + 110, y + 70)) > 150, "wallpaper right half keeps source colors");
            }
            SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(255, 255, 255));
            TextOutW(dc, x, y - 22, names[index], static_cast<int>(wcslen(names[index])));
        }
        GdiFlush();
        {
            Gdiplus::Bitmap rendered(reinterpret_cast<HBITMAP>(GetCurrentObject(dc, OBJ_BITMAP)), nullptr);
            Check(rendered.Save(argv[1], &png) == Gdiplus::Ok, "save actual wallpaper placement preview");
        }
        // GDI+ must respect the memory DC's negative virtual-screen origin.
        SetWindowOrgEx(dc, -100, -50, nullptr);
        RECT negative = { -100, -50, 100, 90 };
        const auto dark = CreateSolidBrush(background); FillRect(dc, &negative, dark); DeleteObject(dark);
        Check(DrawImage(dc, sourcePath, negative, DWPOS_STRETCH), "render on a monitor with negative coordinates");
        Check(GetRValue(GetPixel(dc, -80, 0)) > 150 && GetBValue(GetPixel(dc, 80, 0)) > 150,
            "negative monitor origin preserves wallpaper alignment");
        SetWindowOrgEx(dc, 0, 0, nullptr);
        RECT screen = { GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN) };
        Check(surface.Ensure(display, screen.right - screen.left, screen.bottom - screen.top), "allocate full physical desktop surface");
        SetWindowOrgEx(surface.DC(), screen.left, screen.top, nullptr);
        Check(PaintCurrentDesktopWallpaper(surface.DC(), screen), "render current real Shell wallpaper without modifying system settings");
        result = 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << std::endl; }
    DeleteFileW(sourcePath); ReleaseDC(nullptr, display); CoUninitialize();
    return result;
}
