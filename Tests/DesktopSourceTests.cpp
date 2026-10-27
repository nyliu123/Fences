#include "../Fences.Native/DesktopSource.h"
#include <commctrl.h>
#include <iostream>
#include <stdexcept>

namespace
{
    int checks = 0;
    void Check(bool value, const char* message)
    {
        if (!value) throw std::runtime_error(message);
        ++checks; std::cout << "PASS: " << message << std::endl;
    }
    void Pump()
    {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        Sleep(10);
    }
}
// Reads Explorer and creates a hidden owned view. It never hides or changes
// Explorer's original view, wallpaper, registry values or personal files.
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2 || FAILED(OleInitialize(nullptr))) return 2;
    HMODULE module = nullptr; void* context = nullptr;
    using Destroy = void(__cdecl*)(void*);
    Destroy destroy = nullptr;
    try
    {
        DesktopSource original;
        Check(SUCCEEDED(ReadDesktopSource(nullptr, original)), "read actual Explorer desktop items and metrics");
        std::cout << "ORIGINAL: icon=" << original.iconSize << ", spacing=" << original.spacing.x << 'x' << original.spacing.y
            << ", items=" << original.items.size() << std::endl;
        HWND host = nullptr;
        EnumWindows([](HWND window, LPARAM parameter) -> BOOL
        {
            wchar_t name[64]; GetClassNameW(window, name, ARRAYSIZE(name));
            if (wcscmp(name, L"Progman") && wcscmp(name, L"WorkerW")) return TRUE;
            const auto child = FindWindowExW(window, nullptr, L"SHELLDLL_DefView", nullptr);
            if (child) { *reinterpret_cast<HWND*>(parameter) = child; return FALSE; }
            return TRUE;
        }, reinterpret_cast<LPARAM>(&host));
        Check(host != nullptr, "discover original desktop host without changing it");
        Check(SUCCEEDED(ReadDesktopSource(host, original)), "snapshot belongs to the actual original host");
        DesktopSource rejected;
        Check(ReadDesktopSource(GetDesktopWindow(), rejected) == E_HANDLE, "reject an unrelated source window");
        module = LoadLibraryW(argv[1]); Check(module != nullptr, "load final native desktop module");
        using Create = HRESULT(__cdecl*)(HWND, const wchar_t*, BOOL, HWND, UINT, void**);
        using Status = HRESULT(__cdecl*)(void*);
        using Window = HWND(__cdecl*)(void*);
        using Preferences = HRESULT(__cdecl*)(void*, DesktopPreferences*);
        using Metrics = HRESULT(__cdecl*)(void*, int*, POINT*);
        using Count = int(__cdecl*)(void*);
        using Position = HRESULT(__cdecl*)(void*, int, wchar_t*, UINT, POINT*);
        auto create = reinterpret_cast<Create>(GetProcAddress(module, "FencesCreateView"));
        destroy = reinterpret_cast<Destroy>(GetProcAddress(module, "FencesDestroyView"));
        auto status = reinterpret_cast<Status>(GetProcAddress(module, "FencesViewStatus"));
        auto window = reinterpret_cast<Window>(GetProcAddress(module, "FencesViewWindow"));
        auto metrics = reinterpret_cast<Metrics>(GetProcAddress(module, "FencesOriginalMetrics"));
        auto get = reinterpret_cast<Preferences>(GetProcAddress(module, "FencesGetPreferences"));
        auto set = reinterpret_cast<Preferences>(GetProcAddress(module, "FencesSetPreferences"));
        auto count = reinterpret_cast<Count>(GetProcAddress(module, "FencesVisibleCount"));
        auto position = reinterpret_cast<Position>(GetProcAddress(module, "FencesItemPosition"));
        Check(create && destroy && status && window && metrics && get && set && count && position, "resolve native view entry points");
        Check(SUCCEEDED(create(host, nullptr, TRUE, nullptr, 0, &context)), "create a hidden actual-desktop view");
        const auto start = GetTickCount64();
        while (status(context) == E_PENDING && GetTickCount64() - start < 10000) Pump();
        Check(status(context) == S_OK, "actual desktop view finishes filtered enumeration");
        Check(!IsWindowVisible(window(context)), "read-only acceptance never displays the owned view over the user's desktop");
        int size = 0; POINT spacing = {};
        Check(metrics(context, &size, &spacing) == S_OK && size == original.iconSize &&
            spacing.x == original.spacing.x && spacing.y == original.spacing.y, "capture original icon size and spacing exactly");
        DesktopPreferences prefs = {}; prefs.size = sizeof(prefs);
        Check(get(context, &prefs) == S_OK, "read hosted preferences");
        prefs.iconSize = size; Check(set(context, &prefs) == S_OK, "apply inherited icon size");
        for (int index = 0; index < 50; ++index) Pump();
        HWND list = nullptr;
        EnumChildWindows(window(context), [](HWND child, LPARAM parameter) -> BOOL
        {
            wchar_t name[64]; GetClassNameW(child, name, ARRAYSIZE(name));
            if (!wcscmp(name, WC_LISTVIEWW)) *reinterpret_cast<HWND*>(parameter) = child;
            return TRUE;
        }, reinterpret_cast<LPARAM>(&list));
        Check(list != nullptr, "discover owned native icon list");
        const auto actualSpacing = ListView_GetItemSpacing(list, FALSE);
        Check(LOWORD(actualSpacing) == spacing.x && HIWORD(actualSpacing) == spacing.y, "hosted spacing equals the original Explorer grid");
        Check(!(ListView_GetExtendedListViewStyle(list) & LVS_EX_TRANSPARENTSHADOWTEXT), "alpha-shadow text is disabled in actual desktop mode");
        {
            const auto dc = CreateCompatibleDC(nullptr);
            BITMAPINFO info = {}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = 32; info.bmiHeader.biHeight = -32;
            info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
            void* pixels = nullptr;
            const auto bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
            const auto previous = SelectObject(dc, bitmap);
            auto colors = static_cast<DWORD*>(pixels);
            if (!pixels) { DeleteDC(dc); throw std::runtime_error("allocate completed-item paint target"); }
            for (int index = 0; index < 1024; ++index) colors[index] = 0x00ffffffu;
            NMLVCUSTOMDRAW draw = {}; draw.nmcd.hdr.hwndFrom = list; draw.nmcd.hdr.code = NM_CUSTOMDRAW;
            draw.nmcd.dwDrawStage = CDDS_ITEMPOSTPAINT; draw.nmcd.hdc = dc; draw.nmcd.rc = { 0, 0, 32, 32 };
            SendMessageW(GetParent(list), WM_NOTIFY, 0, reinterpret_cast<LPARAM>(&draw)); GdiFlush();
            const bool opaque = colors[33] == 0xffffffffu;
            SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc);
            Check(opaque, "completed item text buffer repairs zero-alpha white glyphs before composition");
        }
        const int hosted = count(context);
        Check(hosted >= 0 && static_cast<size_t>(hosted) == original.items.size(), "hosted view preserves the original desktop item count");
        for (int index = 0; index < hosted; ++index)
        {
            wchar_t name[32768]; POINT point;
            Check(position(context, index, name, ARRAYSIZE(name), &point) == S_OK, "read hosted desktop identity");
            bool found = false;
            for (const auto& candidate : original.items)
                if (CompareStringOrdinal(name, -1, candidate.c_str(), -1, TRUE) == CSTR_EQUAL) { found = true; break; }
            Check(found, "hosted item was present on the original desktop");
        }
        std::cout << "PASS: " << checks << " original-desktop checks; hosted=" << hosted << std::endl;
        destroy(context); context = nullptr; FreeLibrary(module); module = nullptr; OleUninitialize(); return 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << std::endl; }
    if (context && destroy) destroy(context);
    if (module) FreeLibrary(module);
    OleUninitialize(); return 1;
}
