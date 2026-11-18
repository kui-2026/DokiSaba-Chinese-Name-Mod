using System;
using System.Linq;
using System.Reflection;
using App.Game.Input;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace DokisabaPrototype;

internal static class NicknameKeyboard
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
        surname = Property(editor, "_surNickNameInput", tmp);
        givenName = Property(editor, "_nameNickNameInput", tmp);
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
        var harmony = new Harmony("local.dokisaba.prototype.p1.keyboard");
        harmony.Patch(awake, postfix: new HarmonyMethod(typeof(NicknameKeyboard), nameof(AfterAwake)));
        log.LogInfo($"P1.1 Awake postfix registered={Harmony.GetPatchInfo(awake)?.Owners.Contains(harmony.Id) == true}");
    }

    private static void AfterAwake(object __instance, App.Game.InGame.PlayerEdit.NameEditScene __0)
    {
        try
        {
            var currentLocale = Convert.ToInt32(locale.GetValue(__instance));
            if (currentLocale != 3 || (int)__0 != 0) return;
            Apply(__instance, surname, "surname", currentLocale);
            Apply(__instance, givenName, "givenName", currentLocale);
        }
        catch (Exception error)
        {
            log.LogWarning($"P1.1 Awake failed: {error.GetBaseException().Message}");
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
                log.LogWarning($"P1.1 locale={currentLocale} field={field} wrapper match=False");
                return;
            }

            var before = (SoftwareKeyboardMode)mode.GetValue(focus)!;
            var reapplied = before == SoftwareKeyboardMode.Alphabet;
            if (reapplied)
                configure.Invoke(focus, new object?[] { SoftwareKeyboardMode.KanaOnly, header.GetValue(focus), reset.GetValue(focus) });
            var after = (SoftwareKeyboardMode)mode.GetValue(focus)!;
            var hasRegex = Pointer(regex.GetValue(focus)) != IntPtr.Zero;
            log.LogInfo($"P1.1 locale={currentLocale} field={field} reapplied={reapplied} mode={(int)before}->{(int)after} regexPresent={hasRegex}");
        }
        catch (Exception error)
        {
            log.LogWarning($"P1.1 locale={currentLocale} field={field} failed: {error.GetBaseException().Message}");
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


