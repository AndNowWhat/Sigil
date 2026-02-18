using System;
using System.IO;

namespace Sigil.Services;

/// <summary>
/// Extracts an embedded PE (DLL or EXE) from an executable.
/// Use on loader.exe to see if the "agent" payload is embedded (second MZ/PE with DLL characteristics).
/// </summary>
public sealed class EmbeddedDllExtractor
{
    private const ushort ImageFileDll = 0x2000;
    private const uint PeSignature = 0x00004550; // "PE\0\0"

    /// <summary>
    /// Finds an embedded DLL in the exe and extracts it to a temp file.
    /// Skips the main exe at offset 0; looks for a second MZ that is a valid DLL PE.
    /// </summary>
    /// <param name="exePath">Full path to the loader exe (or any exe with an embedded DLL).</param>
    /// <returns>Path to the extracted DLL file, or null if no embedded DLL found.</returns>
    public string? ExtractToTempFile(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Exe path is required.", nameof(exePath));

        var fullPath = Path.GetFullPath(exePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Exe not found: {fullPath}");

        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 64)
            return null;

        var dllStart = FindEmbeddedDllOffset(bytes);
        if (dllStart < 0)
            return null;

        var dllSize = GetPeSize(bytes, dllStart);
        if (dllStart + dllSize > bytes.Length)
            return null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"Sigil_extracted_{Guid.NewGuid():N}.dll");
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            fs.Write(bytes, dllStart, dllSize);
        }

        return tempPath;
    }

    /// <summary>
    /// Returns the file offset of an embedded DLL (second MZ that is a valid DLL PE), or -1.
    /// </summary>
    private static int FindEmbeddedDllOffset(byte[] data)
    {
        for (var i = 0; i < data.Length - 64; i++)
        {
            if (data[i] != 0x4D || data[i + 1] != 0x5A) // "MZ"
                continue;

            var eLfanew = BitConverter.ToInt32(data, i + 0x3C);
            if (eLfanew <= 0 || i + eLfanew + 6 > data.Length)
                continue;

            var peSig = BitConverter.ToUInt32(data, i + eLfanew);
            if (peSig != PeSignature)
                continue;

            var coff = i + eLfanew + 4;
            var characteristics = BitConverter.ToUInt16(data, coff + 18);

            if ((characteristics & ImageFileDll) == 0)
                continue;

            return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the size in bytes of the PE image (from start of MZ to end of last section).
    /// </summary>
    private static int GetPeSize(byte[] data, int start)
    {
        var eLfanew = BitConverter.ToInt32(data, start + 0x3C);
        var coff = start + eLfanew + 4;
        var numberOfSections = BitConverter.ToUInt16(data, coff + 2);
        var sizeOfOptionalHeader = BitConverter.ToUInt16(data, coff + 16);

        var sectionTable = coff + 20 + sizeOfOptionalHeader;
        var maxEnd = 0;

        for (var s = 0; s < numberOfSections && sectionTable + 40 <= data.Length; s++, sectionTable += 40)
        {
            var pointerToRawData = BitConverter.ToInt32(data, sectionTable + 20);
            var sizeOfRawData = BitConverter.ToInt32(data, sectionTable + 16);
            var end = pointerToRawData + sizeOfRawData;
            if (end > maxEnd)
                maxEnd = end;
        }

        return maxEnd > 0 ? maxEnd : data.Length - start;
    }
}
