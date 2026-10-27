// Standalone research harness. Does not modify the live desktop or Fences data.
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commdlg.h>
#include <dlgs.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <exdisp.h>
#include <shlguid.h>
#include <propkey.h>
#include <gdiplus.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <functional>
#include <stdexcept>
#include <string>
#include <vector>
#include "ContextMenuProbe.h"

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

static int assertions = 0;
static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
    ++assertions;
    std::printf("PASS: %s\n", message);
    std::fflush(stdout);
}

static void Require(HRESULT result)
{
    if (FAILED(result))
    {
        char message[80];
        std::snprintf(message, sizeof(message), "COM operation failed: 0x%08lX", result);
        throw std::runtime_error(message);
    }
}

static void Pump(unsigned milliseconds)
{
    const auto deadline = GetTickCount64() + milliseconds;
    do
    {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        Sleep(10);
    } while (GetTickCount64() < deadline);
}

static bool WaitFor(const std::function<bool()>& predicate)
{
    const auto deadline = GetTickCount64() + 8000;
    while (!predicate() && GetTickCount64() < deadline) Pump(20);
    return predicate();
}

class ItemFilter final : public IFolderFilter
{
    LONG references = 1;
    const std::wstring excludedPath;
public:
    LONG excludedCalls = 0;
    explicit ItemFilter(const fs::path& path) : excludedPath(path.wstring()) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override
    {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_IFolderFilter) return E_NOINTERFACE;
        *result = static_cast<IFolderFilter*>(this);
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        const auto remaining = InterlockedDecrement(&references);
        if (!remaining) delete this;
        return remaining;
    }
    HRESULT STDMETHODCALLTYPE ShouldShow(IShellFolder* folder, PCIDLIST_ABSOLUTE,
        PCUITEMID_CHILD child) override
    {
        ComPtr<IShellItem> item;
        if (FAILED(SHCreateItemWithParent(nullptr, folder, child, IID_PPV_ARGS(&item)))) return S_OK;
        PWSTR path = nullptr;
        if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path))) return S_OK;
        const bool excluded = _wcsicmp(path, excludedPath.c_str()) == 0;
        CoTaskMemFree(path);
        if (excluded) InterlockedIncrement(&excludedCalls);
        return excluded ? S_FALSE : S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetEnumFlags(IShellFolder*, PCIDLIST_ABSOLUTE,
        HWND*, DWORD* flags) override
    {
        if (!flags) return E_POINTER;
        // Prove the exclusion is independent of the file's Hidden attribute.
        *flags = SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN | SHCONTF_INCLUDESUPERHIDDEN;
        return S_OK;
    }
};

static ComPtr<IShellView> InspectLiveDesktop()
{
    // Query capabilities only. Never set a filter/flag or enumerate personal file names.
    ComPtr<IShellWindows> windows;
    Require(CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&windows)));
    VARIANT location, root;
    VariantInit(&location); VariantInit(&root);
    location.vt = VT_I4; location.lVal = CSIDL_DESKTOP;
    long handle = 0;
    ComPtr<IDispatch> dispatch;
    Require(windows->FindWindowSW(&location, &root, SWC_DESKTOP, &handle, SWFO_NEEDDISPATCH, &dispatch));
    ComPtr<IServiceProvider> provider;
    Require(dispatch.As(&provider));
    ComPtr<IShellBrowser> browser;
    Require(provider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser)));
    ComPtr<IShellView> view;
    Require(browser->QueryActiveShellView(&view));
    const GUID legacyView = { 0x37a378c0, 0xf82d, 0x11ce, {0xae, 0x65, 0x08, 0x00, 0x2b, 0x2e, 0x12, 0x62} };
    const GUID interfaces[] = { IID_IFolderView2, IID_IFolderFilterSite, legacyView };
    const char* names[] = { "IFolderView2", "IFolderFilterSite", "IShellFolderView" };
    for (int index = 0; index < ARRAYSIZE(interfaces); ++index)
    {
        ComPtr<IUnknown> result;
        const auto viewResult = view->QueryInterface(interfaces[index], reinterpret_cast<void**>(result.GetAddressOf()));
        result.Reset();
        const auto browserResult = browser->QueryInterface(interfaces[index], reinterpret_cast<void**>(result.GetAddressOf()));
        std::printf("CAPABILITY: live desktop %s view=0x%08lX browser=0x%08lX\n", names[index], viewResult, browserResult);
    }
    return view;
}

