using System;
using System.Linq;
using System.Reflection;
using App.Game.Input;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace DokisabaPrototype;

internal static class P0_Input_Prototype
{
    private static ManualLogSource log = null!;
    private static PropertyInfo locale = null!;
    private static PropertyInfo surname = null!;
    private static PropertyInfo givenName = null!;
    private static PropertyInfo baseInput = null!;
    private static PropertyInfo mode = null!;
    private static PropertyInfo regex = null!;
    private static PropertyInfo header = null!;
    private static PropertyInfo reset = null!;
    private static MethodInfo getComponentInParent = null!;
    private static MethodInfo configure = null!;
    private static MethodInfo il2cppTypeFrom = null!;
    private static ConstructorInfo wrapFocus = null!;
    private static Type focusType = null!;
    private static PropertyInfo tmpText = null!;
    private static IntPtr surnameTarget;
    private static IntPtr givenNameTarget;

    internal static void Install(ManualLogSource logger)
    {
        log = logger;
        var game = typeof(SoftwareKeyboardMode).Assembly;
        var editor = game.GetType("App.Game.InGame.PlayerEdit.PlayerEditNameInput", true)!;
        var scene = game.GetType("App.Game.InGame.PlayerEdit.NameEditScene", true)!;
        var focus = game.GetType("App.Game.UI.Components.FocusableInputField", true)!;
        var tmp = Assembly.Load("Unity.TextMeshPro").GetType("TMPro.TMP_InputField", true)!;
        var component = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Component", true)!;
        var awake = Method(editor, "Awake", typeof(void), scene);
        configure = Method(focus, "SetSoftwareKeyboardConfig", typeof(void),
            typeof(SoftwareKeyboardMode), typeof(string), typeof(string));

        locale = Property(editor, "_gameLocale",
            Assembly.Load("Table").GetType("Table.GAMELOCALE+Defs", true)!);
        surname = Property(editor, "_surInput", tmp);
        givenName = Property(editor, "_nameInput", tmp);
        baseInput = Property(focus, "BaseInputField", tmp);
        mode = Property(focus, "_softwareKeyboardMode", typeof(SoftwareKeyboardMode));
        regex = Property(focus, "_validateRegex");
        header = Property(focus, "_softwareHeaderText", typeof(string));
        reset = Property(focus, "_initResetText", typeof(string));
        focusType = focus;
        wrapFocus = focus.GetConstructor(new[] { typeof(IntPtr) })
            ?? throw new MissingMethodException(focus.FullName, ".ctor(IntPtr)");
        var interop = typeof(Il2CppObjectBase).Assembly.GetType("Il2CppInterop.Runtime.Il2CppType", true)!;
        il2cppTypeFrom = interop.GetMethod("From", new[] { typeof(Type) })
            ?? throw new MissingMethodException(interop.FullName, "From(Type)");
        getComponentInParent = Method(component, "GetComponentInParent",
            component, il2cppTypeFrom.ReturnType, typeof(bool));
        tmpText = Property(tmp, "text", typeof(string));
        var harmony = new Harmony("local.dokisaba.prototype.p0");
        harmony.Patch(awake, postfix: new HarmonyMethod(typeof(P0_Input_Prototype), nameof(AfterAwake)));
        log.LogInfo($"P0.4 Awake postfix registered={Harmony.GetPatchInfo(awake)?.Owners.Contains(harmony.Id) == true}");
        try
        {
            var append = Method(tmp, "Append", typeof(void), typeof(string));
            var diagnostic = new Harmony("local.dokisaba.prototype.p0.paste");
            diagnostic.Patch(append,
                prefix: new HarmonyMethod(typeof(P0_Input_Prototype), nameof(BeforeAppend)),
                postfix: new HarmonyMethod(typeof(P0_Input_Prototype), nameof(AfterAppend)));
            log.LogInfo($"P0.4 Append(string) diagnostic registered={Harmony.GetPatchInfo(append)?.Owners.Contains(diagnostic.Id) == true}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 paste diagnostic unavailable: {error.GetBaseException().GetType().Name}");
        }
    }

    private static void AfterAwake(object __instance)
    {
        try
        {
            surnameTarget = givenNameTarget = IntPtr.Zero;
            var currentLocale = Convert.ToInt32(locale.GetValue(__instance));
            if (currentLocale != 3) return;
            Apply(__instance, surname, "surname", currentLocale);
            Apply(__instance, givenName, "givenName", currentLocale);
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 Awake failed: {error.GetBaseException().Message}");
        }
    }

