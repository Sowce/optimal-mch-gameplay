using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace DrillDoublePress
{
    public class DrillPlugin : IDalamudPlugin
    {
        public string Name => "Optimal MCH Gameplay";

        private const uint DrillActionId = 16498;
        private uint backlogCheck = 0;
        private List<short> pastDrills = new List<short>();
        private short lastButtonClick = 0;
        private bool heDoneDidIt = false;

        [PluginService] private static IGameInteropProvider GameInteropProvider { get; set; } = null!;
        [PluginService] private static IPluginLog PluginLog { get; set; } = null!;
        [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
        [PluginService] private static IChatGui Chat { get; set; } = null!;
        [PluginService] private static ISigScanner SigScanner { get; set; } = null!;
        [PluginService] private static IClientState ClientState { get; set; } = null!;

        private Hook<GetIconDelegate> GetIconHook;
        private Hook<IsIconReplaceableDelegate> IsIconReplaceableHook;
        private Hook<ActionManager.Delegates.UseActionLocation> UseActionLocationHook;
        private Hook<RaptureHotbarModule.Delegates.ExecuteSlotById> ExecuteSlotByIdHook;

        private delegate ulong IsIconReplaceableDelegate(uint actionID);
        private delegate uint GetIconDelegate(IntPtr actionManager, uint actionID);

        private unsafe bool UseActionLocationDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, Vector3* location, uint extraParam, byte a7)
        {
            var ret = UseActionLocationHook.Original(thisPtr, actionType, actionId, targetId, location, extraParam, a7);

            (new Thread(() =>
            {
                Thread.Sleep(500);
                try
                {
                    if (actionId == DrillActionId)
                    {
                        PluginLog.Debug($"Drill cast! Action ID: {actionId}, param: {extraParam}");
                        CheckForDoublePress();
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Error in UseActionLocationDetour: {ex}");
                }
            })).Start();


            return ret;
        }

        private unsafe byte ExecuteSlotByIdDetour(RaptureHotbarModule* thisPtr, uint hotbarId, uint slotId)
        {
            var ret = ExecuteSlotByIdHook.Original(thisPtr, hotbarId, slotId);

            lastButtonClick = (short)(hotbarId * 100 + slotId);

            return ret;
        }

        private void CommandHandler(string command, string arguments)
        {
            if (uint.TryParse(arguments, out var value))
            {
                if (value == 0)
                {
                    Chat.Print($"Wrong value, 1 is lowest allowed Flip");
                    return;
                }

                backlogCheck = value - 1;
                Chat.Print($"New value set to {backlogCheck + 1} PERFECTWORLD");
            }
            else
            {
                Chat.Print($"Current value is {backlogCheck + 1} PERFECTWORLD");
            }
        }

        public DrillPlugin()
        {
            CommandManager.AddHandler("/mchisbadvalue", new CommandInfo(this.CommandHandler)
            {
                HelpMessage = "Drill backlog size (how many since duplicate to count it, lower is easier)"
            });

            InitializeHooks();
        }

        private unsafe void InitializeHooks()
        {
            UseActionLocationHook = GameInteropProvider.HookFromAddress<ActionManager.Delegates.UseActionLocation>(
                ActionManager.MemberFunctionPointers.UseActionLocation,
                UseActionLocationDetour
            );

            ExecuteSlotByIdHook = GameInteropProvider.HookFromAddress<RaptureHotbarModule.Delegates.ExecuteSlotById>(
                RaptureHotbarModule.MemberFunctionPointers.ExecuteSlotById,
                ExecuteSlotByIdDetour
            );

            GetIconHook = GameInteropProvider.HookFromAddress<GetIconDelegate>(ActionManager.Addresses.GetAdjustedActionId.Value, GetIconDetour);
            IsIconReplaceableHook = GameInteropProvider.HookFromAddress<IsIconReplaceableDelegate>(SigScanner.ScanText("40 53 48 83 EC 20 8B D9 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 1F"), IsIconReplaceableDetour);

            UseActionLocationHook.Enable();
            ExecuteSlotByIdHook.Enable();
            GetIconHook.Enable();
            IsIconReplaceableHook.Enable();
        }

        private ulong IsIconReplaceableDetour(uint actionID) => 1;

        private unsafe uint GetIconDetour(IntPtr actionManager, uint actionID)
        {
            try
            {
                if (ClientState.LocalPlayer == null)
                    return GetIconHook.Original(actionManager, actionID);

                if (heDoneDidIt)
                    return DrillActionId;
                else
                    return GetIconHook.Original(actionManager, actionID);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Don't crash the game");
                return GetIconHook.Original(actionManager, actionID);
            }
        }


        private void CheckForDoublePress()
        {
            PluginLog.Debug($"Drill cast from: {lastButtonClick}");

            if (pastDrills.Count != 0 && pastDrills.Count - pastDrills.LastIndexOf(lastButtonClick) >= backlogCheck)
            {
                //Process.GetCurrentProcess().Kill();
                //Chat.Print("BOMBA");
                heDoneDidIt = true;
                (new Thread(() =>
                {
                    Thread.Sleep(15000);
                    heDoneDidIt = false;
                })).Start();
            }

            pastDrills.Add(lastButtonClick);
        }

        public void Dispose()
        {
            GetIconHook?.Dispose();
            UseActionLocationHook?.Dispose();
            UseActionLocationHook?.Dispose();
            IsIconReplaceableHook?.Dispose();

            CommandManager.RemoveHandler("/mchisbadvalue");
        }
    }
}
