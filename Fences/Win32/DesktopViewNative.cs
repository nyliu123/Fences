using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace NoFences.Win32
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DesktopCaptionBounds
    {
        internal int Left, Top, Right, Bottom;
        internal Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DesktopPreferences
    {
        internal uint Size;
        internal uint Flags;
        internal int IconSize;
        internal Guid SortFormat;
        internal uint SortProperty;
        internal int SortDirection;
        internal static DesktopPreferences Empty => new DesktopPreferences { Size = (uint)Marshal.SizeOf<DesktopPreferences>() };
    }

    internal static class DesktopViewNative
    {
        private const string Library = "Fences.Desktop.dll";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesCreateView(IntPtr parent, string folder, bool wallpaper, IntPtr notify, uint message, out IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesCreateFilteredView(IntPtr parent, string folder, bool wallpaper, IntPtr notify, uint message,
            string excluded, uint characters, out IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FencesDestroyView(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FencesViewWindow(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesViewStatus(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesExcludePaths(IntPtr context, string paths, uint characters);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesRefreshView(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesRefreshBackground(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesTranslateKey(IntPtr context, IntPtr target, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesOriginalMetrics(IntPtr context, out int size, out Point spacing);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesSetCaptionWindow(IntPtr context, IntPtr window);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FencesCaptionMetrics(IntPtr context, out DesktopCaptionBounds bounds, out int height);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesCaptionAt(IntPtr context, int index, StringBuilder name, uint capacity, out DesktopCaptionBounds bounds, out uint state);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionBackground(IntPtr context, IntPtr dc);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionSurface(IntPtr context, IntPtr dc);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern UIntPtr FencesCaptionRevision(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionDirty(IntPtr context, out DesktopCaptionBounds bounds);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FencesAcknowledgeCaption(IntPtr context, UIntPtr revision);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionSurfaceRegion(IntPtr context, IntPtr dc, ref DesktopCaptionBounds bounds);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesTakeRename(IntPtr context, StringBuilder name, uint capacity, out DesktopCaptionBounds bounds, out bool file, out IntPtr target);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesCommitRename(IntPtr target, IntPtr owner, string name);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FencesActivateRename(IntPtr context, IntPtr target);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FencesEndRename(IntPtr context, bool restoreFocus);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FencesReleaseRename(IntPtr target);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionLineHeight(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FencesCaptionEditor(IntPtr context, out DesktopCaptionBounds bounds);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesCaptionDecoration(IntPtr context, int index, out DesktopCaptionBounds bounds, out int state);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesDrawCaptionDecoration(IntPtr context, int state, IntPtr dc, ref DesktopCaptionBounds bounds);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesResizeView(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesGetPreferences(IntPtr context, ref DesktopPreferences preferences);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesSetPreferences(IntPtr context, ref DesktopPreferences preferences);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool FencesIsBackgroundPoint(IntPtr context, Point point);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FencesVisibleCount(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesItemPosition(IntPtr context, int index, StringBuilder name, uint capacity, out Point point);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int FencesSetItemPosition(IntPtr context, string name, Point point);
    }
}