    private static void Apply(object editor, PropertyInfo inputProperty, string field, int currentLocale)
    {
        try
        {
            var input = inputProperty.GetValue(editor);
            var identity = Pointer(input);
            var nativeType = il2cppTypeFrom.Invoke(null, new object[] { focusType });
            var component = identity == IntPtr.Zero || Pointer(nativeType) == IntPtr.Zero ? null :
                getComponentInParent.Invoke(input, new[] { nativeType, (object)true });
            var address = Pointer(component);
            var focus = address == IntPtr.Zero ? null : wrapFocus.Invoke(new object[] { address });
            if (focus is null || Pointer(baseInput.GetValue(focus)) != identity)
            {
                log.LogWarning($"P0.4 locale={currentLocale} field={field} wrapper match=False");
                return;
            }

            if (field == "surname") surnameTarget = identity;
            else givenNameTarget = identity;
            var before = (SoftwareKeyboardMode)mode.GetValue(focus)!;
            var reapplied = before == SoftwareKeyboardMode.Alphabet;
            if (reapplied)
                configure.Invoke(focus, new object?[] { SoftwareKeyboardMode.Default, header.GetValue(focus), reset.GetValue(focus) });
            var after = (SoftwareKeyboardMode)mode.GetValue(focus)!;
            var hasRegex = Pointer(regex.GetValue(focus)) != IntPtr.Zero;
            LogInputProperties(input!, field);
            log.LogInfo($"P0.4 locale={currentLocale} field={field} reapplied={reapplied} mode={(int)before}->{(int)after} regexPresent={hasRegex}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 locale={currentLocale} field={field} failed: {error.GetBaseException().Message}");
        }
    }

    private static void LogInputProperties(object input, string field)
    {
        try
        {
            object? Read(string property) => input.GetType().GetProperty(property)?.GetValue(input);
            var validator = Read("inputValidator");
            var callback = Read("onValidateInput");
            var target = callback?.GetType().GetProperty("Target")?.GetValue(callback);
            var method = callback?.GetType().GetProperty("Method")?.GetValue(callback);
            var methodName = method?.GetType().GetProperty("Name")?.GetValue(method);
            log.LogInfo($"P0.4 TMP field={field} characterValidation={Read("characterValidation")} contentType={Read("contentType")} inputType={Read("inputType")} keyboardType={Read("keyboardType")} validator={validator?.GetType().FullName ?? "none"} callback={callback?.GetType().FullName ?? "none"} callbackTarget={target?.GetType().FullName ?? "unknown"} callbackMethod={methodName ?? "unknown"} characterLimit={Read("characterLimit")} readOnly={Read("readOnly")} interactable={Read("interactable")}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 TMP snapshot failed field={field}: {error.GetBaseException().GetType().Name}");
        }
    }
    private static void BeforeAppend(object __instance, string __0,
        out (string Field, string Codes, string Probe, string Before)? __state)
    {
        __state = null;
        var address = Pointer(__instance);
        if (address == IntPtr.Zero || (address != surnameTarget && address != givenNameTarget)) return;
        // Limit diagnostics to the agreed test probes; never record other clipboard input.
        var codes = __0 switch
        {
            "水" => "U+6C34",
            "水無月" => "U+6C34 U+7121 U+6708",
            "ABC123" => "ASCII control probe",
            _ => null
        };
        if (codes is null) return;
        try
        {
            __state = (address == surnameTarget ? "surname" : "givenName", codes, __0,
                (string?)tmpText.GetValue(__instance) ?? "");
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 paste pre-read failed: {error.GetBaseException().GetType().Name}");
        }
    }

    private static void AfterAppend(object __instance, (string Field, string Codes, string Probe, string Before)? __state)
    {
        if (__state is not { } trace) return;
        try
        {
            var after = (string?)tmpText.GetValue(__instance) ?? "";
            var included = after.Contains(trace.Probe, StringComparison.Ordinal);
            log.LogInfo($"P0.4 Append(string) field={trace.Field} probe={trace.Codes} beforeLength={trace.Before.Length} afterLength={after.Length} changed={after != trace.Before} probePresent={included}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P0.4 paste post-read failed: {error.GetBaseException().GetType().Name}");
        }
    }
    private static IntPtr Pointer(object? value) =>
        value is Il2CppObjectBase wrapper ? wrapper.Pointer : IntPtr.Zero;

    private static MethodInfo Method(Type owner, string name, Type result, params Type[] args)
    {
        var method = AccessTools.DeclaredMethod(owner, name, args);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new MissingMethodException(owner.FullName, name);
        return method;
    }

    private static PropertyInfo Property(Type owner, string name, Type? expected = null)
    {
        var property = AccessTools.DeclaredProperty(owner, name);
        if (property?.GetMethod is null || (expected is not null && property.PropertyType != expected))
            throw new MissingMemberException(owner.FullName, name);
        return property;
    }
}

