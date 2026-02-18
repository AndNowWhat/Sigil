using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sigil.Services;

/// <summary>
/// Injects the agent DLL into a target process. Two options:
/// 1. LoadLibrary inject: write DLL to temp file, then CreateRemoteThread(LoadLibraryA, path). Simple; works if the DLL has a normal DllMain.
/// 2. Manual map: allocate in target, copy PE, relocate, resolve imports, TLS, SEH, call entry. Required if the loader uses manual map only (no file on disk).
/// This class implements (1). For (2) see docs/LOADER_EXTRACTED_STRINGS.md and existing C++ manual mappers.
/// </summary>
public sealed class InjectionService
{
    private const uint ProcessAllAccess = 0x001F0FFF;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint MemRelease = 0x8000;
    private const uint Th32csSnapModule = 0x00000008;

    /// <summary>
    /// Injects a DLL into the target process using LoadLibrary. The DLL must be on disk (e.g. temp file).
    /// </summary>
    /// <param name="processId">Target process ID (e.g. game client).</param>
    /// <param name="dllPath">Full path to the DLL file. Must be accessible from the target process (e.g. under ProgramData or temp).</param>
    public void InjectDllByLoadLibrary(int processId, string dllPath)
    {
        if (processId <= 0)
            throw new ArgumentException("Invalid process ID.", nameof(processId));
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            throw new FileNotFoundException("DLL file not found.", dllPath);

        var dllPathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");
        IntPtr hProcess = IntPtr.Zero;
        IntPtr pathAlloc = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(ProcessAllAccess, false, processId);
            if (hProcess == IntPtr.Zero)
                throw new InvalidOperationException($"OpenProcess failed for PID {processId}. Error: {Marshal.GetLastWin32Error()}");

            pathAlloc = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllPathBytes.Length, MemCommit | MemReserve, PageReadWrite);
            if (pathAlloc == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAllocEx failed. Error: " + Marshal.GetLastWin32Error());

            if (!WriteProcessMemory(hProcess, pathAlloc, dllPathBytes, (uint)dllPathBytes.Length, out _))
                throw new InvalidOperationException("WriteProcessMemory failed. Error: " + Marshal.GetLastWin32Error());

            IntPtr loadLibraryAddr = GetRemoteProcAddress(processId, "kernel32.dll", "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
                throw new InvalidOperationException("Could not get LoadLibraryA address in target process.");

            hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, pathAlloc, 0, out _);
            if (hThread == IntPtr.Zero)
                throw new InvalidOperationException("CreateRemoteThread failed. Error: " + Marshal.GetLastWin32Error());

            if (WaitForSingleObject(hThread, 15000) != 0) // WAIT_OBJECT_0 = 0
                throw new InvalidOperationException("Remote thread did not complete in time.");
        }
        finally
        {
            if (pathAlloc != IntPtr.Zero && hProcess != IntPtr.Zero)
                VirtualFreeEx(hProcess, pathAlloc, UIntPtr.Zero, MemRelease);
            if (hThread != IntPtr.Zero)
                CloseHandle(hThread);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Gets the address of a function in the target process (e.g. LoadLibraryA in kernel32).
    /// </summary>
    private static IntPtr GetRemoteProcAddress(int processId, string moduleName, string procName)
    {
        IntPtr localModule = GetModuleHandle(moduleName);
        if (localModule == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr localProc = GetProcAddress(localModule, procName);
        if (localProc == IntPtr.Zero)
            return IntPtr.Zero;
        long offset = localProc.ToInt64() - localModule.ToInt64();

        IntPtr remoteModuleBase = GetRemoteModuleBase(processId, moduleName);
        if (remoteModuleBase == IntPtr.Zero)
            return IntPtr.Zero;
        return new IntPtr(remoteModuleBase.ToInt64() + offset);
    }

    private static IntPtr GetRemoteModuleBase(int processId, string moduleName)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapModule, (uint)processId);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return IntPtr.Zero;
        try
        {
            var me = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            if (!Module32First(snapshot, ref me))
                return IntPtr.Zero;
            do
            {
                var name = me.szModule.TrimEnd('\0');
                if (string.Equals(name, moduleName, StringComparison.OrdinalIgnoreCase))
                    return me.modBaseAddr;
            } while (Module32Next(snapshot, ref me));
            return IntPtr.Zero;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }
}
