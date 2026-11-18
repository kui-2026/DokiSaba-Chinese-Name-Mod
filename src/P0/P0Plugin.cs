using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace DokisabaPrototype;

[BepInPlugin("local.dokisaba.prototype.p0only", "Dokisaba P0 Input Prototype", "0.4.0")]
public sealed class P0Plugin : BasePlugin
{
    public override void Load()
    {
        if (!Config.Bind("Prototype", "P0Input", false, "Enable localized name input.").Value)
            return;
        try
        {
            using var binary = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            using var sha = SHA256.Create();
            var digest = Convert.ToHexString(sha.ComputeHash(binary));
            if (!string.Equals(digest, "FFA76F32E04638CF4A117104B105FFC9F8B560F5C1A1269E739689A2D2FB133D", StringComparison.OrdinalIgnoreCase))
            {
                Log.LogWarning("P0 disabled: unexpected GameAssembly SHA256.");
                return;
            }

            P0_Input_Prototype.Install(Log);
        }
        catch (Exception error)
        {
            Log.LogError($"P0 unavailable: {error.Message}");
        }
    }
}




