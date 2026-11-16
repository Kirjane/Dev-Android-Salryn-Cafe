# Salryn POS Beta RC1 — source-bound staging and installer build script.
#
# Enforces source custody (Git HEAD SHA, clean worktree, optional -ExpectedSourceSha)
# before any network download, staging cleanup, web build, .NET publish, or packaging.
# Emits a deterministic SHA-bound manifest inside staging (packaged into {app}) and
# binds the compiled setup EXE to the verified source commit and stage inventory.
#
# ISCC discovery order (when -BuildInstaller is requested):
#   1. -InnoSetupCompilerPath parameter
#   2. INNO_SETUP_ISCC environment variable
#   3. ISCC.exe on the system PATH
#   4. %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe  (per-user install)
#   5. C:\Program Files (x86)\Inno Setup 6\ISCC.exe
#   6. C:\Program Files\Inno Setup 6\ISCC.exe
#
# Usage:
#   # Stage only into private per-user default output (<LocalAppData>\SalrynInstallerBuilds\run-...):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1
#
#   # Stage + compile setup EXE into private per-user default output:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -ExpectedSourceSha <40-hex-sha>
#
#   # Stage + compile into explicit private non-existent directory:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -ExpectedSourceSha <40-hex-sha> -OutputDir <new-private-dir>

[CmdletBinding()]
param(
    [string]$Version = "0.1.0-rc1",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [string]$InnoSetupCompilerPath = "",
    [string]$ExpectedSourceSha = "",
    [string]$ExpectedStageManifestSha256 = "",
    [string[]]$AdditionalProtectedRoots = @(),
    [switch]$SkipWebBuild,
    [switch]$BuildInstaller,
    [switch]$CompileExistingStageOnly,
    [switch]$VerifyStageOnly,
    [switch]$ForceNpmCi,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) { Write-Host "[installer-rc1] $Message" }

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Write-ProvenanceFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$Detail
    )
    $safeSha = if ([string]::IsNullOrWhiteSpace($HeadSha)) { "UNKNOWN" } else { $HeadSha }
    $msg = "[PROVENANCE_ERROR] code=$Code head=$safeSha detail=$Detail"
    Write-Host $msg
    throw $msg
}

if ($Help) {
    Write-Host @"
Build-SalrynBetaRc1Installer.ps1

  Stages Salryn.Desktop.WebView, Salryn.Api.Local, branding assets, and React dist
  from a verified clean Git commit, generates salryn-installer-provenance-manifest.json,
  and compiles installer\SalrynSetup.iss via Inno Setup 6 when -BuildInstaller is set.

Parameters:
  -Version <semver>                    Product version (default 0.1.0-rc1)
  -Configuration <name>                dotnet publish configuration (default Release)
  -OutputDir <path>                    Artifact root (mutable runs require a new non-existent private dir; default auto-generated unique dir under <KnownFolder:LocalAppData>\SalrynInstallerBuilds\run-*)
  -InnoSetupCompilerPath <path>        Explicit path to ISCC.exe (overrides auto-discovery)
  -ExpectedSourceSha <40-hex>          Require HEAD to match this 40-char Git SHA (required for -BuildInstaller)
  -ExpectedStageManifestSha256 <hex>   Expected 64-hex stage manifest SHA-256 (enforced in -VerifyStageOnly when provided)
  -AdditionalProtectedRoots <paths>    Extra directory roots treated as protected output boundaries
  -SkipWebBuild                        NON-RELEASE/NOT-ATTESTED dev staging only; forbidden with -BuildInstaller
  -BuildInstaller                      Enforces fresh Option A build (-ExpectedSourceSha required, clean npm ci; no reuse) + ISCC
  -CompileExistingStageOnly            Disabled in Option A (always fails closed with COMPILE_EXISTING_STAGE_FORBIDDEN)
  -VerifyStageOnly                     Strictly read-only stage manifest verification (-OutputDir required; no writes/deletes; forbidden with -BuildInstaller)
  -ForceNpmCi                          Retained for CLI compatibility; clean lockfile npm ci runs unconditionally on all fresh builds
  -Help                                Show this help
"@
    exit 0
}

function Find-RepoRoot {
    $current = (Resolve-Path $PSScriptRoot).Path
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $current "Salryn.sln")) { return $current }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate Salryn.sln from $PSScriptRoot"
        }
        $current = $parent
    }
}

