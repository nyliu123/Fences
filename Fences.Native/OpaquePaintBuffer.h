#pragma once
#include <windows.h>
#include <algorithm>

class ScopedWindowPaint
{
    HWND window;
    PAINTSTRUCT state = {};
    HDC dc;
public:
    explicit ScopedWindowPaint(HWND target) : window(target), dc(BeginPaint(target, &state)) {}
    ~ScopedWindowPaint() { if (dc) EndPaint(window, &state); }
    HDC DC() const { return dc; }
    const PAINTSTRUCT& State() const { return state; }
};

// A persistent client-sized DIB. All GDI drawing finishes before alpha is
// normalized, so transparent glyphs cannot reach the desktop compositor.
class OpaquePaintBuffer
{
    HDC memory = nullptr;
    HBITMAP bitmap = nullptr;
    HGDIOBJ previous = nullptr;
    DWORD* pixels = nullptr;
    int width = 0, height = 0;
public:
    OpaquePaintBuffer() = default;
    OpaquePaintBuffer(const OpaquePaintBuffer&) = delete;
    OpaquePaintBuffer& operator=(const OpaquePaintBuffer&) = delete;
    ~OpaquePaintBuffer() { Close(); }
    void Close()
    {
        if (memory && previous) SelectObject(memory, previous);
        if (bitmap) DeleteObject(bitmap);
        if (memory) DeleteDC(memory);
        memory = nullptr; bitmap = nullptr; previous = nullptr; pixels = nullptr; width = height = 0;
    }
    bool Ensure(HDC target, int cx, int cy)
    {
        if (memory && cx == width && cy == height) return true;
        Close();
        if (cx <= 0 || cy <= 0) return false;
        memory = CreateCompatibleDC(target);
        if (!memory) return false;
        BITMAPINFO info = {}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = cx; info.bmiHeader.biHeight = -cy;
        info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
        void* data = nullptr;
        bitmap = CreateDIBSection(target, &info, DIB_RGB_COLORS, &data, nullptr, 0);
        if (!bitmap) { Close(); return false; }
        previous = SelectObject(memory, bitmap);
        if (!previous || previous == HGDI_ERROR) { previous = nullptr; Close(); return false; }
        pixels = static_cast<DWORD*>(data); width = cx; height = cy;
        return true;
    }
    HDC DC() const { return memory; }
    void MakeOpaque(RECT area)
    {
        GdiFlush();
        const int left = (std::max)(0L, area.left), right = (std::min)(static_cast<LONG>(width), area.right);
        const int top = (std::max)(0L, area.top), bottom = (std::min)(static_cast<LONG>(height), area.bottom);
        for (int y = top; y < bottom; ++y)
            for (int x = left; x < right; ++x) pixels[static_cast<size_t>(y) * width + x] |= 0xff000000u;
    }
};
