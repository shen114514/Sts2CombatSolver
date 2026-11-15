using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private Task<bool>? parasolRemoval;
    private MerchantRoom? parasolRoom;
    internal void ParasolRemovalStarted(MerchantCardRemovalEntry entry,MerchantInventory? inventory,
        bool ignoreCost,bool cancelable,Task<bool> task)
    {
        // Observe the native free, mandatory removal; never initiate a second purchase.
        if(!ignoreCost||cancelable||task.IsCompleted||inventory?.Player!=service.Player||
            !ReferenceEquals(entry,inventory.CardRemovalEntry)||service.Player.GetRelic<LordsParasol>()==null||
            service.State.CurrentRoom is not MerchantRoom room)return;
        parasolRemoval=task;parasolRoom=room;
        service.RecordAuto("parasol_removal_started",new{free=true,mandatory=true});
    }
    private void PrepareParasolRemoval(object? screen)
    {
        if(parasolRemoval==null)return;
        if(parasolRemoval.IsCompleted||!ReferenceEquals(parasolRoom,service.State.CurrentRoom))
        {parasolRemoval=null;parasolRoom=null;removalTarget=null;return;}
        if(screen is not NDeckCardSelectScreen selector||removalTarget!=null)return;
        if(GameActions.Field(selector,"_cards") is not IEnumerable<CardModel> cards||
            GameActions.Field(selector,"_prefs") is not CardSelectorPrefs {MinSelect:1,MaxSelect:1}||
            GameActions.Field(selector,"_selectedCards") is not HashSet<CardModel> selected||selected.Count!=0)return;
        var state=SnapshotReader.Read(service.State,service.Player);var value=new StrategicValue(state);
        var candidates=cards.Where(c=>c.IsRemovable&&service.Player.Deck.Cards.Contains(c))
            .Select(c=>(Card:c,Score:value.Remove(SnapshotReader.ReadCard(c)))).OrderByDescending(c=>c.Score).ToArray();
        if(candidates.Length==0)return;
        // Mandatory means even a deck without basics must choose from actual legal cards.
        shoppingRoom=parasolRoom;removalTarget=candidates[0].Card;
        service.RecordAuto("parasol_removal_plan",new{state,selected=SnapshotReader.ReadCard(removalTarget),
            candidates=candidates.Select(c=>new{card=SnapshotReader.ReadCard(c.Card),score=c.Score}),
            reason="领主阳伞原生免费必选删牌；按现有移除估值选择，未知效果仍为近似"});
    }
}

// 0.113.0 把 OnTryPurchaseWrapper 改名成 OnTryPurchaseInternal，而且是 protected virtual
// （Publicizer 不公开 virtual 成员），所以这里只能用字符串名给 Harmony 找。
internal sealed class ParasolRemovalPatch : CombatSolver.Patching.IPatchMethod
{
    public static CombatSolver.Patching.ModPatchTarget[] GetTargets()=>
        [new(typeof(MerchantCardRemovalEntry),"OnTryPurchaseInternal",[typeof(MerchantInventory),typeof(bool)])];
    // 0.113: OnTryPurchaseInternal 返回 Task<(bool ok, int cost)>，这里退回旧的 bool 语义
    private static async Task<bool> Unwrap(Task<(bool, int)> source) => (await source).Item1;


    static void Postfix(MerchantCardRemovalEntry __instance,MerchantInventory? inventory,bool ignoreCost,Task<(bool,int)> __result)=>
        AdvisorService.Current?.Guard(()=>AdvisorService.Current.Autopilot.ParasolRemovalStarted(__instance,inventory,ignoreCost,false,Unwrap(__result)));
}