struct Entry
{
    std::wstring name;
    POINT position;
};

class Browser
{
    ComPtr<IExplorerBrowser> browser;
    bool initialized = false;
public:
    ComPtr<IFolderFilterSite> filterSite;
    void Open(HWND host, const RECT& bounds, const fs::path& folder, IFolderFilter* filter)
    {
        Require(CoCreateInstance(CLSID_ExplorerBrowser, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&browser)));
        Require(browser->SetOptions(static_cast<EXPLORER_BROWSER_OPTIONS>(
            EBO_NAVIGATEONCE | EBO_NOBORDER | EBO_NOTRAVELLOG | EBO_NOPERSISTVIEWSTATE)));
        FOLDERSETTINGS settings = { FVM_ICON, FWF_AUTOARRANGE | FWF_ALIGNLEFT | FWF_NOHEADERINALLVIEWS };
        Require(browser->Initialize(host, &bounds, &settings));
        initialized = true;
        Require(browser.As(&filterSite));
        if (filter) Require(filterSite->SetFilter(filter));
        PIDLIST_ABSOLUTE location = nullptr;
        Require(SHParseDisplayName(folder.c_str(), nullptr, &location, 0, nullptr));
        const auto result = browser->BrowseToIDList(location, SBSP_ABSOLUTE);
        CoTaskMemFree(location);
        Require(result);
    }
    ComPtr<IFolderView2> View() const
    {
        ComPtr<IFolderView2> view;
        if (browser) browser->GetCurrentView(IID_PPV_ARGS(&view));
        return view;
    }
    std::vector<Entry> Items() const
    {
        std::vector<Entry> entries;
        const auto view = View();
        if (!view) return entries;
        int count = 0;
        if (FAILED(view->ItemCount(SVGIO_ALLVIEW, &count))) return entries;
        ComPtr<IShellFolder> folder;
        Require(view->GetFolder(IID_PPV_ARGS(&folder)));
        for (int index = 0; index < count; ++index)
        {
            PITEMID_CHILD child = nullptr;
            Require(view->Item(index, &child));
            STRRET name;
            wchar_t buffer[MAX_PATH] = {};
            POINT position = {};
            const auto named = folder->GetDisplayNameOf(child, SHGDN_INFOLDER | SHGDN_FORPARSING, &name);
            if (SUCCEEDED(named)) StrRetToBufW(&name, child, buffer, ARRAYSIZE(buffer));
            view->GetItemPosition(child, &position);
            CoTaskMemFree(child);
            if (SUCCEEDED(named)) entries.push_back({ buffer, position });
        }
        return entries;
    }
    bool Contains(const wchar_t* name) const
    {
        const auto entries = Items();
        return std::any_of(entries.begin(), entries.end(), [&](const Entry& item) { return item.name == name; });
    }
    void Refresh()
    {
        const auto view = View();
        ComPtr<IShellView> shell;
        Require(view.As(&shell));
        Require(shell->Refresh());
    }
    void Sort()
    {
        const auto view = View();
        Require(view->SetViewModeAndIconSize(FVM_ICON, 48));
        SORTCOLUMN sort = { PKEY_ItemNameDisplay, SORT_ASCENDING };
        Require(view->SetSortColumns(&sort, 1));
    }
    void ProbeLegacyRemoval(const wchar_t* name)
    {
        const auto view = View();
        ComPtr<IShellView> shell;
        Require(view.As(&shell));
        HWND viewWindow = nullptr;
        Require(shell->GetWindow(&viewWindow));
        int count;
        Require(view->ItemCount(SVGIO_ALLVIEW, &count));
        const auto entries = Items();
        for (int index = 0; index < count; ++index)
        {
            if (entries[index].name != name) continue;
            PITEMID_CHILD child = nullptr;
            Require(view->Item(index, &child));
            // SFVM_REMOVEOBJECT = 6 in the historical Windows SDK. Own process/view only.
            const auto result = SHShellFolderView_Message(viewWindow, 6, reinterpret_cast<LPARAM>(child));
            CoTaskMemFree(child);
            Pump(200);
            std::printf("OBSERVATION: legacy RemoveObject result=%lld, before=%d, after=%zu\n",
                static_cast<long long>(result), count, Items().size());
            Refresh();
            Check(WaitFor([&] { return Contains(name); }), "legacy removal does not persist across view refresh");
            return;
        }
        throw std::runtime_error("Legacy probe target not present");
    }
    void Close()
    {
        filterSite.Reset();
        if (initialized) { browser->Destroy(); initialized = false; }
        browser.Reset();
    }
    ~Browser() { Close(); }
};

