#include "../Fences.Native/DesktopView.h"
#include "../Fences.Native/DesktopInteraction.h"
#include <commctrl.h>
#include <iostream>
#include <stdexcept>

namespace
{
    int checks = 0;
    void Check(bool value, const char* name) { if (!value) throw std::runtime_error(name); ++checks; std::cout << "PASS: " << name << std::endl; }
    void Select(HWND list, int index) { ListView_SetItemState(list, index, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED); ListView_SetSelectionMark(list, index); }
}
int main()
{
    InitCommonControls();
    HWND host = CreateWindowExW(0, L"STATIC", L"Owned selection fixture", WS_OVERLAPPED, 0, 0, 700, 500, nullptr, nullptr, nullptr, nullptr);
    HWND list = CreateWindowExW(0, WC_LISTVIEWW, L"", WS_CHILD | LVS_ICON, 0, 0, 700, 500, host, nullptr, nullptr, nullptr);
    BYTE previous[256] = {}, keyboard[256] = {}; GetKeyboardState(previous); SetKeyboardState(keyboard);
    int result = 0;
    try
    {
        for (int i = 0; i < 2; ++i) { LVITEMW item = {}; item.mask = LVIF_TEXT; item.iItem = i; item.pszText = const_cast<wchar_t*>(i ? L"second" : L"first"); ListView_InsertItem(list, &item); }
        DesktopInteraction interaction;
        const auto blank = MAKELPARAM(600, 400);
        auto BlankClick = [&]()
        {
            interaction.Before(list, WM_LBUTTONDOWN, MK_LBUTTON, blank, nullptr);
            return interaction.After(list, WM_LBUTTONUP, blank, nullptr);
        };
        Select(list, 0);
        Check(BlankClick(), "blank click requests one deferred selection cleanup");
        auto epoch = interaction.SelectionEpoch();
        Check(ListView_GetSelectedCount(list) == 0 && ListView_GetNextItem(list, -1, LVNI_FOCUSED) == -1 && ListView_GetSelectionMark(list) == -1,
            "blank click clears selection, focus and range anchor");
        Select(list, 0); // A late Shell activation after a menu command.
        Check(interaction.FlushSelection(list, nullptr, epoch) && ListView_GetSelectedCount(list) == 0,
            "deferred cleanup removes a late restoration after menu return");
        Check(!interaction.FlushSelection(list, nullptr, epoch), "cleanup is consumed once");
        BlankClick(); epoch = interaction.SelectionEpoch();
        RECT icon; ListView_GetItemRect(list, 1, &icon, LVIR_ICON);
        interaction.Before(list, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM((icon.left + icon.right) / 2, (icon.top + icon.bottom) / 2), nullptr);
        Select(list, 1);
        Check(!interaction.FlushSelection(list, nullptr, epoch) && ListView_GetSelectedCount(list) == 1,
            "new input invalidates old cleanup and preserves the new selection");
        interaction.Before(list, WM_LBUTTONDOWN, MK_LBUTTON, blank, nullptr);
        interaction.Before(list, WM_MOUSEMOVE, MK_LBUTTON, MAKELPARAM(650, 450), nullptr);
        Check(!interaction.After(list, WM_LBUTTONUP, MAKELPARAM(650, 450), nullptr) && ListView_GetSelectedCount(list) == 1,
            "marquee dragging is not mistaken for a blank click");
        Select(list, 0); Select(list, 1);
        DesktopInteraction::Deselect(list, nullptr, 0);
        Check(ListView_GetSelectedCount(list) == 1 && ListView_GetItemState(list, 1, LVIS_SELECTED | LVIS_FOCUSED) == (LVIS_SELECTED | LVIS_FOCUSED) && ListView_GetSelectionMark(list) == 1,
            "dismissing an edited item preserves another item's selection and anchor");
        std::cout << "PASS: " << checks << " selection interaction assertions" << std::endl;
    }
    catch (const std::exception& error) { std::cerr << error.what() << std::endl; result = 1; }
    SetKeyboardState(previous); DestroyWindow(host); return result;
}
