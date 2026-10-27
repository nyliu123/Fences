#include "../Fences.Native/DesktopMenus.cpp"
#include <chrono>
#include <iostream>

class MenuProvider final : public IContextMenu3
{
    LONG references = 1;
    static void Add(HMENU menu, UINT id, const wchar_t* name, HMENU child = nullptr)
    {
        MENUITEMINFOW item = { sizeof(item) };
        item.fMask = MIIM_ID | MIIM_STRING | MIIM_SUBMENU;
        item.wID = id; item.dwTypeData = const_cast<wchar_t*>(name); item.hSubMenu = child;
        if (!InsertMenuItemW(menu, GetMenuItemCount(menu), TRUE, &item)) throw std::runtime_error("fixture menu allocation");
    }
public:
    int queries = 0, initializations = 0;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (id == IID_IUnknown || id == IID_IContextMenu || id == IID_IContextMenu2 || id == IID_IContextMenu3)
            *value = static_cast<IContextMenu3*>(this);
        if (!*value) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override { auto count = InterlockedDecrement(&references); if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu, UINT, UINT first, UINT, UINT) override
    {
        auto view = CreatePopupMenu(), create = CreatePopupMenu();
        Add(menu, first, L"View", view); Add(menu, first + 1, L"Sort"); Add(menu, first + 2, L"Refresh"); Add(menu, first + 3, L"New", create);
        Add(view, first + 4, L"Show icons"); Add(create, first + 5, L"Folder");
        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 6);
    }
    HRESULT STDMETHODCALLTYPE InvokeCommand(LPCMINVOKECOMMANDINFO) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR index, UINT flags, UINT*, LPSTR value, UINT count) override
    {
        ++queries;
        const wchar_t* verbs[] = { L"view", L"arrange", L"refresh", L"New", L"showdesktopicons", L"NewFolder" };
        if (flags != GCS_VERBW || index >= ARRAYSIZE(verbs)) return E_INVALIDARG;
        return wcscpy_s(reinterpret_cast<wchar_t*>(value), count, verbs[index]) ? E_FAIL : S_OK;
    }
    HRESULT STDMETHODCALLTYPE HandleMenuMsg(UINT message, WPARAM, LPARAM) override
    { if (message == WM_INITMENUPOPUP) ++initializations; return S_OK; }
    HRESULT STDMETHODCALLTYPE HandleMenuMsg2(UINT message, WPARAM w, LPARAM l, LRESULT* result) override
    { if (result) *result = 0; return HandleMenuMsg(message, w, l); }
};

int main()
{
    try
    {
        ComPtr<MenuProvider> provider; provider.Attach(new MenuProvider());
        {
            Menu menu(provider.Get());
            const auto started = std::chrono::steady_clock::now();
            for (int i = 0; i < 1000; ++i)
                if (!menu.FindVerb(L"view") || !menu.FindVerb(L"refresh") || !menu.FindVerb(L"showdesktopicons") || !menu.FindVerb(L"NewFolder"))
                    throw std::runtime_error("cached native verbs were lost");
            const auto elapsed = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - started).count();
            std::cout << "4000_command_lookups_ms=" << elapsed << " provider_queries=" << provider->queries
                << " submenu_initializations=" << provider->initializations << std::endl;
            if (provider->queries != 6 || provider->initializations != 2) throw std::runtime_error("repeated provider enumeration/initialization");
        }
        {
            Menu nextPopup(provider.Get());
            if (!nextPopup.FindVerb(L"showdesktopicons") || provider->queries != 12 || provider->initializations != 4)
                throw std::runtime_error("a new popup reused old provider state");
        }
        std::cout << "PASS: one provider snapshot per popup; nested verbs and fresh popup state preserved" << std::endl;
        return 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << std::endl; return 1; }
}
