using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Mod;
namespace CombatSolver;
internal static class RoomOptionsNativeTest
{
    internal static bool IsRequested=>System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="ROOM_OPTIONS";
    private static void Flags(RoomAutomationFeature only=RoomAutomationFeature.None)=>SolverSettings.ApplyForTesting(SolverSettings.Current with
    {AutoRewardsEnabled=only==RoomAutomationFeature.Rewards,AutoShopEnabled=only==RoomAutomationFeature.Shop,
        AutoEventsEnabled=only==RoomAutomationFeature.Events,AutoCampEnabled=only==RoomAutomationFeature.Camp});
    private static async Task Until(Func<bool> predicate,string label,int seconds=25)
    {
        var deadline=DateTime.UtcNow.AddSeconds(seconds);
        while(!predicate()&&DateTime.UtcNow<deadline)await Task.Delay(100);
        if(!predicate())throw new InvalidOperationException(label+" timed out; status="+RoomAutomation.Status);
    }
    private static void Check(bool condition,string label,List<string> checks)
    {if(!condition)throw new InvalidOperationException(label);checks.Add(label);Entry.Logger.Info("[room-options-test] PASS "+label);}
    internal static async Task RunAsync(NGame host,RunState run,Player player,List<string> checks)
    {
        var original=SolverSettings.Current;
        try
        {
            if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving room fixture refused.");
            Flags();
            Check(!RoomAutomation.Options.Any,"NewRoomOptionsDefaultOff",checks);
            Check(!SolverSettingsPanel.ReadRoomAutomationFlag(SolverSettings.DeserializeForTesting("{}"),RoomAutomationFeature.Rewards)
                &&!SolverSettings.DeserializeForTesting("{}").AutoShopEnabled&&!SolverSettings.DeserializeForTesting("{}").AutoEventsEnabled
                &&!SolverSettings.DeserializeForTesting("{}").AutoCampEnabled,"OldSettingsMissingNewFieldsRemainManual",checks);
            var canvas=new CanvasLayer{Layer=250};host.AddChild(canvas);
            var panel=new SolverSettingsPanel{Position=new Vector2(60,40),Size=new Vector2(1180,900)};canvas.AddChild(panel);
            foreach(var feature in new[]{RoomAutomationFeature.Rewards,RoomAutomationFeature.Shop,RoomAutomationFeature.Events,RoomAutomationFeature.Camp})
            {
                Flags();panel.Reload();panel.SetRoomAutomationToggleForTesting(feature,true);
                Check(RoomAutomation.Options.Allows(feature)&&RoomAutomation.Options.Any
                    &&Enum.GetValues<RoomAutomationFeature>().Where(f=>f!=feature).All(f=>!RoomAutomation.Options.Allows(f)),"UiToggleIndependent:"+feature,checks);
                var restored=SolverSettings.RoundTripForTesting(SolverSettings.Current);
                Check(SolverSettingsPanel.ReadRoomAutomationFlag(restored,feature),"SettingSerialization:"+feature,checks);
                panel.SetRoomAutomationToggleForTesting(feature,false);
                Check(!RoomAutomation.Options.Any,"UiToggleOff:"+feature,checks);
            }
            panel.Reload();
            Check(panel.RoomAutomationTogglesConfiguredForTesting,"AllFourUiRowsMatchSettings",checks);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            panel.ScrollRoomAutomationIntoViewForTesting();
            await Task.Delay(300);
            string directory=System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!;
            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,"room-options-controls.png"));
            canvas.QueueFree();await Task.Delay(300);Flags();

