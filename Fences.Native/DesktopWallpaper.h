#pragma once
#include <windows.h>

// Uses the current per-monitor Shell wallpaper, including slideshow changes.
// PaintDesktop alone can retain the process's previous slideshow image.
bool PaintCurrentDesktopWallpaper(HDC dc, const RECT& virtualScreen);
