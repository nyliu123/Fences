---
version: alpha
name: "Fences"
description: "A quiet Windows desktop organizer rendered as restrained smoked acrylic."
colors:
  primary: "#202832"
  title-highlight: "#010101"
  border-highlight: "#FFFFFF"
  title-text: "#FFFFFF"
  text-primary: "#FFFFFF"
typography:
  sans:
    fontFamily: "Segoe UI, Microsoft YaHei UI, sans-serif"
rounded:
  DEFAULT: "0rem"
spacing:
  item-gap: "0.9375rem"
  title-offset: "0.1875rem"
components:
  fence:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.DEFAULT}"
    padding: "{spacing.item-gap}"
  fence-title:
    backgroundColor: "{colors.title-highlight}"
    textColor: "{colors.title-text}"
  fence-border:
    backgroundColor: "{colors.border-highlight}"
---

# Fences Design System

## Overview

Fences is a compact Windows desktop utility for people who organize frequently used files without leaving the desktop. Its visual reference is smoked acrylic laid directly over wallpaper: recognizable as a Windows surface, but quieter than a normal application window. The product register is utilitarian. The memorable signature is real background blur with a cool graphite tint; controls, typography, and motion remain restrained.

The interface must not resemble a floating web dashboard, an opaque file manager, or decorative glass with strong gradients. `DESIGN.md` records intent; the runtime values are owned by `FenceWindow` and `BlurUtil` because this WinForms project has no shared theme layer.

## Colors

The Fence uses `primary` for the default native composition tint and a restrained translucent body so wallpaper remains perceptible. The title is rendered by a separate fully opaque overlay and never inherits the content surface opacity. Black and white remain the defaults, while each Fence may override title text, title background, body tint, and body transparency through the canonical Personalization dialog. Icons and item labels are always rendered at full opacity above the translucent body; file labels retain the existing dark text shadow so wallpaper color does not reduce readability. High-contrast behavior remains owned by Windows.

## Typography

Segoe UI remains the default typeface to match Windows shell typography. Microsoft YaHei UI is the Chinese fallback. Each Fence may override title and item-label families and sizes through Personalization; unavailable fonts safely fall back to Segoe UI.

## Layout

The existing grid and resizing behavior remain canonical. The default icon size is 48 px and the default item gap is 15 px; both values are independently adjustable from Personalization and update cell geometry without altering the saved Fence position or dimensions.

## Elevation & Depth

Depth comes from native acrylic blur, a translucent graphite wash, one subtle title highlight, the existing desktop shadow, and a one-pixel inner border. Do not add stacked shadows, opaque cards, or ornamental gradients.

## Shapes

Fence geometry defaults to rectangular edges. Personalization offers a per-Fence corner radius from 0 to 48 px, owned by `FenceAppearance.CornerRadius`, persisted with the other appearance settings, and shared by the native window region, background outline, and live preview. All personalization sliders ignore the mouse wheel; pointer dragging and keyboard arrows remain available.

## Iconography

The Fences application and tray icon uses a complete cool-graphite enclosure around three desktop-item tiles, with one restrained Windows-blue accent. The thick, closed silhouette fills the icon canvas closely so it remains prominent and legible at 16 px. The icon has no text, ornamental detail, or containing app-tile shape.

## Components

The Fence surface uses four coordinated native windows: a translucent acrylic body, a per-pixel transparent item layer for fully opaque icons and labels, a fully opaque title layer, and a nearly invisible pointer-input layer. The icon and title layers remain above the body during movement, menus, and focus changes; the pointer-input layer stays below the title and editors while receiving mouse and drop events. The runtime owner for per-Fence visual tokens is `FenceAppearance`; `DESIGN.md` documents its defaults, `PersonalizationDialog` edits a complete snapshot, and `FenceWindow` coordinates the renderers. Personalization uses a compact two-column workshop: a wallpaper-backed live preview on the left, scrollable white setting sections on the right, and a fixed footer for reset, cancel, and apply actions. The same borderless text control renders the title in both normal and editing states, so its font, alignment, and bounds never change; only a stationary left click enables editing and selects the original title, while any actual title-bar movement is treated strictly as a drag. Right-clicking first commits an active title edit, then clears the title control's focus, selection, caret, and editing visuals before opening the Fence menu. Enter, focus loss, or a click elsewhere in the Fence also commits the change. Fence items default to a 48 px icon inside a 96 px-wide cell with a two-line label area; personalized icon, spacing, and label sizes update rendering and hit targets together. Clicking an item icon toggles its full wrapped name, while clicking an item name first clears the full-name state and then opens the white, single-line inline editor. While the editor is open, the opaque item layer keeps the icon but suppresses the original name so the two text renderers never overlap. The editor width hugs the measured name like Explorer, and file-name editing selects the stem by default so the extension is preserved unless deliberately changed. A per-Fence blank-area context-menu toggle controls whether file extensions are shown. Hover and selection states keep the established Windows accent colors. Dragging an item exports the real stored path through the Windows file-drop contract and displays a background-free ghost at the original pointer offset; a successful external move removes it from Fence metadata. “移回桌面” remains the keyboard-accessible, non-drag alternative.

