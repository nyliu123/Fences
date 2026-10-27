#pragma once
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <functional>

struct DesktopPreferences
{
    UINT size;
    DWORD flags;
    int iconSize;
    GUID sortFormat;
    DWORD sortProperty;
    int sortDirection;
};

bool ShowDesktopMenu(HWND owner, IFolderView2* view, POINT position, bool* viewChanged = nullptr);

extern "C"
{
    __declspec(dllexport) HRESULT __cdecl FencesCreateView(HWND parent, const wchar_t* folder,
        BOOL wallpaper, HWND notify, UINT message, void** result);
    __declspec(dllexport) HRESULT __cdecl FencesCreateFilteredView(HWND parent, const wchar_t* folder,
        BOOL wallpaper, HWND notify, UINT message, const wchar_t* excluded, UINT characters, void** result);
    __declspec(dllexport) void __cdecl FencesDestroyView(void* context);
    __declspec(dllexport) HWND __cdecl FencesViewWindow(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesViewStatus(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesExcludePaths(void* context, const wchar_t* paths, UINT characters);
    __declspec(dllexport) HRESULT __cdecl FencesRefreshView(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesRefreshBackground(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesTranslateKey(void* context, HWND target, UINT message, WPARAM wParam, LPARAM lParam);
    __declspec(dllexport) HRESULT __cdecl FencesOriginalMetrics(void* context, int* size, POINT* spacing);
    __declspec(dllexport) HRESULT __cdecl FencesSetCaptionWindow(void* context, HWND window);
    __declspec(dllexport) HFONT __cdecl FencesCaptionMetrics(void* context, RECT* bounds, int* height);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionAt(void* context, int index, wchar_t* name, UINT capacity, RECT* bounds, DWORD* state);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionBackground(void* context, HDC dc);
    __declspec(dllexport) int __cdecl FencesCaptionLineHeight(void* context);
    __declspec(dllexport) HWND __cdecl FencesCaptionEditor(void* context, RECT* bounds);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionDecoration(void* context, int index, RECT* bounds, int* state);
    __declspec(dllexport) HRESULT __cdecl FencesDrawCaptionDecoration(void* context, int state, HDC dc, const RECT* bounds);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionSurface(void* context, HDC dc);
    __declspec(dllexport) UINT_PTR __cdecl FencesCaptionRevision(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionDirty(void* context, RECT* bounds);
    __declspec(dllexport) void __cdecl FencesAcknowledgeCaption(void* context, UINT_PTR revision);
    __declspec(dllexport) HRESULT __cdecl FencesCaptionSurfaceRegion(void* context, HDC dc, const RECT* bounds);
    __declspec(dllexport) HRESULT __cdecl FencesTakeRename(void* context, wchar_t* name, UINT capacity, RECT* bounds, BOOL* file, void** target);
    __declspec(dllexport) HRESULT __cdecl FencesCommitRename(void* target, HWND owner, const wchar_t* name);
    __declspec(dllexport) void __cdecl FencesActivateRename(void* context, void* target);
    __declspec(dllexport) void __cdecl FencesEndRename(void* context, BOOL restoreFocus);
    __declspec(dllexport) void __cdecl FencesReleaseRename(void* target);
    __declspec(dllexport) HRESULT __cdecl FencesResizeView(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesGetPreferences(void* context, DesktopPreferences* preferences);
    __declspec(dllexport) HRESULT __cdecl FencesSetPreferences(void* context, const DesktopPreferences* preferences);
    __declspec(dllexport) BOOL __cdecl FencesIsBackgroundPoint(void* context, POINT point);
    __declspec(dllexport) int __cdecl FencesVisibleCount(void* context);
    __declspec(dllexport) HRESULT __cdecl FencesItemPosition(void* context, int index, wchar_t* name, UINT capacity, POINT* point);
    __declspec(dllexport) HRESULT __cdecl FencesSetItemPosition(void* context, const wchar_t* name, POINT point);
}
