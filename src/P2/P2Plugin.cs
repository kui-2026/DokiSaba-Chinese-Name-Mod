using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace DokisabaPrototype;

[BepInPlugin("local.dokisaba.prototype.p2", "Dokisaba P2 Original EVS", "0.2.1")]
public sealed class P2Plugin : BasePlugin
{
    private const string GameAssemblyHash = "FFA76F32E04638CF4A117104B105FFC9F8B560F5C1A1269E739689A2D2FB133D";
    private const int ExplicitSheetEvsGate = 0x8F5CC0;
    private static readonly byte[] Original = Convert.FromHexString("83F8017542");
    // Known GameLocale enum: Japanese=1 and SimplifiedChinese=3. Both are odd.
    // Keep the original branch target and the preceding audio-enable check.
    // test al,1; nop; je +0x42 (skip original playback for other locales).
    private static readonly byte[] Replacement = Convert.FromHexString("A801907442");

    public override void Load()
    {
        if (!Config.Bind("Prototype", "P2OriginalEvs", true, "Enable original nickname EVS in Simplified Chinese.").Value)
            return;

        try
        {
            Install();
            Log.LogInfo("P2.1 explicit-sheet EVS language gate installed (Japanese and Simplified Chinese). Original playback runs without P2 Harmony detours.");
        }
        catch (Exception error)
        {
            Log.LogError($"P2 unavailable: {error.Message}");
            return;
        }
    }

    private static void Install()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The verified target is Windows x64.");
        var binary = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using (var stream = File.OpenRead(binary))
        using (var sha = SHA256.Create())
        {
            if (Convert.ToHexString(sha.ComputeHash(stream)) != GameAssemblyHash)
                throw new InvalidOperationException("Unexpected GameAssembly SHA256.");
        }

        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(m =>
            string.Equals(m.FileName, binary, StringComparison.OrdinalIgnoreCase));
        if (module.ModuleMemorySize < ExplicitSheetEvsGate + Original.Length)
            throw new InvalidOperationException("EVS gate outside GameAssembly.");
        var address = IntPtr.Add(module.BaseAddress, ExplicitSheetEvsGate);
        var current = new byte[Original.Length];
        Marshal.Copy(address, current, 0, current.Length);
        if (!current.SequenceEqual(Original)) throw new InvalidOperationException("EVS instructions do not match.");

        var size = (UIntPtr)Original.Length;
        if (!VirtualProtect(address, size, 0x40, out var protection))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Marshal.Copy(Replacement, 0, address, Replacement.Length);
            Marshal.Copy(address, current, 0, current.Length);
            if (!current.SequenceEqual(Replacement) || !FlushInstructionCache(process.Handle, address, size))
                throw new InvalidOperationException("EVS write or cache verification failed.");
        }
        catch
        {
            Marshal.Copy(Original, 0, address, Original.Length);
            FlushInstructionCache(process.Handle, address, size);
            throw;
        }
        finally
        {
            if (!VirtualProtect(address, size, protection, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}
