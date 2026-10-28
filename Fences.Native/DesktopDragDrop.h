#pragma once
#include "DesktopView.h"
#include <wrl/client.h>

// Use Shell data rather than CF_HDROP alone: virtual identities, multiple
// selections, preferred effects and performed-move feedback must travel intact.
inline HRESULT GetDesktopDragData(IFolderView2* view, IDataObject** data, DWORD* allowed)
{
    if (!view || !data || !allowed) return E_POINTER;
    *data = nullptr; *allowed = DROPEFFECT_NONE;
    Microsoft::WRL::ComPtr<IShellItemArray> items;
    auto result = view->GetSelection(FALSE, &items);
    if (FAILED(result) || !items) return FAILED(result) ? result : E_FAIL;
    SFGAOF attributes = 0;
    result = items->GetAttributes(SIATTRIBFLAGS_AND, SFGAO_CANCOPY | SFGAO_CANMOVE | SFGAO_CANLINK, &attributes);
    if (FAILED(result)) return result;
    *allowed = ((attributes & SFGAO_CANCOPY) ? DROPEFFECT_COPY : 0) |
        ((attributes & SFGAO_CANMOVE) ? DROPEFFECT_MOVE : 0) | ((attributes & SFGAO_CANLINK) ? DROPEFFECT_LINK : 0);
    if (!*allowed) return S_FALSE;
    Microsoft::WRL::ComPtr<IShellView> shell;
    result = view->QueryInterface(IID_PPV_ARGS(&shell));
    return SUCCEEDED(result) ? shell->GetItemObject(SVGIO_SELECTION, IID_PPV_ARGS(data)) : result;
}
