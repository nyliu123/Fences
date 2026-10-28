# Windows regression checks

`OriginalPathsTests --recycle-only --offscreen` uses `RecycleRestoreFixture.cpp` for real Recycle Bin restoration coverage: four GUID-named file/folder fixtures are recycled and dropped through the registered native desktop target, with automatic and manual arrangement and different sort directions. Assertions check intact contents, retained sorting preferences, a live caption surface and complete rendered snapshots. Only the exact owned fixtures are restored; existing bin entries remain unchanged. Run this scenario in its own process, separately from `--rename-only`. Build the required helper beside the regression executable in the x64 developer environment:

```powershell
cl /nologo /LD /EHsc /std:c++17 /utf-8 Tests\RecycleRestoreFixture.cpp /Fo:Fences\obj\RegressionTests\RecycleRestoreFixture.obj /Fe:Fences\obj\RegressionTests\RecycleRestoreFixture.dll /link ole32.lib shell32.lib uuid.lib user32.lib propsys.lib
```

The restoration mode fails explicitly if this helper is missing; the ordinary suite does not run the scenario. The helper reproduces Shell clearing the view's sort-column count to zero; this state must preserve the configured primary sort rather than fail a one-column read.

`DesktopTransferTests.cpp` verifies production OLE registration on the item and caption windows, registration release, real file moves into and out of an isolated desktop folder, the production drag source's Shell data object and original path, Recycle Bin move feedback, ascending/descending sorting of new arrivals, selection preservation, and manual-layout preservation. It never drops into the real Recycle Bin or changes the real desktop. Compile in the x64 Visual Studio developer environment, linking `Fences/obj/NativeDesktop/Release/Fences.Desktop.lib` plus `ole32 shell32 uuid user32 comctl32`, and place the final Release `Fences.Desktop.dll` beside the executable. Physical pointer dragging, right-drag menus, and cross-monitor behavior still require a live desktop check.

The dotted-shortcut case in `OriginalPathsTests` verifies the actual native editor selection and saves `desktop-rename-dotted-shortcut.png`. In `--offscreen`, the item-menu return regression explicitly uses Rename; Shell clipboard command execution is not asserted in that mode.

## 1.7 original-path desktop integration

Version 1.7.15's scrolling checks sample exact RGBA pixels with a colored translucent panel, verify the thumb is opaque in ordinary/dragging states, and change the panel color to check live updates. `scroll-custom-color.png` is an additional rendered composition.

Version 1.7.14 adds `ScrollingInteractionTests.cs`: an owned 30-folder fixture checks actual layered-background thumb pixels, positive track geometry, drag clamping/capture release, paging, wheel behavior, hit exclusion and overflow removal; `scroll-top.png` / `scroll-bottom.png` are rendered compositions. Compile with the same manifest/x64/WinForms/Drawing references as the integration tests. `DesktopSelectionInteractionTests.cpp` uses the production interaction class and a native owned list to check late selection restoration, one-shot cleanup, invalidation by new input, marquee preservation and single-target deselection; link `ole32 shell32 uuid user32 comctl32`.

The integration suite records each published rename-dismissal visual state for both unchanged and actually renamed files, verifies a newly selected item remains selected, and invokes a command through the real native item menu before returning to a blank desktop point. In `--offscreen`, the menu test uses Rename instead of Copy and explicitly reports that Shell clipboard commands are not verified. This verifies the shared return path, not every third-party menu verb or physical desktop interaction.

Version 1.7.13 observes the original native EDIT's final `WM_WINDOWPOSCHANGED` geometry independently of the replacement editor. It compares actual width/initial height, checks wrapped long names and height restoration, retains stem-only selection and real Enter/Esc rename round trips, and verifies partition editors use their name-column width. The native control remains hidden until it is cancelled. In `--offscreen` mode only, live drag hit-testing is explicitly skipped if another process covers the owned target; skipped assertions are not counted or reported as verified. Default live mode remains strict. Rendered editor images are saved with the other `VisualChecks` snapshots.

