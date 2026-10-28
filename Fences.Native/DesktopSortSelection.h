#pragma once
#include "DesktopView.h"
#include <wrl/client.h>

// Shell refresh enumerates asynchronously. Keep selection by identity until the
// next user input, so a later layout can restore items that were not ready yet.
class DesktopSortSelection
{
    Microsoft::WRL::ComPtr<IShellItemArray> selected;
    PITEMID_CHILD focused = nullptr;
    UINT_PTR epoch = 0;
    bool pending = false;
    static int Find(IFolderView2* view, IShellFolder* folder, PCUITEMID_CHILD identity)
    {
        int count = 0; view->ItemCount(SVGIO_ALLVIEW, &count);
        for (int index = 0; index < count; ++index)
        {
            PITEMID_CHILD child = nullptr;
            if (FAILED(view->Item(index, &child))) continue;
            const auto compared = folder->CompareIDs(SHCIDS_CANONICALONLY, identity, child);
            const bool matches = ILIsEqual(identity, child) || (SUCCEEDED(compared) && static_cast<short>(HRESULT_CODE(compared)) == 0);
            CoTaskMemFree(child);
            if (matches) return index;
        }
        return -1;
    }
public:
    ~DesktopSortSelection() { Clear(); }
    void Clear()
    {
        selected.Reset(); CoTaskMemFree(focused); focused = nullptr; pending = false;
    }
    void Capture(IFolderView2* view, UINT_PTR inputEpoch)
    {
        if (pending && epoch == inputEpoch) return;
        Clear(); epoch = inputEpoch; pending = true;
        view->GetSelection(FALSE, &selected);
        int index = -1;
        if (SUCCEEDED(view->GetFocusedItem(&index)) && index >= 0) view->Item(index, &focused);
    }
    void Restore(IFolderView2* view, UINT_PTR inputEpoch)
    {
        if (!pending) return;
        if (epoch != inputEpoch) { Clear(); return; }
        Microsoft::WRL::ComPtr<IShellFolder> folder;
        if (FAILED(view->GetFolder(IID_PPV_ARGS(&folder)))) return;
        DWORD count = 0;
        if (selected) selected->GetCount(&count);
        for (DWORD index = 0; index < count; ++index)
        {
            Microsoft::WRL::ComPtr<IShellItem> item; PIDLIST_ABSOLUTE identity = nullptr;
            if (SUCCEEDED(selected->GetItemAt(index, &item)) && SUCCEEDED(SHGetIDListFromObject(item.Get(), &identity)))
            {
                const auto itemIndex = Find(view, folder.Get(), ILFindLastID(identity));
                DWORD state = 0;
                if (itemIndex >= 0 && SUCCEEDED(view->GetSelectionState(ILFindLastID(identity), &state)) && !(state & SVSI_SELECT))
                    view->SelectItem(itemIndex, SVSI_SELECT | SVSI_NOTAKEFOCUS);
                CoTaskMemFree(identity);
            }
        }
        const auto focusedIndex = focused ? Find(view, folder.Get(), focused) : -1;
        int currentFocus = -1; view->GetFocusedItem(&currentFocus);
        if (focusedIndex >= 0 && focusedIndex != currentFocus) view->SelectItem(focusedIndex, SVSI_FOCUSED | SVSI_NOTAKEFOCUS);
    }
};
