using System.Runtime.InteropServices;

namespace PCInspector.Services;

public static class FileLocationService
{
    public static bool CanLocate(string? path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && File.Exists(path);

    public static Task ShowAsync(string path) => Task.Run(() =>
    {
        // Recheck at click time: the file may have disappeared since the menu opened.
        if (!CanLocate(path)) throw new FileNotFoundException("The file is missing or inaccessible.", path);
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
        IntPtr item = IntPtr.Zero;
        try
        {
            // Pass the file as a Shell item, not as a command to execute.
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out item, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
            CoUninitialize();
        }
    });

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext,
        out IntPtr item, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);
}
