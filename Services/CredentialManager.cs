using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sigil.Services;

internal static class CredentialManager
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    public static void WriteSecret(string target, string secret)
    {
        var secretBytes = Encoding.Unicode.GetBytes(secret);
        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = target,
            CredentialBlobSize = (uint)secretBytes.Length,
            CredentialBlob = Marshal.StringToCoTaskMemUni(secret),
            Persist = CRED_PERSIST_LOCAL_MACHINE,
            AttributeCount = 0,
            Attributes = IntPtr.Zero,
            UserName = Environment.UserName,
            TargetAlias = string.Empty,
            Comment = "Sigil stored token"
        };

        try
        {
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException("Failed to write credential.");
            }
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(credential.CredentialBlob);
            }
        }
    }

    public static string? ReadSecret(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public static void DeleteSecret(string target)
    {
        CredDelete(target, CRED_TYPE_GENERIC, 0);
    }
}