`MixedIconRenderingTests.cs` uses the full production `DesktopIntegration` with owned storage and an owned original icon layer. It creates valid custom-icon executables (including a requireAdministrator manifest, never executed), custom folders, executable/folder/broken/system-namespace shortcuts, URL, wide PNG, tall JPEG, BMP/GIF, transparent PNG, valid PDF/DOCX/ZIP, JSON, TXT, ICO, WAV and an unknown extension. A system WMV sample is used only if available; otherwise video is explicitly skipped. It does not edit desktop files, user configuration, file associations or the Recycle Bin.

The first completed surface is saved BEFORE any reference `WM_PRINTCLIENT` call, after only normal message pumping: no hover or manual refresh primes the test. Custom artwork and thumbnail aspect ratios are checked independently of the reference, so a generic placeholder cannot pass merely by matching another placeholder. The test then requires exact RGB equality of every native icon region at 48/32/96 pixels and verifies opaque matching pixels in the actual caption presentation layer. It also adds a new executable, changes PNG content and renames a PNG without manual refresh. `MixedIcons/presented-*.png` and `compare-*.png` are rendering evidence, not physical-screen text-opacity acceptance. Compiling the same probe against the old 1.7.11 binaries provides a failing negative control.

```powershell
csc /nologo /platform:x64 /win32manifest:Fences/app.manifest /r:Fences/bin/Release/Fences.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:Fences/obj/RegressionTests/MixedIconRenderingTests.exe Tests/MixedIconRenderingTests.cs
# Run beside the matching Release executable, native DLL, config and language resources.
& ./Fences/obj/RegressionTests/MixedIconRenderingTests.exe
```

Version 1.7.11 checks that configuration loading precedes native view creation, initial membership filtering uses one navigation and zero explicit view refreshes, rename keeps logical selection without painting its backdrop, and same-directory desktop-release feedback uses an arrow. Feedback cases include copy/link modifiers and effects, other icons/windows and cancellation; they call the production feedback handler against owned native target windows, without moving the user's pointer. These checks do not constitute a physical drag through Explorer. The legacy drag suite continues checking move rollback and drag-ghost tracking.

`DesktopPerformanceTests` reports initialization, activation and the first fully enumerated rendered frame separately (`startup_first_enumerated_frame_ms`). This is NOT a measurement of real icon/thumbnail extraction completion; use the mixed-content checks above for that. `--members` assigns 40 of the 80 files to an owned fence before startup. Historical Release enumeration-frame samples on 2026-10-27: 478 → 362 ms without members and 873 → 624 ms with 40 members (1.7.10 → 1.7.11). These are application-internal test-window timings with local text files, not cold process-launch measurements or guarantees for thumbnail/network/Shell extensions. Startup counters, idle/hover preparation counts and rendered/unchanged frame checks are separate from elapsed time.

`DesktopPerformanceTests.cs` creates an owned 80-file desktop and reports elapsed time, calling-thread managed allocations, Shell display-name queries and presentations for 100 unchanged updates and 30 hover transitions. `--verify` compares 12 incremental frames byte-for-byte with forced complete compositions and checks external rename updates without manual refresh. Build against the target executable with `System.Windows.Forms` / `System.Drawing`, its manifest and x64; run beside the matching executable/resources/native DLL. It never uses the user's real desktop/configuration. `NativeMenuPerformanceTests.cpp` includes the production menu implementation, uses a deterministic `IContextMenu3` fixture and verifies 4,000 lookups initialize each submenu once while the next popup refreshes provider state. Link `ole32 shell32 uuid user32 comctl32`.

Release comparison on 2026-10-27 (80 files, matching x64 binaries):

| Measurement | 1.7.9 | 1.7.10 |
| --- | ---: | ---: |
| 100 unchanged updates, managed bytes allocated | 1,061,299,448 | 224 |
| 100 unchanged updates, Shell display-name resolutions | 8,000 | 0 |
| 30 hover transitions, 1000 × 700 client | 2,472 ms | 677 ms |
| 30 hover transitions, managed bytes allocated | 318,799,872 | 3,046,576 |
| 30 hover transitions, 1707 × 1048 client (`--large`) | 2,440 ms | 855 ms |

