using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Mod;
namespace CombatSolver;
internal static class CampRestoreNativeTest
{
    internal static bool IsRequested=>System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="CAMP_RESTORE";
    private static void Flags(bool enabled)=>SolverSettings.ApplyForTesting(SolverSettings.Current with
    {AutoCampEnabled=enabled,AutoRewardsEnabled=false,AutoShopEnabled=false,AutoEventsEnabled=false});
    private static async Task Until(Func<bool> condition,string label)
    {var end=DateTime.UtcNow.AddSeconds(35);while(!condition()&&DateTime.UtcNow<end)await Task.Delay(100);if(!condition())throw new InvalidOperationException(label+": "+RoomAutomation.Status);}
    private static void Check(bool ok,string name,List<string> checks)
    {if(!ok)throw new InvalidOperationException(name);checks.Add(name);Entry.Logger.Info("[camp-restore-test] PASS "+name);}
    internal static async Task RunAsync(RunState run,Player player,List<string> checks)
    {
        if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving camp fixture refused");
        var original=SolverSettings.Current;
        try
        {
            Flags(false);
            var pet=ModelDb.Relic<Wuppy>().ToMutable() as Wuppy??throw new InvalidOperationException("Pet missing");
            pet.MaxHp=60;await RelicCmd.Obtain(pet,player);pet.CurrentHp=59;
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            var levels=player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);int hp=player.Creature.CurrentHp;
            Flags(true);
            await Until(()=>AdvisorService.Current?.Decision?.Kind=="rest","camp decision");
            var decision=AdvisorService.Current!.Decision!;
            Check(decision.Options.Single(o=>o.Key=="RESTORE") is {Enabled:true,Kind:"effect"},"EnabledRestoreRecognizedAsModeledEffect",checks);
            Check(decision.Evaluation.RecommendedKey=="SMITH","RestoreDoesNotVetoBetterSmithDecision",checks);
            await Until(()=>player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)>levels,"native smith upgrade");
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"native camp proceed");Flags(false);
            Check(player.Creature.CurrentHp==hp&&pet.CurrentHp==59,"NativeSmithCompletesWithoutSleepOrPetRestore",checks);
            foreach(var card in player.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(card);
            NMapScreen.Instance?.Close(false);pet.CurrentHp=0;
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            int submits=RoomAutomation.SubmittedActions;await Task.Delay(2500);
            Check(pet.CurrentHp==0&&RoomAutomation.SubmittedActions==submits,"CampOffPreservesDeadPetAndMakesNoSelection",checks);
            Flags(true);await Until(()=>pet.CurrentHp==60,"native pet restore");
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"pet restore proceed");Flags(false);
            Check(player.Creature.CurrentHp==hp,"NativeRestoreRevivesPetAndDoesNotHealPlayer",checks);
            NMapScreen.Instance?.Close(false);pet.CurrentHp=0;player.Creature.SetCurrentHpInternal(1);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            Flags(true);await Until(()=>player.Creature.CurrentHp>1,"low HP player recovery");
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"sleep proceed");Flags(false);
            Check(pet.CurrentHp==0,"CriticalPlayerHpChoosesSleepInsteadOfPetRestore",checks);
        }
        finally{SolverSettings.ApplyForTesting(original);}
    }
}
