# Windows regression checks

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

Shutdown checks assert that the body, item/title layers and title editor are destroyed before restoring a deleted Fence's items, that a locked item reopens the remaining Fence after removal fails, and that every Fence closes before exit recovery restores any items. `FenceShutdownTests --preview <unique-event-name>` opens an isolated visual test host; signal its named auto-reset event once to remove the Fence and once more to close the host. This mode uses temporary storage, never the user's desktop.

Data migration checks use isolated temporary application-data roots. They verify new storage/log paths, old XML compatibility, stored-item and pending-release path rebasing, preservation of external paths and unknown fields, independent old/new Fences, log collisions, interrupted migration, conflict refusal, locked-file retry, and exit recovery from the migrated root. They do not migrate the user's actual application data.

Deletion checks cover persistence failure before recycling, cancellation/error rollback, skipped operations, successful and missing-item cleanup, foreign-path rejection, temporary desktop items, and item/background menu visibility. They render the item menu to `Fences/obj/VisualChecks/item-delete-menu.png`. GUID-named test file/folder fixtures are sent through the real Windows Recycle Bin and restored before temporary-directory cleanup; existing user files and recycle-bin entries are left untouched.

Shortcut checks render executable, document and folder shortcut menus, verify target-dependent labels, row order, bold actions, and rejection of missing/corrupt/non-shortcut targets. `--explorer` additionally verifies actual parent-folder navigation and target selection for file/folder links; only newly opened Explorer windows still displaying the test's GUID-named folder are closed. Existing elevation tests continue checking shortcut arguments and working directories.

`SfxDefaultDirectoryTests.cs` verifies the actual embedded release/admin and application/asInvoker manifests, reads the native extraction dialog's default and explicitly overridden directory, checks the field remains editable, and validates extraction hashes and preservation of an existing configuration sample. Prepare `Fences/obj/SfxModule/test-package.exe` by concatenating `7z-test.sfx` with the exact release archive payload after the production module prefix. This test-only copy avoids UAC automation; final administrator approval and writing to Program Files require manual acceptance. The rendered native dialog is captured at `Fences/obj/VisualChecks/sfx-default-directory.png`.
