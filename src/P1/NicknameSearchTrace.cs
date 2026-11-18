using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using App.Game.Input;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Msg;

namespace DokisabaPrototype;

internal static class NicknameSearchTrace
{
    [ThreadStatic] private static Search? active;
    private static ManualLogSource log = null!;
    private static PropertyInfo selectedLocale = null!;
    private static MethodInfo toGameLocale = null!;
    private static Dictionary<(int Type, int Id), string> originalKana = null!;
    private static bool traceBinding;

    private sealed class Search
    {
        internal int Fallbacks;
        internal int Missing;
        internal string Query = "";
    }

    internal static void Install(ManualLogSource logger)
    {
        log = logger;
        originalKana = ReadOriginalKana();
        var game = typeof(SoftwareKeyboardMode).Assembly;
        var selection = game.GetType("App.Game.InGame.PlayerEdit.NickNameSelection", true)!;
        var table = Assembly.Load("Table");
        var title = table.GetType("Table.GAMETITLE+Defs", true)!;
        var kind = table.GetType("Table.NICKNAMEVOICE+Defs", true)!;
        var getItems = AccessTools.DeclaredMethod(selection, "GetItems", new[] { typeof(string), title, kind });
        if (getItems is null || !getItems.IsStatic || getItems.ReturnType.IsValueType)
            throw new MissingMethodException(selection.FullName, "static GetItems(string, GAMETITLE.Defs, NICKNAMEVOICE.Defs)");

        var messages = typeof(MessageIDPair).Assembly;
        var manager = messages.GetType("Msg.MessageManager", true)!;
        var getMessage = AccessTools.DeclaredMethod(manager, "GetMessage", new[]
        {
            typeof(MessageIDPair), typeof(bool), messages.GetType("Msg.IReadOnlyMessageParameter", true)!
        }) ?? throw new MissingMethodException(manager.FullName, "GetMessage(MessageIDPair,...)");
        if (getMessage.ReturnType != typeof(string)) throw new InvalidOperationException("Unexpected message return type.");

        var settings = Assembly.Load("Unity.Localization")
            .GetType("UnityEngine.Localization.Settings.LocalizationSettings", true)!;
        selectedLocale = settings.GetProperty("SelectedLocale", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(settings.FullName, "SelectedLocale");
        var extension = game.GetType("App.Game.Localization.LocaleExtension", true)!;
        toGameLocale = AccessTools.DeclaredMethod(extension, "ToGameLocale", new[] { selectedLocale.PropertyType })
            ?? throw new MissingMethodException(extension.FullName, "ToGameLocale(Locale)");

        var harmony = new Harmony("local.dokisaba.prototype.p1.searchtrace");
        try
        {
            harmony.Patch(getItems, prefix: Patch(nameof(Begin)), postfix: Patch(nameof(Complete)),
                finalizer: Patch(nameof(End)));
            harmony.Patch(getMessage, postfix: Patch(nameof(FillMissingCandidate)));
            log.LogInfo($"P1.4 original kana fallback installed: {originalKana.Count} message keys.");
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
        // Button binding is observational. Its failure cannot undo the fallback.
        try
        {
            var setItem = selection.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(m => m.Name == "SetItem" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 3);
            var bindingTrace = new Harmony("local.dokisaba.prototype.p1.bindingtrace");
            bindingTrace.Patch(setItem, postfix: Patch(nameof(AfterBind)));
        }
        catch (Exception error)
        {
            log.LogWarning($"P1.4 binding trace unavailable: {error.GetBaseException().Message}");
        }
    }

    private static Dictionary<(int Type, int Id), string> ReadOriginalKana()
    {
        var path = Path.Combine(Paths.GameRootPath, "Dokisaba_Data", "StreamingAssets", "dbdata", "NickNameList.bytes");
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != "DBC9723B8576556F15D551738DB07B411B7FEEB012A666A7F1C11D327285BDDA")
            throw new InvalidDataException("Unexpected original nickname table.");
        var utf8 = new UTF8Encoding(false, true);
        var rows = new Dictionary<(int, int), string>();
        for (var i = 0; i < 1553; i++)
        {
            var row = 32096 + i * 32;
            var title = BitConverter.ToInt32(bytes, row + 4);
            var offset = BitConverter.ToInt32(bytes, row + 12);
            var id = BitConverter.ToInt32(bytes, row + 20);
            // Mirrors the original searcher's message type mapping for these two titles.
            var type = title == 1 ? 1 : title == 2 ? 3 : 0;
            if (type == 0 || id <= 0) continue;
            var end = Array.IndexOf(bytes, (byte)0, offset);
            if (offset < 64 || end <= offset || end >= 32085)
                throw new InvalidDataException("Invalid original nickname string range.");
            var kana = utf8.GetString(bytes, offset, end - offset);
            if (rows.TryGetValue((type, id), out var previous) && previous != kana)
                throw new InvalidDataException("Conflicting original nickname message key.");
            rows[(type, id)] = kana;
        }
        return rows;
    }

    private static HarmonyMethod Patch(string name) => new(typeof(NicknameSearchTrace), name);

    private static void Begin(string __0, out Search? __state)
    {
        __state = active;
        active = null;
        traceBinding = false;
        try
        {
            var locale = selectedLocale.GetValue(null);
            if (locale is null || Convert.ToInt32(toGameLocale.Invoke(null, new[] { locale })) != 3)
                return;
            active = new Search { Query = __0 ?? "" };
            var preview = string.Concat(active.Query.Take(32).Select(c => char.IsControl(c) ? ' ' : c));
            log.LogInfo($"P1.4 GetItems entered locale=3 route=JP windowOpenPath=True queryLength={active.Query.Length} query={preview}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P1.4 search scope failed: {error.GetBaseException().GetType().Name}");
        }
    }

    private static void FillMissingCandidate(MessageIDPair __0, ref string? __result)
    {
        if (active is not { } search || !string.IsNullOrEmpty(__result)) return;
        try
        {
            var key = ((int)__0.MessageType, __0.ID);
            if (originalKana.TryGetValue(key, out var kana))
            {
                __result = kana;
                search.Fallbacks++;
            }
            else
            {
                search.Missing++;
            }
        }
        catch (Exception error)
        {
            search.Missing++;
            log.LogWarning($"P1.4 candidate fallback failed: {error.GetBaseException().GetType().Name}");
        }
    }

    private static void Complete(object? __result)
    {
        if (active is not { } search) return;
        var count = "unknown";
        try
        {
            count = __result?.GetType().GetProperty("Count")?.GetValue(__result)?.ToString() ?? count;
        }
        catch (Exception) { /* Runtime wrapper may expose only the enumerable interface. */ }
        traceBinding = __result is not null;
        log.LogInfo($"P1.4 GetItems returned candidates={count} fallback={search.Fallbacks} unmapped={search.Missing}");
    }

    private static void End(Exception? __exception, Search? __state)
    {
        var search = active;
        active = __state;
        if (__exception is not null && search is not null)
            log.LogWarning($"P1.4 GetItems failed type={__exception.GetType().Name} fallback={search.Fallbacks} unmapped={search.Missing}");
    }

    private static void AfterBind()
    {
        if (!traceBinding) return;
        traceBinding = false;
        log.LogInfo("P1.4 first candidate button bound.");
    }
}


