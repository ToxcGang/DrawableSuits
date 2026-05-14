using GameNetcodeStuff;
using HarmonyLib;

namespace DrawableSuits;

[HarmonyPatch(typeof(StartOfRound))]
internal static class StartOfRoundPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("LoadUnlockables")]
    private static void LoadUnlockablesPostfix()
    {
        DrawableSuitsPlugin.Registry?.ReapplyAll();
    }

    [HarmonyPostfix]
    [HarmonyPatch("PositionSuitsOnRack")]
    private static void PositionSuitsOnRackPostfix()
    {
        DrawableSuitsPlugin.Registry?.ReapplyAll();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(StartOfRound.SyncSuitsClientRpc))]
    private static void SyncSuitsClientRpcPostfix()
    {
        DrawableSuitsPlugin.Registry?.ReapplyAll();
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnPlayerConnectedClientRpc")]
    private static void OnPlayerConnectedClientRpcPostfix()
    {
        DrawableSuitsPlugin.Registry?.ReapplyAll();
        DrawableSuitsPlugin.Sync?.RequestActiveDesigns();
    }
}

[HarmonyPatch(typeof(UnlockableSuit))]
internal static class UnlockableSuitPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(UnlockableSuit.SwitchSuitForPlayer))]
    private static void SwitchSuitForPlayerPostfix(PlayerControllerB player)
    {
        DrawableSuitsPlugin.Registry?.ApplyToPlayer(player);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(UnlockableSuit.SwitchSuitForAllPlayers))]
    private static void SwitchSuitForAllPlayersPostfix()
    {
        DrawableSuitsPlugin.Registry?.ReapplyAll();
    }
}

[HarmonyPatch(typeof(PlayerControllerB))]
internal static class PlayerControllerBPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.ConnectClientToPlayerObject))]
    private static void ConnectClientToPlayerObjectPostfix(PlayerControllerB __instance)
    {
        DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance);
        DrawableSuitsPlugin.Sync?.RequestActiveDesigns();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerControllerB.SpawnPlayerAnimation))]
    private static void SpawnPlayerAnimationPostfix(PlayerControllerB __instance)
    {
        DrawableSuitsPlugin.Registry?.ApplyToPlayer(__instance);
    }
}

[HarmonyPatch(typeof(QuickMenuManager))]
internal static class QuickMenuManagerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    private static void StartPostfix(QuickMenuManager __instance)
    {
        PauseMenuButtonInjector.EnsureButton(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(QuickMenuManager.OpenQuickMenu))]
    private static void OpenQuickMenuPostfix(QuickMenuManager __instance)
    {
        PauseMenuButtonInjector.EnsureButton(__instance);
        PauseMenuButtonInjector.SelectIfNeeded(__instance);
    }
}
