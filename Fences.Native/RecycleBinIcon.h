#pragma once
#include "DesktopView.h"

class RecycleBinIcon
{
    HICON icon = nullptr;
    int size = 0;
    bool full = false;
public:
    PIDLIST_ABSOLUTE identity = nullptr;
    int index = -1;
    ~RecycleBinIcon();
    HRESULT Open();
    void Locate(IFolderView2* view);
    HRESULT Refresh(HWND list, bool force);
    void Draw(HWND list, HDC dc, const RECT& update);
    bool Full() const { return full; }
    RECT Bounds(HWND list) const;
};