static void CreateFixture(const fs::path& file)
{
    std::ofstream stream(file, std::ios::binary);
    stream << "Fences isolated desktop-view research fixture\n";
    if (!stream) throw std::runtime_error("Cannot create fixture");
}

static BY_HANDLE_FILE_INFORMATION FileIdentity(const fs::path& file)
{
    const auto handle = CreateFileW(file.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot inspect fixture identity");
    BY_HANDLE_FILE_INFORMATION result = {};
    const bool succeeded = GetFileInformationByHandle(handle, &result) != FALSE;
    CloseHandle(handle);
    if (!succeeded) throw std::runtime_error("Cannot read fixture identity");
    return result;
}

static const wchar_t* fixtureDialogPath = nullptr;
static VOID CALLBACK AcceptFixtureDialog(HWND dialog, UINT, UINT_PTR timer, DWORD)
{
    KillTimer(dialog, timer);
    SendMessageW(dialog, CDM_SETCONTROLTEXT, edt1, reinterpret_cast<LPARAM>(fixtureDialogPath));
    SendMessageW(GetDlgItem(dialog, IDOK), BM_CLICK, 0, 0);
}

static UINT_PTR CALLBACK FileDialogHook(HWND hook, UINT message, WPARAM, LPARAM data)
{
    if (message == WM_NOTIFY && reinterpret_cast<OFNOTIFYW*>(data)->hdr.code == CDN_INITDONE)
    {
        const auto notification = reinterpret_cast<OFNOTIFYW*>(data);
        SendMessageW(GetParent(hook), CDM_SETCONTROLTEXT, edt1, notification->lpOFN->lCustData);
        SetTimer(GetParent(hook), 1, 300, AcceptFixtureDialog);
    }
    return 0;
}

static VOID CALLBACK CancelFixtureDialog(PVOID parameter, BOOLEAN)
{
    EnumThreadWindows(static_cast<DWORD>(reinterpret_cast<ULONG_PTR>(parameter)), [](HWND window, LPARAM) -> BOOL
    {
        wchar_t name[64]; GetClassNameW(window, name, ARRAYSIZE(name));
        if (wcscmp(name, L"#32770") == 0)
        {
            std::fprintf(stderr, "TIMEOUT: cancelling an owned test file-picker window\n");
            PostMessageW(window, WM_COMMAND, IDCANCEL, 0);
        }
        return TRUE;
    }, 0);
}

static void SelectOriginalPath(HWND owner, const fs::path& target)
{
    wchar_t selected[32768];
    wcscpy_s(selected, target.c_str());
    const auto directory = target.parent_path().wstring();
    OPENFILENAMEW dialog = {};
    dialog.lStructSize = sizeof(dialog);
    dialog.hwndOwner = owner;
    dialog.lpstrFile = selected;
    dialog.nMaxFile = ARRAYSIZE(selected);
    dialog.lpstrInitialDir = directory.c_str();
    dialog.lpstrFilter = L"All files\0*.*\0\0";
    dialog.lpstrTitle = L"Fences 研究：从原路径选择文件";
    dialog.Flags = OFN_EXPLORER | OFN_ENABLEHOOK | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_DONTADDTORECENT;
    dialog.lpfnHook = FileDialogHook;
    dialog.lCustData = reinterpret_cast<LPARAM>(target.c_str());
    fixtureDialogPath = target.c_str();
    HANDLE timeout = nullptr;
    if (!CreateTimerQueueTimer(&timeout, nullptr, CancelFixtureDialog,
        reinterpret_cast<PVOID>(static_cast<ULONG_PTR>(GetCurrentThreadId())), 8000, 0, WT_EXECUTEONLYONCE))
        throw std::runtime_error("Cannot establish the test file-picker timeout");
    const bool accepted = GetOpenFileNameW(&dialog) != FALSE;
    if (timeout) DeleteTimerQueueTimer(nullptr, timeout, INVALID_HANDLE_VALUE);
    fixtureDialogPath = nullptr;
    Check(accepted && _wcsicmp(selected, target.c_str()) == 0,
        "a real native file picker selects the excluded file at its unchanged original path");
}

static void Capture(HWND window, const fs::path& destination)
{
    RECT rectangle;
    GetWindowRect(window, &rectangle);
    const int width = rectangle.right - rectangle.left, height = rectangle.bottom - rectangle.top;
    HDC screen = GetDC(nullptr);
    HDC memory = CreateCompatibleDC(screen);
    HBITMAP bitmap = CreateCompatibleBitmap(screen, width, height);
    HGDIOBJ previous = SelectObject(memory, bitmap);
    const bool printed = PrintWindow(window, memory, PW_RENDERFULLCONTENT) != FALSE;
    SelectObject(memory, previous);
    if (!printed) { DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(nullptr, screen); throw std::runtime_error("PrintWindow failed"); }
    Gdiplus::Bitmap image(bitmap, nullptr);
    const CLSID png = { 0x557cf406, 0x1a04, 0x11d3, {0x9a, 0x73, 0, 0, 0xf8, 0x1e, 0xf3, 0x2e} };
    const auto saved = image.Save(destination.c_str(), &png, nullptr);
    DeleteObject(bitmap);
    DeleteDC(memory);
    ReleaseDC(nullptr, screen);
    Require(saved == Gdiplus::Ok ? S_OK : E_FAIL);
}

static LRESULT CALLBACK HostProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_CLOSE) return 0; // The bounded test owns cleanup.
    return DefWindowProcW(window, message, wParam, lParam);
}