Within a Fence, dragging an item to a grid gap or row edge displays a slim insertion marker using that Fence's body background color on the opaque item layer. Its height and top alignment match the dragged item's selection bounds, including expanded names. Releasing commits the order to Fence metadata; leaving or cancelling clears the marker without changing order. Scrolling near the content edges exposes off-screen destinations. Locked Fences reject reorder, and item context-menu actions “向前移动” / “向后移动” provide the non-drag alternative. `FenceWindow.ItemReordering` owns the interaction and `ItemInsertionLayout` owns insertion geometry; existing cell sizes and external file-drop behavior remain canonical.

External file drags leave the source untouched while hovering; file moves begin only on drop. Drop failures restore earlier imports and queue error dialogs after the OLE callback returns so Explorer can finish its drag operation. The native item menu offers “以管理员身份运行” for executable files and shortcuts with existing executable targets. `ExecutableLauncher` resolves shortcut arguments and working directories and requests Windows elevation with the `runas` verb; cancelling the UAC prompt is a normal outcome.

Personalization's `FencePreviewPanel` shows a scaled, screenshot-like view of the current Fence's title and real project icons over the desktop wallpaper, with an approximate blurred/tinted body and opaque title, icons, and labels. Empty Fences use Windows stock icons. Preview dimensions preserve the Fence's aspect ratio, settings use the same grid metrics as the live Fence, and resizing invalidates the entire buffered canvas so no old frame survives. Batch personalization previews the first Fence's content.

Personalization headings stand alone without explanatory subtitle text. Pointer hover clears on leaving the Fence, while explicit selection remains independent. Context menus hit-test the invocation point: item actions appear only over an item, and the title counts as Fence background even during title editing. `DesktopWindowHost` attaches the body and icon layer to the native desktop host; `DesktopInputWindow` pins the title, pointer-input, and rename windows immediately above that host and below ordinary applications while allowing keyboard focus. Context-menu and editor activation never raises the Fence as an application group.

The system-tray icon is the persistent application-level control surface. Its menu mirrors applicable Fence settings as batch operations, including “批量个性化”, owns Fence creation, and provides a non-destructive application exit. Individual Fence menus own only settings and actions for that Fence; they do not create other Fences.

Removing a Fence closes its body, title/item layers and editor windows before moving its items back to the desktop. Failed removal reopens the remaining membership. Application exit stops file-dialog access monitoring, closes all Fence windows, and only then runs desktop recovery. `FenceShutdown` synchronizes queued window removals with the compositor before desktop file notifications.

`FileDialogMonitor` observes native Windows shell Open/Save dialogs through WinEvent hooks. `DesktopFileAccess` restores existing Fence items to the desktop when another application opens such a dialog, updates the live Fence paths, and keeps them available until all consuming processes have exited. Closing the file dialog alone never reclaims a path. Process IDs and start times are persisted in `FenceInfo.DesktopAccessClients`, so restarting Fences preserves active access and PID reuse cannot extend an expired client. Release journaling preserves membership; temporary release never merges with or overwrites a pre-existing desktop item. Process-exit events trigger reclaim without polling. Move failures retain the current path and report through the tray and error log. Custom file browsers that do not use the native shell dialog are outside the detector's scope.

The item context menu places a divider above “移回桌面”, immediately followed by “删除”; both actions are bold and share the existing desktop-release font. The Delete action appears only for an item, names the target in a confirmation with No as the default, and uses Windows recycling with an undo record. `FenceManager.RecycleItem` persists membership removal before recycling; failure or cancellation restores membership when the item still exists. A failed metadata write prevents the actual delete. Successful deletion clears stale selection/hover state and must not be reversed by exit recovery. “删除” is distinct from removing a Fence, which restores its contents to the desktop.

For valid Shell shortcuts, “打开文件所在位置” or “打开文件夹所在位置” precedes “以管理员身份运行”. The action opens the actual target's parent directory and selects the file or folder through the Windows Shell, without executing it or opening the stored shortcut's directory. Broken/non-filesystem targets and ordinary items do not offer this action. `ShellShortcut` owns shared resolution of the target, arguments and working directory; elevation continues to preserve the latter two values.

## Do's and Don'ts

- **Do:** Keep wallpaper perceptible below the opaque black title bar while maintaining readable white titles and item labels.
- **Do:** Preserve Windows-native interaction and localization conventions.
- **Don't:** Use blur without a tint and legibility overlay.
- **Don't:** Introduce decorative animation or reshape unrelated dialogs.

The public application/assembly name is `Fences`, defined for UI captions by `Program.AppName` and for build output by `Fences.csproj`. The existing `NoFences` source namespace remains compatible with resource and reflection references. `AppDataPaths` owns the runtime storage root `%LocalAppData%\Fences` and its `Logs` subdirectory. Before starting the recovery process, `AppDataMigration` runs under the legacy recovery mutex and moves old `%LocalAppData%\NoFences` data into the new root, atomically rebasing stored-item and pending-release paths while retaining external paths and unknown XML fields. Migration can resume after interruption and never overwrites a colliding Fence. Legacy single-instance and recovery mutex names remain coordination identifiers; normal operation and exit recovery use the new storage path.
