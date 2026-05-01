using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeClipboard
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static bool SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            EmptyClipboard();
            // 转为 UTF-16 LE（C# string 默认就是 UTF-16）
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0"); // null-terminated
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero)
                return false;

            IntPtr target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
                return false;

            Marshal.Copy(bytes, 0, target, bytes.Length);
            GlobalUnlock(hGlobal);

            return SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }
}