using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    internal static class ItemRecycler
    {
        private const uint FOF_SILENT = 0x0004;
        private const uint FOF_NOCONFIRMATION = 0x0010;
        private const uint FOF_NOERRORUI = 0x0400;
        private const uint FOFX_RECYCLEONDELETE = 0x00080000;
        private const uint FOFX_EARLYFAILURE = 0x00100000;
        private const uint FOFX_ADDUNDORECORD = 0x20000000;

        internal static void Recycle(string path)
        {
            IShellItem item = null;
            IFileOperation operation = null;
            try
            {
                var itemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(path), IntPtr.Zero, ref itemId, out item));
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                operation.SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI |
                    FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE | FOFX_ADDUNDORECORD);
                operation.DeleteItem(item, null);
                operation.PerformOperations();
                operation.GetAnyOperationsAborted(out var aborted);
                if (aborted)
                    throw new OperationCanceledException("项目未移入回收站。");
            }
            finally
            {
                if (operation != null)
                    Marshal.FinalReleaseComObject(operation);
                if (item != null)
                    Marshal.FinalReleaseComObject(item);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            // Only the interface identity is required; this class does not call its methods.
        }

        // Preserve the native vtable order, including methods unused by recycling.
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise([MarshalAs(UnmanagedType.Interface)] object sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
            void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
            void SetOwnerWindow(IntPtr owner);
            void ApplyPropertiesToItem([MarshalAs(UnmanagedType.Interface)] object item);
            void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object items);
            void RenameItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.LPWStr)] string name,
                [MarshalAs(UnmanagedType.Interface)] object sink);
            void RenameItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            void MoveItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.Interface)] object destination,
                [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] object sink);
            void MoveItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.Interface)] object destination);
            void CopyItem([MarshalAs(UnmanagedType.Interface)] object item, [MarshalAs(UnmanagedType.Interface)] object destination,
                [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] object sink);
            void CopyItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.Interface)] object destination);
            void DeleteItem([MarshalAs(UnmanagedType.Interface)] IShellItem item, [MarshalAs(UnmanagedType.Interface)] object sink);
            void DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
            void NewItem([MarshalAs(UnmanagedType.Interface)] object destination, uint attributes,
                [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template,
                [MarshalAs(UnmanagedType.Interface)] object sink);
            void PerformOperations();
            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }
    }
}