if (-not ("SalrynNativePathResolver" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class SalrynNativePathResolver
{
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint READ_CONTROL = 0x00020000;
    private const uint FILE_LIST_DIRECTORY = 0x0001;
    private const uint FILE_ADD_SUBDIRECTORY = 0x0004;
    private const uint FILE_TRAVERSE = 0x0020;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x0001;
    private const uint FILE_SHARE_WRITE = 0x0002;
    private const uint FILE_SHARE_DELETE = 0x0004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_OPEN = 1;
    private const uint FILE_CREATE = 2;
    private const uint FILE_DIRECTORY_FILE = 0x00000001;
    private const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
    private const uint FILE_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x0010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x0400;
    private const uint OBJ_CASE_INSENSITIVE = 0x00000040;
    private const uint VOLUME_NAME_DOS = 0x0;
    private const int SE_FILE_OBJECT = 1;
    private const uint OWNER_SECURITY_INFORMATION = 0x00000001;
    private const uint DACL_SECURITY_INFORMATION = 0x00000004;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out SafeFileHandle FileHandle,
        uint DesiredAccess,
        ref OBJECT_ATTRIBUTES ObjectAttributes,
        out IO_STATUS_BLOCK IoStatusBlock,
        IntPtr AllocationSize,
        uint FileAttributes,
        uint ShareAccess,
        uint CreateDisposition,
        uint CreateOptions,
        IntPtr EaBuffer,
        uint EaLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool IsValidSecurityDescriptor(IntPtr pSecurityDescriptor);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(
        IntPtr handle,
        int ObjectType,
        uint SecurityInfo,
        out IntPtr ppsidOwner,
        out IntPtr ppsidGroup,
        out IntPtr ppDacl,
        out IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    private const uint KF_FLAG_DONT_VERIFY = 0x00004000;
    private const uint KF_FLAG_NO_ALIAS    = 0x00000800;

    public static string GetTrustedLocalAppDataPath() {
        Guid folderIdLocalAppData = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        IntPtr pPath = IntPtr.Zero;
        try {
            int hr = SHGetKnownFolderPath(folderIdLocalAppData, KF_FLAG_DONT_VERIFY | KF_FLAG_NO_ALIAS, IntPtr.Zero, out pPath);
            if (hr != 0 || pPath == IntPtr.Zero) {
                throw new InvalidOperationException("SHGetKnownFolderPath(FOLDERID_LocalAppData) failed with HRESULT 0x" + hr.ToString("X8"));
            }
            string resolved = Marshal.PtrToStringUni(pPath);
            if (string.IsNullOrWhiteSpace(resolved)) {
                throw new InvalidOperationException("SHGetKnownFolderPath(FOLDERID_LocalAppData) returned an empty path.");
            }
            return resolved;
        } finally {
            if (pPath != IntPtr.Zero) {
                Marshal.FreeCoTaskMem(pPath);
            }
        }
    }

    private static string GetFinalPathFromHandle(SafeFileHandle handle, string path)
    {
        StringBuilder sb = new StringBuilder(512);
        uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
        if (len == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW failed: " + path);

        if (len >= sb.Capacity)
        {
            sb = new StringBuilder((int)len + 2);
            len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
            if (len == 0 || len >= sb.Capacity)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW buffer resize failed: " + path);
        }

        string raw = sb.ToString();
        if (raw.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            return "\\\\" + raw.Substring(8);
        if (raw.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
            return raw.Substring(4);
        return raw;
    }

    public static string GetFinalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.");

        using (SafeFileHandle handle = CreateFileW(
            path,
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open path for canonical resolution: " + path);
            return GetFinalPathFromHandle(handle, path);
        }
    }

    private static void ValidateSingleLeafSegment(string leafName)
    {
        if (string.IsNullOrWhiteSpace(leafName) ||
            leafName == "." ||
            leafName == ".." ||
            leafName.IndexOf('\\') >= 0 ||
            leafName.IndexOf('/') >= 0 ||
            leafName.IndexOf(':') >= 0 ||
            leafName.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Invalid single directory leaf segment: " + leafName);
        }
    }

    public static bool IsValidSecurityDescriptorBytes(byte[] sdBytes)
    {
        if (sdBytes == null || sdBytes.Length < 20)
            return false;
        IntPtr pSd = Marshal.AllocHGlobal(sdBytes.Length);
        try
        {
            Marshal.Copy(sdBytes, 0, pSd, sdBytes.Length);
            return IsValidSecurityDescriptor(pSd);
        }
        finally
        {
            Marshal.FreeHGlobal(pSd);
        }
    }

    public static void GetHandleIdentityAndPath(SafeFileHandle handle, string contextLabel, out string fileId, out bool isReparse, out bool isDir, out string finalPath)
    {
        if (handle == null || handle.IsInvalid)
            throw new InvalidOperationException("Directory handle is null or invalid: " + contextLabel);
        BY_HANDLE_FILE_INFORMATION info;
        if (!GetFileInformationByHandle(handle, out info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed: " + contextLabel);
        isReparse = (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        isDir = (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        fileId = info.dwVolumeSerialNumber.ToString("X8") + ":" + info.nFileIndexHigh.ToString("X8") + ":" + info.nFileIndexLow.ToString("X8");
        finalPath = isReparse ? string.Empty : GetFinalPathFromHandle(handle, contextLabel);
    }

    public static byte[] GetSecurityDescriptorFromHandle(SafeFileHandle handle, string contextLabel)
    {
        if (handle == null || handle.IsInvalid)
            throw new InvalidOperationException("Directory handle is null or invalid for ACL inspection: " + contextLabel);
        IntPtr pOwner, pGroup, pDacl, pSacl, pSd = IntPtr.Zero;
        uint rc = GetSecurityInfo(
            handle.DangerousGetHandle(),
            SE_FILE_OBJECT,
            OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
            out pOwner,
            out pGroup,
            out pDacl,
            out pSacl,
            out pSd);
        if (rc != 0)
            throw new Win32Exception((int)rc, "GetSecurityInfo failed: " + contextLabel);
        try
        {
            uint len = GetSecurityDescriptorLength(pSd);
            byte[] buf = new byte[len];
            Marshal.Copy(pSd, buf, 0, (int)len);
            return buf;
        }
        finally
        {
            if (pSd != IntPtr.Zero)
                LocalFree(pSd);
        }
    }

    public static SafeFileHandle OpenRootDirectoryNoDeleteShare(string rootPath, out string fileId, out bool isReparse, out bool isDir, out string finalPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be empty.");

        SafeFileHandle handle = CreateFileW(
            rootPath,
            FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to lock root directory: " + rootPath);

        try
        {
            GetHandleIdentityAndPath(handle, rootPath, out fileId, out isReparse, out isDir, out finalPath);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static SafeFileHandle OpenRelativeDirectoryNoDeleteShare(
        SafeFileHandle parentHandle,
        string leafName,
        out int ntStatus,
        out string fileId,
        out bool isReparse,
        out bool isDir,
        out string finalPath)
    {
        if (parentHandle == null || parentHandle.IsInvalid)
            throw new ArgumentException("Parent directory handle is invalid.");
        ValidateSingleLeafSegment(leafName);

        IntPtr pNameBuf = Marshal.StringToHGlobalUni(leafName);
        IntPtr pUnicode = IntPtr.Zero;
        try
        {
            UNICODE_STRING u = new UNICODE_STRING();
            u.Length = checked((ushort)(leafName.Length * 2));
            u.MaximumLength = checked((ushort)(u.Length + 2));
            u.Buffer = pNameBuf;
            pUnicode = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));
            Marshal.StructureToPtr(u, pUnicode, false);

            OBJECT_ATTRIBUTES oa = new OBJECT_ATTRIBUTES();
            oa.Length = Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES));
            oa.RootDirectory = parentHandle.DangerousGetHandle();
            oa.ObjectName = pUnicode;
            oa.Attributes = OBJ_CASE_INSENSITIVE;
            oa.SecurityDescriptor = IntPtr.Zero;
            oa.SecurityQualityOfService = IntPtr.Zero;

            SafeFileHandle h;
            IO_STATUS_BLOCK iosb;
            ntStatus = NtCreateFile(
                out h,
                FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
                ref oa,
                out iosb,
                IntPtr.Zero,
                FILE_ATTRIBUTE_DIRECTORY,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                FILE_OPEN,
                FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_REPARSE_POINT,
                IntPtr.Zero,
                0);
            if (ntStatus != 0 || h == null || h.IsInvalid)
            {
                if (h != null) h.Dispose();
                fileId = string.Empty;
                isReparse = false;
                isDir = false;
                finalPath = string.Empty;
                return null;
            }
            try
            {
                GetHandleIdentityAndPath(h, leafName, out fileId, out isReparse, out isDir, out finalPath);
                return h;
            }
            catch
            {
                h.Dispose();
                throw;
            }
        }
        finally
        {
            if (pUnicode != IntPtr.Zero) Marshal.FreeHGlobal(pUnicode);
            if (pNameBuf != IntPtr.Zero) Marshal.FreeHGlobal(pNameBuf);
        }
    }

    public static SafeFileHandle CreateRelativeDirectoryPrivate(
        SafeFileHandle parentHandle,
        string leafName,
        byte[] sdBytes,
        out int ntStatus,
        out string fileId,
        out bool isReparse,
        out bool isDir,
        out string finalPath)
    {
        if (parentHandle == null || parentHandle.IsInvalid)
            throw new ArgumentException("Parent directory handle is invalid.");
        ValidateSingleLeafSegment(leafName);
        if (sdBytes == null || sdBytes.Length < 20)
            throw new ArgumentException("Private security descriptor cannot be null or empty.");

        IntPtr pNameBuf = Marshal.StringToHGlobalUni(leafName);
        IntPtr pUnicode = IntPtr.Zero;
        IntPtr pSd = Marshal.AllocHGlobal(sdBytes.Length);
        try
        {
            Marshal.Copy(sdBytes, 0, pSd, sdBytes.Length);
            if (!IsValidSecurityDescriptor(pSd))
                throw new InvalidOperationException("Security descriptor failed IsValidSecurityDescriptor before NtCreateFile.");

            UNICODE_STRING u = new UNICODE_STRING();
            u.Length = checked((ushort)(leafName.Length * 2));
            u.MaximumLength = checked((ushort)(u.Length + 2));
            u.Buffer = pNameBuf;
            pUnicode = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(UNICODE_STRING)));
            Marshal.StructureToPtr(u, pUnicode, false);

            OBJECT_ATTRIBUTES oa = new OBJECT_ATTRIBUTES();
            oa.Length = Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES));
            oa.RootDirectory = parentHandle.DangerousGetHandle();
            oa.ObjectName = pUnicode;
            oa.Attributes = OBJ_CASE_INSENSITIVE;
            oa.SecurityDescriptor = pSd;
            oa.SecurityQualityOfService = IntPtr.Zero;

            SafeFileHandle h;
            IO_STATUS_BLOCK iosb;
            ntStatus = NtCreateFile(
                out h,
                FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_ADD_SUBDIRECTORY | FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
                ref oa,
                out iosb,
                IntPtr.Zero,
                FILE_ATTRIBUTE_DIRECTORY,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                FILE_CREATE,
                FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_REPARSE_POINT,
                IntPtr.Zero,
                0);
            if (ntStatus != 0 || h == null || h.IsInvalid)
            {
                if (h != null) h.Dispose();
                fileId = string.Empty;
                isReparse = false;
                isDir = false;
                finalPath = string.Empty;
                return null;
            }
            try
            {
                GetHandleIdentityAndPath(h, leafName, out fileId, out isReparse, out isDir, out finalPath);
                return h;
            }
            catch
            {
                h.Dispose();
                throw;
            }
        }
        finally
        {
            if (pSd != IntPtr.Zero) Marshal.FreeHGlobal(pSd);
            if (pUnicode != IntPtr.Zero) Marshal.FreeHGlobal(pUnicode);
            if (pNameBuf != IntPtr.Zero) Marshal.FreeHGlobal(pNameBuf);
        }
    }

    public static SafeFileHandle OpenDirectoryNoDeleteShare(string path, out string fileId, out bool isReparse, out string finalPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.");

        SafeFileHandle handle = CreateFileW(
            path,
            FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to lock directory: " + path);

        try
        {
            BY_HANDLE_FILE_INFORMATION info;
            if (!GetFileInformationByHandle(handle, out info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed: " + path);

            isReparse = (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            if (!isReparse && (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new InvalidOperationException("Target is not a directory: " + path);

            fileId = info.dwVolumeSerialNumber.ToString("X8") + ":" + info.nFileIndexHigh.ToString("X8") + ":" + info.nFileIndexLow.ToString("X8");
            finalPath = isReparse ? string.Empty : GetFinalPathFromHandle(handle, path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static bool TryGetFileIdentityNoFollow(string path, out string fileId, out bool isReparse, out bool isDirectory)
    {
        fileId = string.Empty;
        isReparse = false;
        isDirectory = false;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        using (SafeFileHandle handle = CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND)
                    return false;
                throw new Win32Exception(err, "Unable to inspect file identity: " + path);
            }

            BY_HANDLE_FILE_INFORMATION info;
            if (!GetFileInformationByHandle(handle, out info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandle failed: " + path);

            isReparse = (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            isDirectory = (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
            fileId = info.dwVolumeSerialNumber.ToString("X8") + ":" + info.nFileIndexHigh.ToString("X8") + ":" + info.nFileIndexLow.ToString("X8");
            return true;
        }
    }
}
"@ -ErrorAction Stop
}

function Normalize-PathWithSeparator {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full -match '^[A-Za-z]:$') {
        return "$full\"
    }
    return "$full\"
}

function Get-ProtectedRootsList {
    param(
        [string]$HeadSha = "UNKNOWN",
        [string[]]$ExtraProtectedRoots = @()
    )
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        $candidates.Add((Join-Path $env:ProgramData "Salryn"))
    }
    try {
        $trustedLocalAppData = [SalrynNativePathResolver]::GetTrustedLocalAppDataPath()
        $candidates.Add((Join-Path $trustedLocalAppData "Salryn"))
    } catch {
        Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed to resolve OS-trusted LocalAppData path for protected root enforcement: $($_.Exception.Message)"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles "Kirjane Information Technology Services"))
    }
    $pf86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($pf86)) {
        $candidates.Add((Join-Path $pf86 "Kirjane Information Technology Services"))
    }
    foreach ($extra in @($ExtraProtectedRoots)) {
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            $candidates.Add($extra)
        }
    }
    $fromEnv = [System.Environment]::GetEnvironmentVariable("SALRYN_EXTRA_PROTECTED_ROOTS")
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        foreach ($seg in ($fromEnv -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($seg)) {
                $candidates.Add($seg.Trim())
            }
        }
    }

    $expanded = New-Object System.Collections.Generic.List[string]
    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c) -or $c.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Invalid protected root path '$c'."
        }
        $lexFull = [System.IO.Path]::GetFullPath($c)
        $lexTrim = $lexFull.TrimEnd('\', '/')
        if ($lexFull -notmatch '^[A-Za-z]:\\') {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Protected root must be a local drive path: $c"
        }

        $root = [System.IO.Path]::GetPathRoot($lexFull)
        $trimmedForParts = $lexFull.TrimEnd('\')
        if ($trimmedForParts -match '^[A-Za-z]:$') { $trimmedForParts = "$trimmedForParts\" }
        $remainder = $trimmedForParts.Substring($root.Length)
        $parts = if ([string]::IsNullOrEmpty($remainder)) { @() } else { $remainder.Split('\') }

        $current = $root.TrimEnd('\') + "\"
        try {
            $rootItem = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        } catch {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed inspecting drive root for protected path '$c': $_"
        }
        if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Drive root for protected path is a reparse point: $current"
        }
        try {
            $null = [SalrynNativePathResolver]::GetFinalPath($current)
        } catch {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed canonical resolution for drive root '$current': $_"
        }

        $deepestExisting = $current
        $nonExistentParts = New-Object System.Collections.Generic.List[string]
        $sawNonExistent = $false

        foreach ($part in $parts) {
            if ([string]::IsNullOrWhiteSpace($part)) { continue }
            $current = Join-Path $current $part
            try {
                $segItem = Get-Item -LiteralPath $current -Force -ErrorAction Stop
            } catch [System.Management.Automation.ItemNotFoundException] {
                $sawNonExistent = $true
                $nonExistentParts.Add($part)
                continue
            } catch {
                Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed inspecting protected root segment '$current': $_"
            }

            if ($sawNonExistent) {
                Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Inconsistent filesystem state along protected root '$current'."
            }
            if (($segItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Protected root path contains reparse point/junction at '$current'."
            }
            try {
                $null = [SalrynNativePathResolver]::GetFinalPath($current)
            } catch {
                Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed canonical resolution for protected root segment '$current': $_"
            }
            $deepestExisting = $current
        }

        try {
            $resolvedDeepest = [SalrynNativePathResolver]::GetFinalPath($deepestExisting)
        } catch {
            Write-ProvenanceFailure -Code "PROTECTED_ROOT_RESOLUTION_FAILED" -HeadSha $HeadSha -Detail "Failed canonical resolution for protected root '$deepestExisting': $_"
        }
        $resolvedFull = $resolvedDeepest
        foreach ($missingPart in $nonExistentParts) {
            $resolvedFull = Join-Path $resolvedFull $missingPart
        }

        $expanded.Add($lexTrim)
        $resolvedTrim = ([System.IO.Path]::GetFullPath($resolvedFull)).TrimEnd('\', '/')
        $expanded.Add($resolvedTrim)
    }
    return ,@($expanded | Select-Object -Unique)
}

function Assert-SupportedLocalDrivePath {
    param(
        [Parameter(Mandatory = $true)][string]$RawPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if ([string]::IsNullOrWhiteSpace($RawPath)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} cannot be empty."
    }
    if ($RawPath.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "UNC/device path not supported for ${ContextLabel}: $RawPath"
    }
    $full = [System.IO.Path]::GetFullPath($RawPath)
    if ($full -notmatch '^[A-Za-z]:\\') {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} must resolve to a local drive-letter path: $full"
    }
    $driveRoot = $full.Substring(0, 3)
    try {
        $driveInfo = New-Object System.IO.DriveInfo($driveRoot)
        if ($driveInfo.DriveType -eq [System.IO.DriveType]::Network -or
            $driveInfo.DriveType -eq [System.IO.DriveType]::Unknown) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Unsupported drive type '$($driveInfo.DriveType)' for ${ContextLabel}: $full"
        }
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Failed to validate drive for ${ContextLabel}: $_"
    }
    return $full
}

function Resolve-CanonicalPathThroughAncestors {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    $full = Assert-SupportedLocalDrivePath -RawPath $TargetPath -HeadSha $HeadSha -ContextLabel $ContextLabel
    $trimmed = $full.TrimEnd('\')
    if ($trimmed -match '^[A-Za-z]:$') {
        $trimmed = "$trimmed\"
    }

    # Walk top-down through every component from drive root to target leaf (including non-existent children)
    $root = [System.IO.Path]::GetPathRoot($full)
    $remainder = $trimmed.Substring($root.Length)
    $segments = if ([string]::IsNullOrEmpty($remainder)) { @() } else { $remainder.Split('\') }

    $current = $root.TrimEnd('\') + "\"
    $deepestExisting = $current
    $nonExistentParts = New-Object System.Collections.Generic.List[string]
    $sawNonExistent = $false

    $rootItem = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($null -eq $rootItem) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Drive root unavailable for ${ContextLabel}: $current"
    }
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel} ancestry: $current"
    }

    foreach ($seg in $segments) {
        $current = Join-Path $current $seg
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel} ancestry: $($item.FullName)"
            }
            if ($sawNonExistent) {
                Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Inconsistent filesystem state along ${ContextLabel} ancestry: $current"
            }
            $deepestExisting = $current
        } else {
            $sawNonExistent = $true
            $nonExistentParts.Add($seg)
        }
    }

    try {
        $kernelResolved = [SalrynNativePathResolver]::GetFinalPath($deepestExisting)
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Failed canonical path resolution for ${ContextLabel} ($deepestExisting): $_"
    }

    if ($kernelResolved.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $kernelResolved -notmatch '^[A-Za-z]:\\') {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Canonical resolution left supported local drive namespace for ${ContextLabel}: $kernelResolved"
    }

    # Verify kernel-resolved deepest existing ancestor itself has no reparse attribute
    $resolvedWalk = $kernelResolved
    while (-not [string]::IsNullOrWhiteSpace($resolvedWalk)) {
        $rItem = Get-Item -LiteralPath $resolvedWalk -Force -ErrorAction SilentlyContinue
        if ($null -ne $rItem -and (($rItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Canonical target ancestry contains reparse point for ${ContextLabel}: $resolvedWalk"
        }
        $parentWalk = Split-Path -Parent $resolvedWalk
        if ([string]::IsNullOrWhiteSpace($parentWalk) -or $parentWalk -eq $resolvedWalk) { break }
        $resolvedWalk = $parentWalk
    }

    $canonicalFull = $kernelResolved
    foreach ($part in $nonExistentParts) {
        $canonicalFull = Join-Path $canonicalFull $part
    }

    return [ordered]@{
        LexicalFull     = $full
        CanonicalFull   = [System.IO.Path]::GetFullPath($canonicalFull)
        DeepestExisting = $deepestExisting
    }
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/symlink forbidden in ${ContextLabel}: $($item.FullName)"
    }
}

function Assert-TreeFreeOfReparsePointsAndAliases {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$AllowedParentRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [string]$ReparseErrorCode = "REPARSE_POINT_FORBIDDEN",
        [string[]]$ProtectedRoots = @(),
        [switch]$AcquireDirectoryLocks
    )

    try {
        $rootItem = Get-Item -LiteralPath $RootDir -Force -ErrorAction Stop
    } catch [System.Management.Automation.ItemNotFoundException] {
        return [ordered]@{
            RootExists         = $false
            RootFull           = [System.IO.Path]::GetFullPath($RootDir)
            AllowedCanonPrefix = ""
            RootCanonPrefix    = ""
            Files              = @()
            Directories        = @()
            DirIdentities      = @{}
            FileIdentities     = @{}
            LockedHandles      = @{}
        }
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Failed inspecting directory '${ContextLabel}' ($RootDir): $_"
    }

    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel}: $($rootItem.FullName)"
    }
    if (-not $rootItem.PSIsContainer) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Expected directory for ${ContextLabel} but found file: $($rootItem.FullName)"
    }

    $allowedFull = [System.IO.Path]::GetFullPath($AllowedParentRoot)
    $allowedLexPrefix = Normalize-PathWithSeparator -Path $allowedFull
    $rootLexFull = [System.IO.Path]::GetFullPath($rootItem.FullName)
    $rootLexPrefix = Normalize-PathWithSeparator -Path $rootLexFull

    $dirIdentities = @{}
    $fileIdentities = @{}
    $lockedHandles = @{}

    try {
        $allowedCanon = [SalrynNativePathResolver]::GetFinalPath($allowedFull)
        $rootId = ""; $rootRep = $false; $rootCanon = ""
        if ($AcquireDirectoryLocks) {
            $hRoot = [SalrynNativePathResolver]::OpenDirectoryNoDeleteShare($rootLexFull, [ref]$rootId, [ref]$rootRep, [ref]$rootCanon)
            $lockedHandles[$rootLexFull] = $hRoot
            if ($rootRep) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel}: $rootLexFull"
            }
        } else {
            $rootIsDir = $false
            $rootExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($rootLexFull, [ref]$rootId, [ref]$rootRep, [ref]$rootIsDir)
            if (-not $rootExists -or $rootRep -or (-not $rootIsDir)) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Reparse point/junction or invalid root in ${ContextLabel}: $rootLexFull"
            }
            $rootCanon = [SalrynNativePathResolver]::GetFinalPath($rootLexFull)
        }
        $dirIdentities[$rootLexFull] = $rootId

        $allowedCanonPrefix = Normalize-PathWithSeparator -Path $allowedCanon
        $rootCanonPrefix = Normalize-PathWithSeparator -Path $rootCanon

        if (-not $rootLexPrefix.StartsWith($allowedLexPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not $rootCanonPrefix.StartsWith($allowedCanonPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Directory escapes allowed root in ${ContextLabel}: $rootCanon"
        }

        $protectedNorms = @($ProtectedRoots | ForEach-Object { Normalize-PathWithSeparator -Path $_ })
        $files = New-Object System.Collections.Generic.List[string]
        $dirs = New-Object System.Collections.Generic.List[string]
        $queue = New-Object System.Collections.Generic.Queue[string]
        $queue.Enqueue($rootLexFull)

        while ($queue.Count -gt 0) {
            $currentDir = $queue.Dequeue()
            foreach ($rawChild in [System.IO.Directory]::EnumerateFileSystemEntries($currentDir)) {
                $childFull = [System.IO.Path]::GetFullPath($rawChild)
                if (-not $childFull.StartsWith($rootLexPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Child path escapes ${ContextLabel} root: $childFull"
                }

                $cId = ""; $cRep = $false; $cIsDir = $false
                $cExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($childFull, [ref]$cId, [ref]$cRep, [ref]$cIsDir)
                if (-not $cExists) {
                    Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Child disappeared during scan of ${ContextLabel}: $childFull"
                }
                if ($cRep) {
                    Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel}: $childFull"
                }

                if ($cIsDir) {
                    $cIdFinal = $cId; $cRepFinal = $false; $childCanon = ""
                    if ($AcquireDirectoryLocks) {
                        $hChild = [SalrynNativePathResolver]::OpenDirectoryNoDeleteShare($childFull, [ref]$cIdFinal, [ref]$cRepFinal, [ref]$childCanon)
                        $lockedHandles[$childFull] = $hChild
                        if ($cRepFinal -or ($cIdFinal -ne $cId)) {
                            Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Directory became reparse point or swapped during scan of ${ContextLabel}: $childFull"
                        }
                    } else {
                        $childCanon = [SalrynNativePathResolver]::GetFinalPath($childFull)
                    }

                    $childCanonNorm = Normalize-PathWithSeparator -Path $childCanon
                    if (-not $childCanonNorm.StartsWith($rootCanonPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                        -not $childCanonNorm.StartsWith($allowedCanonPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Descendant resolves outside allowed root in ${ContextLabel}: $childCanon"
                    }
                    foreach ($protNorm in $protectedNorms) {
                        if ($childCanonNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                            $protNorm.StartsWith($childCanonNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                            Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "Descendant resolves to protected root in ${ContextLabel}: $childCanon"
                        }
                    }
                    $dirIdentities[$childFull] = $cIdFinal
                    $dirs.Add($childFull)
                    $queue.Enqueue($childFull)
                } else {
                    $fileIdentities[$childFull] = $cId
                    $files.Add($childFull)
                }
            }
        }

        return [ordered]@{
            RootExists         = $true
            RootFull           = $rootLexFull
            AllowedCanonPrefix = $allowedCanonPrefix
            RootCanonPrefix    = $rootCanonPrefix
            Files              = @($files)
            Directories        = @($dirs)
            DirIdentities      = $dirIdentities
            FileIdentities     = $fileIdentities
            LockedHandles      = $lockedHandles
        }
    } catch {
        foreach ($hKey in @($lockedHandles.Keys)) {
            $hObj = $lockedHandles[$hKey]
            if ($null -ne $hObj) {
                try { $hObj.Dispose() } catch { }
            }
        }
        $lockedHandles.Clear()
        throw
    }
}

function Remove-OwnedDirectoryTreeSafely {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDir,
        [Parameter(Mandatory = $true)][string]$AllowedParentRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [string]$ReparseErrorCode = "REPARSE_POINT_FORBIDDEN",
        [string[]]$ProtectedRoots = @()
    )

    $scan = $null
    $lockedHandles = @{}
    try {
        $scan = Assert-TreeFreeOfReparsePointsAndAliases `
            -RootDir $TargetDir `
            -AllowedParentRoot $AllowedParentRoot `
            -HeadSha $HeadSha `
            -ContextLabel $ContextLabel `
            -ReparseErrorCode $ReparseErrorCode `
            -ProtectedRoots $ProtectedRoots `
            -AcquireDirectoryLocks
        if (-not $scan.RootExists) { return }
        $lockedHandles = $scan.LockedHandles

        if (-not [string]::IsNullOrWhiteSpace($env:SALRYN_TEST_POST_SCAN_HOOK) -and
            (Test-Path -LiteralPath $env:SALRYN_TEST_POST_SCAN_HOOK -PathType Leaf)) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:SALRYN_TEST_POST_SCAN_HOOK -ContextLabel $ContextLabel -TargetRoot $scan.RootFull
        }

        # Re-verify every scanned directory identity and reparse status before deleting any files
        foreach ($dirCheck in (@($scan.RootFull) + @($scan.Directories))) {
            $cId = ""; $cRep = $false; $cIsDir = $false
            $cExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($dirCheck, [ref]$cId, [ref]$cRep, [ref]$cIsDir)
            if ((-not $cExists) -or $cRep -or (-not $cIsDir) -or ($cId -ne $scan.DirIdentities[$dirCheck])) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Directory identity or reparse state changed before file delete in ${ContextLabel}: $dirCheck"
            }
        }

        foreach ($filePath in $scan.Files) {
            $fId = ""; $fRep = $false; $fIsDir = $false
            $fExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($filePath, [ref]$fId, [ref]$fRep, [ref]$fIsDir)
            if (-not $fExists) { continue }
            if ($fRep -or $fIsDir -or ($fId -ne $scan.FileIdentities[$filePath])) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "File identity changed or reparse point appeared before delete in ${ContextLabel}: $filePath"
            }
            $fAttr = [System.IO.File]::GetAttributes($filePath)
            if (($fAttr -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                [System.IO.File]::SetAttributes($filePath, ($fAttr -band (-bnot [System.IO.FileAttributes]::ReadOnly)))
            }
            [System.IO.File]::Delete($filePath)
        }

        $sortedDirs = @($scan.Directories | Sort-Object -Property { $_.Length } -Descending)
        foreach ($dirPath in $sortedDirs) {
            $remEntries = @([System.IO.Directory]::EnumerateFileSystemEntries($dirPath))
            if ($remEntries.Count -gt 0) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Unexpected entry appeared during cleanup of ${ContextLabel} (possible concurrent modification/race): $($remEntries[0])"
            }
            $dId = ""; $dRep = $false; $dIsDir = $false
            $dExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($dirPath, [ref]$dId, [ref]$dRep, [ref]$dIsDir)
            if ($dExists) {
                if ($dRep -or (-not $dIsDir) -or ($dId -ne $scan.DirIdentities[$dirPath])) {
                    Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Directory identity changed before bottom-up delete in ${ContextLabel}: $dirPath"
                }
                if ($lockedHandles.ContainsKey($dirPath) -and $null -ne $lockedHandles[$dirPath]) {
                    $lockedHandles[$dirPath].Dispose()
                    [void]$lockedHandles.Remove($dirPath)
                }
                $dAttr = [System.IO.File]::GetAttributes($dirPath)
                if (($dAttr -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                    [System.IO.File]::SetAttributes($dirPath, ($dAttr -band (-bnot [System.IO.FileAttributes]::ReadOnly)))
                }
                [System.IO.Directory]::Delete($dirPath, $false)
            }
        }

        $rootRem = @([System.IO.Directory]::EnumerateFileSystemEntries($scan.RootFull))
        if ($rootRem.Count -gt 0) {
            Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Unexpected entry appeared in root during cleanup of ${ContextLabel} (possible race): $($rootRem[0])"
        }
        $rId = ""; $rRep = $false; $rIsDir = $false
        $rExists = [SalrynNativePathResolver]::TryGetFileIdentityNoFollow($scan.RootFull, [ref]$rId, [ref]$rRep, [ref]$rIsDir)
        if ($rExists) {
            if ($rRep -or (-not $rIsDir) -or ($rId -ne $scan.DirIdentities[$scan.RootFull])) {
                Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Root directory identity changed before delete in ${ContextLabel}: $($scan.RootFull)"
            }
            if ($lockedHandles.ContainsKey($scan.RootFull) -and $null -ne $lockedHandles[$scan.RootFull]) {
                $lockedHandles[$scan.RootFull].Dispose()
                [void]$lockedHandles.Remove($scan.RootFull)
            }
            $rAttr = [System.IO.File]::GetAttributes($scan.RootFull)
            if (($rAttr -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                [System.IO.File]::SetAttributes($scan.RootFull, ($rAttr -band (-bnot [System.IO.FileAttributes]::ReadOnly)))
            }
            [System.IO.Directory]::Delete($scan.RootFull, $false)
        }
    } finally {
        foreach ($hKey in @($lockedHandles.Keys)) {
            $hObj = $lockedHandles[$hKey]
            if ($null -ne $hObj) {
                try { $hObj.Dispose() } catch { }
            }
        }
        $lockedHandles.Clear()
    }
}

function Assert-NoReparsePointsOrTraversal {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [string]$ReparseErrorCode = "REPARSE_POINT_FORBIDDEN"
    )
    if (-not (Test-Path -LiteralPath $RootDir)) {
        $rawItem = Get-Item -LiteralPath $RootDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $rawItem -and (($rawItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            Write-ProvenanceFailure -Code $ReparseErrorCode -HeadSha $HeadSha -Detail "Reparse point/symlink forbidden in ${ContextLabel}: $($rawItem.FullName)"
        }
        Write-ProvenanceFailure -Code "MISSING_REQUIRED_DIRECTORY" -HeadSha $HeadSha -Detail "Required directory missing in ${ContextLabel}: $RootDir"
    }
    [void](Assert-TreeFreeOfReparsePointsAndAliases `
        -RootDir $RootDir `
        -AllowedParentRoot $RootDir `
        -HeadSha $HeadSha `
        -ContextLabel $ContextLabel `
        -ReparseErrorCode $ReparseErrorCode)
}

function Assert-SafeManifestRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Empty relativePath in ${ContextLabel}."
    }
    if ($RelativePath.StartsWith("/") -or $RelativePath.StartsWith("\") -or $RelativePath.Contains("\") -or $RelativePath -match '^[A-Za-z]:') {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Non-normalized relativePath in ${ContextLabel}: '$RelativePath'"
    }
    $segments = $RelativePath.Split('/')
    foreach ($seg in $segments) {
        if ([string]::IsNullOrEmpty($seg) -or $seg -eq "." -or $seg -eq "..") {
            Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Path traversal segment in ${ContextLabel}: '$RelativePath'"
        }
    }
    $canonicalPrefix = Normalize-PathWithSeparator -Path $RootDir
    $combined = [System.IO.Path]::GetFullPath((Join-Path $RootDir ($RelativePath -replace '/', '\')))
    if (-not $combined.StartsWith($canonicalPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Manifest relativePath escapes root in ${ContextLabel}: '$RelativePath'"
    }
}

function Get-Sha256HexFromString {
    param([Parameter(Mandatory = $true)][string]$Text)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $bytes = $utf8NoBom.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-FileSha256Lower {
    param([Parameter(Mandatory = $true)][string]$FilePath)
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $FilePath).Hash.ToLowerInvariant()
}

function Get-DirectoryFileHashInventory {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [string[]]$ExcludeRelativePaths = @(),
        [switch]$IncludeBinaryVersions
    )
    $canonicalPrefix = Normalize-PathWithSeparator -Path $RootDir
    $excludeSet = @{}
    foreach ($ex in $ExcludeRelativePaths) {
        if (-not [string]::IsNullOrWhiteSpace($ex)) {
            $excludeSet[$ex.Replace('\', '/').ToLowerInvariant()] = $true
        }
    }

    $rawFiles = Get-ChildItem -LiteralPath $RootDir -Recurse -File -Force -ErrorAction Stop
    $entries = New-Object System.Collections.Generic.List[object]

    foreach ($file in $rawFiles) {
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        $rel = $full.Substring($canonicalPrefix.Length).Replace('\', '/')
        if ($excludeSet.ContainsKey($rel.ToLowerInvariant())) {
            continue
        }
        $sha256 = Get-FileSha256Lower -FilePath $full
        $entry = [ordered]@{
            relativePath = $rel
            bytes        = [int64]$file.Length
            sha256       = $sha256
        }
        if ($IncludeBinaryVersions -and ($file.Extension -eq ".exe" -or $file.Extension -eq ".dll")) {
            $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($full)
            $entry["fileVersion"] = $fvi.FileVersion
            $entry["productVersion"] = $fvi.ProductVersion
        }
        $entries.Add($entry)
    }

    $sorted = $entries | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture)
    return ,@($sorted)
}

function Get-CanonicalInventorySha256 {
    param([AllowEmptyCollection()][array]$InventoryEntries)
    $sb = New-Object System.Text.StringBuilder
    foreach ($entry in $InventoryEntries) {
        [void]$sb.Append($entry.relativePath).Append("`n").Append([string]$entry.bytes).Append("`n").Append($entry.sha256).Append("`n")
    }
    return (Get-Sha256HexFromString -Text $sb.ToString())
}

function Get-WebSourceInputDigest {
    param(
        [Parameter(Mandatory = $true)][string]$WebRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha
    )
    if (-not (Test-Path -LiteralPath $WebRoot)) {
        Write-ProvenanceFailure -Code "MISSING_WEB_ROOT" -HeadSha $HeadSha -Detail "Web project folder missing: $WebRoot"
    }
    Assert-NotReparsePoint -Path $WebRoot -HeadSha $HeadSha -ContextLabel "web-root"
    $canonicalPrefix = Normalize-PathWithSeparator -Path $WebRoot
    $entries = New-Object System.Collections.Generic.List[object]

    $topItems = Get-ChildItem -LiteralPath $WebRoot -Force -ErrorAction Stop | Where-Object {
        $_.Name -ne "node_modules" -and $_.Name -ne "dist" -and $_.Name -ne "tsconfig.tsbuildinfo"
    }
    $allFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($top in $topItems) {
        if (($top.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point in web source: $($top.FullName)"
        }
        if ($top.PSIsContainer) {
            $webQueue = New-Object System.Collections.Generic.Queue[string]
            $webQueue.Enqueue($top.FullName)
            while ($webQueue.Count -gt 0) {
                $curWebDir = $webQueue.Dequeue()
                foreach ($child in @(Get-ChildItem -LiteralPath $curWebDir -Force -ErrorAction Stop)) {
                    if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point in web source: $($child.FullName)"
                    }
                    if ($child.PSIsContainer) {
                        $webQueue.Enqueue($child.FullName)
                    } elseif ($child.Name -ne "tsconfig.tsbuildinfo") {
                        $allFiles.Add($child)
                    }
                }
            }
        } else {
            $allFiles.Add($top)
        }
    }

    foreach ($file in $allFiles) {
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        $rel = $full.Substring($canonicalPrefix.Length).Replace('\', '/')
        $entries.Add([ordered]@{
            relativePath = $rel
            bytes        = [int64]$file.Length
            sha256       = (Get-FileSha256Lower -FilePath $full)
        })
    }

    $sorted = @($entries | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture))
    return (Get-CanonicalInventorySha256 -InventoryEntries $sorted)
}

function Resolve-GitSourceProvenance {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$ExpectedSha
    )
    Assert-NotReparsePoint -Path $RepoRoot -HeadSha "UNKNOWN" -ContextLabel "repo-root"
    $gitCmd = Get-Command "git" -ErrorAction SilentlyContinue
    if (-not $gitCmd) {
        Write-ProvenanceFailure -Code "GIT_UNAVAILABLE" -HeadSha "UNKNOWN" -Detail "git executable was not found on PATH."
    }

    $inside = (& git -C $RepoRoot rev-parse --is-inside-work-tree 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $inside -ne "true") {
        Write-ProvenanceFailure -Code "NOT_A_GIT_WORKTREE" -HeadSha "UNKNOWN" -Detail "Repository root is not a valid Git worktree: $RepoRoot"
    }

    $headRaw = (& git -C $RepoRoot rev-parse --verify HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $headRaw -notmatch '^[0-9a-fA-F]{40}$') {
        Write-ProvenanceFailure -Code "INVALID_GIT_HEAD" -HeadSha "UNKNOWN" -Detail "Could not resolve full 40-character HEAD commit SHA (got '$headRaw')."
    }
    $headSha = $headRaw.ToLowerInvariant()

    $branchRaw = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branchRaw)) {
        Write-ProvenanceFailure -Code "INVALID_GIT_BRANCH" -HeadSha $headSha -Detail "Could not resolve Git branch for HEAD."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        $trimmedExpected = $ExpectedSha.Trim()
        if ($trimmedExpected -notmatch '^[0-9a-fA-F]{40}$') {
            Write-ProvenanceFailure -Code "EXPECTED_SHA_INVALID" -HeadSha $headSha -Detail "-ExpectedSourceSha must be a full 40-character hexadecimal SHA; got '$ExpectedSha'."
        }
        if ($trimmedExpected.ToLowerInvariant() -ne $headSha) {
            Write-ProvenanceFailure -Code "EXPECTED_SHA_MISMATCH" -HeadSha $headSha -Detail "Expected source SHA '$($trimmedExpected.ToLowerInvariant())' does not match HEAD '$headSha'."
        }
    }

    $statusOutput = @(& git -C $RepoRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "GIT_STATUS_FAILED" -HeadSha $headSha -Detail "git status --porcelain failed with exit code $LASTEXITCODE."
    }
    $dirtyLines = @($statusOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($dirtyLines.Count -gt 0) {
        $sample = ($dirtyLines | Select-Object -First 10) -join "; "
        Write-ProvenanceFailure -Code "DIRTY_WORKTREE" -HeadSha $headSha -Detail "Worktree contains $($dirtyLines.Count) dirty/untracked item(s): $sample"
    }

    return [ordered]@{
        HeadSha      = $headSha
        GitBranch    = $branchRaw
        DetachedHead = ($branchRaw -eq "HEAD")
        WorktreeClean = $true
    }
}

function Assert-OutputDirSafe {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string[]]$ExtraProtectedRoots = @()
    )
    $resolvedArtifact = Resolve-CanonicalPathThroughAncestors -TargetPath $ArtifactRoot -HeadSha $HeadSha -ContextLabel "OutputDir"
    $resolvedRepo = Resolve-CanonicalPathThroughAncestors -TargetPath $RepoRoot -HeadSha $HeadSha -ContextLabel "RepoRoot"

    $candidateArtifactPaths = @(
        ($resolvedArtifact.LexicalFull),
        ($resolvedArtifact.CanonicalFull)
    ) | Select-Object -Unique

    $protectedRoots = Get-ProtectedRootsList -HeadSha $HeadSha -ExtraProtectedRoots $ExtraProtectedRoots

    $repoCandidates = @(
        (Normalize-PathWithSeparator -Path $resolvedRepo.LexicalFull),
        (Normalize-PathWithSeparator -Path $resolvedRepo.CanonicalFull)
    ) | Select-Object -Unique
    $allowedRepoArtifactsNorms = @(
        (Normalize-PathWithSeparator -Path (Join-Path $resolvedRepo.LexicalFull "artifacts")),
        (Normalize-PathWithSeparator -Path (Join-Path $resolvedRepo.CanonicalFull "artifacts"))
    ) | Select-Object -Unique

    foreach ($candPath in $candidateArtifactPaths) {
        $candNorm = Normalize-PathWithSeparator -Path $candPath
        foreach ($repoNorm in $repoCandidates) {
            if ($candNorm.Equals($repoNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $repoNorm.StartsWith($candNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir cannot be the repository root or an ancestor of it: $candPath"
            }
            if ($candNorm.StartsWith($repoNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                $underArtifacts = $false
                foreach ($artNorm in $allowedRepoArtifactsNorms) {
                    if ($candNorm.StartsWith($artNorm, [System.StringComparison]::OrdinalIgnoreCase) -and
                        -not $candNorm.Equals($artNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $underArtifacts = $true
                    }
                }
                if (-not $underArtifacts) {
                    Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir inside repository must reside strictly beneath artifacts\: $candPath"
                }
            }
        }
    }

    foreach ($candPath in $candidateArtifactPaths) {
        $artifactNorm = Normalize-PathWithSeparator -Path $candPath
        foreach ($prot in $protectedRoots) {
            $protNorm = Normalize-PathWithSeparator -Path $prot
            if ($artifactNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $protNorm.StartsWith($artifactNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "OutputDir overlaps or resolves to protected Salryn/stand-in directory '$prot' (resolved='$candPath')"
            }
        }
    }

    # Deep non-following scan of any existing OutputDir subtree (catches nested junctions/symlinks inside
    # staging, staging\Salryn\dist, assets, shell-publish, or setup before any delete or write occurs).
    $existingOutItem = Get-Item -LiteralPath $resolvedArtifact.LexicalFull -Force -ErrorAction SilentlyContinue
    if ($null -ne $existingOutItem) {
        [void](Assert-TreeFreeOfReparsePointsAndAliases `
            -RootDir $resolvedArtifact.LexicalFull `
            -AllowedParentRoot $resolvedArtifact.LexicalFull `
            -HeadSha $HeadSha `
            -ContextLabel "OutputDir" `
            -ReparseErrorCode "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" `
            -ProtectedRoots $protectedRoots)
    }

    return $resolvedArtifact
}

function Remove-StaleSetupArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$SetupDir,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string[]]$ExtraProtectedRoots = @()
    )
    [void](Assert-OutputDirSafe -ArtifactRoot $ArtifactRoot -RepoRoot $RepoRoot -HeadSha $HeadSha -ExtraProtectedRoots $ExtraProtectedRoots)
    $setupDirItem = Get-Item -LiteralPath $SetupDir -Force -ErrorAction SilentlyContinue
    if ($null -eq $setupDirItem) { return }
    if (($setupDirItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Setup directory cannot be a reparse point: $SetupDir"
    }
    Assert-NoReparsePointsOrTraversal -RootDir $SetupDir -HeadSha $HeadSha -ContextLabel "setup-dir" -ReparseErrorCode "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN"
    $setupExePath = Join-Path $SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    foreach ($staleTarget in @($setupExePath, "$setupExePath.sha256.txt", "$setupExePath.provenance.json")) {
        $staleItem = Get-Item -LiteralPath $staleTarget -Force -ErrorAction SilentlyContinue
        if ($null -ne $staleItem) {
            if (($staleItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Setup artifact target cannot be a reparse point: $staleTarget"
            }
            if (($staleItem.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                [System.IO.File]::SetAttributes($staleTarget, ($staleItem.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)))
            }
            [System.IO.File]::Delete($staleTarget)
        }
    }
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) { return $ExplicitPath }
        throw "ISCC.exe not found at the path supplied via -InnoSetupCompilerPath: $ExplicitPath"
    }

    $fromEnv = [System.Environment]::GetEnvironmentVariable("INNO_SETUP_ISCC")
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        if (Test-Path -LiteralPath $fromEnv) { return $fromEnv }
        throw "ISCC.exe not found at path set in INNO_SETUP_ISCC environment variable: $fromEnv"
    }

    $fromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($fromPath -and (Test-Path -LiteralPath $fromPath)) { return $fromPath }

    $standardPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $standardPaths) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, set INNO_SETUP_ISCC, or pass -InnoSetupCompilerPath."
}

function Assert-StagedBinariesAndAssets {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    $requiredFiles = @(
        "Salryn.Desktop.WebView.exe",
        "Salryn.Desktop.WebView.dll",
        "Salryn.Api.Local.exe",
        "Salryn.Api.Local.dll",
        "Salryn.Core.dll",
        "Salryn.Infrastructure.dll",
        "Salryn.Composition.Local.dll",
        "Salryn.Host.Local.dll",
        "WebView2Loader.dll",
        "runtimes\win-x64\native\WebView2Loader.dll",
        "dist\index.html",
        "assets\branding\salryn-icon.ico",
        "assets\branding\salryn-icon.png",
        "assets\branding\salryn-banner-clear.png",
        "assets\branding\salryn-banner-filled.png",
        "salryn-beta-rc1-installer-stage.txt"
    )

    foreach ($rel in $requiredFiles) {
        $full = Join-Path $PublishDir $rel
        if (-not (Test-Path -LiteralPath $full)) {
            Write-ProvenanceFailure -Code "MISSING_STAGED_FILE" -HeadSha $HeadSha -Detail "Required staged artifact missing: $rel"
        }
        $item = Get-Item -LiteralPath $full -Force
        if ($item.Length -le 0) {
            Write-ProvenanceFailure -Code "EMPTY_STAGED_FILE" -HeadSha $HeadSha -Detail "Required staged artifact is empty: $rel"
        }
    }

    $versionCheckedBinaries = @(
        "Salryn.Desktop.WebView.exe",
        "Salryn.Desktop.WebView.dll",
        "Salryn.Api.Local.exe",
        "Salryn.Api.Local.dll",
        "Salryn.Core.dll",
        "Salryn.Infrastructure.dll",
        "Salryn.Composition.Local.dll",
        "Salryn.Host.Local.dll"
    )

    $expectedSuffix = "+$HeadSha"
    foreach ($binRel in $versionCheckedBinaries) {
        $binPath = Join-Path $PublishDir $binRel
        $prodVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($binPath).ProductVersion
        if ([string]::IsNullOrWhiteSpace($prodVer) -or
            (-not $prodVer.EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) -or
            (-not $prodVer.StartsWith("$ExpectedVersion+", [System.StringComparison]::OrdinalIgnoreCase))) {
            Write-ProvenanceFailure -Code "STALE_BINARY_PROVENANCE" -HeadSha $HeadSha -Detail "Staged binary '$binRel' ProductVersion '$prodVer' does not match expected '$ExpectedVersion$expectedSuffix'."
        }
    }
}

function Assert-LockedWebDependencies {
    param(
        [Parameter(Mandatory = $true)][string]$WebRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha
    )

    $pkgJsonPath = Join-Path $WebRoot "package.json"
    $pkgLockPath = Join-Path $WebRoot "package-lock.json"
    if (-not (Test-Path -LiteralPath $pkgJsonPath) -or -not (Test-Path -LiteralPath $pkgLockPath)) {
        Write-ProvenanceFailure -Code "WEB_LOCKFILE_MISSING" -HeadSha $HeadSha -Detail "Missing package.json or package-lock.json in $WebRoot."
    }
    Assert-NotReparsePoint -Path $pkgJsonPath -HeadSha $HeadSha -ContextLabel "web-package-json"
    Assert-NotReparsePoint -Path $pkgLockPath -HeadSha $HeadSha -ContextLabel "web-package-lock"

    & git -C $RepoRoot ls-files --error-unmatch "src/Salryn.App.Web/package.json" "src/Salryn.App.Web/package-lock.json" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "WEB_LOCKFILE_MISSING" -HeadSha $HeadSha -Detail "src/Salryn.App.Web/package.json and package-lock.json must be tracked in Git."
    }

    $prevCheckEnv = $env:SALRYN_CHECK_WEB_ROOT
    $prevEap = $ErrorActionPreference
    try {
        $env:SALRYN_CHECK_WEB_ROOT = $WebRoot
        $ErrorActionPreference = 'Continue'
        $jsPreLockCheck = "const fs=require('fs'),path=require('path');const w=process.env.SALRYN_CHECK_WEB_ROOT;const l=JSON.parse(fs.readFileSync(path.join(w,'package-lock.json'),'utf8'));if(!l.lockfileVersion||l.lockfileVersion<2){console.log('bad_lockfile_version');process.exit(3);}const lp=l.packages||{};const missing=[];for(const k of Object.keys(lp)){if(!k||lp[k].optional)continue;const integ=lp[k].integrity;if(!integ||!/^sha(512|384|256)-/.test(integ))missing.push(k);}if(missing.length){console.log('missing_integrity:'+missing.slice(0,5).join(';'));process.exit(4);}"
        $preCheckOut = & node -e $jsPreLockCheck 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-ProvenanceFailure -Code "WEB_LOCKFILE_INTEGRITY_MISSING" -HeadSha $HeadSha -Detail "Tracked package-lock.json lacks valid lockfileVersion/SRI integrity digests: $($preCheckOut -join ' ')"
        }
    } finally {
        $ErrorActionPreference = $prevEap
        $env:SALRYN_CHECK_WEB_ROOT = $prevCheckEnv
    }

    $nmDir = Join-Path $WebRoot "node_modules"
    $nmLock = Join-Path $nmDir ".package-lock.json"
    $nmExists = Get-Item -LiteralPath $nmDir -Force -ErrorAction SilentlyContinue
    if ($null -ne $nmExists) {
        Remove-OwnedDirectoryTreeSafely -TargetDir $nmDir -AllowedParentRoot $WebRoot -HeadSha $HeadSha -ContextLabel "web-node-modules"
    }

    $npmCmdDisplay = "npm ci --prefer-offline --ignore-scripts --no-audit --no-fund"
    Write-Info "Installing locked web dependencies via clean $npmCmdDisplay..."
    & npm --prefix $WebRoot ci --prefer-offline --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "WEB_NPM_CI_FAILED" -HeadSha $HeadSha -Detail "npm ci failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $nmLock)) {
        Write-ProvenanceFailure -Code "WEB_LOCKFILE_MISMATCH" -HeadSha $HeadSha -Detail "node_modules\.package-lock.json missing in $WebRoot after clean npm ci."
    }
    $viteBinPath = Join-Path $nmDir "vite\bin\vite.js"
    if (-not (Test-Path -LiteralPath $viteBinPath)) {
        Write-ProvenanceFailure -Code "WEB_LOCKFILE_MISMATCH" -HeadSha $HeadSha -Detail "Expected vite CLI missing after clean npm ci: $viteBinPath"
    }
    Assert-NotReparsePoint -Path $viteBinPath -HeadSha $HeadSha -ContextLabel "web-vite-bin"

    $prevCheckEnv2 = $env:SALRYN_CHECK_WEB_ROOT
    $prevEap2 = $ErrorActionPreference
    try {
        $env:SALRYN_CHECK_WEB_ROOT = $WebRoot
        $ErrorActionPreference = 'Continue'
        $jsLockCheck = "const fs=require('fs'),path=require('path');const w=process.env.SALRYN_CHECK_WEB_ROOT;const l=JSON.parse(fs.readFileSync(path.join(w,'package-lock.json'),'utf8'));const n=JSON.parse(fs.readFileSync(path.join(w,'node_modules/.package-lock.json'),'utf8'));const lp=l.packages||{},np=n.packages||{};const b=[];for(const k of Object.keys(lp)){if(!k||lp[k].optional)continue;if(!np[k]||np[k].version!==lp[k].version||np[k].integrity!==lp[k].integrity){b.push('diff:'+k);continue;}const pjPath=path.join(w,k,'package.json');if(!fs.existsSync(pjPath)){b.push('missing:'+k);continue;}const pj=JSON.parse(fs.readFileSync(pjPath,'utf8'));if(pj.version!==lp[k].version)b.push('ver:'+k);}for(const k of Object.keys(np)){if(!lp[k]||lp[k].version!==np[k].version||lp[k].integrity!==np[k].integrity)b.push('extra:'+k);}if(b.length){console.log(b.slice(0,5).join(';'));process.exit(2);}"
        $verifyOut = & node -e $jsLockCheck 2>&1
        $verifyExit = $LASTEXITCODE
        if ($verifyExit -ne 0) {
            Write-ProvenanceFailure -Code "WEB_LOCKFILE_MISMATCH" -HeadSha $HeadSha -Detail "Installed node_modules does not match tracked package-lock.json: $($verifyOut -join ' ')"
        }
    } finally {
        $ErrorActionPreference = $prevEap2
        $env:SALRYN_CHECK_WEB_ROOT = $prevCheckEnv2
    }

    return [ordered]@{
        PackageLockSha256 = (Get-FileSha256Lower -FilePath $pkgLockPath)
        NpmInstallCommand = $npmCmdDisplay
    }
}

function Assert-StageManifestMatchesDisk {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedConfiguration,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$WebRoot,
        [Parameter(Mandatory = $true)][string]$WebDistDir,
        [Parameter(Mandatory = $true)][string]$WebProvenanceStampPath,
        [string]$ExpectedManifestSha256 = "",
        [switch]$RequireSourceTreeAssetParity,
        [switch]$RequireAuthoritativeReleaseStage
    )

    Assert-NoReparsePointsOrTraversal -RootDir $PublishDir -HeadSha $HeadSha -ContextLabel "staging"
    Assert-StagedBinariesAndAssets -PublishDir $PublishDir -HeadSha $HeadSha -ExpectedVersion $ExpectedVersion

    $manifestPath = Join-Path $PublishDir "salryn-installer-provenance-manifest.json"
    $manifestShaPath = Join-Path $PublishDir "salryn-installer-provenance-manifest.json.sha256"
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $manifestShaPath)) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_MISSING" -HeadSha $HeadSha -Detail "Stage manifest or companion .sha256 missing in staging directory."
    }

    $actualManifestSha = Get-FileSha256Lower -FilePath $manifestPath
    $shaLine = (Get-Content -LiteralPath $manifestShaPath -Raw).Trim()
    if ($shaLine -notmatch '^([0-9a-fA-F]{64})\s+\*?salryn-installer-provenance-manifest\.json$') {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_SHA_FORMAT_INVALID" -HeadSha $HeadSha -Detail "Malformed manifest .sha256 file: '$shaLine'"
    }
    $recordedManifestSha = $Matches[1].ToLowerInvariant()
    if ($actualManifestSha -ne $recordedManifestSha) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_HASH_MISMATCH" -HeadSha $HeadSha -Detail "Manifest SHA256 mismatch: expected $recordedManifestSha, actual $actualManifestSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedManifestSha256)) {
        $trimmedManifestExpected = $ExpectedManifestSha256.Trim()
        if ($trimmedManifestExpected -notmatch '^[0-9a-fA-F]{64}$') {
            Write-ProvenanceFailure -Code "EXPECTED_STAGE_MANIFEST_SHA_INVALID" -HeadSha $HeadSha -Detail "-ExpectedStageManifestSha256 must be a valid 64-char hex SHA-256."
        }
        $normalizedExpectedManifestSha = $trimmedManifestExpected.ToLowerInvariant()
        if ($actualManifestSha -ne $normalizedExpectedManifestSha) {
            Write-ProvenanceFailure -Code "EXPECTED_STAGE_MANIFEST_SHA_MISMATCH" -HeadSha $HeadSha -Detail "Staged manifest SHA-256 '$actualManifestSha' does not match external pinned digest '$normalizedExpectedManifestSha'."
        }
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    } catch {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_PARSE_FAILED" -HeadSha $HeadSha -Detail "Failed to parse manifest JSON: $_"
    }

    if ($manifest.schemaVersion -ne "1.0" -or
        $manifest.source.commitSha -ne $HeadSha -or
        $manifest.source.worktreeClean -ne $true -or
        $manifest.productVersion -ne $ExpectedVersion -or
        $manifest.configuration -ne $ExpectedConfiguration) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_METADATA_MISMATCH" -HeadSha $HeadSha -Detail "Manifest metadata does not match active source HEAD='$HeadSha', version='$ExpectedVersion', config='$ExpectedConfiguration'."
    }

    if ($RequireAuthoritativeReleaseStage) {
        if ($manifest.releaseAuthority -ne "SOURCE_BOUND_FRESH_BUILD" -or
            $manifest.authoritativeInstallerAllowed -ne $true -or
            $manifest.buildOptions.skipWebBuild -ne $false) {
            Write-ProvenanceFailure -Code "NONRELEASE_STAGE_FORBIDDEN_FOR_INSTALLER" -HeadSha $HeadSha -Detail "Stage is marked '$($manifest.releaseAuthority)' (authoritativeInstallerAllowed=$($manifest.authoritativeInstallerAllowed)); Option A forbids non-release stages from producing an installer EXE."
        }
    }

    $manifestFiles = @($manifest.files)
    foreach ($mf in $manifestFiles) {
        Assert-SafeManifestRelativePath -RelativePath ([string]$mf.relativePath) -RootDir $PublishDir -HeadSha $HeadSha -ContextLabel "stage-manifest"
    }

    $liveInventory = Get-DirectoryFileHashInventory -RootDir $PublishDir -ExcludeRelativePaths @(
        "salryn-installer-provenance-manifest.json",
        "salryn-installer-provenance-manifest.json.sha256"
    )
    $liveInventorySha = Get-CanonicalInventorySha256 -InventoryEntries $liveInventory

    if ([int]$manifest.payloadFileCount -ne $liveInventory.Count -or
        $manifest.payloadInventorySha256 -ne $liveInventorySha) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_INVENTORY_MISMATCH" -HeadSha $HeadSha -Detail "Staged files on disk ($($liveInventory.Count) files, $liveInventorySha) differ from manifest ($($manifest.payloadFileCount) files, $($manifest.payloadInventorySha256))."
    }

    $expectedManifestId = "salryn-pos-$ExpectedVersion-$($HeadSha.Substring(0,12))-$($liveInventorySha.Substring(0,12))"
    if ($manifest.manifestId -ne $expectedManifestId) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_ID_MISMATCH" -HeadSha $HeadSha -Detail "Manifest ID '$($manifest.manifestId)' does not match recomputed ID '$expectedManifestId'."
    }

    for ($i = 0; $i -lt $liveInventory.Count; $i++) {
        $expectedEntry = $manifestFiles[$i]
        $actualEntry = $liveInventory[$i]
        if ($expectedEntry.relativePath -ne $actualEntry.relativePath -or
            [int64]$expectedEntry.bytes -ne [int64]$actualEntry.bytes -or
            $expectedEntry.sha256 -ne $actualEntry.sha256) {
            Write-ProvenanceFailure -Code "STAGE_MANIFEST_FILE_MISMATCH" -HeadSha $HeadSha -Detail "Staged file mismatch at '$($actualEntry.relativePath)' vs manifest '$($expectedEntry.relativePath)'."
        }
    }

    # Verify tracked branding assets in staging match clean Git worktree bytes
    foreach ($asset in @("salryn-icon.ico", "salryn-icon.png", "salryn-banner-clear.png", "salryn-banner-filled.png")) {
        $repoAssetSha = Get-FileSha256Lower -FilePath (Join-Path $RepoRoot "assets\branding\$asset")
        $stageAssetSha = Get-FileSha256Lower -FilePath (Join-Path $PublishDir "assets\branding\$asset")
        if ($repoAssetSha -ne $stageAssetSha) {
            Write-ProvenanceFailure -Code "STAGE_BRANDING_ASSET_MISMATCH" -HeadSha $HeadSha -Detail "Staged branding asset '$asset' hash ($stageAssetSha) does not match source tree ($repoAssetSha)."
        }
    }

    # Verify web source input digest against live clean source tree
    $liveWebSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $WebRoot -HeadSha $HeadSha
    if ($manifest.buildOptions.webSourceInputSha256 -ne $liveWebSourceInputSha256) {
        Write-ProvenanceFailure -Code "STAGE_WEB_SOURCE_DIGEST_MISMATCH" -HeadSha $HeadSha -Detail "Manifest webSourceInputSha256 ($($manifest.buildOptions.webSourceInputSha256)) does not match clean source tree ($liveWebSourceInputSha256)."
    }

    # Verify staged dist matches manifest webDistInventorySha256
    $stagedDistPath = Join-Path $PublishDir "dist"
    $stagedDistInv = Get-DirectoryFileHashInventory -RootDir $stagedDistPath
    $stagedDistInvSha = Get-CanonicalInventorySha256 -InventoryEntries $stagedDistInv
    if ($stagedDistInvSha -ne [string]$manifest.buildOptions.webDistInventorySha256) {
        Write-ProvenanceFailure -Code "STAGE_WEB_DIST_PROVENANCE_MISMATCH" -HeadSha $HeadSha -Detail "Staged dist inventory ($stagedDistInvSha) does not match manifest webDistInventorySha256 ($($manifest.buildOptions.webDistInventorySha256))."
    }

    if ($RequireSourceTreeAssetParity -or $RequireAuthoritativeReleaseStage) {
        $verifiedWorktreeDist = Assert-VerifiedSkipWebDist `
            -WebDistDir $WebDistDir `
            -StampPath $WebProvenanceStampPath `
            -ExpectedHeadSha $HeadSha `
            -ExpectedWebSourceInputSha256 $liveWebSourceInputSha256
        if ($stagedDistInvSha -ne $verifiedWorktreeDist.InventorySha256) {
            Write-ProvenanceFailure -Code "STAGE_WEB_DIST_PROVENANCE_MISMATCH" -HeadSha $HeadSha -Detail "Staged dist inventory ($stagedDistInvSha) does not match verified source-tree web dist ($($verifiedWorktreeDist.InventorySha256))."
        }
    }

    # Verify tracked package-lock.json digest when manifest recorded it for a fresh npm ci build
    $hasPkgLockProp = $null -ne $manifest.buildOptions.PSObject.Properties["packageLockSha256"]
    if ($manifest.buildOptions.skipWebBuild -eq $false -and $hasPkgLockProp -and -not [string]::IsNullOrWhiteSpace([string]$manifest.buildOptions.packageLockSha256)) {
        $livePkgLockSha = Get-FileSha256Lower -FilePath (Join-Path $WebRoot "package-lock.json")
        if ([string]$manifest.buildOptions.packageLockSha256 -ne $livePkgLockSha) {
            Write-ProvenanceFailure -Code "STAGE_WEB_LOCKFILE_DIGEST_MISMATCH" -HeadSha $HeadSha -Detail "Manifest packageLockSha256 ($($manifest.buildOptions.packageLockSha256)) does not match clean source tree ($livePkgLockSha)."
        }
    }

    return [ordered]@{
        Manifest               = $manifest
        ManifestSha256         = $actualManifestSha
        LiveInventory          = $liveInventory
        PayloadInventorySha256 = $liveInventorySha
    }
}

