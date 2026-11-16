using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Mod;
namespace CombatSolver;

internal static class RoomFlowNativeTest
{
    internal static bool IsRequested=>System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="ROOM_FLOW";
    private static void Check(bool ok,string label,List<string> checks)
    {if(!ok)throw new InvalidOperationException(label);checks.Add(label);Entry.Logger.Info("[room-flow-test] PASS "+label);}
    private static async Task Until(Func<bool> predicate,string label,int seconds=30)
    {
        var deadline=DateTime.UtcNow.AddSeconds(seconds);
        while(!predicate()&&DateTime.UtcNow<deadline)await Task.Delay(100);
        if(!predicate())throw new InvalidOperationException(label+" timed out: "+RoomAutomation.Status);
    }
    private static void Flags(bool rewards=false,bool events=false)=>SolverSettings.ApplyForTesting(SolverSettings.Current with
    {AutoRewardsEnabled=rewards,AutoShopEnabled=false,AutoCampEnabled=false,AutoEventsEnabled=events,AutoRouteEnabled=true});
    private static void Screenshot(NGame host,string name)=>host.GetViewport().GetTexture().GetImage().SavePng(
        Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!,name+".png"));
    internal static async Task CheckMenuAsync(NGame host,List<string> checks)
    {
        await Until(()=>SolverOverlay.IsVisible,"main menu overlay");
        Check(!RunManager.Instance.IsInProgress&&SolverOverlay.IsVisible,"OverlayVisibleBeforeStartingAnyCombat",checks);
        Screenshot(host,"menu-overlay");
        Check(SolverOverlay.ToggleVisibilityFromShortcut()&&!SolverOverlay.IsVisible&&SolverOverlay.RestoreButtonVisibleForTesting,
            "HideWorksOutsideCombatAndLeavesRestoreButton",checks);
        await Task.Delay(600);Check(!SolverOverlay.IsVisible,"IdleRefreshRespectsUserHide",checks);
        Screenshot(host,"menu-overlay-hidden");
        SolverOverlay.RestoreFromButtonForTesting();await Task.Delay(100);
        Check(SolverOverlay.IsVisible&&!SolverOverlay.RestoreButtonVisibleForTesting,"NativeRestoreButtonShowsPanelOutsideCombat",checks);
    }
    internal static async Task RunAsync(NGame host,RunState run,Player player,List<string> checks)
    {
        var original=SolverSettings.Current;
        try
        {
            if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving flow fixture refused");
            Flags();
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<WuppyInTrouble>(),showTransition:false);
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
            var eventRoom=ActiveScreenContext.Instance.GetCurrentScreen() as NEventRoom??throw new InvalidOperationException("Native event room missing");
            var service=AdvisorService.Current??new AdvisorService(run,player);
            var plan=EventAdapter.Read(service,eventRoom)??throw new InvalidOperationException("Wuppy choices missing");
            Check(plan.Choices.Length==3&&plan.Choices.All(c=>c.Supported),"WuppyAllThreeNativeInitialOptionsModeled",checks);
            Check(plan.Choices.Any(c=>c.HpLoss>0)&&plan.Choices.Any(c=>c.Potions==1)&&plan.Choices.Any(c=>c.Burden>0),
                "WuppyCostsIncludeHpCurseAndPotionAlternative",checks);
            int count=player.Relics.Count;await Task.Delay(2200);
            Check(player.Relics.Count==count,"WuppyRemainsManualWhenEventSwitchOff",checks);
            Flags(events:true);
            await Until(()=>player.Relics.Any(r=>r is MegaCrit.Sts2.Core.Models.Relics.Wuppy),"native Wuppy acquisition");
            var pet=player.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.Wuppy>().Single();
            Check(pet.MaxHp is 30 or 60&&pet.CurrentHp==pet.MaxHp,"WuppyAutomaticallyObtainedNativePetInsteadOfSkipping",checks);
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"Wuppy native proceed to map");Flags();
            Check(SolverOverlay.IsVisible,"OverlayRemainsVisibleAfterEvent",checks);

            NMapScreen.Instance?.Close(false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);
            int relicCount=player.Relics.Count;Flags(rewards:true);
            await Until(()=>player.Relics.Count>relicCount,"automatic chest relic");
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"automatic chest native proceed");Flags();
            Check(player.Relics.Count==relicCount+1,"AutoChestOpensPicksOnceAndProceedsToMap",checks);
            Screenshot(host,"treasure-map-complete");

            NMapScreen.Instance?.Close(false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);
            var treasure=ActiveScreenContext.Instance.GetCurrentScreen() as NTreasureRoom??throw new InvalidOperationException("Native treasure room missing");
            GameActions.Invoke(treasure,"OnChestButtonReleased",GameActions.Field(treasure,"_chestButton"));
            var collection=GameActions.Descendants<NTreasureRoomRelicCollection>(treasure).Single();
            await Until(()=>GameActions.Descendants<NTreasureRoomRelicHolder>(collection).Any(h=>h.IsEnabled&&h.IsVisibleInTree()),"manual chest relic holder");
            await Task.Delay(300);
            var holder=GameActions.Descendants<NTreasureRoomRelicHolder>(collection).First(h=>h.IsEnabled&&h.IsVisibleInTree());
            relicCount=player.Relics.Count;GameActions.Invoke(collection,"PickRelic",holder);
            await Until(()=>GameActions.Field(treasure,"_proceedButtonState")?.ToString()=="Proceed","manual chest pick finished");
            Flags(rewards:true);
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"proceed after manual chest pick");Flags();
            Check(player.Relics.Count==relicCount+1,"ManualChestPickAlsoResumesAutomaticProceedWithoutReopening",checks);
            var before=run.CurrentMapCoord;StandaloneSmokeTest.SetRoutingForFlowTest(true);
            await Until(()=>run.CurrentMapCoord!=before,"route continues after treasure");
            StandaloneSmokeTest.SetRoutingForFlowTest(false);
            Check(run.CurrentMapCoord!=before,"AutoRouteTravelsAfterTreasureCompletion",checks);
        }
        finally{StandaloneSmokeTest.SetRoutingForFlowTest(false);SolverSettings.ApplyForTesting(original);}
    }
}