int wmain(int count, wchar_t** arguments)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if (count == 3 && wcscmp(arguments[1], L"--context-menu-command") == 0)
        return MenuResearch::RunCommandHelper(arguments[2]);
    if (count != 2) { std::fprintf(stderr, "Usage: DesktopViewFilteringProbe.exe <evidence-directory>\n"); return 2; }
    const auto output = fs::absolute(arguments[1]);
    fs::create_directories(output);
    Require(OleInitialize(nullptr));
    ULONG_PTR graphics = 0;
    Gdiplus::GdiplusStartupInput graphicsInput;
    Gdiplus::GdiplusStartup(&graphics, &graphicsInput, nullptr);
    GUID guid;
    CoCreateGuid(&guid);
    wchar_t suffix[40];
    StringFromGUID2(guid, suffix, ARRAYSIZE(suffix));
    const auto fixture = fs::temp_directory_path() / (std::wstring(L"Fences-ViewFilter-") + suffix);
    HWND host = nullptr;
    bool fixtureOwned = false;
    int exitCode = 0;
    try
    {
        const auto desktopView = InspectLiveDesktop();
        fixtureOwned = fs::create_directory(fixture);
        if (!fixtureOwned) throw std::runtime_error("Refusing to reuse a pre-existing fixture directory");
        const wchar_t* names[] = { L"01-before.txt", L"02-分区项目.txt", L"03-after.txt", L"04-more.txt", L"05-last.txt" };
        for (const auto name : names) CreateFixture(fixture / name);
        const auto target = fixture / names[1];
        const auto originalAttributes = GetFileAttributesW(target.c_str());
        const auto originalIdentity = FileIdentity(target);
        WIN32_FILE_ATTRIBUTE_DATA originalData;
        GetFileAttributesExW(target.c_str(), GetFileExInfoStandard, &originalData);

        WNDCLASSW windowClass = {};
        windowClass.lpfnWndProc = HostProcedure;
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpszClassName = L"FencesIsolatedViewFilteringProbe";
        windowClass.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        RegisterClassW(&windowClass);
        host = CreateWindowExW(0, windowClass.lpszClassName, L"Fences 研究：文件路径不变，仅过滤图标（临时测试目录）",
            WS_OVERLAPPEDWINDOW, 60, 60, 1080, 560, nullptr, nullptr, windowClass.hInstance, nullptr);
        Check(host != nullptr, "isolated native host created");
        const wchar_t* labels[] = { L"普通文件视图：全部项目", L"过滤视图：隐藏分区项目，其余项目补位" };
        for (int index = 0; index < 2; ++index)
        {
            const auto label = CreateWindowExW(0, L"STATIC", labels[index], WS_CHILD | WS_VISIBLE,
                16 + index * 524, 14, 510, 30, host, nullptr, windowClass.hInstance, nullptr);
            SendMessageW(label, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
        }
        ShowWindow(host, SW_SHOWNOACTIVATE);
        {
            Browser ordinary, filtered;
            RECT left = { 12, 50, 524, 485 }, right = { 536, 50, 1048, 485 };
            ComPtr<ItemFilter> filter;
            filter.Attach(new ItemFilter(target));
            ordinary.Open(host, left, fixture, nullptr);
            filtered.Open(host, right, fixture, filter.Get());
            Check(WaitFor([&] { return ordinary.Items().size() == 5 && filtered.Items().size() == 4; }),
                "ordinary view has five icons while filtered view has four");
            Check(!filtered.Contains(names[1]) && ordinary.Contains(names[1]), "only the filtered view hides the target");
            Check(filter->excludedCalls > 0, "native Shell calls the per-item filter");
            Check(GetFileAttributesW(target.c_str()) == originalAttributes && !(originalAttributes & FILE_ATTRIBUTE_HIDDEN),
                "target is not moved, renamed or marked Hidden");
            ordinary.Sort(); filtered.Sort(); Pump(300);
            auto positions = [](const std::vector<Entry>& entries)
            {
                std::vector<std::pair<LONG, LONG>> points;
                for (const auto& entry : entries) points.emplace_back(entry.position.x, entry.position.y);
                std::sort(points.begin(), points.end());
                return points;
            };
            auto allPositions = positions(ordinary.Items()), compactPositions = positions(filtered.Items());
            Check(compactPositions.size() == 4 && std::equal(compactPositions.begin(), compactPositions.end(), allPositions.begin()),
                "remaining icons occupy the first four native grid positions with no gap");
            Capture(host, output / L"filtered-native-view.png");
            assertions += MenuResearch::Run(host, filtered.View().Get(), desktopView.Get(), fixture, output, Capture, Pump);
            ordinary.Refresh(); filtered.Refresh();
            Check(WaitFor([&] { return ordinary.Items().size() == 5 && filtered.Items().size() == 4; }) && !filtered.Contains(names[1]),
                "context-menu operations preserve filtering and restore the baseline fixture set");
            ordinary.ProbeLegacyRemoval(names[1]);
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                filtered.Refresh(); ordinary.Refresh();
                Check(WaitFor([&] { return ordinary.Items().size() == 5 && filtered.Items().size() == 4; }) && !filtered.Contains(names[1]),
                    "view refresh retains exclusion and ordinary-view visibility");
            }
            std::ifstream readable(target, std::ios::binary);
            std::string content((std::istreambuf_iterator<char>(readable)), std::istreambuf_iterator<char>());
            Check(content == "Fences isolated desktop-view research fixture\n", "original path remains directly readable");
            readable.close();
            SelectOriginalPath(host, target);
            Check(!filtered.Contains(names[1]), "opening and closing a native file picker does not reveal the filtered icon");
            const auto fresh = fixture / L"06-new.txt";
            CreateFixture(fresh);
            SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, fresh.c_str(), nullptr);
            Check(WaitFor([&] { return ordinary.Contains(L"06-new.txt") && filtered.Contains(L"06-new.txt"); }),
                "Shell change notifications add new ordinary items to both views");
            Check(!filtered.Contains(names[1]), "file creation does not reveal the excluded icon");
            Require(filtered.filterSite->SetFilter(nullptr)); filtered.Refresh();
            Check(WaitFor([&] { return filtered.Items().size() == 6; }), "removing filter restores all icons without file movement");
            Require(filtered.filterSite->SetFilter(filter.Get())); filtered.Refresh();
            Check(WaitFor([&] { return filtered.Items().size() == 5; }), "reapplying filter hides only the target again");
            filtered.Close();
            filtered.Open(host, right, fixture, filter.Get());
            Check(WaitFor([&] { return filtered.Items().size() == 5; }) && !filtered.Contains(names[1]),
                "destroying and recreating the hosted view preserves exclusion when filter is reattached");
            WIN32_FILE_ATTRIBUTE_DATA finalData;
            Check(GetFileAttributesExW(target.c_str(), GetFileExInfoStandard, &finalData) &&
                CompareFileTime(&originalData.ftCreationTime, &finalData.ftCreationTime) == 0 &&
                CompareFileTime(&originalData.ftLastWriteTime, &finalData.ftLastWriteTime) == 0 &&
                finalData.dwFileAttributes == originalData.dwFileAttributes &&
                finalData.nFileSizeLow == originalData.nFileSizeLow && finalData.nFileSizeHigh == originalData.nFileSizeHigh,
                "target attributes, creation time, write time and size remain unchanged");
            const auto finalIdentity = FileIdentity(target);
            Check(originalIdentity.dwVolumeSerialNumber == finalIdentity.dwVolumeSerialNumber &&
                originalIdentity.nFileIndexHigh == finalIdentity.nFileIndexHigh && originalIdentity.nFileIndexLow == finalIdentity.nFileIndexLow,
                "file identity is unchanged, without replacing the original with a copy or link");

            const auto hiddenControl = fixture / L"07-hidden-control.txt";
            CreateFixture(hiddenControl);
            Check(SetFileAttributesW(hiddenControl.c_str(), FILE_ATTRIBUTE_HIDDEN) != FALSE,
                "hidden-control fixture created without changing global Explorer settings");
            filtered.Refresh();
            Check(WaitFor([&] { return filtered.Contains(L"07-hidden-control.txt"); }) && !filtered.Contains(names[1]),
                "including hidden files in enumeration still excludes the designated normal file");

            const auto folder = fixture / L"08-folder";
            fs::create_directory(folder);
            CreateFixture(folder / L"child.txt");
            ComPtr<ItemFilter> folderFilter;
            folderFilter.Attach(new ItemFilter(folder));
            Require(filtered.filterSite->SetFilter(folderFilter.Get()));
            filtered.Refresh(); ordinary.Refresh();
            Check(WaitFor([&] { return ordinary.Contains(L"08-folder") && filtered.Contains(names[1]); }) &&
                !filtered.Contains(L"08-folder") && fs::exists(folder / L"child.txt"),
                "the same per-path filter can exclude a folder while its original contents remain available");

            const auto shortcut = fixture / L"09-shortcut.lnk";
            ComPtr<IShellLinkW> link;
            Require(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link)));
            Require(link->SetPath(target.c_str()));
            ComPtr<IPersistFile> persistentLink;
            Require(link.As(&persistentLink));
            Require(persistentLink->Save(shortcut.c_str(), TRUE));
            ComPtr<ItemFilter> shortcutFilter;
            shortcutFilter.Attach(new ItemFilter(shortcut));
            Require(filtered.filterSite->SetFilter(shortcutFilter.Get()));
            filtered.Refresh(); ordinary.Refresh();
            Check(WaitFor([&] { return ordinary.Contains(L"09-shortcut.lnk") && filtered.Contains(L"08-folder"); }) &&
                !filtered.Contains(L"09-shortcut.lnk") && fs::exists(shortcut) && fs::exists(target),
                "a shortcut can be filtered while both the original shortcut and its target remain in place");
        }
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "FAIL: %s\n", error.what());
        exitCode = 1;
    }
    if (host) DestroyWindow(host);
    // Only remove the GUID directory created above, beneath the discovered temp root.
    if (fixtureOwned && fs::equivalent(fixture.parent_path(), fs::temp_directory_path()) && fixture.filename().wstring().find(L"Fences-ViewFilter-") == 0)
    {
        std::error_code cleanupError;
        fs::remove_all(fixture, cleanupError);
        if (cleanupError) { std::fprintf(stderr, "FAIL: fixture cleanup failed\n"); exitCode = 1; }
    }
    if (fixtureOwned && fs::exists(fixture)) { std::fprintf(stderr, "FAIL: fixture directory remains\n"); exitCode = 1; }
    Gdiplus::GdiplusShutdown(graphics);
    OleUninitialize();
    std::printf("Completed: %d assertions; exit=%d; fixture removed=%s\n", assertions, exitCode,
        fs::exists(fixture) ? "false" : "true");
    return exitCode;
}