These are local elapsed-time samples, not latency guarantees. Earlier runs of the same final Release DLL took 4.6–4.7 seconds for the hover case; subsequent repeated runs took 0.66–0.68 seconds without rebuilding. The cause of that timing variation was not established. Allocation/call reductions and incremental/full pixel parity are separate deterministic checks; third-party popup latency and live desktop compositor opacity still require observation in the target session. The final Release passed 141 original-path assertions, 12 incremental/full comparisons at each size, external rename checks and the native menu provider-count test. Temporary test hosts exit after their checks.

Version 1.7.9 checks a normal opaque top-level rename window, absence of the desktop-child editor, filename/stem selection, Enter/Esc, real file and folder rename round trips, collision preservation, and stable Shell identity after another item is inserted. The collision probe holds the same reentrancy guard as the controller while Shell pumps focus messages. Coverage checks require opaque pixels across the icon, connecting space and caption in the SAME rendered layer, including the actual icon artwork. These tests must not infer a seamless live display merely from matching background colors. `--offscreen` continues to mean that actual display visibility has not been accepted.

Version 1.7.8 adds shared-surface pixel checks for hover, selected-hover, multiple selection and inactive selection. It verifies both the rendered caption backdrop and native icon margin have identical tint/opacity on a uniform fixture. Rename checks assert the actual caption HWND region excludes the editor and that a parent repaint preserves its text/focus. In a session whose screen is unavailable, `OriginalPathsTests --rename-only --offscreen` saves compositions of the real native paint surface, rendered caption bitmap and native EDIT print output. This mode explicitly skips screen acceptance; it must not be described as a successful real-desktop visibility check. The default mode samples the actual screen and rejects unrelated-window coverage.

The 1.7.7 targeted checks start rename with one label click from an unselected state, inspect native-themed selection under the complete name, and assert that menu cancellation, ordinary refresh, theme/composition and generic settings broadcasts do not invalidate wallpaper. `LiveRecycleBinTests <actual Fences PID>` operates on the real running desktop. It requires an initially empty bin, recycles only a GUID-scoped fixture twice and restores that exact fixture after each cycle. It compares actual WM_PAINT icon pixels for empty/full/restored states and asserts no wallpaper reload; it never empties existing bin contents. Icon images saved under `VisualChecks/recycle-*.png` are the actual buffer copied to the desktop, read without forcing a repaint. These checks do not establish live caption opacity.

The 1.7.6 `--rename-only` checks also exercise three slow label clicks, focus transfer back to an empty desktop point, global Shell image notifications, deferral while editing, and wallpaper notifications during rename. The image notification test checks callback refresh and membership preservation; it does not empty the user's Recycle Bin. `DragDropAndElevationTests` verifies cursor tracking without `GiveFeedback`, transparent hit testing, and timer cleanup when the ghost is hidden or closed. Human verification is still needed for a physical drag across monitors and an actual slideshow advance.

`DesktopWallpaperLayoutTests.cpp` includes the production wallpaper renderer and checks six placements with an owned two-color image, background margins, desktop-anchored tiling, negative monitor coordinates, and read-only rendering of the current Shell wallpaper. It saves `wallpaper-layouts.png` for actual layout inspection. Link `ole32 shell32 uuid user32 gdi32 gdiplus`.

The 1.7.5 caption surface has separate selection/edit coverage in `OriginalPathsTests`. `--rename-only` runs the integration/lifecycle checks and rename assertions without touching a busy system clipboard. It checks visible editor text and focus after a 500 ms caption refresh, the stem-only `EM_GETSEL` range, unchanged wallpaper behind selected captions, and zero Alpha across the editor opening. `desktop-caption-selected.png` and `desktop-caption-edit-opening.png` are actual rendered surface snapshots for those coverage checks; they do not validate live opacity. The real desktop's unhovered single solid caption state was checked separately by user observation.