            // Native mixed reward flow: off stays untouched, then on claims gold
            // and really adds the selected card through CardReward.OnSelect.
            await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
            NMapScreen.Instance?.Close(false);
            int gold=player.Gold,deck=player.Deck.Cards.Count;
            var offered=run.CreateCard<WahChoKi>(player);
            Task rewardTask=new RewardsSet(player).WithCustomRewards([
                new GoldReward(25,player),new CardReward([offered],CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster))]).Offer();
            await Until(()=>ActiveScreenContext.Instance?.GetCurrentScreen() is NRewardsScreen,"native rewards opened");
            Flags(RoomAutomationFeature.Camp);int submits=RoomAutomation.SubmittedActions;
            await Task.Delay(2800);
            Check(player.Gold==gold&&player.Deck.Cards.Count==deck&&RoomAutomation.SubmittedActions==submits,"RewardsOffOtherFeatureOnDoesNothing",checks);
            Flags(RoomAutomationFeature.Rewards);
            await Until(()=>ActiveScreenContext.Instance?.GetCurrentScreen() is NCardRewardSelectionScreen,"reward child selector opened",20);
            Flags(RoomAutomationFeature.Events);submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(!player.Deck.Cards.Contains(offered)&&RoomAutomation.SubmittedActions==submits,"RewardSwitchOffLeavesChildSelectorManual",checks);
            Flags(RoomAutomationFeature.Rewards);
            await Until(()=>rewardTask.IsCompleted,"native rewards complete",35);await rewardTask;Flags();
            Check(player.Gold==gold+25&&player.Deck.Cards.Count==deck+1&&player.Deck.Cards.Contains(offered),"RewardsOnNativeGoldAndChosenCardOnce",checks);

            // Protect a useful relic's exact budget before a positively offered card.
            NMapScreen.Instance?.Close(false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
            var shop=NMerchantRoom.Instance!;shop.OpenInventory();
            var inventory=shop.Inventory.Inventory!;
            foreach(var entry in inventory.CardEntries)AccessTools.Property(entry.GetType(),"CreationResult").SetValue(entry,null);
            var filler=run.CreateCard<ArrowStab>(player);var cardEntry=inventory.CardEntries.First();
            AccessTools.Property(cardEntry.GetType(),"CreationResult").SetValue(cardEntry,new CardCreationResult(filler));
            AccessTools.Field(cardEntry.GetType(),"_cost").SetValue(cardEntry,50);
            foreach(var entry in inventory.RelicEntries)AccessTools.Property(entry.GetType(),"Model").SetValue(entry,null);
            var bag=ModelDb.Relic<BagOfPreparation>().ToMutable();var relicEntry=inventory.RelicEntries.First();
            AccessTools.Property(relicEntry.GetType(),"Model").SetValue(relicEntry,bag);AccessTools.Field(relicEntry.GetType(),"_cost").SetValue(relicEntry,175);
            player.Gold=175;
            foreach(var slot in shop.Inventory.GetAllSlots())GameActions.Invoke(slot,"UpdateVisual");
            Flags(RoomAutomationFeature.Events);submits=RoomAutomation.SubmittedActions;await Task.Delay(9000);
            Check(player.Gold==175&&!player.Relics.Contains(bag)&&!player.Deck.Cards.Contains(filler)&&RoomAutomation.SubmittedActions==submits,"ShopOffOtherFeatureOnDoesNotSpend",checks);
            Flags(RoomAutomationFeature.Shop);await Task.Delay(3000);Flags(RoomAutomationFeature.Events);await Task.Delay(9000);
            Check(player.Gold==175&&!player.Relics.Contains(bag),"TurningShopOffCancelsPendingPurchaseDelay",checks);
            Flags(RoomAutomationFeature.Shop);await Until(()=>player.Relics.Contains(bag),"native relic-first purchase",20);
            Check(player.Gold==0&&!player.Deck.Cards.Contains(filler),"ShopOnNativeRelicFirstCardsBoughtZero",checks);
            var service=AdvisorService.Current!;
            service.Guard(()=>throw new InvalidOperationException("Controlled room-adapter fault"));
            await Task.Delay(600);
            Check(!service.Autopilot.Enabled,"AdapterFaultDoesNotRetrySameRoom",checks);

            // Camp is independent of rewards; full HP must not cause a sleep.
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await Until(()=>service.Autopilot.Enabled,"automation resumes after native room change");
            Check(service.Autopilot.Enabled,"AdapterFaultRecoversAtNewRoom",checks);
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
            Flags(RoomAutomationFeature.Rewards);submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(offered.CurrentUpgradeLevel==0&&RoomAutomation.SubmittedActions==submits,"CampOffRewardsOnDoesNotAct",checks);
            int hp=player.Creature.CurrentHp;Flags(RoomAutomationFeature.Camp);
            await Until(()=>ActiveScreenContext.Instance?.GetCurrentScreen() is NDeckUpgradeSelectScreen,"camp child selector opened",20);
            Flags(RoomAutomationFeature.Rewards);submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(offered.CurrentUpgradeLevel==0&&RoomAutomation.SubmittedActions==submits,"CampSwitchOffLeavesUpgradeSelectorManual",checks);
            Flags(RoomAutomationFeature.Camp);
            await Until(()=>offered.CurrentUpgradeLevel==1,"native contextual camp upgrade",25);Flags();
            Check(player.Creature.CurrentHp==hp,"CampOnUpgradesWahChoKiWithoutSleeping",checks);
            await Until(()=>!RoomAutomation.IsBusy,"camp parent task completes",15);

            // The 8 HP trudge is lethal at 5 HP. The supported rest branch heals.
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<DenseVegetation>(),showTransition:false);
            player.Creature.SetCurrentHpInternal(5);
            Flags(RoomAutomationFeature.Shop);submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(player.Creature.CurrentHp==5&&RoomAutomation.SubmittedActions==submits,"EventsOffShopOnDoesNotClick",checks);
            Flags(RoomAutomationFeature.Events);await Until(()=>player.Creature.CurrentHp>5,"native nonlethal event choice",20);Flags();
            Check(player.Creature.CurrentHp>5,"EventsOnNativeHealAvoidsLethalChoice",checks);
            submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(RoomAutomation.SubmittedActions==submits,"AllOffStopsFurtherEventSubmissions",checks);
            await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance?.Close(false);
            var foreign=run.CreateCard<DemonForm>(player);int beforeForeign=player.Deck.Cards.Count;
            var foreignReward=new RewardsSet(player).WithCustomRewards([
                new CardReward([foreign],CardCreationSource.Encounter,player,CardCreationOptions.ForRoom(player,RoomType.Monster))]).Offer();
            Flags(RoomAutomationFeature.Rewards);
            await Until(()=>ActiveScreenContext.Instance?.GetCurrentScreen() is NCardRewardSelectionScreen,"unmodeled foreign reward opened",15);
            submits=RoomAutomation.SubmittedActions;await Task.Delay(2800);
            Check(!foreignReward.IsCompleted&&player.Deck.Cards.Count==beforeForeign&&RoomAutomation.SubmittedActions==submits,
                "UnmodeledForeignCardWaitsForManualInsteadOfSkipping",checks);Flags();
            if(checks.Count<31)throw new InvalidOperationException("Room option checks unexpectedly incomplete.");
        }
        finally{SolverSettings.ApplyForTesting(original);}
    }
}