function New-PrivateDirectorySecurityDescriptorBytes {
    param([Parameter(Mandatory = $true)][string]$HeadSha)

    if ($env:SALRYN_TEST_ISOLATED_HARNESS -eq "1" -and $env:SALRYN_TEST_FORCE_SD_FAILURE -eq "1") {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Security descriptor setup aborted (isolated fault probe)."
    }

    $currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUserSid) {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Could not resolve current Windows user SID for creation-time security descriptor."
    }
    $localSystemSid = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $adminsSid = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)

    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $prop = [System.Security.AccessControl.PropagationFlags]::None
    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $allow = [System.Security.AccessControl.AccessControlType]::Allow

    foreach ($sid in @($currentUserSid, $localSystemSid, $adminsSid)) {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, $rights, $inherit, $prop, $allow)
        $acl.AddAccessRule($rule)
    }

    $sdBytes = $acl.GetSecurityDescriptorBinaryForm()
    if (-not [SalrynNativePathResolver]::IsValidSecurityDescriptorBytes($sdBytes)) {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Constructed creation-time security descriptor failed IsValidSecurityDescriptor validation."
    }
    return ,$sdBytes
}

function Assert-HandleParentAclSafe {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.SafeHandles.SafeFileHandle]$DirHandle,
        [Parameter(Mandatory = $true)][string]$CanonicalPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [switch]$IsDriveRoot,
        [switch]$IsDeepestCreationParent
    )
    try {
        $rawSdBytes = [SalrynNativePathResolver]::GetSecurityDescriptorFromHandle($DirHandle, $ContextLabel)
        $rsd = New-Object System.Security.AccessControl.RawSecurityDescriptor($rawSdBytes, 0)
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Failed reading security descriptor from pinned handle for ${ContextLabel} ($CanonicalPath): $_"
    }

    $currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUserSid) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Cannot resolve current build identity SID while validating ${ContextLabel} ($CanonicalPath)."
    }

    # Owner implicitly holds WRITE_DAC + READ_CONTROL regardless of DACL entries
    $trustedOwnerSids = @(
        $currentUserSid.Value,
        "S-1-5-18",
        "S-1-5-32-544",
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"
    ) | Select-Object -Unique
    if ($null -eq $rsd.Owner -or ($trustedOwnerSids -notcontains $rsd.Owner.Value)) {
        $ownerStr = if ($null -eq $rsd.Owner) { "NULL" } else { $rsd.Owner.Value }
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($CanonicalPath) has untrusted owner SID '$ownerStr'."
    }

    if ((($rsd.ControlFlags -band [System.Security.AccessControl.ControlFlags]::DiscretionaryAclPresent) -eq 0) -or
        ($null -eq $rsd.DiscretionaryAcl)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($CanonicalPath) has NULL DACL (world-accessible)."
    }

    # Explicit trusted trustee allowlist for effective dangerous directory access
    $trustedTrusteeSids = @($currentUserSid.Value, "S-1-5-18", "S-1-5-32-544") | Select-Object -Unique

    # Destructive/takeover/content-write rights:
    # FILE_ADD_FILE(0x2) | FILE_WRITE_EA(0x10) | FILE_DELETE_CHILD(0x40) | FILE_WRITE_ATTRIBUTES(0x100) |
    # DELETE(0x10000) | WRITE_DAC(0x40000) | WRITE_OWNER(0x80000) | GENERIC_ALL(0x10000000) | GENERIC_WRITE(0x40000000)
    $baseDangerousBits = [int64]0x500D0152
    $fullDangerousBits = [int64]0x500D0156  # includes FILE_ADD_SUBDIRECTORY(0x4)
    $maskAll32 = [int64]4294967295          # 0xFFFFFFFF

    $deniedBySid = @{}
    $allowedBySid = @{}

    foreach ($ace in $rsd.DiscretionaryAcl) {
        # Fail closed on non-CommonAce, ObjectAce, CompoundAce, CustomAce, or conditional/callback ACEs
        if (($ace.GetType().FullName -ne "System.Security.AccessControl.CommonAce") -or $ace.IsCallback) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($CanonicalPath) contains unsupported/callback/object ACE type '$($ace.GetType().Name)' ($($ace.AceType))."
        }
        if (($ace.AceType -ne [System.Security.AccessControl.AceType]::AccessAllowed) -and
            ($ace.AceType -ne [System.Security.AccessControl.AceType]::AccessDenied)) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($CanonicalPath) contains unsupported ACE type '$($ace.AceType)'."
        }

        # INHERIT_ONLY ACEs do not grant or deny effective rights on this directory object itself
        $aceFlagsInt = [int]$ace.AceFlags
        $inheritOnlyInt = [int][System.Security.AccessControl.AceFlags]::InheritOnly
        if (($aceFlagsInt -band $inheritOnlyInt) -ne 0) {
            continue
        }

        $sidVal = $ace.SecurityIdentifier.Value
        $rawMask = [int64]([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$ace.AccessMask), 0))
        $expandedMask = $rawMask
        if (($rawMask -band [int64]0x10000000) -ne 0) { $expandedMask = $expandedMask -bor [int64]0x001F01FF } # GENERIC_ALL
        if (($rawMask -band [int64]0x40000000) -ne 0) { $expandedMask = $expandedMask -bor [int64]0x00120116 } # GENERIC_WRITE
        if (($rawMask -band [int64]2147483648) -ne 0) { $expandedMask = $expandedMask -bor [int64]0x00120089 } # GENERIC_READ (0x80000000)
        if (($rawMask -band [int64]0x20000000) -ne 0) { $expandedMask = $expandedMask -bor [int64]0x001200A0 } # GENERIC_EXECUTE

        $curAllowed = if ($allowedBySid.ContainsKey($sidVal)) { [int64]$allowedBySid[$sidVal] } else { [int64]0 }
        $curDenied = if ($deniedBySid.ContainsKey($sidVal)) { [int64]$deniedBySid[$sidVal] } else { [int64]0 }

        if ($ace.AceType -eq [System.Security.AccessControl.AceType]::AccessDenied) {
            $notAllowedYet = ((-bnot $curAllowed) -band $maskAll32)
            $deniedBySid[$sidVal] = ($curDenied -bor ($expandedMask -band $notAllowedYet))
        } else {
            $notDeniedYet = ((-bnot $curDenied) -band $maskAll32)
            $allowedBySid[$sidVal] = ($curAllowed -bor ($expandedMask -band $notDeniedYet))
        }
    }

    foreach ($sidKey in $allowedBySid.Keys) {
        if ($trustedTrusteeSids -contains $sidKey) {
            continue
        }
        $effAllowed = [int64]$allowedBySid[$sidKey]
        # Allow strict fixed-drive-root non-destructive FILE_ADD_SUBDIRECTORY(0x4) ONLY for S-1-5-11
        # when strictly above an already-existing protected child path (not when drive root itself is creation parent).
        $checkMask = if ($IsDriveRoot -and (-not $IsDeepestCreationParent) -and ($sidKey -eq "S-1-5-11")) {
            $baseDangerousBits
        } else {
            $fullDangerousBits
        }
        $effDangerous = $effAllowed -band $checkMask
        if ($effDangerous -ne 0) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_PARENT_ACL" -HeadSha $HeadSha -Detail ("Pinned {0} ({1}) grants effective dangerous rights (effective=0x{2:X8}, dangerous=0x{3:X8}) to non-trusted SID {4}." -f $ContextLabel, $CanonicalPath, $effAllowed, $effDangerous, $sidKey)
        }
    }
}