Live text-opacity acceptance must compare unhovered and hovered labels by direct observation. Captures can contain white text while the actual desktop remains transparent, so captured white pixels, matching binaries and synthetic Alpha checks do not establish success. The explicit-caption test build remains unpublished until that normal-display check succeeds. `DesktopCompositionProbe.cs <Fences PID>` temporarily disables layering on the actual Explorer parent, journals its attributes, starts a recovery watcher, and restores them after at most 120 seconds; the observed result was still transparent, so this hypothesis was rejected. `OpaquePaintBufferTests.cpp` checks dirty-region Alpha/RGB preservation, reuse, resize clipping and GDI resource cleanup without showing UI or taking a capture.

`LiveDesktopTests.cs <absolute Fences.exe path>` is an explicitly invoked live diagnostic, not an isolated test. Back up real application data before running it. It launches the actual executable, loads the real user's fences, temporarily minimizes ordinary windows, and logs the owned view's DPI/styles without capturing. A file named `capture-direct` beside the diagnostic requests a screenshot; `stop-direct` requests normal exit. `FENCES_LIVE_TEST_SECONDS` controls the bounded run (default 90 seconds, maximum 300). Remove old request files before a new run. It posts `WM_QUIT` only to the launched application's GUI thread, waits for cleanup, then undoes its minimize operation. Human observation of the normal desktop is needed to confirm the non-screenshot state; invoking Main by reflection or comparing EXE hashes does not establish that state.

`DesktopSourceTests.cpp` compiles with `Fences.Native/DesktopSource.cpp` and links `ole32 oleaut32 shell32 uuid user32 comctl32 gdi32`. Pass the absolute final `Fences.Desktop.dll` path. It reads the real Explorer desktop and creates an owned hidden production-mode view without hiding or showing over the original. It verifies the full item count and each canonical identity, inherited icon size, exact ListView grid spacing, disabled alpha-shadow text, and repairs a zero-alpha white completed-item buffer through the real custom-draw handler. The number of item checks depends on the current desktop. It never writes user files, settings or registry entries. Wallpaper tests must capture all normal-display states before sending the alpha-probe `WM_PRINTCLIENT` message.

Keyboard coverage queues messages to the owned native view through the registered WinForms filter, using thread-local modifier state restored after each key. It checks Ctrl+A/C/V, F2/Escape, F5 and Delete against real Shell selection, clipboard paths, editing and temporary files. Clipboard formats are materialized before Copy and restored at the end. The deleted fixture is found by its exact original folder and name in the Recycle Bin, restored, then cleaned up. No global keyboard hook is installed. Wallpaper coverage also checks opaque background alpha in a 32-bit paint target and opaque white glyph pixels in every on-screen icon label.

`DesktopWallpaperTests.cs` uses the current system wallpaper read-only in an isolated native host with 30 temporary icons. It directly captures on-screen pixels with `CopyFromScreen`, comparing 298 item-cell corner samples against a full-wallpaper reference after initial display, partial/full redraw, resize, membership filtering and switching to small icons. The 1.7.0 DLL failed 51 of 60 initial on-screen samples despite passing the previous `PrintWindow` capture, which triggers a full repaint and masks the fault. The test captures the rendered states in `Fences/obj/RegressionTests/VisualChecks/wallpaper-*.png`; inspect these for continuous wallpaper and readable labels. Keep the owned test window unobscured during this screen-capture test. It never changes wallpaper settings or takes over Explorer. Run it against the same Release DLL being packaged:

```powershell
csc /nologo /platform:x64 /win32manifest:Fences/app.manifest /r:Fences/bin/Release/Fences.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:Fences/obj/RegressionTests/DesktopWallpaperTests.exe Tests/DesktopWallpaperTests.cs
Copy-Item Fences/bin/Release/Fences.Desktop.dll Fences/obj/RegressionTests -Force
& ./Fences/obj/RegressionTests/DesktopWallpaperTests.exe
```

Build requires .NET Framework 4.8 targeting tools, Visual Studio C++ x64 Build Tools and Windows SDK. `Fences.sln` builds `Fences.Desktop.dll` automatically. The standalone integration test uses an owned fake `SHELLDLL_DefView` and original icon layer, GUID-named temporary storage and an independent native desktop view; it does not replace the real Explorer desktop or modify personal desktop files.

