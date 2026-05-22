using System;
using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace DrawableSuits;

[HarmonyPatch(typeof(StartOfRound))]
internal static class StartOfRoundPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("LoadUnlockables")]
    private static void LoadUnlockablesPostfix()
    {
        RunPatch("StartOfRound.LoadUnlockables", () =>
        {
            DrawableSuitsPlugin.Registry?.ReapplyAll();
            SessionSafetyGuard.Run("StartOfRound.LoadUnlockables", true);
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch("PositionSuitsOnRack")]
    private static void PositionSuitsOnRackPostfix()
    {
        RunPatch("StartOfRound.PositionSuitsOnRack", () => DrawableSuitsPlugin.Registry?.ReapplyAll());
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(StartOfRound.SyncSuitsClientRpc))]
    private static void SyncSuitsClientRpcPostfix()
    {
        RunPatch("StartOfRound.SyncSuitsClientRpc", () => DrawableSuitsPlugin.Registry?.ReapplyAll());
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnPlayerConnectedClientRpc")]
    private static void OnPlayerConnectedClientRpcPostfix()
    {
        RunPatch("StartOfRound.OnPlayerConnectedClientRpc", () =>
        {
            DrawableSuitsPlugin.Registry?.ReapplyAll();
            SessionSafetyGuard.Run("StartOfRound.OnPlayerConnectedClientRpc", true);
            DrawableSuitsPlugin.Sync?.RequestActiveDesigns();
        });
    }

    private static void RunPatch(string context, Action action)
    {
        try
        {
            DrawableSuitsPlugin.EnsureRuntimeReady(context);
            action();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Patch failed: {context}", ex);
        }
    }
}

[HarmonyPatch(typeof(UnlockableSuit))]
internal static class UnlockableSuitPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(UnlockableSuit.SwitchSuitForPlayer))]
    private static void SwitchSuitForPlayerPostfix(PlayerControllerB player)
    {
        RunPatch("UnlockableSuit.SwitchSuitForPlayer", () => DrawableSuitsPlugin.Registry?.ApplyToPlayer(player));
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(UnlockableSuit.SwitchSuitForAllPlayers))]
    private static void SwitchSuitForAllPlayersPostfix()
    {
        RunPatch("UnlockableSuit.SwitchSuitForAllPlayers", () => DrawableSuitsPlugin.Registry?.ReapplyAll());
    }

    private static void RunPatch(string context, Action action)
    {
        try
        {
            DrawableSuitsPlugin.EnsureRuntimeReady(context);
            action();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Patch failed: {context}", ex);
        }
    }
}

[HarmonyPatch(typeof(PlayerControllerB))]
internal static class PlayerControllerBPatches
{
    private static readonly string[] OptionalBlockedInputMethods =
    {
        "ScrollMouse_performed",
        "ScrollMouse_canceled",
        "SwitchItem_performed",
        "SwitchItem_canceled",
        "NextItem_performed",
        "PreviousItem_performed",
        "Scan_performed",
        "PingScan_performed",
        "ItemPrimaryUse_performed",
        "ItemInteract_performed",
        "ItemSecondaryUse_performed",
        "ItemTertiaryUse_performed",
        "ActivateItem_performed",
        "ActivateItem_canceled",
        "Discard_performed",
        "Crouch_performed",
        "InspectItem_performed",
        "UseUtilitySlot_performed",
        "Emote1_performed",
        "Emote2_performed",
        "SetFreeCamera_performed",
        "SpeedCheat_performed",
        "BuildMode_performed",
        "ConfirmBuildMode_performed",
        "Delete_performed"
    };

    private static readonly HashSet<string> OptionalPatchedMethods = new(StringComparer.Ordinal);
    private static float _lastBlockedInputLogTime;
    private static string _lastBlockedInputMethod;

