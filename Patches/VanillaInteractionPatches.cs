using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace VivantValley.Patches;

/// <summary>Narrow bridges for vanilla interactions which SMAPI doesn't expose as high-level events.</summary>
internal static class VanillaInteractionPatches
{
    private static ModEntry? owner;
    private static IMonitor? monitor;

    public static void Apply(ModEntry mod, string harmonyId)
    {
        owner = mod;
        monitor = mod.Monitor;
        var harmony = new Harmony(harmonyId + ".VanillaInteractions");
        int patchedMethods = 0;

        patchedMethods += PatchDeclaredOverrides(
            harmony,
            typeof(NPC),
            nameof(NPC.receiveGift),
            new[] { typeof(SObject), typeof(Farmer), typeof(bool), typeof(float), typeof(bool) },
            postfixName: nameof(ReceiveGiftPostfix));
        patchedMethods += PatchDeclaredOverrides(
            harmony,
            typeof(Dialogue),
            nameof(Dialogue.chooseResponse),
            new[] { typeof(Response) },
            prefixName: nameof(ChooseResponsePrefix));
        patchedMethods += PatchDeclaredOverrides(
            harmony,
            typeof(GameLocation),
            nameof(GameLocation.answerDialogue),
            new[] { typeof(Response) },
            prefixName: nameof(LocationAnswerPrefix));

        MethodInfo? eventAnswer = AccessTools.Method(
            typeof(Event),
            nameof(Event.answerDialogue),
            new[] { typeof(string), typeof(int) });
        if (eventAnswer is not null)
        {
            try
            {
                harmony.Patch(
                    eventAnswer,
                    prefix: new HarmonyMethod(typeof(VanillaInteractionPatches), nameof(EventAnswerPrefix)));
                patchedMethods++;
            }
            catch (Exception ex)
            {
                monitor.Log($"无法 Hook Event.answerDialogue，事件选项将只使用其他可用入口：{ex.Message}", LogLevel.Warn);
            }
        }

        if (patchedMethods == 0)
            monitor.Log("没有成功 Hook 原版互动方法；普通对话页面仍会记录，但送礼和选项事实可能缺失。", LogLevel.Error);
        else
            monitor.Log($"原版互动监听已启用，共 Hook {patchedMethods} 个方法。", LogLevel.Info);
    }

    private static int PatchDeclaredOverrides(
        Harmony harmony,
        Type baseType,
        string methodName,
        Type[] argumentTypes,
        string? prefixName = null,
        string? postfixName = null)
    {
        var patched = new HashSet<MethodBase>();
        foreach (Type type in GetLoadableTypes(baseType.Assembly)
                     .Where(type => !type.IsAbstract && baseType.IsAssignableFrom(type)))
        {
            MethodInfo? method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: argumentTypes,
                modifiers: null);
            if (method is null || !patched.Add(method))
                continue;

            try
            {
                harmony.Patch(
                    method,
                    prefix: prefixName is null ? null : new HarmonyMethod(typeof(VanillaInteractionPatches), prefixName),
                    postfix: postfixName is null ? null : new HarmonyMethod(typeof(VanillaInteractionPatches), postfixName));
            }
            catch (Exception ex)
            {
                patched.Remove(method);
                monitor?.Log($"无法 Hook {type.FullName}.{methodName}：{ex.Message}", LogLevel.Warn);
            }
        }

        return patched.Count;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            monitor?.Log("部分游戏类型无法反射加载，将继续 Hook 其余可用类型。", LogLevel.Warn);
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
    }

    private static void ReceiveGiftPostfix(NPC __instance, SObject __0, Farmer __1)
        => Safe(() => owner?.RecordVanillaGift(__instance, __0, __1));

    private static void ChooseResponsePrefix(Dialogue __instance, Response __0)
        => Safe(() => owner?.RecordVanillaDialogueChoice(__instance, __0));

    private static void LocationAnswerPrefix(GameLocation __instance, Response __0)
        => Safe(() => owner?.RecordVanillaLocationChoice(__instance, __0));

    private static void EventAnswerPrefix(Event __instance, string __0, int __1)
        => Safe(() => owner?.RecordVanillaEventChoice(__instance, __0, __1));

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            monitor?.Log($"捕获原版互动时发生错误，已跳过本次记录：{ex}", LogLevel.Error);
        }
    }
}