```powershell
msbuild Fences.sln /t:Build /p:Configuration=Debug '/p:Platform=Any CPU' /v:minimal /nologo
New-Item -ItemType Directory -Force Fences/obj/RegressionTests | Out-Null
Copy-Item Fences/bin/Debug/Fences.exe,Fences/bin/Debug/Fences.Desktop.dll Fences/obj/RegressionTests/
Copy-Item Fences/bin/Debug/Fences.exe.config Fences/obj/RegressionTests/OriginalPathsTests.exe.config
foreach ($culture in @('cs', 'zh-CN')) {
    New-Item -ItemType Directory -Force "Fences/obj/RegressionTests/$culture" | Out-Null
    Copy-Item "Fences/bin/Debug/$culture/Fences.resources.dll" "Fences/obj/RegressionTests/$culture/Fences.resources.dll"
}
csc /nologo /target:exe /platform:x64 /win32manifest:Fences/app.manifest /out:Fences/obj/RegressionTests/OriginalPathsTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests/OriginalPathsTests.cs
& ./Fences/obj/RegressionTests/OriginalPathsTests.exe
```

Checks cover original-path rollback, equal-content independent files, locked-source transfer, collision-safe legacy migration, interrupted-transfer membership repair, native filtering, reference-effect drag/drop, locked-file release, external rename/deletion, native classic menus (including Show desktop icons), modern native file selection, persistent settings and restart consumption, normal shutdown and cross-process crash restoration. The crash helper calls `ExitProcess` without managed cleanup; recovery runs against a fake original layer owned by the surviving test process. Tests capture real native rendering under `Fences/obj/RegressionTests/VisualChecks`.

These tests do not establish complete Windows 10/11, physical mixed-DPI, dynamic wallpaper or arbitrary third-party Shell-extension compatibility, and do not restart the user's Explorer. The remaining suites below retain coverage of the legacy storage/temporary-release implementation and unchanged Fence interactions.

Run from the repository root in a Visual Studio Developer PowerShell with .NET Framework 4.8 build tools:

```powershell
msbuild Fences.sln /t:Build /p:Configuration=Debug /v:minimal /nologo
New-Item -ItemType Directory -Force Fences/obj/RegressionTests | Out-Null
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/RegressionTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll Tests/DragDropAndElevationTests.cs
Copy-Item Fences/bin/Debug/Fences.exe Fences/obj/RegressionTests/Fences.exe
Copy-Item Fences/bin/Debug/Fences.exe.config Fences/obj/RegressionTests/Fences.exe.config
foreach ($culture in @('cs', 'zh-CN')) {
    New-Item -ItemType Directory -Force "Fences/obj/RegressionTests/$culture" | Out-Null
    Copy-Item "Fences/bin/Debug/$culture/Fences.resources.dll" "Fences/obj/RegressionTests/$culture/Fences.resources.dll"
}
& ./Fences/obj/RegressionTests/RegressionTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/AppearanceAndDisplayTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests/AppearanceAndDisplayTests.cs
& ./Fences/obj/RegressionTests/AppearanceAndDisplayTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/FileDialogMonitorTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll Tests/FileDialogMonitorTests.cs
& ./Fences/obj/RegressionTests/FileDialogMonitorTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/DesktopAccessTests.exe /reference:Fences/bin/Debug/Fences.exe Tests/DesktopAccessTests.cs
& ./Fences/obj/RegressionTests/DesktopAccessTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/DesktopAccessIntegrationTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests/DesktopAccessIntegrationTests.cs
& ./Fences/obj/RegressionTests/DesktopAccessIntegrationTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/FenceShutdownTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests/FenceShutdownTests.cs
& ./Fences/obj/RegressionTests/FenceShutdownTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/BrandingCompatibilityTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests/BrandingCompatibilityTests.cs
& ./Fences/obj/RegressionTests/BrandingCompatibilityTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/AppDataMigrationTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll Tests/AppDataMigrationTests.cs
& ./Fences/obj/RegressionTests/AppDataMigrationTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/ItemDeletionTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll Tests/ItemDeletionTests.cs
& ./Fences/obj/RegressionTests/ItemDeletionTests.exe
csc /nologo /target:exe /platform:x64 /out:Fences/obj/RegressionTests/ShortcutMenuTests.exe /reference:Fences/bin/Debug/Fences.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll Tests/ShortcutMenuTests.cs
& ./Fences/obj/RegressionTests/ShortcutMenuTests.exe --explorer
```