function Assert-HandlePrivateDaclAtBirth {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.SafeHandles.SafeFileHandle]$DirHandle,
        [Parameter(Mandatory = $true)][string]$CanonicalPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    try {
        $rawSdBytes = [SalrynNativePathResolver]::GetSecurityDescriptorFromHandle($DirHandle, $ContextLabel)
        $rsd = New-Object System.Security.AccessControl.RawSecurityDescriptor($rawSdBytes, 0)
    } catch {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Failed reading creation-time security descriptor from handle for ${ContextLabel} ($CanonicalPath): $_"
    }

    $currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $currentUserSid) {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Cannot verify creation-time owner SID (null current user)."
    }
    $allowedOwnerSids = @($currentUserSid.Value, "S-1-5-32-544")
    if ($null -eq $rsd.Owner -or ($allowedOwnerSids -notcontains $rsd.Owner.Value)) {
        $actualOwner = if ($null -eq $rsd.Owner) { "NULL" } else { $rsd.Owner.Value }
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Created directory ${ContextLabel} ($CanonicalPath) has unexpected owner SID '$actualOwner'."
    }

    if (($rsd.ControlFlags -band [System.Security.AccessControl.ControlFlags]::DiscretionaryAclProtected) -eq 0) {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Created directory ${ContextLabel} ($CanonicalPath) lacks SE_DACL_PROTECTED at creation."
    }

    if ($null -eq $rsd.DiscretionaryAcl -or $rsd.DiscretionaryAcl.Count -eq 0) {
        Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Created directory ${ContextLabel} ($CanonicalPath) has empty or null DACL."
    }

    $allowedSids = @($currentUserSid.Value, "S-1-5-18", "S-1-5-32-544") | Select-Object -Unique
    $observedSids = New-Object System.Collections.Generic.List[string]
    foreach ($ace in $rsd.DiscretionaryAcl) {
        if (-not ($ace -is [System.Security.AccessControl.QualifiedAce])) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Unexpected ACE type on newly created ${ContextLabel} ($CanonicalPath)."
        }
        if ($ace.IsInherited) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Inherited ACE detected on newly created ${ContextLabel} ($CanonicalPath): $($ace.SecurityIdentifier.Value)"
        }
        if ($ace.AceQualifier -ne [System.Security.AccessControl.AceQualifier]::AccessAllowed) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Unexpected non-Allow ACE on newly created ${ContextLabel} ($CanonicalPath)."
        }
        $sidVal = $ace.SecurityIdentifier.Value
        if ($allowedSids -notcontains $sidVal) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Untrusted principal '$sidVal' present on newly created ${ContextLabel} ($CanonicalPath)."
        }
        if (($ace.AccessMask -band 0x1F01FF) -ne 0x1F01FF) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Unexpected partial access mask 0x$($ace.AccessMask.ToString('X')) for '$sidVal' on ${ContextLabel} ($CanonicalPath)."
        }
        $observedSids.Add($sidVal)
    }

    foreach ($reqSid in $allowedSids) {
        if ($observedSids -notcontains $reqSid) {
            Write-ProvenanceFailure -Code "OUTPUT_ACL_FAILED" -HeadSha $HeadSha -Detail "Missing required SID '$reqSid' in creation-time DACL for ${ContextLabel} ($CanonicalPath)."
        }
    }
}

