using NoFences;
using NoFences.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

internal static class OriginalPathsTests
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Assembly product = typeof(FenceWindow).Assembly;
    private static readonly WndProc procedure = WindowProcedure;
    private static int assertions;
    private static bool renameOnly;
    private static bool offscreen;
    private static IntPtr watchedRenameList;
    private static Bounds nativeRenameBounds;
    private static long nativeRenameStyle;
    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, IntPtr data);
    private static readonly SubclassProcedure renameSizeObserver = ObserveRenameSize;
    private static readonly SubclassProcedure renameEditObserver = ObserveNativeRenameLayout;
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure callback, UIntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    private static IntPtr ObserveRenameSize(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, IntPtr data)
    {
        if (message == 0x4e && l != IntPtr.Zero && Marshal.ReadIntPtr(l) == watchedRenameList && Marshal.ReadInt32(l, 16) == -175)
        {
            var nativeEdit = SendMessage(watchedRenameList, 0x1018, IntPtr.Zero, IntPtr.Zero);
            SetWindowSubclass(nativeEdit, renameEditObserver, new UIntPtr(0xf324), IntPtr.Zero);
        }
        return DefSubclassProc(window, message, w, l);
    }
    private static IntPtr ObserveNativeRenameLayout(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, IntPtr data)
    {
        if (message == 0x47)
        {
            GetWindowRect(window, out var bounds);
            GetClientRect(watchedRenameList, out var client);
            if (bounds.Right > bounds.Left && bounds.Right - bounds.Left <= client.Right && bounds.Bottom - bounds.Top <= client.Bottom)
            { nativeRenameBounds = bounds; nativeRenameStyle = GetWindowLongPtr(window, -16).ToInt64(); }
        }
        if (message == 0x82) RemoveWindowSubclass(window, renameEditObserver, id);
        return DefSubclassProc(window, message, w, l);
    }
    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style; public WndProc Procedure; public int ClassExtra; public int WindowExtra;
        public IntPtr Instance; public IntPtr Icon; public IntPtr Cursor; public IntPtr Background;
        public string MenuName; public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Preferences
    {
        public uint Size; public uint Flags; public int IconSize; public Guid SortFormat; public uint SortProperty; public int SortDirection;
        public static Preferences Empty => new Preferences { Size = (uint)Marshal.SizeOf(typeof(Preferences)) };
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WndClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr module, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendUnicodeMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
    [StructLayout(LayoutKind.Sequential)] private struct ItemState { public uint Mask; public int Index, SubItem; public uint State, StateMask; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, ref ItemState state);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, ref Bounds bounds);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    private delegate bool WindowVisitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowVisitor visitor, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowVisitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint index, StringBuilder text, int size, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern void ExitProcess(uint code);
    [DllImport("comctl32.dll")] private static extern void InitCommonControls();
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint change, uint flags, IntPtr first, IntPtr second);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesVisibleCount(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesGetPreferences(IntPtr context, ref Preferences preferences);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesSetPreferences(IntPtr context, ref Preferences preferences);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int FencesItemPosition(IntPtr context, int index, StringBuilder name, uint capacity, out Point point);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FencesViewWindow(IntPtr context);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int FencesCaptionAt(IntPtr context, int index, StringBuilder name, uint capacity, out Bounds bounds, out uint state);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FencesCaptionEditor(IntPtr context, out Bounds bounds);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesCaptionSurface(IntPtr context, IntPtr dc);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int FencesCaptionDecoration(IntPtr context, int index, out Bounds bounds, out int state);
    [DllImport("Fences.Desktop.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int FencesCommitRename(IntPtr target, IntPtr owner, string name);

    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().FullName + ": " + exception.Message);
            try { Console.Error.WriteLine(exception.StackTrace); } catch { }
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        renameOnly = args.Contains("--rename-only");
        offscreen = args.Contains("--offscreen");
        if (offscreen) Console.WriteLine("LIMITATION: native render inspection only; live screen acceptance is unavailable in this session.");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        InitCommonControls();
        if (args.Length == 3 && args[0] == "--crash") { Crash(args[1], new IntPtr(long.Parse(args[2]))); return; }
        var root = Path.Combine(Path.GetTempPath(), "Fences-original-tests-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(root, "desktop"); Directory.CreateDirectory(desktop);
        ModelCases(root);
        var stateRoot = Path.Combine(root, "state");
        var manager = Manager(root, desktop);
        var module = GetModuleHandle(null);
        var cls = new WndClass { Procedure = procedure, Instance = module, ClassName = "SHELLDLL_DefView", Background = new IntPtr(6) };
        RegisterClass(ref cls);
        var host = CreateWindowEx(0, cls.ClassName, "Fences 1.7.0 isolated desktop", 0x10CF0000, 70, 70, 1000, 680, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        var icons = CreateWindowEx(0, "SysListView32", "Original desktop icons", 0x50000000, 0, 0, 980, 620, host, IntPtr.Zero, module, IntPtr.Zero);
        IDisposable integration = null;
        try
        {
            SetDesktopHost(host);
            var hostType = product.GetType("NoFences.Win32.DesktopWindowHost");
            try
            {
                hostType.GetMethod("Reset", Static).Invoke(null, null);
                var actual = (IntPtr)hostType.GetProperty("Handle", Static).GetValue(null);
                var parentClass = new StringBuilder(128); GetClassName(GetParent(actual), parentClass, parentClass.Capacity);
                Check(parentClass.ToString() == "Progman" || parentClass.ToString() == "WorkerW",
                    "read-only host discovery resolves the Windows desktop rather than an arbitrary Shell folder window");
            }
            finally { SetDesktopHost(host); }
            MigrationCases(root);
            typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, manager);
            var oldSource = Path.Combine(desktop, "旧版项目.txt"); File.WriteAllText(oldSource, "legacy data", Encoding.UTF8);
            var info = new FenceInfo(Guid.NewGuid()) { Name = "原路径分区", PosX = 650, PosY = 130, Width = 260, Height = 300 };
            info.Files.Add(manager.ImportItem(info, oldSource)); manager.UpdateFence(info);
            var stored = info.Files[0];
            Check(!File.Exists(oldSource) && File.Exists(stored), "legacy fixture uses stored-file semantics before upgrading");
            var plain = Path.Combine(desktop, "桌面普通文件.txt"); File.WriteAllText(plain, "plain", Encoding.UTF8);
            var next = Path.Combine(desktop, "第二个分区项目.txt"); File.WriteAllText(next, "second", Encoding.UTF8);
            integration = Integration(manager, stateRoot, host, desktop);
            Call(integration, "Start");
            Check(IsWindowVisible(icons), "starting the view does not hide original icons before configuration is loaded");
            Check(Native(integration) == IntPtr.Zero, "startup defers Shell enumeration until membership has been loaded");
            Check(manager.LoadFences().Count == 0, "1.6 metadata migrates and opens without errors");
            var fence = Application.OpenForms.OfType<FenceWindow>().Single();
            info = (FenceInfo)typeof(FenceWindow).GetProperty("Info", Instance).GetValue(fence);
            Check(info.KeepOriginalPaths && info.Files[0] == oldSource && File.Exists(oldSource) && !File.Exists(stored),
                "upgrade restores the stored item to Desktop and persists original-path membership");
            Check(!IsWindowVisible(icons), "original icon layer is hidden only after recovery journaling");
            Check(File.Exists(Path.Combine(stateRoot, "desktop-view-session.xml")), "active desktop session has a durable recovery record");
            var native = Native(integration);
            Check(SendMessage(FencesViewWindow(native), 0x8000 + 178, new IntPtr(5), IntPtr.Zero).ToInt32() == 1 &&
                SendMessage(FencesViewWindow(native), 0x8000 + 178, new IntPtr(6), IntPtr.Zero).ToInt32() == 0,
                "startup applies migrated membership before one navigation without an extra Shell refresh");
            PumpUntil(() => FencesVisibleCount(native) == 2, "native view excludes the migrated member and shows ordinary desktop items");
            CheckOriginalDragFeedback(fence, info, oldSource, native);
            CheckNativeMenu(native, "desktop-menu-170.png");
            Check(SendMessage(FencesViewWindow(native), 0x8000 + 178, new IntPtr(4), IntPtr.Zero) == IntPtr.Zero,
                "opening and canceling the desktop menu does not invalidate the wallpaper");
            KeyboardCases(native, host, desktop, root, (IMessageFilter)integration);
            CheckPartitionRenameWidth(fence, info, oldSource);
            SelectOriginalFile(oldSource, desktop);
            Check(info.Files.Contains(oldSource) && !Names(native).Contains(oldSource) && File.Exists(oldSource),
                "native file selection does not release or move the hidden desktop member");
            var creation = File.GetCreationTimeUtc(next);
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { next });
            var drag = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.None);
            typeof(FenceWindow).GetMethod("FenceWindow_DragDrop", Instance).Invoke(fence, new object[] { fence, drag });
            Check(drag.Effect == DragDropEffects.Link && info.Files.Contains(next) && File.Exists(next) && File.GetCreationTimeUtc(next) == creation,
                "dropping onto a fence records a link without moving or recreating the source");
            PumpUntil(() => FencesVisibleCount(native) == 1, "drop removes the member icon from the native view");
            Check(File.ReadAllText(next, Encoding.UTF8) == "second", "the original path remains readable after its icon disappears");
            var blocked = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Move, DragDropEffects.None);
            typeof(FenceWindow).GetMethod("FenceWindow_DragDrop", Instance).Invoke(fence, new object[] { fence, blocked });
            Check(blocked.Effect == DragDropEffects.None && File.Exists(next), "a move-only external source is never told to delete an unchanged file");
            var copyOnly = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(FenceWindow).GetMethod("FenceWindow_DragDrop", Instance).Invoke(fence, new object[] { fence, copyOnly });
            Check(copyOnly.Effect == DragDropEffects.None && File.Exists(next),
                "copy-only temporary sources are rejected instead of recording an unretained temporary file");
            using (var held = new FileStream(next, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                manager.ReleaseItem(info, next);
                Check(File.Exists(next) && !info.Files.Contains(next), "releasing a member succeeds even while the real file is exclusively open");
            }
            PumpUntil(() => FencesVisibleCount(native) == 2, "releasing membership restores the icon without file movement");
            Call(manager, "AssignOriginalItems", info, new[] { next });
            var renamed = Path.Combine(desktop, "外部重命名.txt"); File.Move(next, renamed);
            PumpUntil(() => info.Files.Contains(renamed) && !info.Files.Contains(next), "external rename updates the persisted original path through filesystem events");
            PumpUntil(() => !Names(native).Contains(renamed), "renamed membership remains excluded from desktop rendering");
            var second = new FenceInfo(Guid.NewGuid()) { Name = "第二分区", KeepOriginalPaths = true, Width = 260, Height = 250, PosX = 650, PosY = 450 };
            manager.UpdateFence(second);
            var secondWindow = new FenceWindow(second); secondWindow.Show();
            manager.TransferItem(info, second, renamed);
            Check(!info.Files.Contains(renamed) && second.Files.Contains(renamed) && File.Exists(renamed), "cross-fence transfer only changes memberships");
            fence.Refresh(); secondWindow.Refresh();
            PumpUntil(() => FencesVisibleCount(native) == 1, "native membership remains compact after transfer");
            for (var index = 0; index < FencesVisibleCount(native); ++index)
            {
                var name = new StringBuilder(32768); Point point;
                FencesItemPosition(native, index, name, (uint)name.Capacity, out point);
                Console.WriteLine("NATIVE POSITION: " + Path.GetFileName(name.ToString()) + " " + point);
            }
            var renderWait = Stopwatch.StartNew();
            PumpUntil(() => renderWait.ElapsedMilliseconds >= 700, "native asynchronous icon rendering completes before visual capture");
            Capture(host, "original-paths-desktop.png");
            File.Delete(renamed);
            PumpUntil(() => !second.Files.Contains(renamed), "external deletion removes the stale fence membership");
            var preferences = Preferences.Empty; Marshal.ThrowExceptionForHR(FencesGetPreferences(native, ref preferences));
            preferences.Flags |= 0x1000;
            Marshal.ThrowExceptionForHR(FencesSetPreferences(native, ref preferences));
            CheckNativeMenu(native, "desktop-menu-icons-hidden-170.png");
            preferences.Flags &= ~0x1000u;
            preferences.Flags = (preferences.Flags | 4u) & ~1u; preferences.IconSize = 32;
            Marshal.ThrowExceptionForHR(FencesSetPreferences(native, ref preferences));
            var notifications = (NativeWindow)integration.GetType().GetField("notifications", Instance).GetValue(integration);
            PostMessage(notifications.Handle, 0x8000 + 117, new IntPtr(1), FencesViewWindow(native));
            PumpUntil(() => File.Exists(Path.Combine(stateRoot, "desktop-view-settings.xml")) &&
                File.ReadAllText(Path.Combine(stateRoot, "desktop-view-settings.xml"), Encoding.UTF8).Contains("<IconSize>32</IconSize>"),
                "user view preferences are saved to persistent configuration");
            var settings = File.ReadAllText(Path.Combine(stateRoot, "desktop-view-settings.xml"), Encoding.UTF8);
            Check(settings.Contains("<IconSize>32</IconSize>") && settings.Contains("<AutoArrange>false</AutoArrange>"), "saved configuration reflects actual native view state");
            typeof(FenceWindow).GetMethod("RemoveFenceAndClose", Instance).Invoke(fence, null);
            Check(fence.IsDisposed && File.Exists(oldSource), "deleting a fence closes its windows and preserves the original file");
            PumpUntil(() => Names(native).Contains(oldSource), "deleting the partition restores its desktop icon");
            integration.Dispose(); integration = null;
            Check(IsWindowVisible(icons) && !Application.OpenForms.OfType<FenceWindow>().Any(), "shutdown closes all fence windows and restores the original icon layer");
            Check(!File.Exists(Path.Combine(stateRoot, "desktop-view-session.xml")), "normal shutdown clears the recovery record");
            integration = Integration(manager, stateRoot, host, desktop); Call(integration, "Start");
            manager.LoadFences(); native = Native(integration);
            var restored = Preferences.Empty; Marshal.ThrowExceptionForHR(FencesGetPreferences(native, ref restored));
            Check(restored.IconSize == 32 && (restored.Flags & 1) == 0, "a new desktop session loads and consumes the saved view configuration");
            integration.Dispose(); integration = null;
            CrashRecovery(root, host, icons);
            Check(File.Exists(oldSource) && File.ReadAllText(plain, Encoding.UTF8) == "plain", "lifecycle checks preserve original and unrelated desktop data");
        }
        finally
        {
            integration?.Dispose();
            foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) { fence.Close(); fence.Dispose(); }
            DestroyWindow(host);
            Directory.Delete(root, true);
        }
        Console.WriteLine("PASS: " + assertions + " original-path and native desktop assertions");
    }

    private static FenceManager Manager(string root, string desktop)
    {
        var value = (FenceManager)Activator.CreateInstance(typeof(FenceManager), Instance, null, new object[] { Path.Combine(root, "storage"), desktop }, null);
        typeof(FenceManager).GetField("<Instance>k__BackingField", Static).SetValue(null, value);
        return value;
    }
    private static void ModelCases(string root)
    {
        var directory = Path.Combine(root, "model"); Directory.CreateDirectory(directory);
        var manager = Manager(directory, directory);
        manager.GetType().GetProperty("KeepOriginalPaths", Instance).SetValue(manager, true);
        var first = new FenceInfo(Guid.NewGuid()) { KeepOriginalPaths = true };
        var second = new FenceInfo(Guid.NewGuid()) { KeepOriginalPaths = true };
        var firstPath = Path.Combine(directory, "first.txt");
        var secondPath = Path.Combine(directory, "second.txt");
        File.WriteAllText(firstPath, "first"); File.WriteAllText(secondPath, "second");
        first.Files.Add(manager.ImportItem(first, firstPath)); second.Files.Add(manager.ImportItem(second, secondPath));
        manager.UpdateFence(first); manager.UpdateFence(second);
        var metadata = Path.Combine(directory, "storage", second.Id.ToString(), "__fence_metadata.xml");
        bool failed = false;
        using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { Call(manager, "AssignOriginalItems", second, new[] { firstPath }); }
            catch (TargetInvocationException) { failed = true; }
            Check(failed && first.Files.SequenceEqual(new[] { firstPath }) && second.Files.SequenceEqual(new[] { secondPath }) &&
                File.Exists(firstPath) && File.Exists(secondPath), "metadata failure rolls original-path transfer back without changing either real file");
        }
        Check(File.ReadAllText(metadata).Contains(secondPath) && !File.ReadAllText(metadata).Contains(firstPath),
            "failed original-path assignment leaves the receiving persisted membership intact");
        var duplicate = Path.Combine(directory, "other", "first.txt"); Directory.CreateDirectory(Path.GetDirectoryName(duplicate));
        File.WriteAllText(duplicate, "first");
        Check(manager.FindEquivalentImportedItem(first, duplicate, manager.ImportItem(first, duplicate)) == null,
            "original-path mode does not deduplicate two different real files with equal names and content");
        manager.CompleteDuplicateImport(duplicate);
        Check(File.Exists(firstPath) && File.Exists(duplicate), "original-path cleanup never deletes an equivalent independent file");
        first.Locked = true;
        failed = false;
        try { manager.TransferItem(first, second, firstPath); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && first.Files.Contains(firstPath) && !second.Files.Contains(firstPath), "locked source membership rejects cross-fence transfer");
    }
    private static void MigrationCases(string root)
    {
        var directory = Path.Combine(root, "migration"); var desktop = Path.Combine(directory, "desktop"); Directory.CreateDirectory(desktop);
        var manager = Manager(directory, desktop);
        var path = Path.Combine(desktop, "同名项目.txt"); File.WriteAllText(path, "stored contents", Encoding.UTF8);
        var old = new FenceInfo(Guid.NewGuid()) { Name = "迁移", Width = 260, Height = 230 };
        old.Files.Add(manager.ImportItem(old, path)); manager.UpdateFence(old);
        File.WriteAllText(path, "independent desktop contents", Encoding.UTF8);
        manager.GetType().GetProperty("KeepOriginalPaths", Instance).SetValue(manager, true);
        Check(manager.LoadFences().Count == 0, "legacy collision migrates without overwriting the desktop item");
        var migrated = (FenceInfo)typeof(FenceWindow).GetProperty("Info", Instance).GetValue(Application.OpenForms.OfType<FenceWindow>().Single());
        Check(migrated.Files[0] != path && File.ReadAllText(migrated.Files[0], Encoding.UTF8) == "stored contents" &&
            File.ReadAllText(path, Encoding.UTF8) == "independent desktop contents", "migration preserves both colliding files and records the actual recovered name");
        foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) fence.Close();
        var receiver = new FenceInfo(Guid.NewGuid()) { Name = "接收分区", KeepOriginalPaths = true, Width = 260, Height = 230 };
        receiver.Files.Add(migrated.Files[0]); manager.UpdateFence(receiver);
        var sourceMetadata = Path.Combine(directory, "storage", migrated.Id.ToString(), "__fence_metadata.xml");
        File.SetLastWriteTimeUtc(sourceMetadata, DateTime.UtcNow.AddMinutes(-1));
        Check(manager.LoadFences().Count == 0, "interrupted transfer records reload safely");
        var infos = Application.OpenForms.OfType<FenceWindow>().Select(fence =>
            (FenceInfo)typeof(FenceWindow).GetProperty("Info", Instance).GetValue(fence)).ToList();
        Check(infos.Single(info => info.Id == receiver.Id).Files.Count == 1 && infos.Single(info => info.Id == migrated.Id).Files.Count == 0 &&
            File.Exists(migrated.Files[0]), "the newer receiving record resolves interrupted-transfer duplicates without moving or deleting data");
        foreach (var fence in Application.OpenForms.OfType<FenceWindow>().ToArray()) fence.Close();
    }
    private static IDisposable Integration(FenceManager manager, string root, IntPtr parent, string folder)
    {
        Func<List<FenceWindow>> windows = () => Application.OpenForms.OfType<FenceWindow>().ToList();
        Action<string> report = message => { throw new Exception("Unexpected desktop error: " + message); };
        return (IDisposable)Activator.CreateInstance(product.GetType("NoFences.Util.DesktopIntegration"), Instance, null,
            new object[] { manager, windows, report, root, parent, folder }, null);
    }
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Instance).Invoke(target, args);
    private static void SelectOriginalFile(string file, string directory)
    {
        using (var timer = new System.Windows.Forms.Timer { Interval = 200 })
        using (var dialog = new OpenFileDialog { Title = "Fences 1.7 文件选择验证", InitialDirectory = directory, FileName = file, CheckFileExists = true, RestoreDirectory = true })
        {
            var timeout = Stopwatch.StartNew();
            timer.Tick += (sender, e) => EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
            {
                var name = new StringBuilder(256); GetWindowText(window, name, name.Capacity);
                if (name.ToString() == dialog.Title)
                    PostMessage(window, 0x111, new IntPtr(timeout.ElapsedMilliseconds > 7000 ? 2 : 1), IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            timer.Start();
            var selected = dialog.ShowDialog(); timer.Stop();
            Check(selected == DialogResult.OK && dialog.FileName == file, "a real native file picker selects the grouped file at its unchanged path");
        }
    }
    private static void KeyboardCases(IntPtr context, IntPtr host, string desktop, string root, IMessageFilter filter)
    {
        var frame = FencesViewWindow(context);
        IntPtr list = IntPtr.Zero;
        EnumChildWindows(frame, (window, data) => { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "SysListView32") list = window; return true; }, IntPtr.Zero);
        SetForegroundWindow(host); SetFocus(list);
        Check(GetFocus() == list, "owned desktop has keyboard focus");
        var outside = Message.Create(host, 0x100, new IntPtr(0x74), new IntPtr(1));
        Check(!filter.PreFilterMessage(ref outside), "keyboard filter leaves unrelated windows alone");
        if (renameOnly)
        {
            CheckRenameEditor(context, list, Path.Combine(desktop, "桌面普通文件.txt"));
            CheckMouseInteractions(context, list, host);
            CheckShellUpdates(context, list);
            CheckItemMenuReturn(context, list, host, Path.Combine(desktop, "桌面普通文件.txt"));
            return;
        }
        // Materialize delayed-rendered data before native Copy replaces its owner.
        var previousClipboard = Clipboard.GetDataObject();
        var oldClipboard = new DataObject();
        if (previousClipboard != null)
            foreach (var format in previousClipboard.GetFormats(false))
            {
                var value = previousClipboard.GetData(format, false);
                if (value != null) oldClipboard.SetData(format, false, value);
            }
        try
        {
            QueueKey(list, 0x41, true);
            Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero).ToInt32() == 2, "Ctrl+A selects the visible desktop items");
            QueueKey(list, 0x43, true);
            Check(Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count == 2, "Ctrl+C exports native desktop paths");
            var source = Path.Combine(root, "keyboard-copy.txt"); File.WriteAllText(source, "keyboard fixture", Encoding.UTF8);
            Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { source });
            QueueKey(list, 0x56, true);
            var pasted = Path.Combine(desktop, Path.GetFileName(source));
            PumpUntil(() => File.Exists(pasted) && FencesVisibleCount(context) == 3, "Ctrl+V copies the temporary file into the owned desktop");
            var clear = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref clear);
            int selected = -1;
            for (int index = 0; index < FencesVisibleCount(context); ++index)
            {
                var name = new StringBuilder(32768); FencesItemPosition(context, index, name, (uint)name.Capacity, out var point);
                if (name.ToString() == pasted) selected = index;
            }
            Check(selected >= 0, "pasted fixture is addressable in the native view");
            var state = new ItemState { State = 3, StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(selected), ref state);
            CheckRenameEditor(context, list, pasted);
            Check(File.Exists(pasted), "Escape cancels native rename without changing the path");
            SetFocus(list);
            // Supply the real key through the same registered managed filter as the message loop.
            var refresh = Message.Create(list, 0x100, new IntPtr(0x74), new IntPtr(1));
            Check(filter.PreFilterMessage(ref refresh), "F5 is consumed by the Shell accelerator handler");
            PumpUntil(() => FencesVisibleCount(context) == 3, "F5 preserves native view contents");
            var settled = Stopwatch.StartNew();
            while (settled.ElapsedMilliseconds < 400) { Application.DoEvents(); Thread.Sleep(10); }
            EnumChildWindows(frame, (window, data) => { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); if (name.ToString() == "SysListView32") list = window; return true; }, IntPtr.Zero);
            for (int index = 0; index < FencesVisibleCount(context); ++index)
            {
                var name = new StringBuilder(32768); FencesItemPosition(context, index, name, (uint)name.Capacity, out var point);
                if (name.ToString() == pasted) selected = index;
            }
            SendMessage(list, 0x102b, new IntPtr(-1), ref clear);
            SendMessage(list, 0x102b, new IntPtr(selected), ref state);
            SetFocus(list);
            Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1, "Delete fixture is the only selected item after refresh");
            using (var cancel = new System.Windows.Forms.Timer { Interval = 3000 })
            {
                cancel.Tick += (sender, e) => EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
                {
                    var cls = new StringBuilder(128); GetClassName(window, cls, cls.Capacity);
                    if (cls.ToString() == "#32770" && IsWindowVisible(window))
                    {
                        PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                    }
                    return true;
                }, IntPtr.Zero);
                cancel.Start(); QueueKey(list, 0x2e);
                PumpUntil(() => !File.Exists(pasted) && FencesVisibleCount(context) == 2, "Delete removes only the selected temporary desktop file");
            }
            RestoreKeyboardFixture(pasted);
            File.Delete(pasted);
            PumpUntil(() => FencesVisibleCount(context) == 2, "temporary keyboard fixture is removed from the view");
            var deselect = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref deselect);
        }
        finally
        {
            if (previousClipboard != null) Clipboard.SetDataObject(oldClipboard, true, 20, 100); else Clipboard.Clear();
        }
    }
    private static void CheckRenameEditor(IntPtr context, IntPtr list, string path)
    {
        int selectedIndex = -1;
        var clear = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref clear);
        for (int index = 0; index < FencesVisibleCount(context); ++index)
        {
            var name = new StringBuilder(32768); FencesItemPosition(context, index, name, (uint)name.Capacity, out var position);
            if (!string.Equals(name.ToString(), path, StringComparison.OrdinalIgnoreCase)) continue;
            var state = new ItemState { State = 3, StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(index), ref state); selectedIndex = index; break;
        }
        var integrationType = product.GetType("NoFences.Util.DesktopIntegration");
        var integration = integrationType.GetField("current", Static).GetValue(null);
        var captions = integrationType.GetField("captions", Instance).GetValue(integration);
        var frameProperty = captions.GetType().GetProperty("FrameRendered", Instance);
        Bitmap frame = null;
        frameProperty.SetValue(captions, new Action<Bitmap>(image => { frame?.Dispose(); frame = (Bitmap)image.Clone(); }));
        watchedRenameList = list;
        SetWindowSubclass(GetParent(list), renameSizeObserver, new UIntPtr(0xf323), IntPtr.Zero);
        try
        {
            RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); PumpFor(100);
            Call(captions, "RefreshLabels");
            PumpUntil(() => frame != null, "selected-caption render snapshot is available");
            Bounds label = default(Bounds);
            for (int index = 0; index < FencesVisibleCount(context); ++index)
            {
                var name = new StringBuilder(32768);
                if (FencesCaptionAt(context, index, name, (uint)name.Capacity, out var bounds, out var flags) == 0 && name.ToString() == Path.GetFileName(path)) label = bounds;
            }
            var corner = frame.GetPixel(label.Left + 2, label.Top + 1);
            Check(corner.A == 255 && corner.ToArgb() != Color.FromArgb(255, 30, 40, 52).ToArgb() && corner.ToArgb() != SystemColors.Highlight.ToArgb(),
                "native themed selection extends under the filename without a separate solid-blue block");
            SaveCaptionFrame(frame, "desktop-caption-selected.png");
            Capture(GetParent(FencesViewWindow(context)), "desktop-selection-complete.png");
            foreach (var mode in new[] { "hover", "selected-hover", "multiple-selected", "inactive-selected" })
            {
                var reset = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref reset);
                if (mode != "hover")
                {
                    var selected = new ItemState { State = 2, StateMask = 3 };
                    SendMessage(list, 0x102b, new IntPtr(mode == "multiple-selected" ? -1 : selectedIndex), ref selected);
                }
                SendMessage(list, 0x103c, new IntPtr(mode.Contains("hover") ? selectedIndex : -1), IntPtr.Zero);
                SetFocus(mode == "inactive-selected" ? GetParent(FencesViewWindow(context)) : list);
                RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); PumpFor(100); Call(captions, "RefreshLabels");
                if (mode == "multiple-selected") Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero).ToInt32() == FencesVisibleCount(context),
                    "multiple-selection case contains every fixture item");
                CheckSharedBackground(context, frame, selectedIndex, mode);
                Capture(GetParent(FencesViewWindow(context)), "desktop-background-" + mode + ".png");
            }
            var single = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref single);
            single.State = 3; SendMessage(list, 0x102b, new IntPtr(selectedIndex), ref single);
            SetWindowPos(GetParent(FencesViewWindow(context)), new IntPtr(-2), 0, 0, 0, 0, 0x13);
            SetFocus(list); QueueKey(list, 0x71);
            PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "F2 opens the independent filename editor");
            var edit = RenameEditHandle();
            var delay = Stopwatch.StartNew();
            while (delay.ElapsedMilliseconds < 500) { Application.DoEvents(); Thread.Sleep(10); }
            var text = new StringBuilder(1024); GetWindowText(edit, text, text.Capacity);
            Check(IsWindowVisible(edit) && GetFocus() == edit, "caption refresh preserves the visible rename editor and its focus");
            Check(text.ToString() == Path.GetFileName(path), "rename editor retains the full filename text");
            var selection = SendMessage(edit, 0xb0, IntPtr.Zero, IntPtr.Zero).ToInt64();
            Check((selection & 0xffff) == 0 && ((selection >> 16) & 0xffff) == Path.GetFileNameWithoutExtension(path).Length,
                "rename initially selects only the filename stem, preserving its extension");
            Call(captions, "RefreshLabels");
            var editorForm = Control.FromHandle(edit).FindForm();
            Console.WriteLine("RENAME GEOMETRY: native=" + (nativeRenameBounds.Right - nativeRenameBounds.Left) + "x" +
                (nativeRenameBounds.Bottom - nativeRenameBounds.Top) + " style=" + nativeRenameStyle.ToString("X") + " own=" + editorForm.Size);
            Check(nativeRenameBounds.Right > nativeRenameBounds.Left && editorForm.Width == nativeRenameBounds.Right - nativeRenameBounds.Left,
                "rename width exactly matches the original native editor before it is cancelled");
            Check(editorForm.Height == nativeRenameBounds.Bottom - nativeRenameBounds.Top,
                "initial rename height preserves the native editor's vertical padding");
            var textBox = (TextBox)Control.FromHandle(edit);
            Check(textBox.Multiline && textBox.WordWrap, "desktop rename wraps within the native width");
            Check(editorForm.TopLevel && editorForm.Opacity == 1 && editorForm.TransparencyKey == Color.Empty &&
                GetAncestor(edit, 2) != GetAncestor(FencesViewWindow(context), 2),
                "rename uses a normal opaque top-level window outside Explorer's desktop composition");
            Check(SendMessage(list, 0x1018, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero,
                "the invisible desktop-child editor is not kept alive");
            Check(FencesCaptionAt(context, selectedIndex, new StringBuilder(32768), 32768, out var editingBounds, out var editingFlags) == 1,
                "the old name is suppressed by stable Shell identity while the independent editor is open");
            Check(FencesCaptionDecoration(context, selectedIndex, out var editingItem, out var editingState) == 0 && editingState == 1,
                "renaming suppresses the item's selection and hover backdrop");
            Check((SendMessage(list, 0x102c, new IntPtr(selectedIndex), new IntPtr(2)).ToInt32() & 2) != 0,
                "renaming preserves logical selection while removing only its painted highlight");
            using (var surface = new Bitmap(frame.Width, frame.Height))
            {
                using (var graphics = Graphics.FromImage(surface))
                {
                    var dc = graphics.GetHdc();
                    try { Marshal.ThrowExceptionForHR(FencesCaptionSurface(context, dc)); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                Check(surface.GetPixel(editingItem.Left + 2, editingItem.Top + 2).ToArgb() == Color.FromArgb(255, 30, 40, 52).ToArgb(),
                    "native rename render has no selection color behind the icon");
                SaveCaptionFrame(surface, "desktop-rename-no-highlight.png");
            }
            RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); PumpFor(150);
            Capture(GetParent(FencesViewWindow(context)), "desktop-rename-after-parent-redraw.png");
            if (!offscreen)
            {
            var previousEditDpi = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(edit));
            GetWindowRect(edit, out var screenEdit);
            using (var screenshot = new Bitmap(screenEdit.Right - screenEdit.Left, screenEdit.Bottom - screenEdit.Top))
            {
                Check(GetAncestor(WindowFromPoint(new Point((screenEdit.Left + screenEdit.Right) / 2, (screenEdit.Top + screenEdit.Bottom) / 2)), 2) ==
                    GetAncestor(edit, 2), "rename screen sample belongs to the owned test window");
                using (var graphics = Graphics.FromImage(screenshot)) graphics.CopyFromScreen(screenEdit.Left, screenEdit.Top, 0, 0, screenshot.Size);
                int ink = 0, light = 0;
                for (int y = 3; y < screenshot.Height - 3; ++y) for (int x = 3; x < screenshot.Width - 3; ++x)
                {
                    var color = screenshot.GetPixel(x, y);
                    if ((color.R < 80 && color.G < 80 && color.B < 80) || (color.B > color.R + 25 && color.B > 100)) ++ink;
                    if (color.R > 200 && color.G > 200 && color.B > 200) ++light;
                }
                SaveCaptionFrame(screenshot, "desktop-rename-editor-screen.png");
                Check(ink > 20 && light > 20, "rename text/selection and opaque editor remain rendered on screen after the parent repaints");
            }
            SetThreadDpiAwarenessContext(previousEditDpi);
            }
            else CaptureEditor(edit, "desktop-rename-editor-render.png");
            SaveCaptionFrame(frame, "desktop-caption-edit-opening.png");
            var originalText = textBox.Text;
            var initialHeight = editorForm.Height;
            textBox.Text = new string('测', 32) + ".txt";
            Check(editorForm.Width == nativeRenameBounds.Right - nativeRenameBounds.Left && editorForm.Height > initialHeight &&
                SendMessage(edit, 0x00ba, IntPtr.Zero, IntPtr.Zero).ToInt32() > 2,
                "long filenames grow vertically without widening the native rename column");
            CaptureEditor(edit, "desktop-rename-wrapped-long.png");
            textBox.Text = originalText;
            Check(editorForm.Height == initialHeight && textBox.Text == originalText,
                "shortening a filename restores its height and preserves all text");
            QueueKey(edit, 0x1b);
            CheckRenameScreenEdge();
            Check(FencesCaptionDecoration(context, selectedIndex, out var restoredItem, out var restoredState) == 0 &&
                (restoredState == 3 || restoredState == 5 || restoredState == 6),
                "ending rename restores the original selected backdrop");
            var temporaryName = "rename-probe-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path);
            var renamed = Path.Combine(Path.GetDirectoryName(path), temporaryName);
            try
            {
                RenameFixture(list, temporaryName);
                PumpUntil(() => File.Exists(renamed) && !File.Exists(path), "Enter commits the native filename edit to the owned fixture");
                RenameFixture(list, Path.GetFileName(path));
                PumpUntil(() => File.Exists(path) && !File.Exists(renamed), "native rename restores the fixture's original filename");
            }
            finally { if (File.Exists(renamed) && !File.Exists(path)) File.Move(renamed, path); }
            CheckRenameIdentityAndFolder(context, list, path);
            CheckRenameDismissal(context, list, path, integration, captions);
        }
        finally
        {
            RemoveWindowSubclass(GetParent(list), renameSizeObserver, new UIntPtr(0xf323)); watchedRenameList = IntPtr.Zero;
            frameProperty.SetValue(captions, null); frame?.Dispose();
        }
    }

    private static void CheckRenameDismissal(IntPtr context, IntPtr list, string path, object integration, object captions)
    {
        foreach (var changeName in new[] { false, true })
        {
            SelectFixture(context, list, path); SetFocus(list); QueueKey(list, 0x71);
            PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open rename editor for focus-loss checks");
            var edit = (TextBox)Control.FromHandle(RenameEditHandle());
            var form = edit.FindForm();
            var renamed = Path.Combine(Path.GetDirectoryName(path), "dismissal-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            if (changeName) edit.Text = Path.GetFileName(renamed);
            bool flashed = false;
            var seenStates = new List<int>();
            var property = captions.GetType().GetProperty("FrameRendered", Instance);
            var previous = (Action<Bitmap>)property.GetValue(captions);
            property.SetValue(captions, new Action<Bitmap>(image =>
            {
                previous?.Invoke(image);
                if (RenameEditHandle() != IntPtr.Zero) return;
                for (int i = 0; i < FencesVisibleCount(context); ++i)
                {
                    var name = new StringBuilder(32768); FencesItemPosition(context, i, name, (uint)name.Capacity, out var point);
                    if (name.ToString() != path && name.ToString() != renamed) continue;
                    FencesCaptionDecoration(context, i, out var bounds, out var visual);
                    seenStates.Add(visual);
                    if (visual != 1) flashed = true;
                    var pixel = image.GetPixel(bounds.Left + 2, bounds.Top + 2);
                    if (pixel.A != 0 && (pixel.A != 255 || pixel.R != 30 || pixel.G != 40 || pixel.B != 52)) flashed = true;
                }
            }));
            try
            {
                using (var other = new Form { Text = "Owned rename focus target", Size = new Size(160, 80) })
                {
                    other.Show(); SetFocus(other.Handle);
                    // Exercise the same deferred Deactivate completion as an
                    // outside click, without sending input to another app.
                    typeof(Form).GetMethod("OnDeactivate", Instance).Invoke(form, new object[] { EventArgs.Empty });
                    PumpUntil(() => RenameEditHandle() == IntPtr.Zero, "focus loss commits and closes rename without a second desktop click");
                    PumpFor(250); Call(captions, "RefreshLabels");
                    Console.WriteLine("DISMISSAL: renamed=" + changeName + " selected=" + SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) + " states=" + string.Join(",", seenStates));
                    Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero && !flashed,
                        "rename dismissal never publishes the old selected or hovered backdrop, including after a real rename");
                }
            }
            finally
            {
                property.SetValue(captions, previous);
                if (File.Exists(renamed)) File.Move(renamed, path);
                PumpUntil(() => Names(context).Contains(path), "Shell consumes the restored rename fixture before the next selection");
            }
        }
        SelectFixture(context, list, path); SetFocus(list); QueueKey(list, 0x71);
        PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open editor before selecting a different desktop item");
        var current = Control.FromHandle(RenameEditHandle()).FindForm();
        string next = null; int nextIndex = -1;
        for (int i = 0; i < FencesVisibleCount(context); ++i)
        {
            var name = new StringBuilder(32768); FencesItemPosition(context, i, name, (uint)name.Capacity, out var point);
            if (name.ToString() != path) { next = name.ToString(); nextIndex = i; break; }
        }
        SelectFixture(context, list, next); SendMessage(list, 0x1043, IntPtr.Zero, new IntPtr(nextIndex)); SetFocus(list);
        typeof(Form).GetMethod("OnDeactivate", Instance).Invoke(current, new object[] { EventArgs.Empty });
        PumpUntil(() => RenameEditHandle() == IntPtr.Zero, "selecting another item closes the old editor");
        Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1 &&
            (SendMessage(list, 0x102c, new IntPtr(nextIndex), new IntPtr(2)).ToInt32() & 2) != 0,
            "dismissing rename preserves the newly selected item");
    }

    private static void CheckRenameScreenEdge()
    {
        var type = product.GetType("NoFences.ItemEditorWindow");
        var work = Screen.PrimaryScreen.WorkingArea;
        var bounds = new Rectangle(work.Left + 50, work.Bottom - 38, 76, 38);
        using (var editor = (Form)Activator.CreateInstance(type, Instance, null,
            new object[] { "边界.txt", true, SystemFonts.IconTitleFont, bounds, true }, null))
        {
            editor.Show();
            var text = editor.Controls.OfType<TextBox>().Single();
            var initial = editor.Bounds;
            text.Text = new string('测', 32) + ".txt";
            Check(editor.Height > initial.Height && editor.Width == initial.Width && work.Contains(editor.Bounds),
                "wrapped rename near the screen bottom stays fully inside the working area");
            CaptureEditor(text.Handle, "desktop-rename-screen-edge.png");
            text.Text = "边界.txt";
            Console.WriteLine("RENAME EDGE: initial=" + initial + " restored=" + editor.Bounds + " work=" + work);
            Check(editor.Bounds == initial, "shortening a bottom-edge filename restores its original anchor");
        }
    }

    private static void RenameFixture(IntPtr list, string name)
    {
        SetFocus(list); QueueKey(list, 0x71);
        PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open independent editor for rename round trip");
        var edit = RenameEditHandle();
        var text = Marshal.StringToHGlobalUni(name);
        try { SendUnicodeMessage(edit, 0x0c, IntPtr.Zero, text); }
        finally { Marshal.FreeHGlobal(text); }
        var actual = new StringBuilder(1024); GetWindowText(edit, actual, actual.Capacity);
        Check(actual.ToString() == name, "native editor retains typed replacement text");
        RedrawWindow(list, IntPtr.Zero, IntPtr.Zero, 0x105); PumpFor(100);
        QueueKey(edit, 0x0d); PumpFor(150);
    }

    private static void CheckSharedBackground(IntPtr context, Bitmap frame, int index, string mode)
    {
        var text = new StringBuilder(32768);
        var labelResult = FencesCaptionAt(context, index, text, (uint)text.Capacity, out var label, out var state);
        var itemResult = FencesCaptionDecoration(context, index, out var item, out var visual);
        Check(labelResult == 0 && itemResult == 0, "read " + mode + " item geometry");
        Check(mode == "multiple-selected" ? (visual == 3 || visual == 5 || visual == 6) :
            visual == (mode == "hover" ? 2 : mode == "selected-hover" ? 6 : 5),
            "native visual state matches " + mode + " (state " + visual + ")");
        using (var native = new Bitmap(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
        using (var graphics = Graphics.FromImage(native))
        {
            var dc = graphics.GetHdc();
            try { Check(FencesCaptionSurface(context, dc) == 0, "read completed native background for " + mode); }
            finally { graphics.ReleaseHdc(dc); }
            int x = Math.Max(item.Left, label.Left) + 2, y = label.Top + 1;
            Check(frame.GetPixel(x, y).ToArgb() == native.GetPixel(x, y).ToArgb(),
                "caption copies the exact native background pixels for " + mode);
            Check(native.GetPixel(x, item.Top + 8).ToArgb() == native.GetPixel(x, y).ToArgb(),
                "icon and name background tint/opacity agree for " + mode);
            bool continuous = true;
            for (int row = item.Top + 2; row < item.Bottom - 2; ++row)
                if (frame.GetPixel(x, row).A != 255) continuous = false;
            Check(continuous, "one overlay surface continuously covers icon, connecting space and name for " + mode);
            var iconPoint = new Point((item.Left + item.Right) / 2, item.Top + 20);
            Check(frame.GetPixel(iconPoint.X, iconPoint.Y).ToArgb() == native.GetPixel(iconPoint.X, iconPoint.Y).ToArgb(),
                "the same overlay also presents the icon for " + mode);
        }
    }

    private static void CheckMouseInteractions(IntPtr context, IntPtr list, IntPtr host)
    {
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            SetFocus(list);
            var clear = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref clear);
            var label = new Bounds { Left = 2 }; // LVIR_LABEL
            SendMessage(list, 0x100e, IntPtr.Zero, ref label);
            Click(list, (label.Left + label.Right) / 2, (label.Top + label.Bottom) / 2);
            PumpUntil(() => RenameEditHandle() != IntPtr.Zero,
                "one click on an unselected filename opens the native rename editor, attempt " + (attempt + 1));
            var edit = RenameEditHandle();
            Check(IsWindowVisible(edit) && GetFocus() == edit, "mouse rename editor remains visible and focused");
            QueueKey(edit, 0x1b); PumpFor(650);
        }
        // Model opening another application and then returning to an empty spot.
        using (var other = new Form { Text = "Owned focus transfer", Size = new Size(180, 90) })
        {
            other.Show(); other.Activate(); PumpFor(100);
            SetForegroundWindow(host);
            GetClientRect(list, out var client);
            Click(list, client.Right - 15, client.Bottom - 15); PumpFor(300);
            Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero,
                "returning from another window and clicking desktop background clears the opened item selection");
            other.Activate(); PumpFor(80); SetForegroundWindow(host); SetFocus(list); PumpFor(200);
            Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero,
                "later desktop activation does not reselect the previous focused item");
        }
    }

    private static void Click(IntPtr list, int x, int y)
    {
        GetCursorPos(out var previous);
        var screen = new Point(x, y); ClientToScreen(list, ref screen);
        try
        {
            SetCursorPos(screen.X, screen.Y);
            var point = new IntPtr((y << 16) | (x & 0xffff));
            PostMessage(list, 0x201, new IntPtr(1), point);
            PostMessage(list, 0x202, IntPtr.Zero, point);
            PumpFor(900);
        }
        finally { SetCursorPos(previous.X, previous.Y); }
    }

    private static void CheckItemMenuReturn(IntPtr context, IntPtr list, IntPtr host, string path)
    {
        IDataObject previous = null; var saved = new DataObject(); bool useCopy = true;
        try { previous = Clipboard.GetDataObject(); }
        catch (ExternalException) when (offscreen)
        {
            useCopy = false;
            Console.WriteLine("LIMITATION: clipboard unavailable; the real item-menu regression uses Rename instead of Copy.");
        }
        if (previous != null) foreach (var format in previous.GetFormats(false))
        { var data = previous.GetData(format, false); if (data != null) saved.SetData(format, false, data); }
        try
        {
            SelectFixture(context, list, path); SetFocus(list);
            int phase = 0; Exception error = null;
            using (var timer = new System.Windows.Forms.Timer { Interval = 400 })
            {
                timer.Tick += (sender, e) =>
                {
                    IntPtr popup = IntPtr.Zero;
                    EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
                    { var cls = new StringBuilder(64); GetClassName(window, cls, cls.Capacity); if (cls.ToString() == "#32768") popup = window; return true; }, IntPtr.Zero);
                    if (popup == IntPtr.Zero) { timer.Stop(); return; }
                    try
                    {
                        if (phase++ == 0)
                        {
                            var menu = SendMessage(popup, 0x01e1, IntPtr.Zero, IntPtr.Zero);
                            var copy = MenuLabels(menu).FirstOrDefault(label => useCopy ?
                                (label.Contains("复制") || label.Replace("&", "").StartsWith("Copy", StringComparison.OrdinalIgnoreCase)) &&
                                !label.Contains("路径") && !label.Contains("path") && !label.Contains("快捷方式") :
                                label.Contains("重命名") || label.Replace("&", "").StartsWith("Rename", StringComparison.OrdinalIgnoreCase));
                            Check(copy != null, "the real item context menu contains the requested command");
                            var marker = copy.IndexOf('&');
                            var mnemonic = marker >= 0 && marker + 1 < copy.Length ? char.ToLowerInvariant(copy[marker + 1]) : useCopy ? 'c' : 'm';
                            PostMessage(popup, 0x102, new IntPtr(mnemonic), IntPtr.Zero);
                        }
                        else if (phase == 2) PostMessage(popup, 0x100, new IntPtr(13), IntPtr.Zero);
                        else { timer.Stop(); EndMenu(); throw new Exception("Item menu did not accept its command mnemonic."); }
                    }
                    catch (Exception failure) { error = failure; timer.Stop(); EndMenu(); }
                };
                var icon = new Bounds { Left = 1 }; SendMessage(list, 0x100e, IntPtr.Zero, ref icon);
                var point = new Point((icon.Left + icon.Right) / 2, (icon.Top + icon.Bottom) / 2); ClientToScreen(list, ref point);
                timer.Start(); PostMessage(list, 0x7b, list, new IntPtr((point.Y << 16) | (point.X & 0xffff)));
                PumpFor(1800); timer.Stop();
                if (error != null) throw error;
            }
            if (useCopy) Check(Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Contains(path), "Copy was invoked through the real item context menu");
            else PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "Rename was invoked through the real item context menu");
            using (var other = new Form { Text = "Owned context-menu focus target", Size = new Size(160, 80) })
            {
                other.Show(); other.Activate(); PumpFor(80); SetForegroundWindow(host);
                GetClientRect(list, out var client); Click(list, client.Right - 15, client.Bottom - 15); PumpFor(150);
                Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero && SendMessage(list, 0x1042, IntPtr.Zero, IntPtr.Zero).ToInt32() == -1,
                    "returning after an item-menu command and clicking blank space clears selection and its anchor");
                other.Activate(); PumpFor(50); SetFocus(list); PumpFor(100);
                Check(SendMessage(list, 0x1032, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero, "later focus activation does not resurrect the context-menu target");
            }
        }
        finally { if (useCopy) { if (previous != null) Clipboard.SetDataObject(saved, true, 20, 100); else Clipboard.Clear(); } }
    }

    private static void CheckShellUpdates(IntPtr context, IntPtr list)
    {
        var frame = FencesViewWindow(context);
        var before = SendMessage(frame, 0x8000 + 178, new IntPtr(2), IntPtr.Zero).ToInt64();
        // UPDATEIMAGE is the Shell's global Recycle Bin empty/full notification.
        // Invalidate image callbacks without changing any system image or bin data.
        SHChangeNotify(0x8000, 0x1003, IntPtr.Zero, new IntPtr(-1));
        PumpUntil(() => SendMessage(frame, 0x8000 + 178, new IntPtr(2), IntPtr.Zero).ToInt64() > before,
            "Shell image-change notification refreshes the hosted icon callbacks");
        PumpUntil(() => FencesVisibleCount(context) == 2, "icon refresh preserves visible membership");
        var type = product.GetType("NoFences.Util.DesktopIntegration");
        var integration = type.GetField("current", Static).GetValue(null);
        var notices = (NativeWindow)type.GetField("notifications", Instance).GetValue(integration);
        var wallpaperBefore = SendMessage(frame, 0x8000 + 178, new IntPtr(4), IntPtr.Zero);
        SendMessage(notices.Handle, 0x1a, IntPtr.Zero, IntPtr.Zero);
        SendMessage(notices.Handle, 0x320, IntPtr.Zero, IntPtr.Zero);
        SendMessage(notices.Handle, 0x31e, IntPtr.Zero, IntPtr.Zero);
        Call(integration, "Refresh"); PumpFor(500);
        Check(SendMessage(frame, 0x8000 + 178, new IntPtr(4), IntPtr.Zero) == wallpaperBefore,
            "ordinary refresh, general settings, theme and desktop-composition messages never invalidate wallpaper");
        PumpUntil(() => FencesVisibleCount(context) == 2, "ordinary refresh completes before edit checks");
        var state = new ItemState { State = 3, StateMask = 3 }; SendMessage(list, 0x102b, IntPtr.Zero, ref state);
        SetFocus(list); QueueKey(list, 0x71);
        PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open editor before a Shell image update");
        var edit = RenameEditHandle();
        before = SendMessage(frame, 0x8000 + 178, new IntPtr(2), IntPtr.Zero).ToInt64();
        SHChangeNotify(0x8000, 0x1003, IntPtr.Zero, new IntPtr(-1)); PumpFor(300);
        Check(IsWindowVisible(edit) && GetFocus() == edit && SendMessage(frame, 0x8000 + 178, new IntPtr(2), IntPtr.Zero).ToInt64() == before,
            "Shell icon update waits while the native rename editor is active");
        SendMessage(notices.Handle, 0x1a, new IntPtr(0x14), IntPtr.Zero); PumpFor(300);
        Check(IsWindowVisible(edit) && GetFocus() == edit, "wallpaper change redraw leaves rename text and focus intact");
        QueueKey(edit, 0x1b);
        PumpUntil(() => SendMessage(frame, 0x8000 + 178, new IntPtr(2), IntPtr.Zero).ToInt64() > before,
            "deferred Shell image update completes after rename ends");
    }

    private static void PumpFor(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static IntPtr RenameEditHandle()
    {
        var type = product.GetType("NoFences.Util.DesktopIntegration");
        var integration = type.GetField("current", Static).GetValue(null);
        if (integration == null) return IntPtr.Zero;
        var controller = type.GetField("rename", Instance).GetValue(integration);
        var editor = controller?.GetType().GetProperty("Editor", Instance).GetValue(controller) as Form;
        return editor == null || editor.IsDisposed ? IntPtr.Zero : editor.Controls.OfType<TextBox>().Single().Handle;
    }
    private static void SelectFixture(IntPtr context, IntPtr list, string path)
    {
        var reset = new ItemState { StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(-1), ref reset);
        for (int i = 0; i < FencesVisibleCount(context); ++i)
        {
            var name = new StringBuilder(32768); FencesItemPosition(context, i, name, (uint)name.Capacity, out var position);
            if (!string.Equals(name.ToString(), path, StringComparison.OrdinalIgnoreCase)) continue;
            var state = new ItemState { State = 3, StateMask = 3 }; SendMessage(list, 0x102b, new IntPtr(i), ref state); SetFocus(list); return;
        }
        throw new Exception("Fixture not found in the owned view.");
    }
    private static void CheckRenameIdentityAndFolder(IntPtr context, IntPtr list, string path)
    {
        var directory = Path.GetDirectoryName(path);
        var sibling = Path.Combine(directory, "000-before-" + Guid.NewGuid().ToString("N") + ".txt");
        var changed = Path.Combine(directory, "identity-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            SelectFixture(context, list, path); QueueKey(list, 0x71);
            PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open independent editor before view reordering");
            var edit = RenameEditHandle();
            File.WriteAllText(sibling, "keep independent sibling", Encoding.UTF8);
            PumpUntil(() => FencesVisibleCount(context) == 3, "a new sibling arrives while the editor remains open");
            Check(RenameEditHandle() == edit && IsWindowVisible(edit), "view insertion preserves the independent editor window");
            var type = product.GetType("NoFences.Util.DesktopIntegration");
            var integration = type.GetField("current", Static).GetValue(null);
            var controller = type.GetField("rename", Instance).GetValue(integration);
            var target = (IntPtr)controller.GetType().GetField("target", Instance).GetValue(controller);
            var busy = controller.GetType().GetField("completing", Instance);
            int collision;
            busy.SetValue(controller, true);
            try
            {
                collision = FencesCommitRename(target, Control.FromHandle(edit).FindForm().Handle, Path.GetFileName(sibling));
                PumpFor(100); // Preserve the production guard while Shell pumps focus messages.
            }
            finally { busy.SetValue(controller, false); }
            Control.FromHandle(edit).FindForm().Activate();
            Check(collision < 0 &&
                File.Exists(path) && File.ReadAllText(sibling) == "keep independent sibling",
                "Shell rename collision preserves both independent files");
            Check(RenameEditHandle() == edit && IsWindowVisible(edit), "failed collision preserves the independent editor window");
            var value = Marshal.StringToHGlobalUni(Path.GetFileName(changed));
            try { SendUnicodeMessage(edit, 0x0c, IntPtr.Zero, value); } finally { Marshal.FreeHGlobal(value); }
            var replacement = new StringBuilder(1024); GetWindowText(edit, replacement, replacement.Capacity);
            Check(replacement.ToString() == Path.GetFileName(changed), "replacement text survives view reordering");
            QueueKey(edit, 0x0d);
            PumpUntil(() => File.Exists(changed) && !File.Exists(path) && File.Exists(sibling),
                "rename commits the captured Shell identity after list indices change");
            PumpUntil(() => Names(context).Contains(changed), "view consumes the identity-preserving rename");
            SelectFixture(context, list, changed); RenameFixture(list, Path.GetFileName(path));
            PumpUntil(() => File.Exists(path) && !File.Exists(changed), "restore identity fixture");
        }
        finally
        {
            if (RenameEditHandle() != IntPtr.Zero) QueueKey(RenameEditHandle(), 0x1b);
            if (File.Exists(changed) && !File.Exists(path)) File.Move(changed, path);
            if (File.Exists(sibling)) File.Delete(sibling);
        }
        PumpUntil(() => FencesVisibleCount(context) == 2, "remove the temporary sibling");
        var folder = Path.Combine(directory, "folder.with.dots-" + Guid.NewGuid().ToString("N"));
        var renamedFolder = folder + "-changed";
        try
        {
            Directory.CreateDirectory(folder);
            PumpUntil(() => Names(context).Contains(folder), "enumerate the owned folder fixture");
            SelectFixture(context, list, folder); QueueKey(list, 0x71);
            PumpUntil(() => RenameEditHandle() != IntPtr.Zero, "open folder rename editor");
            var edit = RenameEditHandle();
            var selection = SendMessage(edit, 0xb0, IntPtr.Zero, IntPtr.Zero).ToInt64();
            Check((selection & 0xffff) == 0 && ((selection >> 16) & 0xffff) == Path.GetFileName(folder).Length,
                "folder editing selects the entire name, including dots");
            QueueKey(edit, 0x1b);
            RenameFixture(list, Path.GetFileName(renamedFolder));
            PumpUntil(() => Directory.Exists(renamedFolder) && !Directory.Exists(folder), "Shell commits a folder rename");
        }
        finally
        {
            if (RenameEditHandle() != IntPtr.Zero) QueueKey(RenameEditHandle(), 0x1b);
            if (Directory.Exists(folder)) Directory.Delete(folder);
            if (Directory.Exists(renamedFolder)) Directory.Delete(renamedFolder);
        }
        PumpUntil(() => FencesVisibleCount(context) == 2, "folder fixture cleanup leaves original items intact");
    }
    private static void CheckOriginalDragFeedback(FenceWindow fence, FenceInfo info, string path, IntPtr context)
    {
        var frame = FencesViewWindow(context);
        var root = GetAncestor(frame, 2);
        var previousCursor = Cursor.Current;
        var previousActive = typeof(FenceWindow).GetField("activeDragItem", Instance).GetValue(fence);
        try
        {
            SetWindowPos(root, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            SetForegroundWindow(root); PumpFor(80);
            GetWindowRect(frame, out var bounds);
            var background = new Point(bounds.Left + 400, bounds.Top + 400);
            GetWindowThreadProcessId(WindowFromPoint(background), out var hitProcess);
            if (offscreen && hitProcess != (uint)Process.GetCurrentProcess().Id)
            {
                Console.WriteLine("LIMITATION: the owned drag target is covered by another process; live hit-testing assertions are skipped in --offscreen mode.");
                return;
            }
            typeof(FenceWindow).GetField("activeDragItem", Instance).SetValue(fence, path);
            var update = typeof(FenceWindow).GetMethod("UpdateOriginalDragFeedback", Instance);
            Func<DragDropEffects, Keys, Point, bool> usesDefault = (effect, modifiers, point) =>
            {
                var feedback = new GiveFeedbackEventArgs(effect, true);
                update.Invoke(fence, new object[] { feedback, point, modifiers });
                return feedback.UseDefaultCursors;
            };
            var defaultFeedback = usesDefault(DragDropEffects.None, Keys.None, background);
            Console.WriteLine("DRAG FEEDBACK: default=" + defaultFeedback + " hit=" + WindowFromPoint(background) + " frame=" + frame +
                " cursor=" + Cursor.Current?.Handle + " arrow=" + Cursors.Default.Handle);
            Check(!defaultFeedback && Cursor.Current.Handle == Cursors.Default.Handle,
                "same-directory desktop release displays an arrow instead of the prohibited cursor");
            Check(!usesDefault(DragDropEffects.None, Keys.Shift, background), "Shift retains allowed desktop membership-release feedback");
            Check(usesDefault(DragDropEffects.None, Keys.Control, background) && usesDefault(DragDropEffects.None, Keys.Alt, background),
                "copy/link modifiers retain the actual Shell feedback without releasing membership");
            Check(usesDefault(DragDropEffects.Copy, Keys.None, background) && usesDefault(DragDropEffects.Link, Keys.None, background),
                "successful Shell copy/link feedback is never replaced");
            Check(usesDefault(DragDropEffects.None, Keys.None, new Point(bounds.Left + 24, bounds.Top + 24)),
                "dropping on another desktop icon retains prohibited feedback");
            var external = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)).FullName, "external-drag-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(external, "owned drag fixture", Encoding.UTF8);
            try
            {
                info.Files.Add(external);
                typeof(FenceWindow).GetField("activeDragItem", Instance).SetValue(fence, external);
                Check(usesDefault(DragDropEffects.None, Keys.None, background), "non-desktop files retain the actual Shell feedback");
            }
            finally
            {
                info.Files.Remove(external); File.Delete(external);
                typeof(FenceWindow).GetField("activeDragItem", Instance).SetValue(fence, path);
            }
            typeof(FenceWindow).GetField("originalDragWasCancelled", Instance).SetValue(fence, true);
            Check(usesDefault(DragDropEffects.None, Keys.None, background), "cancelled drags never advertise a desktop release");
            typeof(FenceWindow).GetField("originalDragWasCancelled", Instance).SetValue(fence, false);
            using (var blocker = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(background.X - 10, background.Y - 10, 80, 80), TopMost = true })
            {
                blocker.Show();
                Check(usesDefault(DragDropEffects.None, Keys.None, background), "unrelated target windows retain prohibited feedback");
            }
            Check(info.Files.Contains(path) && File.Exists(path), "feedback alone never moves the file or changes membership");
        }
        finally
        {
            typeof(FenceWindow).GetField("activeDragItem", Instance).SetValue(fence, previousActive);
            typeof(FenceWindow).GetField("originalDragWasCancelled", Instance).SetValue(fence, false);
            Cursor.Current = previousCursor;
            SetWindowPos(root, new IntPtr(-2), 0, 0, 0, 0, 0x13);
            SetForegroundWindow(root); SetFocus(frame); PumpFor(50);
        }
    }

    private static void CheckPartitionRenameWidth(FenceWindow fence, FenceInfo info, string path)
    {
        typeof(FenceWindow).GetField("hoveringItem", Instance).SetValue(fence, path);
        try
        {
            Call(fence, "BeginItemRename");
            var editor = (Form)typeof(FenceWindow).GetField("itemEditor", Instance).GetValue(fence);
            var width = (int)typeof(FenceWindow).GetField("itemWidth", Instance).GetValue(fence);
            var text = editor.Controls.OfType<TextBox>().Single();
            Check(editor.Width == width && text.Multiline && text.WordWrap,
                "partition rename uses the configured name-column width with wrapping");
            Check(text.Text == Path.GetFileName(path) && info.Files.Contains(path) && File.Exists(path),
                "partition rename sizing preserves the full filename and original membership");
            CaptureEditor(text.Handle, "fence-rename-column-width.png");
        }
        finally { Call(fence, "CancelItemRename"); }
    }

    private static void SaveCaptionFrame(Bitmap bitmap, string name)
    {
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualChecks"); Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name));
    }
    private static void RestoreKeyboardFixture(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        dynamic bin = shell.NameSpace(10);
        dynamic items = bin.Items();
        try
        {
            bool restored = false;
            for (int index = 0; index < items.Count; ++index)
            {
                dynamic entry = items.Item(index);
                try
                {
                    if (!string.Equals((string)entry.ExtendedProperty("System.Recycle.DeletedFrom"), Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals((string)entry.ExtendedProperty("System.ItemNameDisplay"), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) continue;
                    entry.InvokeVerb("undelete"); restored = true; break;
                }
                finally { Marshal.FinalReleaseComObject(entry); }
            }
            Check(restored, "Delete uses the Windows Recycle Bin");
            PumpUntil(() => File.Exists(path), "restore the owned recycle entry before fixture cleanup");
        }
        finally { Marshal.FinalReleaseComObject(items); Marshal.FinalReleaseComObject(bin); Marshal.FinalReleaseComObject(shell); }
    }
    private static void QueueKey(IntPtr target, int key, bool control = false)
    {
        var previous = new byte[256]; GetKeyboardState(previous);
        var state = (byte[])previous.Clone();
        foreach (var modifier in new[] { 0x10, 0x11, 0x12, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5 }) state[modifier] = 0;
        if (control) state[0x11] = 0x80;
        try
        {
            SetKeyboardState(state);
            PostMessage(target, 0x100, new IntPtr(key), new IntPtr(1));
            PostMessage(target, 0x101, new IntPtr(key), new IntPtr(unchecked((int)0xc0000001)));
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 200) { Application.DoEvents(); Thread.Sleep(10); }
        }
        finally { SetKeyboardState(previous); }
    }
    private static void CheckNativeMenu(IntPtr context, string imageName)
    {
        using (var timer = new System.Windows.Forms.Timer { Interval = 400 })
        {
            bool finished = false; Exception failure = null;
            timer.Tick += (sender, e) =>
            {
                timer.Stop();
                try
                {
                    IntPtr popup = IntPtr.Zero;
                    EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
                    {
                        var cls = new StringBuilder(128); GetClassName(window, cls, cls.Capacity);
                        if (cls.ToString() == "#32768" && IsWindowVisible(window)) popup = window;
                        return true;
                    }, IntPtr.Zero);
                    Check(popup != IntPtr.Zero, "production native view opens its composed background menu");
                    var menu = SendMessage(popup, 0x01e1, IntPtr.Zero, IntPtr.Zero);
                    var labels = MenuLabels(menu);
                    Check(labels.Any(label => label.Contains("显示设置")) && labels.Any(label => label.Contains("个性化")) &&
                        labels.Any(label => label.Contains("刷新")), "production menu retains desktop-specific and hosted-view commands");
                    var viewLabels = MenuLabels(GetSubMenu(menu, 0));
                    Check(viewLabels.Any(label => label.Contains("显示桌面图标")) && !viewLabels.Any(label => label.Contains("详细信息")),
                        "production View submenu uses actual desktop options including Show desktop icons");
                    Capture(popup, imageName);
                }
                catch (Exception exception) { failure = exception; }
                finally { EndMenu(); finished = true; }
            };
            timer.Start();
            var frame = FencesViewWindow(context);
            // Empty area within the owned fake desktop, away from its item and partitions.
            PostMessage(frame, 0x007b, frame, new IntPtr((370 << 16) | 270));
            PumpUntil(() => finished, "native popup preview closes cleanly");
            if (failure != null) throw failure;
        }
    }
    private static List<string> MenuLabels(IntPtr menu)
    {
        var result = new List<string>();
        for (uint index = 0; index < GetMenuItemCount(menu); ++index)
        {
            var text = new StringBuilder(1024); GetMenuString(menu, index, text, text.Capacity, 0x400);
            result.Add(text.ToString());
        }
        return result;
    }
    private static IntPtr Native(IDisposable integration) => (IntPtr)integration.GetType().GetField("context", Instance).GetValue(integration);
    private static void SetDesktopHost(IntPtr host) => product.GetType("NoFences.Win32.DesktopWindowHost").GetField("desktopHost", Static).SetValue(null, host);
    private static HashSet<string> Names(IntPtr context)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < FencesVisibleCount(context); ++index)
        {
            var name = new StringBuilder(32768);
            if (FencesItemPosition(context, index, name, (uint)name.Capacity, out var point) >= 0) result.Add(name.ToString());
        }
        return result;
    }
    private static void Crash(string root, IntPtr host)
    {
        var desktop = Path.Combine(root, "desktop"); SetDesktopHost(host);
        var manager = Manager(root, desktop);
        var integration = Integration(manager, Path.Combine(root, "crash-state"), host, desktop);
        Call(integration, "Start"); manager.LoadFences();
        ExitProcess(37);
    }
    private static void CrashRecovery(string root, IntPtr host, IntPtr icons)
    {
        using (var helper = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
            "--crash \"" + root + "\" " + host.ToInt64()) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
        {
            try
            {
                PumpUntil(() => helper.HasExited, "isolated desktop client exits without running managed cleanup");
                Check(helper.ExitCode == 37 && !IsWindowVisible(icons), "a terminated client leaves a recoverable original-layer visibility change");
                var recovery = product.GetType("NoFences.Util.DesktopViewRecovery");
                recovery.GetMethod("RecoverFromRoot", Static).Invoke(null, new object[] { Path.Combine(root, "crash-state") });
                Check(IsWindowVisible(icons), "recovery validates the shell window identity and restores the layer after client termination");
                Check(!File.Exists(Path.Combine(root, "crash-state", "desktop-view-session.xml")), "crash recovery clears the completed session journal");
            }
            finally { if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(); } }
        }
    }
    private static void Capture(IntPtr window, string name)
    {
        if (offscreen)
        {
            var type = product.GetType("NoFences.Util.DesktopIntegration");
            var integration = type.GetField("current", Static).GetValue(null);
            var context = Native((IDisposable)integration);
            var nativeFrame = FencesViewWindow(context);
            if (window == nativeFrame || window == GetParent(nativeFrame)) { CaptureComposition(integration, context, name); return; }
            CaptureEditor(window, name); return;
        }
        var previousDpi = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
        try
        {
            var root = GetAncestor(window, 2);
            SetWindowPos(root, new IntPtr(-1), 0, 0, 0, 0, 0x13); PumpFor(60);
            GetWindowRect(window, out var bounds);
            using (var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
                Check(true, "actual on-screen desktop/partition rendering is captured");
                var directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "VisualChecks");
                Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name));
            }
        }
        finally { SetThreadDpiAwarenessContext(previousDpi); }
    }
    private static void CaptureComposition(object integration, IntPtr context, string name)
    {
        var captions = integration.GetType().GetField("captions", Instance).GetValue(integration);
        var property = captions.GetType().GetProperty("FrameRendered", Instance);
        var previous = (Action<Bitmap>)property.GetValue(captions);
        Bitmap labels = null;
        try
        {
            property.SetValue(captions, new Action<Bitmap>(image => { previous?.Invoke(image); labels?.Dispose(); labels = (Bitmap)image.Clone(); }));
            Call(captions, "InvalidateBackground"); Call(captions, "RefreshLabels");
            GetClientRect(FencesViewWindow(context), out var bounds);
            using (var composition = new Bitmap(bounds.Right, bounds.Bottom))
            using (var graphics = Graphics.FromImage(composition))
            {
                var dc = graphics.GetHdc();
                try { Check(FencesCaptionSurface(context, dc) == 0, "read actual native item render for composition"); }
                finally { graphics.ReleaseHdc(dc); }
                graphics.DrawImageUnscaled(labels, Point.Empty);
                var edit = RenameEditHandle();
                if (edit != IntPtr.Zero)
                {
                    GetWindowRect(edit, out var editorBounds); GetWindowRect(FencesViewWindow(context), out var frameScreen);
                    using (var editorImage = RenderEditor(edit, editorBounds.Right - editorBounds.Left, editorBounds.Bottom - editorBounds.Top))
                        graphics.DrawImageUnscaled(editorImage, editorBounds.Left - frameScreen.Left, editorBounds.Top - frameScreen.Top);
                }
                SaveCaptionFrame(composition, name);
            }
        }
        finally { property.SetValue(captions, previous); labels?.Dispose(); }
    }
    private static Bitmap RenderEditor(IntPtr window, int width, int height)
    {
        var image = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
            var dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(window, dc, 1)) SendMessage(window, 0x317, dc, new IntPtr(0x14));
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        var className = new StringBuilder(128); GetClassName(window, className, className.Capacity);
        if (className.ToString() == "Edit")
        {
            int ink = 0, light = 0;
            for (int y = 3; y < image.Height - 3; ++y) for (int x = 3; x < image.Width - 3; ++x)
            {
                var color = image.GetPixel(x, y);
                if ((color.R < 80 && color.G < 80 && color.B < 80) || (color.B > color.R + 25 && color.B > 100)) ++ink;
                if (color.R > 200 && color.G > 200 && color.B > 200) ++light;
            }
            Check(ink > 20 && light > 20, "native EDIT renderer contains visible filename ink/selection and an opaque light background");
        }
        return image;
    }
    private static void CaptureEditor(IntPtr window, string name)
    {
        GetWindowRect(window, out var bounds);
        using (var image = RenderEditor(window, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)) SaveCaptionFrame(image, name);
    }
    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr w, IntPtr l) => DefWindowProc(window, message, w, l);
    private static void PumpUntil(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < 12000) { Application.DoEvents(); Thread.Sleep(10); }
        Check(condition(), message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        ++assertions; Console.WriteLine("PASS: " + message);
    }
}
