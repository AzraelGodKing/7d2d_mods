using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

// Two more ways in to the /thrall commands besides the Harmony chat patch:
//  - a chat event handler (the game's own hook for mods), and
//  - a console command: press F1 and type "thrall list". Replies still arrive as whispers/toasts.
public static class ThrallChatEvent
{
    public static ModEvents.EModEventResult OnChat(ref ModEvents.SChatMessageData data)
    {
        try
        {
            if (data.SenderEntityId >= 0 && Thralls.HandleChat(data.SenderEntityId, data.Message))
                return ModEvents.EModEventResult.StopHandlersAndVanilla;
        }
        catch (Exception e) { Log.Warning("[Bait] chat event: " + e.Message); }
        return ModEvents.EModEventResult.Continue;
    }
}

[Preserve]
public class ConsoleCmdThrall : ConsoleCmdAbstract
{
    public override int DefaultPermissionLevel { get { return 1000; } }

    public override string[] getCommands() { return new[] { "thrall", "thralls" }; }
    public override string getDescription() { return "Zombie and Animal Bait: manage your tamed creatures (same as /thrall in chat)"; }
    public override string getHelp() { return "thrall list | hp | name <n> <name> | call <n> | stow [n|all] | stay/follow <n|all> | mode <n|all> aggressive|defend|passive | heal <n> | revive <n> | tether <n|all> [radius] | untether <n|all> | release <n> | kill <n|all> | config [name value]"; }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        int id = -1;
        if (_senderInfo.RemoteClientInfo != null) id = _senderInfo.RemoteClientInfo.entityId;
        else
        {
            var w = GameManager.Instance != null ? GameManager.Instance.World : null;
            var p = w != null ? w.GetPrimaryPlayer() : null;
            if (p != null) id = p.entityId;
        }
        if (id < 0) { SingletonMonoBehaviour<SdtdConsole>.Instance.Output("[Bait] No player to run this for."); return; }
        string msg = "/thrall" + (_params.Count > 0 ? " " + string.Join(" ", _params.ToArray()) : "");
        if (!Thralls.HandleChat(id, msg)) SingletonMonoBehaviour<SdtdConsole>.Instance.Output("[Bait] Taming is not ready yet (world still loading).");
        else SingletonMonoBehaviour<SdtdConsole>.Instance.Output("[Bait] Done. Replies are in chat.");
    }
}