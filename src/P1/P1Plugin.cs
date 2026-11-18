using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace DokisabaPrototype;

[BepInPlugin("local.dokisaba.prototype.p1", "Dokisaba P1 Nickname Prototype", "0.1.4")]
public sealed class P1Plugin : BasePlugin
{
    private const string ExpectedHash = "FFA76F32E04638CF4A117104B105FFC9F8B560F5C1A1269E739689A2D2FB133D";
    private const int GateRva = 0x1062626;
    private const int SearchRva = 0x10538A6;
    private static readonly byte[] ExpectedSearch = Convert.FromHexString("83F801742C");

    // For the verified locale enum, only Japanese (1) and Simplified Chinese (3) are odd.
    // Keep the existing relative branch destination: test al, 1; nop; jne +0x2c.
    private static readonly byte[] JapaneseSearch = Convert.FromHexString("A80190752C");
    private const uint PageExecuteReadWrite = 0x40;
    private static readonly byte[] Expected = Convert.FromHexString(
        "83B8DC000000017405418BCCEB07837F30020F95C1884F588B98DC000000");

    // The known scene enum is PlayerEdit=0, ScenarioGallery=1, MiniDrama=2.
    // For the five known locale values, odd means Japanese=1 or SimplifiedChinese=3.
    // locale + 2*scene <= 3 passes JP scenes 0/1 and SC scene 0.
    // EBX is left with the real locale just as in the original code.
    // ECX and EDX are overwritten by the following original instructions.
    private static readonly byte[] Gate = Convert.FromHexString(
        "8B98DC000000" + // mov ebx, [rax+0xdc]
        "8B5730" +       // mov edx, [rdi+0x30]
        "8D0C53" +       // lea ecx, [rbx+rdx*2]
        "83F903" +       // cmp ecx, 3
        "0F96C1" +       // setbe cl
        "20D9" +         // and cl, bl
        "884F58" +       // mov [rdi+0x58], cl
        "90909090909090");

    public override void Load()
    {
        if (!Config.Bind("Prototype", "P1Nickname", true, "Enable the original nickname picker in Simplified Chinese player editing.").Value)
            return;
        try
        {
            InstallGate();


            Log.LogInfo("P1 nickname permission installed: Simplified Chinese PlayerEdit; Japanese scenes preserved.");
        }
        catch (Exception error)
        {
            Log.LogError($"P1 unavailable: {error.Message}");
            return;
        }
        try { NicknameSearchTrace.Install(Log); }
        catch (Exception error)
        {
            Log.LogWarning($"P1.4 candidate fallback unavailable; nickname input remains enabled: {error.GetBaseException().Message}");
        }
    }

    private void InstallGate()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            Gate.Length != Expected.Length)
            throw new PlatformNotSupportedException("Expected the verified Windows x64 build.");

        var binary = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using (var stream = File.OpenRead(binary))
        using (var sha = SHA256.Create())
        {
            if (!string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), ExpectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected GameAssembly SHA256.");
        }

        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(m =>
            string.Equals(m.FileName, binary, StringComparison.OrdinalIgnoreCase));
        if (module.ModuleMemorySize < Math.Max(GateRva + Gate.Length, SearchRva + JapaneseSearch.Length))
            throw new InvalidOperationException("Nickname patch lies outside the loaded module.");

        var gateAddress = IntPtr.Add(module.BaseAddress, GateRva);
        var searchAddress = IntPtr.Add(module.BaseAddress, SearchRva);
        CheckBytes(gateAddress, Expected);
        CheckBytes(searchAddress, ExpectedSearch);
        WritePatch(process.Handle, gateAddress, Expected, Gate);
        try
        {
            WritePatch(process.Handle, searchAddress, ExpectedSearch, JapaneseSearch);
            try { NicknameKeyboard.Install(Log); }
            catch
            {
                new HarmonyLib.Harmony("local.dokisaba.prototype.p1.keyboard").UnpatchSelf();
                WritePatch(process.Handle, searchAddress, JapaneseSearch, ExpectedSearch);
                throw;
            }
        }
        catch
        {
            WritePatch(process.Handle, gateAddress, Gate, Expected);
            throw;
        }
    }

    private static void CheckBytes(IntPtr address, byte[] expected)
    {
        var current = new byte[expected.Length];
        Marshal.Copy(address, current, 0, current.Length);
        if (!current.SequenceEqual(expected))
            throw new InvalidOperationException("Original nickname instructions do not match.");
    }

    private static void WritePatch(IntPtr process, IntPtr address, byte[] expected, byte[] replacement)
    {
        CheckBytes(address, expected);
        var current = new byte[replacement.Length];
        var length = (UIntPtr)replacement.Length;
        if (!VirtualProtect(address, length, PageExecuteReadWrite, out var protection))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Marshal.Copy(replacement, 0, address, replacement.Length);
            Marshal.Copy(address, current, 0, current.Length);
            if (!current.SequenceEqual(replacement) || !FlushInstructionCache(process, address, length))
                throw new InvalidOperationException("Nickname gate writeback or instruction cache verification failed.");
        }
        catch
        {
            Marshal.Copy(expected, 0, address, expected.Length);
            FlushInstructionCache(process, address, length);
            throw;
        }
        finally
        {
            if (!VirtualProtect(address, length, protection, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}




