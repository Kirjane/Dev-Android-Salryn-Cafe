using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Salryn.Mobile.Foundation.P0;

namespace Salryn.Api.Local.Services;

/// <summary>Windows current-user DPAPI ciphertext. No plaintext signing keys or tokens on disk.</summary>
public sealed class CafeCentralProtectedSecrets(string root) : IDeviceSecretStore
{
    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path=PathFor(key);
        return Task.FromResult(File.Exists(path)?Transform(File.ReadAllBytes(path),false):null);
    }
    public Task WriteAsync(string key, byte[] value, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path=PathFor(key); Directory.CreateDirectory(root);
        byte[] cipher=Transform(value,true);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temporary,cipher);File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
        return Task.CompletedTask;
    }
    private string PathFor(string key) => Path.Combine(root,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))+".dpapi");
    private static byte[] Transform(byte[] input,bool protect)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Cohost Central secret protection requires Windows DPAPI.");
        IntPtr source=Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input,0,source,input.Length); Blob value=new(){Size=input.Length,Data=source}; Blob output;
            bool success=protect?CryptProtectData(ref value,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                :CryptUnprotectData(ref value,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!success)throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            try{byte[] result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;}
            finally{LocalFree(output.Data);}
        }
        finally{for(int i=0;i<input.Length;i++)Marshal.WriteByte(source,i,0);Marshal.FreeHGlobal(source);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob {public int Size;public IntPtr Data;}
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