The test process uses a hidden window parent and temporary storage, without touching real fences or invoking UAC. It checks locked-file drag callbacks, rollback, successful moves, local reordering, allowed effects, and executable/shortcut elevation metadata. Error dialogs are queued but deliberately not displayed. Actual Explorer dragging and UAC acceptance/cancellation require a manual Windows desktop check.

The appearance/display checks cover negative monitor origins, oversized/offscreen bounds, unchanged valid bounds, legacy XML defaults, radius persistence/cloning, wheel protection, live personalization, layered child positioning, and background thumbnail notifications. They render the native settings dialog at two sizes and the rounded Fence background into `Fences/obj/VisualChecks` for visual inspection. Physical mixed-DPI monitor layouts and Explorer desktop-icon removal ordering still require desktop integration checks.

Wheel checks send real `WM_MOUSEWHEEL` and `WM_MOUSEHWHEEL` messages, including fine deltas, with and without focus, and verify keyboard arrows still work. File-dialog tests open and cancel native Open/Save dialogs and confirm ordinary message boxes do not count. Desktop-access tests use GUID-named temporary storage and helper processes to check multi-client lifetime, persisted membership, PID reuse, collisions, edits, folders, and locked files. The integration test opens a file dialog in a second process, verifies files remain available after the dialog closes, reloads fences while the consumer lives, and checks automatic reclaim on process exit. They do not access the user's actual desktop files or start the exit-recovery watcher.

The desktop-access integration test also clicks the actual tray “刷新” command: live consuming apps retain their paths, expired clients are removed, locked-file failures retain data and report an error, and a later refresh retries successfully. Repeated refresh is safe. The native tray menu is rendered to `Fences/obj/RegressionTests/VisualChecks/tray-refresh-menu.png` for visual inspection.

Shutdown checks assert that the body, item/title layers and title editor are destroyed before restoring a deleted Fence's items, that a locked item reopens the remaining Fence after removal fails, and that every Fence closes before exit recovery restores any items. `FenceShutdownTests --preview <unique-event-name>` opens an isolated visual test host; signal its named auto-reset event once to remove the Fence and once more to close the host. This mode uses temporary storage, never the user's desktop.

Data migration checks use isolated temporary application-data roots. They verify new storage/log paths, old XML compatibility, stored-item and pending-release path rebasing, preservation of external paths and unknown fields, independent old/new Fences, log collisions, interrupted migration, conflict refusal, locked-file retry, and exit recovery from the migrated root. They do not migrate the user's actual application data.

Deletion checks cover persistence failure before recycling, cancellation/error rollback, skipped operations, successful and missing-item cleanup, foreign-path rejection, temporary desktop items, and item/background menu visibility. They render the item menu to `Fences/obj/VisualChecks/item-delete-menu.png`. GUID-named test file/folder fixtures are sent through the real Windows Recycle Bin and restored before temporary-directory cleanup; existing user files and recycle-bin entries are left untouched.

Shortcut checks render executable, document and folder shortcut menus, verify target-dependent labels, row order, bold actions, and rejection of missing/corrupt/non-shortcut targets. `--explorer` additionally verifies actual parent-folder navigation and target selection for file/folder links; only newly opened Explorer windows still displaying the test's GUID-named folder are closed. Existing elevation tests continue checking shortcut arguments and working directories.

`SfxDefaultDirectoryTests.cs` verifies the actual embedded release/admin and application/asInvoker manifests, reads the native extraction dialog's default and explicitly overridden directory, checks the field remains editable, and validates extraction hashes and preservation of an existing configuration sample. Prepare `Fences/obj/SfxModule/test-package.exe` by concatenating `7z-test.sfx` with the exact release archive payload after the production module prefix. This test-only copy avoids UAC automation; final administrator approval and writing to Program Files require manual acceptance. The rendered native dialog is captured at `Fences/obj/VisualChecks/sfx-default-directory.png`.
