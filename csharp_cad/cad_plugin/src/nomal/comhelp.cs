using System;
using System.Runtime.InteropServices;
using System.Security;
namespace com_tools
{
public static class ComHelper
{
    private const string OLE32 = "ole32.dll";
    private const string OLEAUT32 = "oleaut32.dll";

    /// <summary>
    /// 获取正在运行的 COM 对象实例 (.NET Core / .NET 5+ 替代 Marshal.GetActiveObject)
    /// </summary>
    /// <param name="progID">程序的 ProgID，例如 "Excel.Application"</param>
    /// <returns>COM 对象实例</returns>
    /// <exception cref="COMException">当未找到活动对象或发生其他 COM 错误时抛出</exception>
    public static object GetActiveObject(string progID)
    {
        if (string.IsNullOrEmpty(progID))
            throw new ArgumentNullException(nameof(progID));

        Guid clsid;
        
        // 尝试先用 CLSIDFromProgIDEx (更现代，支持某些特殊情况)
        try
        {
            CLSIDFromProgIDEx(progID, out clsid);
        }
        catch
        {
            // 如果失败，回退到 CLSIDFromProgID
            CLSIDFromProgID(progID, out clsid);
        }

        return GetActiveObject(clsid);
    }

    /// <summary>
    /// 通过 CLSID 获取正在运行的 COM 对象实例
    /// </summary>
    public static object GetActiveObject(Guid clsid)
    {
        object comObject;
        GetActiveObject(ref clsid, IntPtr.Zero, out comObject);
        return comObject;
    }

    [DllImport(OLE32, PreserveSig = false, ExactSpelling = true)]
    [SuppressUnmanagedCodeSecurity]
    private static extern void CLSIDFromProgIDEx([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport(OLE32, PreserveSig = false, ExactSpelling = true)]
    [SuppressUnmanagedCodeSecurity]
    private static extern void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport(OLEAUT32, PreserveSig = false, ExactSpelling = true)]
    [SuppressUnmanagedCodeSecurity]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object ppunk);
}}