    internal static void ApplyOptionalGameplayInputPatches(Harmony harmony)
    {
        if (harmony == null)
        {
            return;
        }

        var prefix = AccessTools.Method(typeof(PlayerControllerBPatches), nameof(OptionalGameplayInputPrefix));
        if (prefix == null)
        {
            DrawableSuitsDiagnostics.Warn("Optional gameplay input prefix was not found; scan/inventory fallback patches were not applied.");
            return;
        }

        var patched = new List<string>();
        var missing = new List<string>();
        for (var i = 0; i < OptionalBlockedInputMethods.Length; i++)
        {
            var methodName = OptionalBlockedInputMethods[i];
            if (OptionalPatchedMethods.Contains(methodName))
            {
                continue;
            }

            MethodInfo method = null;
            try
            {
                method = AccessTools.Method(typeof(PlayerControllerB), methodName);
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Optional PlayerControllerB input method lookup failed for {methodName}", ex);
                missing.Add(methodName);
                continue;
            }

            if (method == null)
            {
                missing.Add(methodName);
                continue;
            }

            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                OptionalPatchedMethods.Add(methodName);
                patched.Add(methodName);
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Failed to apply optional PlayerControllerB input block for {methodName}", ex);
            }
        }

        DrawableSuitsDiagnostics.Info($"Optional gameplay input patches applied. patched=[{string.Join(", ", patched)}]; missing=[{string.Join(", ", missing)}]");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.ConnectClientToPlayerObject))]
    private static void ConnectClientToPlayerObjectPostfix(PlayerControllerB __instance)
    {
        RunPatch("PlayerControllerB.ConnectClientToPlayerObject", () =>
        {
            DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance);
            SessionSafetyGuard.Run("PlayerControllerB.ConnectClientToPlayerObject", true);
            DrawableSuitsPlugin.Sync?.RequestActiveDesigns();
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.SpawnPlayerAnimation))]
    private static void SpawnPlayerAnimationPostfix(PlayerControllerB __instance)
    {
        RunPatch("PlayerControllerB.SpawnPlayerAnimation", () =>
        {
            DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance);
            SessionSafetyGuard.Run("PlayerControllerB.SpawnPlayerAnimation", true);
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch("Jump_performed")]
    private static bool JumpPerformedPrefix(PlayerControllerB __instance)
    {
        return AllowGameplayInput("Jump_performed", __instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch("Interact_performed")]
    private static bool InteractPerformedPrefix(PlayerControllerB __instance)
    {
        return AllowGameplayInput("Interact_performed", __instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch("QEItemInteract_performed")]
    private static bool QeItemInteractPerformedPrefix(PlayerControllerB __instance)
    {
        return AllowGameplayInput("QEItemInteract_performed", __instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch("Look_performed")]
    private static bool LookPerformedPrefix(PlayerControllerB __instance)
    {
        return AllowGameplayInput("Look_performed", __instance);
    }

    private static bool OptionalGameplayInputPrefix(PlayerControllerB __instance, MethodBase __originalMethod)
    {
        return AllowGameplayInput(__originalMethod?.Name ?? "OptionalGameplayInput", __instance);
    }

    private static void RunPatch(string context, Action action)
    {
        try
        {
            DrawableSuitsPlugin.EnsureRuntimeReady(context);
            action();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Patch failed: {context}", ex);
        }
    }

    private static bool AllowGameplayInput(string method, PlayerControllerB player)
    {
        if (!DrawableSuitsPlugin.IsEditorOpen)
        {
            return true;
        }

        var shouldLog = Time.unscaledTime - _lastBlockedInputLogTime > 0.75f
            || !string.Equals(_lastBlockedInputMethod, method, StringComparison.Ordinal);
        if (shouldLog)
        {
            _lastBlockedInputLogTime = Time.unscaledTime;
            _lastBlockedInputMethod = method;
            DrawableSuitsDiagnostics.Info($"Blocked PlayerControllerB.{method} while DrawableSuits editor is open. player={DrawableSuitsPlugin.DescribeUnityObject(player)}");
        }

        return false;
    }
}

[HarmonyPatch(typeof(QuickMenuManager))]
internal static class QuickMenuManagerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    private static void StartPostfix(QuickMenuManager __instance)
    {
        RunPatch("QuickMenuManager.Start", () => PauseMenuButtonInjector.EnsureButton(__instance));
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(QuickMenuManager.OpenQuickMenu))]
    private static void OpenQuickMenuPostfix(QuickMenuManager __instance)
    {
        RunPatch("QuickMenuManager.OpenQuickMenu", () =>
        {
            PauseMenuButtonInjector.EnsureButton(__instance);
            PauseMenuButtonInjector.SelectIfNeeded(__instance);
        });
    }

    private static void RunPatch(string context, Action action)
    {
        try
        {
            DrawableSuitsPlugin.EnsureRuntimeReady(context);
            action();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Patch failed: {context}", ex);
        }
    }
}
