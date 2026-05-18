using System;
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
        RunPatch("StartOfRound.LoadUnlockables", () => DrawableSuitsPlugin.Registry?.ReapplyAll());
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
    private static float _lastBlockedInputLogTime;
    private static string _lastBlockedInputMethod;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.ConnectClientToPlayerObject))]
    private static void ConnectClientToPlayerObjectPostfix(PlayerControllerB __instance)
    {
        RunPatch("PlayerControllerB.ConnectClientToPlayerObject", () =>
        {
            DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance);
            DrawableSuitsPlugin.Sync?.RequestActiveDesigns();
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.SpawnPlayerAnimation))]
    private static void SpawnPlayerAnimationPostfix(PlayerControllerB __instance)
    {
        RunPatch("PlayerControllerB.SpawnPlayerAnimation", () => DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance));
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