function Get-ProtectedRootFileIds {
    param([Parameter(Mandatory = $true)][string[]]$ProtectedRoots)
    $idMap = @{}
    foreach ($pr in $ProtectedRoots) {
        $fId = ""; $fRep = $false; $fDir = $false
        if ([SalrynNativePathResolver]::TryGetFileIdentityNoFollow($pr, [ref]$fId, [ref]$fRep, [ref]$fDir)) {
            if (-not [string]::IsNullOrWhiteSpace($fId)) {
                $idMap[$fId] = $pr
            }
        }
    }
    return $idMap
}

function Assert-HandleCanonicalContainment {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.SafeHandles.SafeFileHandle]$Handle,
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ExpectedVolumePrefix,
        [Parameter(Mandatory = $true)][string]$RepoRootCanonical,
        [Parameter(Mandatory = $true)][string[]]$ProtectedRoots,
        [Parameter(Mandatory = $true)][hashtable]$ProtectedFileIds,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [switch]$AllowRepoArtifactsAncestorOnly,
        [switch]$RequireStrictRepoArtifactsChild
    )

    if ($env:SALRYN_TEST_ISOLATED_HARNESS -eq "1" -and $env:SALRYN_TEST_FORCE_HANDLE_READ_FAILURE -eq "1") {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Kernel handle identity verification failed (isolated fault probe) for ${ContextLabel}."
    }

    $hId = ""; $hRep = $false; $hDir = $false; $hFinal = ""
    try {
        [SalrynNativePathResolver]::GetHandleIdentityAndPath($Handle, $ContextLabel, [ref]$hId, [ref]$hRep, [ref]$hDir, [ref]$hFinal)
    } catch {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Failed reading handle identity for ${ContextLabel}: $_"
    }

    if ($hRep) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Pinned handle for ${ContextLabel} resolved to a reparse point."
    }
    if (-not $hDir -or [string]::IsNullOrWhiteSpace($hId) -or [string]::IsNullOrWhiteSpace($hFinal)) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Invalid handle metadata for ${ContextLabel} (isDir=$hDir, id='$hId', final='$hFinal')."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVolumePrefix)) {
        $volPrefix = ($hId -split ':')[0]
        if (-not $volPrefix.Equals($ExpectedVolumePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Volume serial mismatch for ${ContextLabel}: expected $ExpectedVolumePrefix, actual $volPrefix ($hFinal)."
        }
    }

    $actualNorm = Normalize-PathWithSeparator -Path $hFinal
    $expectedNorm = Normalize-PathWithSeparator -Path $ExpectedPath
    if (-not $actualNorm.Equals($expectedNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Canonical path mismatch for ${ContextLabel}: expected '$expectedNorm', actual '$actualNorm'."
    }

    if ($ProtectedFileIds.ContainsKey($hId)) {
        Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "Handle FileId '$hId' for ${ContextLabel} matches protected directory '$($ProtectedFileIds[$hId])'."
    }

    foreach ($prot in $ProtectedRoots) {
        $protNorm = Normalize-PathWithSeparator -Path $prot
        if ($AllowRepoArtifactsAncestorOnly) {
            # Existing ancestor may naturally contain e.g. system roots only above protected subdirs? Wait:
            # an existing ancestor equal to OR inside a protected root is always forbidden;
            # C:\ is ancestor of C:\ProgramData\Salryn so ancestor containment is allowed ONLY for strict system ancestors above protected roots.
            if ($actualNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($hFinal) lies inside protected root '$prot'."
            }
        } else {
            if ($actualNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $protNorm.StartsWith($actualNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "Pinned ${ContextLabel} ($hFinal) overlaps protected root '$prot'."
            }
        }
    }

    if ($RequireStrictRepoArtifactsChild) {
        $repoNorm = Normalize-PathWithSeparator -Path $RepoRootCanonical
        $artifactsNorm = Normalize-PathWithSeparator -Path (Join-Path $RepoRootCanonical "artifacts")
        if ($actualNorm.Equals($repoNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
            $repoNorm.StartsWith($actualNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} ($hFinal) cannot be repo root or ancestor."
        }
        if ($actualNorm.StartsWith($repoNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
            if (-not $actualNorm.StartsWith($artifactsNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $actualNorm.Equals($artifactsNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} ($hFinal) inside repository must reside strictly beneath artifacts\."
            }
        }
    }

    return [ordered]@{
        FileId        = $hId
        VolumeSerial  = ($hId -split ':')[0]
        CanonicalPath = $hFinal.TrimEnd('\')
    }
}

function New-AnchoredPrivateDirectoryChild {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.SafeHandles.SafeFileHandle]$ParentHandle,
        [Parameter(Mandatory = $true)][string]$ParentCanonical,
        [Parameter(Mandatory = $true)][string]$ParentFileId,
        [Parameter(Mandatory = $true)][string]$LeafName,
        [Parameter(Mandatory = $true)][byte[]]$PrivateSdBytes,
        [Parameter(Mandatory = $true)][string]$RepoRootCanonical,
        [Parameter(Mandatory = $true)][string[]]$ProtectedRoots,
        [Parameter(Mandatory = $true)][hashtable]$ProtectedFileIds,
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$HandleList,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel,
        [switch]$RequireStrictRepoArtifactsChild
    )

    # Re-verify pinned parent handle before creation
    $pIdNow = ""; $pRepNow = $false; $pDirNow = $false; $pFinNow = ""
    [SalrynNativePathResolver]::GetHandleIdentityAndPath($ParentHandle, "$ContextLabel-parent", [ref]$pIdNow, [ref]$pRepNow, [ref]$pDirNow, [ref]$pFinNow)
    if ($pRepNow -or (-not $pDirNow) -or ($pIdNow -ne $ParentFileId) -or
        (-not (Normalize-PathWithSeparator -Path $pFinNow).Equals((Normalize-PathWithSeparator -Path $ParentCanonical), [System.StringComparison]::OrdinalIgnoreCase))) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_CANONICAL_CONTAINMENT_FAILED" -HeadSha $HeadSha -Detail "Pinned parent handle identity shifted before creating ${ContextLabel} '$LeafName'."
    }

    $ntStatus = 0; $cId = ""; $cRep = $false; $cDir = $false; $cFinal = ""
    $childHandle = [SalrynNativePathResolver]::CreateRelativeDirectoryPrivate($ParentHandle, $LeafName, $PrivateSdBytes, [ref]$ntStatus, [ref]$cId, [ref]$cRep, [ref]$cDir, [ref]$cFinal)
    if ($null -eq $childHandle -or $ntStatus -ne 0) {
        # Collision? Inspect leaf with FILE_OPEN_REPARSE_POINT to classify junction vs existing path without following link
        if ($ntStatus -eq -1073741771) {
            $prSt = 0; $prId = ""; $prRep = $false; $prDir = $false; $prFin = ""
            $probeH = [SalrynNativePathResolver]::OpenRelativeDirectoryNoDeleteShare($ParentHandle, $LeafName, [ref]$prSt, [ref]$prId, [ref]$prRep, [ref]$prDir, [ref]$prFin)
            if ($null -ne $probeH) { try { $probeH.Dispose() } catch {} }
            if ($prRep) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Pre-existing reparse point/junction blocked creation of ${ContextLabel} '$LeafName' under $ParentCanonical."
            }
            Write-ProvenanceFailure -Code "NONPRIVATE_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "Pre-existing entry blocked exclusive creation of ${ContextLabel} '$LeafName' under $ParentCanonical."
        }
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail ("NtCreateFile(FILE_CREATE) failed for {0} '{1}' under {2} (NTSTATUS=0x{3:X8})." -f $ContextLabel, $LeafName, $ParentCanonical, $ntStatus)
    }

    $HandleList.Add($childHandle)

    $expectedChildPath = Join-Path ($ParentCanonical.TrimEnd('\') + "\") $LeafName
    $parentVol = ($ParentFileId -split ':')[0]
    $childIdentity = Assert-HandleCanonicalContainment `
        -Handle $childHandle `
        -ExpectedPath $expectedChildPath `
        -ExpectedVolumePrefix $parentVol `
        -RepoRootCanonical $RepoRootCanonical `
        -ProtectedRoots $ProtectedRoots `
        -ProtectedFileIds $ProtectedFileIds `
        -HeadSha $HeadSha `
        -ContextLabel $ContextLabel `
        -RequireStrictRepoArtifactsChild:$RequireStrictRepoArtifactsChild

    Assert-HandlePrivateDaclAtBirth `
        -DirHandle $childHandle `
        -CanonicalPath $childIdentity.CanonicalPath `
        -HeadSha $HeadSha `
        -ContextLabel $ContextLabel

    return [ordered]@{
        Handle        = $childHandle
        FileId        = $childIdentity.FileId
        CanonicalPath = $childIdentity.CanonicalPath
    }
}

# ---------------------------------------------------------------------------
# 1. Fail-closed mode & source custody preflight BEFORE any mutation
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version cannot be empty."
}

$repoRoot = Find-RepoRoot
Set-Location $repoRoot

$probeHeadSha = "UNKNOWN"
$probeGitOut = (& git -C $repoRoot rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -eq 0 -and $probeGitOut -match '^[0-9a-fA-F]{40}$') {
    $probeHeadSha = $probeGitOut.ToLowerInvariant()
}

# Enforce command-mode & Option-A constraints BEFORE touching or deleting anything on disk (F3)
if ($CompileExistingStageOnly) {
    Write-ProvenanceFailure -Code "COMPILE_EXISTING_STAGE_FORBIDDEN" -HeadSha $probeHeadSha -Detail "-CompileExistingStageOnly is disabled under Option A fresh-build-only trust architecture."
}
if ($VerifyStageOnly -and $BuildInstaller) {
    Write-ProvenanceFailure -Code "VERIFY_ONLY_CANNOT_BUILD_INSTALLER" -HeadSha $probeHeadSha -Detail "-VerifyStageOnly is read-only and cannot be combined with -BuildInstaller."
}
if ($VerifyStageOnly -and ($SkipWebBuild -or $ForceNpmCi)) {
    Write-ProvenanceFailure -Code "VERIFY_ONLY_INVALID_OPTIONS" -HeadSha $probeHeadSha -Detail "-VerifyStageOnly is strictly read-only and cannot be combined with -SkipWebBuild or -ForceNpmCi."
}
if ($VerifyStageOnly -and [string]::IsNullOrWhiteSpace($OutputDir)) {
    Write-ProvenanceFailure -Code "VERIFY_ONLY_REQUIRES_OUTPUT_DIR" -HeadSha $probeHeadSha -Detail "-VerifyStageOnly requires an explicit -OutputDir pointing to a staged or built per-run directory."
}
if ($BuildInstaller -and [string]::IsNullOrWhiteSpace($ExpectedSourceSha)) {
    Write-ProvenanceFailure -Code "EXPECTED_SHA_REQUIRED_FOR_INSTALLER" -HeadSha $probeHeadSha -Detail "-BuildInstaller requires an explicit 40-character -ExpectedSourceSha binding HEAD to the approved commit."
}
if ($BuildInstaller -and $SkipWebBuild) {
    Write-ProvenanceFailure -Code "SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER" -HeadSha $probeHeadSha -Detail "-SkipWebBuild cannot be used with -BuildInstaller; trusted packages require a fresh locked web build."
}

$provenance = Resolve-GitSourceProvenance -RepoRoot $repoRoot -ExpectedSha $ExpectedSourceSha
$headSha = $provenance.HeadSha
Write-Info "[PROVENANCE_PREFLIGHT] PASS head=$headSha branch=$($provenance.GitBranch) worktreeClean=true"

$defaultOutputLocationUsed = [bool]([string]::IsNullOrWhiteSpace($OutputDir))
$outputSource = if ($defaultOutputLocationUsed) { "PRIVATE_USER_LOCALAPPDATA_DEFAULT" } else { "EXPLICIT_OUTPUT_DIR" }

if ($defaultOutputLocationUsed) {
    $runStamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + "-" + ([guid]::NewGuid().ToString("N").Substring(0, 8))
    $trustedLocalAppData = $null
    try {
        $trustedLocalAppData = [SalrynNativePathResolver]::GetTrustedLocalAppDataPath()
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "Failed to resolve OS-trusted LocalAppData known folder for default output directory: $($_.Exception.Message)"
    }
    Assert-SupportedLocalDrivePath -RawPath $trustedLocalAppData -HeadSha $headSha -ContextLabel "TrustedLocalAppData"
    $defaultParentRoot = Join-Path $trustedLocalAppData "SalrynInstallerBuilds"
    $rawArtifactRoot = Join-Path $defaultParentRoot "run-$runStamp"
} else {
    if ($OutputDir.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "UNC/device OutputDir path is not allowed: $OutputDir"
    }
    $rawArtifactRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
}
$resolvedOutputInfo = Assert-OutputDirSafe -ArtifactRoot $rawArtifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
$artifactRoot = $resolvedOutputInfo.LexicalFull

$stagingRoot          = Join-Path $artifactRoot "staging"
$publishDir           = Join-Path $stagingRoot "Salryn"
$setupDir             = Join-Path $artifactRoot "setup"
$shellTemp            = Join-Path $artifactRoot "shell-publish"
$webRoot              = Join-Path $repoRoot "src\Salryn.App.Web"
$webDistSrc           = Join-Path $webRoot "dist"
$webProvenanceStamp   = Join-Path $webDistSrc ".salryn-web-build-provenance.json"
$stagedDist           = Join-Path $publishDir "dist"
$iconSource           = Join-Path $repoRoot "assets\branding\salryn-icon.ico"
$issScript            = Join-Path $repoRoot "installer\SalrynSetup.iss"
$webview2RedistDir    = Join-Path $repoRoot "installer\redist"
$webview2Bootstrapper = Join-Path $webview2RedistDir "MicrosoftEdgeWebview2Setup.exe"
$webview2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
$stageProtectedRoots  = Get-ProtectedRootsList -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots

# Check critical repo input paths for reparse points before creating outputs
Assert-NoReparsePointsOrTraversal -RootDir (Join-Path $repoRoot "installer") -HeadSha $headSha -ContextLabel "installer-dir"
Assert-NoReparsePointsOrTraversal -RootDir (Join-Path $repoRoot "assets\branding") -HeadSha $headSha -ContextLabel "branding-dir"

if (-not (Test-Path -LiteralPath $iconSource)) {
    Write-ProvenanceFailure -Code "MISSING_BRANDING_ASSET" -HeadSha $headSha -Detail "Missing installer icon at $iconSource."
}
if (-not (Test-Path -LiteralPath $issScript)) {
    Write-ProvenanceFailure -Code "MISSING_ISS_SCRIPT" -HeadSha $headSha -Detail "Missing Inno Setup script at $issScript."
}

function Assert-VerifiedSkipWebDist {
    param(
        [Parameter(Mandatory = $true)][string]$WebDistDir,
        [Parameter(Mandatory = $true)][string]$StampPath,
        [Parameter(Mandatory = $true)][string]$ExpectedHeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWebSourceInputSha256
    )
    if (-not (Test-Path -LiteralPath (Join-Path $WebDistDir "index.html")) -or
        -not (Test-Path -LiteralPath $StampPath)) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "-SkipWebBuild requires both dist\index.html and .salryn-web-build-provenance.json."
    }

    Assert-NoReparsePointsOrTraversal -RootDir $WebDistDir -HeadSha $ExpectedHeadSha -ContextLabel "web-dist"
    try {
        $stamp = Get-Content -LiteralPath $StampPath -Raw | ConvertFrom-Json
    } catch {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Failed to parse .salryn-web-build-provenance.json: $_"
    }

    if ($stamp.schemaVersion -ne "1.0" -or $stamp.sourceCommitSha -ne $ExpectedHeadSha) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist stamp commit SHA '$($stamp.sourceCommitSha)' does not match HEAD '$ExpectedHeadSha'."
    }
    if ($stamp.webSourceInputSha256 -ne $ExpectedWebSourceInputSha256) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web source input digest changed since web dist stamp was generated."
    }
    $stampFiles = @($stamp.files)
    foreach ($wf in $stampFiles) {
        Assert-SafeManifestRelativePath -RelativePath ([string]$wf.relativePath) -RootDir $WebDistDir -HeadSha $ExpectedHeadSha -ContextLabel "web-dist-stamp"
    }

    $currentInventory = Get-DirectoryFileHashInventory -RootDir $WebDistDir -ExcludeRelativePaths @(".salryn-web-build-provenance.json")
    $currentInventorySha = Get-CanonicalInventorySha256 -InventoryEntries $currentInventory
    if ($currentInventory.Count -eq 0 -or
        [int]$stamp.fileCount -ne $currentInventory.Count -or
        $stamp.inventorySha256 -ne $currentInventorySha) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist files on disk do not match .salryn-web-build-provenance.json."
    }
    for ($i = 0; $i -lt $currentInventory.Count; $i++) {
        $se = $stampFiles[$i]
        $ae = $currentInventory[$i]
        if ($se.relativePath -ne $ae.relativePath -or [int64]$se.bytes -ne [int64]$ae.bytes -or $se.sha256 -ne $ae.sha256) {
            Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist file '$($ae.relativePath)' differs from provenance stamp."
        }
    }

    return [ordered]@{
        Inventory       = $currentInventory
        InventorySha256 = $currentInventorySha
    }
}

if ($VerifyStageOnly) {
    Write-Info "VerifyStageOnly set; inspecting existing stage custody read-only against HEAD $headSha..."
    if (-not (Test-Path -LiteralPath $publishDir)) {
        Write-ProvenanceFailure -Code "MISSING_STAGE_DIRECTORY" -HeadSha $headSha -Detail "Existing stage directory not found: $publishDir"
    }
    $readOnlyVerify = Assert-StageManifestMatchesDisk `
        -PublishDir $publishDir `
        -HeadSha $headSha `
        -ExpectedVersion $Version `
        -ExpectedConfiguration $Configuration `
        -RepoRoot $repoRoot `
        -WebRoot $webRoot `
        -WebDistDir $webDistSrc `
        -WebProvenanceStampPath $webProvenanceStamp `
        -ExpectedManifestSha256 $ExpectedStageManifestSha256 `
        -RequireSourceTreeAssetParity
    Write-Host "[STAGE_VERIFIED_READONLY] manifestId=$($readOnlyVerify.Manifest.manifestId) manifestSha256=$($readOnlyVerify.ManifestSha256) releaseAuthority=$($readOnlyVerify.Manifest.releaseAuthority)"
    exit 0
}

# Mutable build / staging lane (F2): strictly require a non-existent private per-run -OutputDir
$preExistingOutputItem = Get-Item -LiteralPath $artifactRoot -Force -ErrorAction SilentlyContinue
if ($null -ne $preExistingOutputItem) {
    Write-ProvenanceFailure -Code "NONPRIVATE_OUTPUT_DIR_FORBIDDEN" -HeadSha $headSha -Detail "Mutable builds require a non-existent private per-run -OutputDir; existing directory/file reuse is forbidden under F2: $artifactRoot"
}

if ($SkipWebBuild) {
    Write-Info "SkipWebBuild set (NON-RELEASE / NOT ATTESTED dev staging only); verifying dist stamp..."
    $preWebSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $webRoot -HeadSha $headSha
    $verifiedSkipWeb = Assert-VerifiedSkipWebDist `
        -WebDistDir $webDistSrc `
        -StampPath $webProvenanceStamp `
        -ExpectedHeadSha $headSha `
        -ExpectedWebSourceInputSha256 $preWebSourceInputSha256
}

# Scan source project bin/obj trees, web dist, and web node_modules for nested reparse points BEFORE any output creation or cleanup
$srcRootDir = Join-Path $repoRoot "src"
Assert-NotReparsePoint -Path $srcRootDir -HeadSha $headSha -ContextLabel "src-root"
$projectDirs = New-Object System.Collections.Generic.List[string]
foreach ($subProj in @(Get-ChildItem -LiteralPath $srcRootDir -Directory -Force -ErrorAction Stop)) {
    if (($subProj.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $headSha -Detail "Reparse point/junction forbidden in src directory: $($subProj.FullName)"
    }
    $hasCsproj = @(Get-ChildItem -LiteralPath $subProj.FullName -Filter "*.csproj" -File -Force -ErrorAction SilentlyContinue).Count -gt 0
    if ($hasCsproj) {
        $projectDirs.Add($subProj.FullName)
        foreach ($buildSub in @("bin", "obj")) {
            $scanTarget = Join-Path $subProj.FullName $buildSub
            $scanItem = Get-Item -LiteralPath $scanTarget -Force -ErrorAction SilentlyContinue
            if ($null -ne $scanItem) {
                [void](Assert-TreeFreeOfReparsePointsAndAliases -RootDir $scanTarget -AllowedParentRoot $subProj.FullName -HeadSha $headSha -ContextLabel "project-$buildSub" -ProtectedRoots $stageProtectedRoots)
            }
        }
    }
}
$existingDistItem = Get-Item -LiteralPath $webDistSrc -Force -ErrorAction SilentlyContinue
if ($null -ne $existingDistItem) {
    [void](Assert-TreeFreeOfReparsePointsAndAliases -RootDir $webDistSrc -AllowedParentRoot $webRoot -HeadSha $headSha -ContextLabel "web-dist" -ProtectedRoots $stageProtectedRoots)
}
$existingNmItem = Get-Item -LiteralPath (Join-Path $webRoot "node_modules") -Force -ErrorAction SilentlyContinue
if ($null -ne $existingNmItem) {
    [void](Assert-TreeFreeOfReparsePointsAndAliases -RootDir $existingNmItem.FullName -AllowedParentRoot $webRoot -HeadSha $headSha -ContextLabel "web-node-modules" -ProtectedRoots $stageProtectedRoots)
}

# Anchor existing ancestors from fixed drive root, validate parent handle/ACL/containment, and create
# missing output parent segments + private per-run OutputDir + staging/setup/shell children exclusively
# bound to pinned parent directory objects with private SE_DACL_PROTECTED DACL from birth (F2 R5).
$pinnedOutputHandles = New-Object System.Collections.Generic.List[object]
$pinnedArtifactRootHandle = $null
$pinnedArtifactRootCanonical = ""
$pinnedArtifactRootFileId = ""

try {
    $resolvedRepoInfo = Resolve-CanonicalPathThroughAncestors -TargetPath $repoRoot -HeadSha $headSha -ContextLabel "RepoRoot"
    $repoCanonicalClean = $resolvedRepoInfo.CanonicalFull.TrimEnd('\')
    $protectedFileIds = Get-ProtectedRootFileIds -ProtectedRoots $stageProtectedRoots

    $lexFullOut = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\')
    if ($lexFullOut -notmatch '^[A-Za-z]:\\.+') {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "OutputDir must be a non-root fixed local directory path: $artifactRoot"
    }
    $driveRootOut = [System.IO.Path]::GetPathRoot($lexFullOut)
    try {
        $outDriveInfo = New-Object System.IO.DriveInfo($driveRootOut)
        if ($outDriveInfo.DriveType -ne [System.IO.DriveType]::Fixed) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir drive type must be Fixed (found '$($outDriveInfo.DriveType)'): $lexFullOut"
        }
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Unable to validate drive locality for OutputDir '$lexFullOut': $_"
    }

    $rawRemainder = $lexFullOut.Substring($driveRootOut.Length)
    $allSegments = @($rawRemainder.Split('\'))
    if ($allSegments.Count -eq 0) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir contains no path segments: $lexFullOut"
    }
    foreach ($segCheck in $allSegments) {
        if ([string]::IsNullOrWhiteSpace($segCheck) -or $segCheck -eq "." -or $segCheck -eq ".." -or $segCheck.IndexOf(':') -ge 0) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Unsafe OutputDir segment '$segCheck' in path: $lexFullOut"
        }
    }

    $rootFId = ""; $rootRep = $false; $rootIsDir = $false; $rootFinal = ""
    $rootDriveHandle = [SalrynNativePathResolver]::OpenRootDirectoryNoDeleteShare($driveRootOut, [ref]$rootFId, [ref]$rootRep, [ref]$rootIsDir, [ref]$rootFinal)
    $pinnedOutputHandles.Add($rootDriveHandle)
    if ($rootRep -or (-not $rootIsDir) -or [string]::IsNullOrWhiteSpace($rootFId)) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $headSha -Detail "Drive root failed non-reparse directory check: $driveRootOut"
    }
    $volumeSerialExpected = ($rootFId -split ':')[0]

    $curHandle = $rootDriveHandle
    $curCanonical = $rootFinal.TrimEnd('\')
    $curFileId = $rootFId
    $walkedPathExpected = $driveRootOut.TrimEnd('\')
    $pinnedExistingAncestorChain = New-Object System.Collections.Generic.List[object]
    $pinnedExistingAncestorChain.Add(@{ Handle = $rootDriveHandle; Canonical = $curCanonical; IsDriveRoot = $true })
    $missingSegments = New-Object System.Collections.Generic.List[string]
    $stoppedAtMissing = $false

    for ($idx = 0; $idx -lt $allSegments.Count; $idx++) {
        $seg = $allSegments[$idx]
        $walkedPathExpected = "$walkedPathExpected\$seg"

        if ($stoppedAtMissing) {
            $missingSegments.Add($seg)
            continue
        }

        $ntSt = 0; $segId = ""; $segRep = $false; $segDir = $false; $segFinal = ""
        $segHandle = [SalrynNativePathResolver]::OpenRelativeDirectoryNoDeleteShare($curHandle, $seg, [ref]$ntSt, [ref]$segId, [ref]$segRep, [ref]$segDir, [ref]$segFinal)
        if ($null -eq $segHandle -or $ntSt -ne 0) {
            if ($ntSt -eq -1073741772 -or $ntSt -eq -1073741766) {
                # STATUS_OBJECT_NAME_NOT_FOUND (0xC0000034) / STATUS_OBJECT_PATH_NOT_FOUND (0xC000003A)
                $stoppedAtMissing = $true
                $missingSegments.Add($seg)
                continue
            }
            if ($ntSt -eq -1073741565) {
                # STATUS_NOT_A_DIRECTORY -> existing non-directory file
                Write-ProvenanceFailure -Code "NONPRIVATE_OUTPUT_DIR_FORBIDDEN" -HeadSha $headSha -Detail "Existing non-directory entry blocks private OutputDir creation at '$walkedPathExpected'."
            }
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail ("Failed opening OutputDir ancestor '{0}' (NTSTATUS=0x{1:X8})." -f $walkedPathExpected, $ntSt)
        }

        $pinnedOutputHandles.Add($segHandle)
        if ($segRep) {
            Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $headSha -Detail "Reparse point/junction forbidden along OutputDir path at '$walkedPathExpected'."
        }
        if (-not $segDir) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "OutputDir path segment is not a directory: '$walkedPathExpected'."
        }

        # If final target OutputDir itself already exists, fail closed unconditionally under F2
        if ($idx -eq ($allSegments.Count - 1)) {
            Write-ProvenanceFailure -Code "NONPRIVATE_OUTPUT_DIR_FORBIDDEN" -HeadSha $headSha -Detail "Mutable builds require a non-existent private per-run -OutputDir: $artifactRoot"
        }

        $verifiedAnc = Assert-HandleCanonicalContainment `
            -Handle $segHandle `
            -ExpectedPath $walkedPathExpected `
            -ExpectedVolumePrefix $volumeSerialExpected `
            -RepoRootCanonical $repoCanonicalClean `
            -ProtectedRoots $stageProtectedRoots `
            -ProtectedFileIds $protectedFileIds `
            -HeadSha $headSha `
            -ContextLabel "output-ancestor" `
            -AllowRepoArtifactsAncestorOnly

        $pinnedExistingAncestorChain.Add(@{ Handle = $segHandle; Canonical = $verifiedAnc.CanonicalPath; IsDriveRoot = $false })
        $curHandle = $segHandle
        $curCanonical = $verifiedAnc.CanonicalPath
        $curFileId = $verifiedAnc.FileId
    }

    if ($missingSegments.Count -eq 0) {
        Write-ProvenanceFailure -Code "NONPRIVATE_OUTPUT_DIR_FORBIDDEN" -HeadSha $headSha -Detail "OutputDir already exists: $artifactRoot"
    }

    # Validate effective parent-ACL safety across the pinned drive root AND every existing ancestor handle
    for ($ancIdx = 0; $ancIdx -lt $pinnedExistingAncestorChain.Count; $ancIdx++) {
        $ancEntry = $pinnedExistingAncestorChain[$ancIdx]
        $isCreationParent = ($ancIdx -eq ($pinnedExistingAncestorChain.Count - 1))
        Assert-HandleParentAclSafe `
            -DirHandle $ancEntry.Handle `
            -CanonicalPath $ancEntry.Canonical `
            -HeadSha $headSha `
            -ContextLabel $(if ($ancEntry.IsDriveRoot) { "drive-root-parent" } elseif ($isCreationParent) { "output-parent" } else { "output-ancestor" }) `
            -IsDriveRoot:([bool]$ancEntry.IsDriveRoot) `
            -IsDeepestCreationParent:$isCreationParent
    }

    # Verify intended canonical target from pinned parent handle BEFORE any creation
    $intendedArtifactCanonical = $curCanonical.TrimEnd('\')
    foreach ($ms in $missingSegments) {
        $intendedArtifactCanonical = "$intendedArtifactCanonical\$ms"
    }
    $intendedNorm = Normalize-PathWithSeparator -Path $intendedArtifactCanonical
    $repoNormCheck = Normalize-PathWithSeparator -Path $repoCanonicalClean
    $artifactsNormCheck = Normalize-PathWithSeparator -Path (Join-Path $repoCanonicalClean "artifacts")
    if ($intendedNorm.Equals($repoNormCheck, [System.StringComparison]::OrdinalIgnoreCase) -or
        $repoNormCheck.StartsWith($intendedNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "Pinned canonical OutputDir cannot be repository root or ancestor: $intendedArtifactCanonical"
    }
    if ($intendedNorm.StartsWith($repoNormCheck, [System.StringComparison]::OrdinalIgnoreCase) -and
        ((-not $intendedNorm.StartsWith($artifactsNormCheck, [System.StringComparison]::OrdinalIgnoreCase)) -or
         $intendedNorm.Equals($artifactsNormCheck, [System.StringComparison]::OrdinalIgnoreCase))) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "Pinned canonical OutputDir inside repository must reside strictly beneath artifacts\: $intendedArtifactCanonical"
    }
    foreach ($prot in $stageProtectedRoots) {
        $protNorm = Normalize-PathWithSeparator -Path $prot
        if ($intendedNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
            $protNorm.StartsWith($intendedNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $headSha -Detail "Pinned canonical OutputDir overlaps protected directory '$prot': $intendedArtifactCanonical"
        }
    }

    # Re-run static OutputDir safety check with anchors pinned
    [void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)

    # Deterministic pre-creation race barrier (strictly gated to isolated test harness under %TEMP%)
    if ($env:SALRYN_TEST_ISOLATED_HARNESS -eq "1" -and
        -not [string]::IsNullOrWhiteSpace($env:SALRYN_TEST_PRE_CREATE_HOOK) -and
        (Test-Path -LiteralPath $env:SALRYN_TEST_PRE_CREATE_HOOK -PathType Leaf)) {
        $hookFullPre = [System.IO.Path]::GetFullPath($env:SALRYN_TEST_PRE_CREATE_HOOK)
        $tempRootPre = ([System.IO.Path]::GetFullPath($env:TEMP)).TrimEnd('\') + '\'
        if ($hookFullPre.StartsWith($tempRootPre, [System.StringComparison]::OrdinalIgnoreCase)) {
            Assert-NotReparsePoint -Path $hookFullPre -HeadSha $headSha -ContextLabel "test-pre-create-hook"
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $hookFullPre `
                -ParentCanonical $curCanonical `
                -TargetRoot $intendedArtifactCanonical `
                -FirstMissingSegment $missingSegments[0]
        }
    }

    # Construct and validate private SE_DACL_PROTECTED security descriptor BEFORE NtCreateFile(FILE_CREATE)
    $privateSdBytes = New-PrivateDirectorySecurityDescriptorBytes -HeadSha $headSha

    # Create missing parent segments + final OutputDir bound to pinned parent handle with DACL at birth
    for ($mIdx = 0; $mIdx -lt $missingSegments.Count; $mIdx++) {
        $mSeg = $missingSegments[$mIdx]
        $isFinalOutLeaf = ($mIdx -eq ($missingSegments.Count - 1))
        $ctxLbl = if ($isFinalOutLeaf) { "output-root" } else { "output-missing-parent" }

        $createdRes = New-AnchoredPrivateDirectoryChild `
            -ParentHandle $curHandle `
            -ParentCanonical $curCanonical `
            -ParentFileId $curFileId `
            -LeafName $mSeg `
            -PrivateSdBytes $privateSdBytes `
            -RepoRootCanonical $repoCanonicalClean `
            -ProtectedRoots $stageProtectedRoots `
            -ProtectedFileIds $protectedFileIds `
            -HandleList $pinnedOutputHandles `
            -HeadSha $headSha `
            -ContextLabel $ctxLbl `
            -RequireStrictRepoArtifactsChild:$isFinalOutLeaf

        $curHandle = $createdRes.Handle
        $curCanonical = $createdRes.CanonicalPath
        $curFileId = $createdRes.FileId
    }

    $pinnedArtifactRootHandle = $curHandle
    $pinnedArtifactRootCanonical = $curCanonical
    $pinnedArtifactRootFileId = $curFileId

    # Optional isolated test inspection hook right when OutputDir is born (before any child mkdir/writes)
    if ($env:SALRYN_TEST_ISOLATED_HARNESS -eq "1" -and
        -not [string]::IsNullOrWhiteSpace($env:SALRYN_TEST_POST_CREATE_ROOT_HOOK) -and
        (Test-Path -LiteralPath $env:SALRYN_TEST_POST_CREATE_ROOT_HOOK -PathType Leaf)) {
        $hookFullPost = [System.IO.Path]::GetFullPath($env:SALRYN_TEST_POST_CREATE_ROOT_HOOK)
        $tempRootPost = ([System.IO.Path]::GetFullPath($env:TEMP)).TrimEnd('\') + '\'
        if ($hookFullPost.StartsWith($tempRootPost, [System.StringComparison]::OrdinalIgnoreCase)) {
            Assert-NotReparsePoint -Path $hookFullPost -HeadSha $headSha -ContextLabel "test-post-create-hook"
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $hookFullPost `
                -ArtifactRootCanonical $pinnedArtifactRootCanonical `
                -FileId $pinnedArtifactRootFileId
        }
    }

    # Create staging, staging\Salryn, setup, and shell-publish bound directly to pinned parent handles with DACL at birth
    $stagingRes = New-AnchoredPrivateDirectoryChild -ParentHandle $pinnedArtifactRootHandle -ParentCanonical $pinnedArtifactRootCanonical -ParentFileId $pinnedArtifactRootFileId -LeafName "staging" -PrivateSdBytes $privateSdBytes -RepoRootCanonical $repoCanonicalClean -ProtectedRoots $stageProtectedRoots -ProtectedFileIds $protectedFileIds -HandleList $pinnedOutputHandles -HeadSha $headSha -ContextLabel "staging-root" -RequireStrictRepoArtifactsChild
    [void](New-AnchoredPrivateDirectoryChild -ParentHandle $stagingRes.Handle -ParentCanonical $stagingRes.CanonicalPath -ParentFileId $stagingRes.FileId -LeafName "Salryn" -PrivateSdBytes $privateSdBytes -RepoRootCanonical $repoCanonicalClean -ProtectedRoots $stageProtectedRoots -ProtectedFileIds $protectedFileIds -HandleList $pinnedOutputHandles -HeadSha $headSha -ContextLabel "publish-dir" -RequireStrictRepoArtifactsChild)
    [void](New-AnchoredPrivateDirectoryChild -ParentHandle $pinnedArtifactRootHandle -ParentCanonical $pinnedArtifactRootCanonical -ParentFileId $pinnedArtifactRootFileId -LeafName "setup" -PrivateSdBytes $privateSdBytes -RepoRootCanonical $repoCanonicalClean -ProtectedRoots $stageProtectedRoots -ProtectedFileIds $protectedFileIds -HandleList $pinnedOutputHandles -HeadSha $headSha -ContextLabel "setup-dir" -RequireStrictRepoArtifactsChild)
    [void](New-AnchoredPrivateDirectoryChild -ParentHandle $pinnedArtifactRootHandle -ParentCanonical $pinnedArtifactRootCanonical -ParentFileId $pinnedArtifactRootFileId -LeafName "shell-publish" -PrivateSdBytes $privateSdBytes -RepoRootCanonical $repoCanonicalClean -ProtectedRoots $stageProtectedRoots -ProtectedFileIds $protectedFileIds -HandleList $pinnedOutputHandles -HeadSha $headSha -ContextLabel "shell-temp" -RequireStrictRepoArtifactsChild)
    New-Item -ItemType Directory -Force -Path $webview2RedistDir | Out-Null

    function Ensure-WebView2Bootstrapper {
        if (Test-Path -LiteralPath $webview2Bootstrapper) {
            Assert-NotReparsePoint -Path $webview2Bootstrapper -HeadSha $headSha -ContextLabel "webview2-bootstrapper"
            $hash = Get-FileSha256Lower -FilePath $webview2Bootstrapper
            Write-Info "WebView2 bootstrapper present: $webview2Bootstrapper"
            Write-Info "WebView2 bootstrapper SHA-256: $hash"
            return
        }

        Write-Info "WebView2 bootstrapper missing; downloading from Microsoft..."
        Write-Info "URL: $webview2BootstrapperUrl"
        try {
            Invoke-WebRequest -Uri $webview2BootstrapperUrl -OutFile $webview2Bootstrapper -UseBasicParsing
        } catch {
            throw "Failed to download Microsoft Edge WebView2 Evergreen Bootstrapper from $webview2BootstrapperUrl. $_"
        }

        if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
            throw "WebView2 bootstrapper download completed but file is missing: $webview2Bootstrapper"
        }

        $hash = Get-FileSha256Lower -FilePath $webview2Bootstrapper
        $size = (Get-Item -LiteralPath $webview2Bootstrapper).Length
        Write-Info "Downloaded WebView2 bootstrapper ($size bytes)"
        Write-Info "WebView2 bootstrapper SHA-256: $hash"
    }

    Ensure-WebView2Bootstrapper
    $redistBytes = [int64](Get-Item -LiteralPath $webview2Bootstrapper).Length
    $redistSha256 = Get-FileSha256Lower -FilePath $webview2Bootstrapper

    $dotnetVersion = (& dotnet --version 2>$null | Out-String).Trim()
    $nodeVersion = (& node --version 2>$null | Out-String).Trim()
    $npmVersion = (& npm --version 2>$null | Out-String).Trim()

    # ---------------------------------------------------------------------------
    # 2. Build / Stage Pipeline (fresh source-bound build required for release)
    # ---------------------------------------------------------------------------
    $webSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $webRoot -HeadSha $headSha
    $webDepInfo = $null

    if (-not $SkipWebBuild) {
        $webDepInfo = Assert-LockedWebDependencies -WebRoot $webRoot -RepoRoot $repoRoot -HeadSha $headSha
        Write-Info "Cleaning previous web dist safely and running fresh npm run build..."
        Remove-OwnedDirectoryTreeSafely -TargetDir $webDistSrc -AllowedParentRoot $webRoot -HeadSha $headSha -ContextLabel "web-dist" -ProtectedRoots $stageProtectedRoots
        & npm --prefix $webRoot run build
        if ($LASTEXITCODE -ne 0) {
            Write-ProvenanceFailure -Code "WEB_BUILD_FAILED" -HeadSha $headSha -Detail "npm run build failed (exit $LASTEXITCODE)."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $webDistSrc "index.html"))) {
            Write-ProvenanceFailure -Code "MISSING_WEB_DIST" -HeadSha $headSha -Detail "Web dist missing index.html after npm run build."
        }
        Assert-NoReparsePointsOrTraversal -RootDir $webDistSrc -HeadSha $headSha -ContextLabel "web-dist"

        $webDistInventory = Get-DirectoryFileHashInventory -RootDir $webDistSrc -ExcludeRelativePaths @(".salryn-web-build-provenance.json")
        if ($webDistInventory.Count -eq 0) {
            Write-ProvenanceFailure -Code "EMPTY_WEB_DIST" -HeadSha $headSha -Detail "Web dist inventory is empty after npm run build."
        }
        $webDistInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $webDistInventory

        $stampObject = [ordered]@{
            schemaVersion        = "1.0"
            sourceCommitSha      = $headSha
            webSourceInputSha256 = $webSourceInputSha256
            builtUtc             = (Get-Date).ToUniversalTime().ToString("o")
            fileCount            = $webDistInventory.Count
            inventorySha256      = $webDistInventorySha256
            files                = $webDistInventory
        }
        Write-Utf8NoBom -Path $webProvenanceStamp -Content ($stampObject | ConvertTo-Json -Depth 8)
    } else {
        $webDistInventory = $verifiedSkipWeb.Inventory
        $webDistInventorySha256 = $verifiedSkipWeb.InventorySha256
        Write-Info "Verified cached web dist against commit $headSha ($($webDistInventory.Count) files)."
    }

    # TOCTOU re-check output root immediately before compiling/publishing into pinned private dirs
    [void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)

    $apiProj = Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj"
    $shellProj = Join-Path $repoRoot "src\Salryn.Desktop.WebView\Salryn.Desktop.WebView.csproj"

    # Safely remove prior configuration bin/obj intermediates (leaving NuGet project.assets.json intact)
    # so stale compiled DLLs cannot leak into fresh SourceRevisionId-stamped assemblies.
    foreach ($projDir in $projectDirs) {
        foreach ($sub in @("bin\$Configuration", "obj\$Configuration")) {
            $cleanTarget = Join-Path $projDir $sub
            Remove-OwnedDirectoryTreeSafely `
                -TargetDir $cleanTarget `
                -AllowedParentRoot $projDir `
                -HeadSha $headSha `
                -ContextLabel "project-$sub" `
                -ProtectedRoots $stageProtectedRoots
        }
    }

    Write-Info "Publishing Salryn.Api.Local (SourceRevisionId=$headSha)..."
    & dotnet publish $apiProj `
        -c $Configuration -o $publishDir --self-contained false --disable-build-servers `
        "/p:UseAppHost=true" `
        "/p:Version=$Version" `
        "/p:SourceRevisionId=$headSha" `
        "/p:IncludeSourceRevisionInInformationalVersion=true" `
        "/p:Deterministic=true"
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "DOTNET_PUBLISH_API_FAILED" -HeadSha $headSha -Detail "dotnet publish Salryn.Api.Local failed (exit $LASTEXITCODE)."
    }

    Write-Info "Publishing Salryn.Desktop.WebView (SourceRevisionId=$headSha)..."
    & dotnet publish $shellProj `
        -c $Configuration -o $shellTemp --self-contained false --disable-build-servers `
        "/p:UseAppHost=true" `
        "/p:Version=$Version" `
        "/p:SourceRevisionId=$headSha" `
        "/p:IncludeSourceRevisionInInformationalVersion=true" `
        "/p:Deterministic=true"
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "DOTNET_PUBLISH_SHELL_FAILED" -HeadSha $headSha -Detail "dotnet publish Salryn.Desktop.WebView failed (exit $LASTEXITCODE)."
    }

    Copy-Item -Path (Join-Path $shellTemp '*') -Destination $publishDir -Recurse -Force

    $webview2LoaderSrc = Join-Path $shellTemp "runtimes\win-x64\native\WebView2Loader.dll"
    if (-not (Test-Path -LiteralPath $webview2LoaderSrc)) {
        $webview2LoaderSrc = Get-ChildItem -Path $shellTemp -Recurse -Filter "WebView2Loader.dll" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'win-x64' } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $webview2LoaderSrc -or -not (Test-Path -LiteralPath $webview2LoaderSrc)) {
        Write-ProvenanceFailure -Code "MISSING_WEBVIEW2_LOADER" -HeadSha $headSha -Detail "Shell publish is missing WebView2Loader.dll under $shellTemp."
    }

    $webview2NativeDir = Join-Path $publishDir "runtimes\win-x64\native"
    New-Item -ItemType Directory -Force -Path $webview2NativeDir | Out-Null
    Copy-Item -Force -LiteralPath $webview2LoaderSrc -Destination (Join-Path $webview2NativeDir "WebView2Loader.dll")
    Copy-Item -Force -LiteralPath $webview2LoaderSrc -Destination (Join-Path $publishDir "WebView2Loader.dll")
    Write-Info "WebView2Loader staged: $(Join-Path $publishDir 'WebView2Loader.dll')"

    foreach ($rid in @("win-arm64", "win-x86")) {
        $src = Join-Path $shellTemp "runtimes\$rid\native\WebView2Loader.dll"
        if (Test-Path -LiteralPath $src) {
            $destDir = Join-Path $publishDir "runtimes\$rid\native"
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
            Copy-Item -Force -LiteralPath $src -Destination (Join-Path $destDir "WebView2Loader.dll")
        }
    }

    # Stage React dist (excluding internal build stamp file) and verify parity
    Remove-OwnedDirectoryTreeSafely `
        -TargetDir $stagedDist `
        -AllowedParentRoot $artifactRoot `
        -HeadSha $headSha `
        -ContextLabel "staged-dist" `
        -ReparseErrorCode "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" `
        -ProtectedRoots $stageProtectedRoots
    Copy-Item -Recurse -Force -LiteralPath $webDistSrc -Destination $stagedDist
    $stagedStampCopy = Join-Path $stagedDist ".salryn-web-build-provenance.json"
    $stagedStampItem = Get-Item -LiteralPath $stagedStampCopy -Force -ErrorAction SilentlyContinue
    if ($null -ne $stagedStampItem) {
        if (($stagedStampItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $headSha -Detail "Reparse point found at staged stamp copy: $stagedStampCopy"
        }
        [System.IO.File]::Delete($stagedStampCopy)
    }
    Assert-NoReparsePointsOrTraversal -RootDir $stagedDist -HeadSha $headSha -ContextLabel "staged-dist"
    $stagedDistInventory = Get-DirectoryFileHashInventory -RootDir $stagedDist
    $stagedDistInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $stagedDistInventory
    if ($stagedDistInventorySha256 -ne $webDistInventorySha256) {
        Write-ProvenanceFailure -Code "STAGED_DIST_PARITY_MISMATCH" -HeadSha $headSha -Detail "Staged dist inventory does not match verified web dist inventory."
    }

    # Stage branding assets
    $brandingStage = Join-Path $publishDir "assets\branding"
    New-Item -ItemType Directory -Force -Path $brandingStage | Out-Null
    foreach ($asset in @("salryn-icon.ico", "salryn-icon.png", "salryn-banner-clear.png", "salryn-banner-filled.png")) {
        Copy-Item -Force -LiteralPath (Join-Path $repoRoot "assets\branding\$asset") -Destination (Join-Path $brandingStage $asset)
    }

    $releaseAuthority = if ($SkipWebBuild) { "NONRELEASE_DEV_STAGE_NOT_ATTESTED" } else { "SOURCE_BOUND_FRESH_BUILD" }
    $authoritativeAllowed = (-not $SkipWebBuild)

    # Human-readable stage metadata (relative paths only; no hostnames/usernames/secrets)
    $stagedUtc = (Get-Date).ToUniversalTime().ToString("o")
    $stageStampContent = (@(
        "Product=Salryn POS"
        "Publisher=Kirjane Information Technology Services"
        "ProductVersion=$Version"
        "Configuration=$Configuration"
        "ReleaseAuthority=$releaseAuthority"
        "AuthoritativeInstallerAllowed=$authoritativeAllowed"
        "SourceCommitSha=$headSha"
        "GitBranch=$($provenance.GitBranch)"
        "WorktreeClean=true"
        "SkipWebBuild=$([bool]$SkipWebBuild)"
        "WebDistInventorySha256=$webDistInventorySha256"
        "StagedUtc=$stagedUtc"
        "Entry=Salryn.Desktop.WebView"
        "HostProcess=Salryn.Api.Local"
        "HostUrl=http://127.0.0.1:5299/"
        "WebDist=dist\"
        "InstallerIcon=assets\branding\salryn-icon.ico"
        "ProductionDataPath=%ProgramData%\Salryn\data\salryn.db"
        "WebView2=EvergreenBootstrapper"
        "WebView2Bootstrapper=installer\redist\MicrosoftEdgeWebview2Setup.exe"
        "ManifestFile=salryn-installer-provenance-manifest.json"
    ) -join "`r`n") + "`r`n"
    Write-Utf8NoBom -Path (Join-Path $publishDir "salryn-beta-rc1-installer-stage.txt") -Content $stageStampContent

    # Verify staged binaries, assets, and reparse-point cleanliness BEFORE emitting manifest
    Assert-NoReparsePointsOrTraversal -RootDir $publishDir -HeadSha $headSha -ContextLabel "staging"
    Assert-StagedBinariesAndAssets -PublishDir $publishDir -HeadSha $headSha -ExpectedVersion $Version

    # Build deterministic SHA-bound manifest covering all staged payload files
    $stageInventory = Get-DirectoryFileHashInventory -RootDir $publishDir -ExcludeRelativePaths @(
        "salryn-installer-provenance-manifest.json",
        "salryn-installer-provenance-manifest.json.sha256"
    ) -IncludeBinaryVersions
    $payloadInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $stageInventory
    $manifestId = "salryn-pos-$Version-$($headSha.Substring(0,12))-$($payloadInventorySha256.Substring(0,12))"
    $pkgLockSha = if ($null -ne $webDepInfo) { $webDepInfo.PackageLockSha256 } else { "" }
    $npmInstallCmd = if ($null -ne $webDepInfo) { $webDepInfo.NpmInstallCommand } else { "SKIPPED_NONRELEASE_DEV_STAGE" }

    $manifestObject = [ordered]@{
        schemaVersion               = "1.0"
        manifestId                  = $manifestId
        product                     = "Salryn POS"
        publisher                   = "Kirjane Information Technology Services"
        productVersion              = $Version
        configuration               = $Configuration
        releaseAuthority            = $releaseAuthority
        authoritativeInstallerAllowed = $authoritativeAllowed
        builtUtc                    = $stagedUtc
        source                      = [ordered]@{
            commitSha     = $headSha
            branch        = $provenance.GitBranch
            detachedHead  = $provenance.DetachedHead
            worktreeClean = $true
        }
        buildOptions                = [ordered]@{
            skipWebBuild            = [bool]$SkipWebBuild
            npmInstallCommand       = $npmInstallCmd
            packageLockSha256       = $pkgLockSha
            webDistInventorySha256  = $webDistInventorySha256
            webSourceInputSha256    = $webSourceInputSha256
        }
        toolchain                   = [ordered]@{
            dotnetSdk  = $dotnetVersion
            node       = $nodeVersion
            npm        = $npmVersion
            powershell = $PSVersionTable.PSVersion.ToString()
        }
        redistributables            = @(
            [ordered]@{
                relativePath = "installer/redist/MicrosoftEdgeWebview2Setup.exe"
                bytes        = $redistBytes
                sha256       = $redistSha256
            }
        )
        excludedFromSelfHash        = @(
            "salryn-installer-provenance-manifest.json",
            "salryn-installer-provenance-manifest.json.sha256"
        )
        payloadFileCount            = $stageInventory.Count
        payloadInventorySha256      = $payloadInventorySha256
        files                       = $stageInventory
    }

    $manifestPath = Join-Path $publishDir "salryn-installer-provenance-manifest.json"
    $manifestShaPath = Join-Path $publishDir "salryn-installer-provenance-manifest.json.sha256"
    Write-Utf8NoBom -Path $manifestPath -Content ($manifestObject | ConvertTo-Json -Depth 10)
    $manifestSha256 = Get-FileSha256Lower -FilePath $manifestPath
    Write-Utf8NoBom -Path $manifestShaPath -Content "$manifestSha256  salryn-installer-provenance-manifest.json`n"

    Write-Info "Generated stage manifest: $manifestId ($($stageInventory.Count) payload files, authority=$releaseAuthority, inventorySha256=$payloadInventorySha256)"

    $stageVerifyParams = @{
        PublishDir                      = $publishDir
        HeadSha                         = $headSha
        ExpectedVersion                 = $Version
        ExpectedConfiguration           = $Configuration
        RepoRoot                        = $repoRoot
        WebRoot                         = $webRoot
        WebDistDir                      = $webDistSrc
        WebProvenanceStampPath          = $webProvenanceStamp
        RequireSourceTreeAssetParity    = [bool]$BuildInstaller
        RequireAuthoritativeReleaseStage = [bool]$BuildInstaller
    }

# Always verify on-disk stage matches manifest and HEAD cleanly
$verifiedStage = Assert-StageManifestMatchesDisk @stageVerifyParams
Write-Info "Verified stage custody: manifestId=$($verifiedStage.Manifest.manifestId) manifestSha256=$($verifiedStage.ManifestSha256)"

Write-Host ""
Write-Info "Staging complete: $publishDir"

# ---------------------------------------------------------------------------
# 3. Installer EXE compile (requires Inno Setup 6)
# ---------------------------------------------------------------------------
if (-not $BuildInstaller) {
    Write-Host ""
    Write-Host "BETA RC1 INSTALLER STAGE COMPLETE (staging only)"
    Write-Host "Source SHA : $headSha"
    Write-Host "Output root: $artifactRoot"
    Write-Host "Output src : $outputSource"
    Write-Host "Manifest   : $(Join-Path $publishDir 'salryn-installer-provenance-manifest.json')"
    Write-Host "App stage  : $publishDir"
    Write-Host "Setup dir  : $setupDir"
    Write-Host "Verify cmd : powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -VerifyStageOnly -OutputDir `"$artifactRoot`" -Version `"$Version`" -Configuration `"$Configuration`" -ExpectedSourceSha $headSha -ExpectedStageManifestSha256 $($verifiedStage.ManifestSha256)"
    Write-Host ""
    Write-Host "Installer EXE not compiled because -BuildInstaller was not requested."
    exit 0
}

Write-Info "Resolving Inno Setup 6 compiler (ISCC.exe)..."
$iscc = Resolve-IsccPath -ExplicitPath $InnoSetupCompilerPath

if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
    Write-ProvenanceFailure -Code "MISSING_WEBVIEW2_BOOTSTRAPPER" -HeadSha $headSha -Detail "WebView2 bootstrapper required for installer compile is missing: $webview2Bootstrapper"
}
Assert-NotReparsePoint -Path $webview2Bootstrapper -HeadSha $headSha -ContextLabel "webview2-bootstrapper"

$expectedRedistSha = [string]$verifiedStage.Manifest.redistributables[0].sha256
if ($redistSha256 -ne $expectedRedistSha) {
    Write-ProvenanceFailure -Code "REDIST_HASH_MISMATCH" -HeadSha $headSha -Detail "WebView2 bootstrapper SHA256 '$redistSha256' does not match manifest '$expectedRedistSha'."
}

[void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)
$setupExePath = Join-Path $setupDir "Salryn-POS-BetaRC1-Setup.exe"
$shaFile = "$setupExePath.sha256.txt"
$provenanceFile = "$setupExePath.provenance.json"

# Purge pre-existing installer output and custody files BEFORE compiling so stale EXEs can never be picked or left valid
Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots

# Re-verify stage immediately before ISCC invocation
$verifiedBeforeCompile = Assert-StageManifestMatchesDisk @stageVerifyParams

Write-Info "Found ISCC: $iscc"
Write-Info "Compiling $issScript into isolated output $setupDir..."
$isccVersion = ""
try {
    $isccVersion = [string]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($iscc).ProductVersion)
} catch {
    $isccVersion = ""
}

$compileStartUtc = (Get-Date).ToUniversalTime()
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMySourceRevision=$headSha" `
    "/DStagingDir=$publishDir" `
    "/DSetupOutputDir=$setupDir" `
    $issScript
if ($LASTEXITCODE -ne 0) {
    Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
    Write-ProvenanceFailure -Code "ISCC_COMPILE_FAILED" -HeadSha $headSha -Detail "ISCC failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $setupExePath)) {
    Write-ProvenanceFailure -Code "SETUP_EXE_NOT_PRODUCED" -HeadSha $headSha -Detail "ISCC exited 0 but expected setup EXE was not created at $setupExePath."
}
[void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)
Assert-NotReparsePoint -Path $setupExePath -HeadSha $headSha -ContextLabel "setup-exe"

    $setupItem = Get-Item -LiteralPath $setupExePath -Force
    if ($setupItem.Length -le 0) {
        Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
        Write-ProvenanceFailure -Code "SETUP_EXE_EMPTY" -HeadSha $headSha -Detail "Compiled setup EXE is zero bytes: $setupExePath"
    }
    if ($setupItem.LastWriteTimeUtc -lt $compileStartUtc.AddSeconds(-2)) {
        Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
        Write-ProvenanceFailure -Code "STALE_SETUP_EXE_DETECTED" -HeadSha $headSha -Detail "Setup EXE timestamp ($($setupItem.LastWriteTimeUtc.ToString('o'))) predates compile start ($($compileStartUtc.ToString('o')))."
    }

# Re-verify stage after ISCC completes to confirm staging did not drift mid-compile
$verifiedAfterCompile = Assert-StageManifestMatchesDisk @stageVerifyParams
if ($verifiedAfterCompile.ManifestSha256 -ne $verifiedBeforeCompile.ManifestSha256) {
    Write-ProvenanceFailure -Code "STAGE_CHANGED_DURING_COMPILE" -HeadSha $headSha -Detail "Stage manifest changed during ISCC execution."
}

$setupSha256 = Get-FileSha256Lower -FilePath $setupExePath
$setupBytes = [int64]$setupItem.Length
$compiledUtc = (Get-Date).ToUniversalTime().ToString("o")
$packedStatus = "PACKED_PAYLOAD_NOT_VERIFIED"

$shaTxtContent = (@(
    "File=$($setupItem.Name)"
    "Bytes=$setupBytes"
    "SHA256=$setupSha256"
    "Version=$Version"
    "Configuration=$Configuration"
    "ReleaseAuthority=$($verifiedAfterCompile.Manifest.releaseAuthority)"
    "SourceCommitSha=$headSha"
    "GitBranch=$($provenance.GitBranch)"
    "WorktreeClean=true"
    "ManifestId=$($verifiedAfterCompile.Manifest.manifestId)"
    "StageManifestSHA256=$($verifiedAfterCompile.ManifestSha256)"
    "PayloadFileCount=$($verifiedAfterCompile.Manifest.payloadFileCount)"
    "PayloadInventorySHA256=$($verifiedAfterCompile.PayloadInventorySha256)"
    "WebView2BootstrapperSHA256=$redistSha256"
    "PackedPayloadVerification=$packedStatus"
    "ComputedUtc=$compiledUtc"
) -join "`r`n") + "`r`n"
Write-Utf8NoBom -Path $shaFile -Content $shaTxtContent

$provenanceObject = [ordered]@{
    schemaVersion               = "1.0"
    product                     = "Salryn POS"
    publisher                   = "Kirjane Information Technology Services"
    setupFileName               = $setupItem.Name
    setupExeBytes               = $setupBytes
    setupExeSha256              = $setupSha256
    productVersion              = $Version
    configuration               = $Configuration
    releaseAuthority            = $verifiedAfterCompile.Manifest.releaseAuthority
    authoritativeInstallerAllowed = [bool]$verifiedAfterCompile.Manifest.authoritativeInstallerAllowed
    privatePerRunOutput         = $true
    defaultOutputLocationUsed   = $defaultOutputLocationUsed
    outputSource                = $outputSource
    parentAnchoredHandleCreation = $true
    aclProtectedAtCreation      = $true
    builtUtc                    = $compiledUtc
    sourceCommitSha             = $headSha
    gitBranch                   = $provenance.GitBranch
    detachedHead                = $provenance.DetachedHead
    worktreeClean               = $true
    manifestId                  = $verifiedAfterCompile.Manifest.manifestId
    stageManifestSha256         = $verifiedAfterCompile.ManifestSha256
    payloadFileCount            = $verifiedAfterCompile.Manifest.payloadFileCount
    payloadInventorySha256      = $verifiedAfterCompile.PayloadInventorySha256
    packageLockSha256           = $pkgLockSha
    npmInstallCommand           = $npmInstallCmd
    redistributableRelativePath = "installer/redist/MicrosoftEdgeWebview2Setup.exe"
    redistributableSha256       = $redistSha256
    isccVersion                 = $isccVersion
    packedPayloadVerification   = $packedStatus
    trustBoundaryNote           = "Clean Git HEAD, locked npm ci web dependencies, handle-anchored private per-run output creation with protected DACL at birth, binary ProductVersion (+SHA), full staging inventory hashes, and WebView2 bootstrapper SHA256 were verified immediately before and after ISCC.exe execution; packed bytes inside the compressed Inno Setup EXE were not extracted or independently inspected."
}
Write-Utf8NoBom -Path $provenanceFile -Content ($provenanceObject | ConvertTo-Json -Depth 6)

Write-Host ""
Write-Host "BETA RC1 INSTALLER EXE COMPILED (SOURCE-BOUND)"
Write-Host "Source SHA  : $headSha"
Write-Host "Output root : $artifactRoot"
Write-Host "Output src  : $outputSource"
Write-Host "Manifest ID : $($verifiedAfterCompile.Manifest.manifestId)"
Write-Host "Setup EXE   : $($setupItem.FullName)"
Write-Host "SHA-256     : $setupSha256"
Write-Host "SHA file    : $shaFile"
Write-Host "Provenance  : $provenanceFile"
Write-Host "App stage   : $publishDir"
Write-Host "Verify cmd  : powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -VerifyStageOnly -OutputDir `"$artifactRoot`" -Version `"$Version`" -Configuration `"$Configuration`" -ExpectedSourceSha $headSha -ExpectedStageManifestSha256 $($verifiedAfterCompile.ManifestSha256)"
Write-Host "Packed check: $packedStatus"

    exit 0
} catch {
    try {
        Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
    } catch {
        Write-Warning "Cleanup of stale setup artifacts on failure aborted: $_"
    }
    throw
} finally {
    if ($null -ne $pinnedOutputHandles) {
        for ($hi = $pinnedOutputHandles.Count - 1; $hi -ge 0; $hi--) {
            $h = $pinnedOutputHandles[$hi]
            if ($null -ne $h) {
                try { $h.Dispose() } catch {}
            }
        }
        $pinnedOutputHandles.Clear()
    }
}
