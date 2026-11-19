using System.Runtime.InteropServices;
using System.Text;

namespace EasyDay.Core.Security;

/// <summary>
/// 敏感字段（API Key）的本地保护。用 Windows DPAPI 按当前用户加密，密文只能在同一台机器的同一账户解开。
/// 非 Windows 或加密失败时退回明文，保证功能可用；调用方无需判断平台。
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:v1:";
    private const int UiForbidden = 0x1;

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        if (plain.StartsWith(Prefix, StringComparison.Ordinal) || !OperatingSystem.IsWindows())
        {
            return plain;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = Transform(data, protect: true);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plain;
        }
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored;
        }

        try
        {
            var cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(Transform(cipher, protect: false));
        }
        catch
        {
            // 换了机器/账户后旧密文解不开：当作未配置，让用户重新填一次
            return string.Empty;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var inputBlob = new DataBlob { Size = input.Length, Data = inHandle.AddrOfPinnedObject() };
        DataBlob outputBlob;

        try
        {
            var ok = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob);

            if (!ok)
            {
                throw new InvalidOperationException(
                    $"DPAPI 调用失败：{Marshal.GetLastWin32Error()}");
            }

            try
            {
                var result = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
                return result;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            inHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
