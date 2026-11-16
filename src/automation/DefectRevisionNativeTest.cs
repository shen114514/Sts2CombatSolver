using System.Diagnostics;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Resources;

namespace CombatSolver;
internal static class DefectRevisionNativeTest
{
    internal static bool IsRequested=>StandaloneSmokeTest.IsRunning&&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="DEFECT_REVISION";

    internal static async Task RunAsync(NGame host,RunState run,Player player,List<string> checks)
    {
        if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving fixture refused");
        var sizes=await SolverOverlay.ExerciseStableSearchLayoutForTestingAsync(host);
        checks.Add("SearchPanelStableAtDefaultAndCustomSize");
        var state=await FreshCombat(player);
        foreach(var type in new[]{typeof(Defragment),typeof(Coolheaded),typeof(LightningRod)})
        {
            var card=state.CreateCard(ModelDb.AllCards.Single(c=>c.GetType()==type),player);
            await CardPileCmd.AddGeneratedCardToCombat(card,PileType.Hand,null);
            var root=CombatRootSnapshot.Capture(state);var sim=root.ForkSimulator();
            sim.AutoPlay(sim.State.GetPlayerCombatState(player).Hand.Cards.Single(c=>ReferenceEquals(c.Original,card)));
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(),card,null);
            CheckState(root,sim,state,player,type.Name+"NativeStateAndRngMatch",checks);
        }
        for(int turn=1;turn<=2;turn++)
        {
            var root=CombatRootSnapshot.Capture(state);var sim=root.ForkSimulator();
            var power=((SimulatedCombatState)sim.State.CombatState).GetPower<LightningRodPower>(player.Creature)!;
            AfterEnergyResetMirrors.Invoke(power,new(){Simulator=sim,Player=player});
            await player.Creature.GetPower<LightningRodPower>()!.AfterEnergyReset(player);
            CheckState(root,sim,state,player,$"LightningRodDelayedChannel{turn}MatchesNative",checks);
        }

        state=await FreshCombat(player);
        player.Creature.SetCurrentHpInternal(1);
        foreach(var enemy in state.Enemies)enemy.SetCurrentHpInternal(15);
        await PotionCmd.TryToProcure<FirePotion>(player);
        await PotionCmd.TryToProcure<FirePotion>(player);
        var original=SolverSettings.Current;
        try
        {
            SolverSettings.ApplyForTesting(original with{PerformancePreset=SolverPerformancePreset.Custom,
                SearchTimeLimitSeconds=1,SearchBeamWidth=12,SearchMaxExpandedNodes=5000,
                SearchMaxCardBranchesPerNode=8,SearchMaxDegreeOfParallelism=1,PotionDirectives=[],
                IgnoreLongTermRewards=true,StopFullAutoOnWorseRecalculation=false});
            SolverController.ApplyPersistentSettings(SolverSettings.Capture());
            var root=CombatRootSnapshot.Capture(state);var names=SolverDisplayNames.Capture(state);
            var damage=BattleDamageTracker.Observe(state);
            var policy=SolverController.CaptureSearchPolicy(SolverSettings.Capture(),state,false,null);
            var baseline=await Task.Run(()=>new CombatBeamSolver(root,names,damage,policy,
                potionPolicyOverride:SolverPotionPolicy.Disabled).Solve());
            if(!baseline.OnlyDeathRoutesFound)throw new InvalidOperationException("No-potion fixture must predict death");
            var clock=Stopwatch.StartNew();var exhausted=policy with{BudgetOverrideMilliseconds=0};
            var context=new SearchPassContext(root,names,damage,exhausted,policy.Profile,clock,
                new SearchBudgetLedger(clock,exhausted),CancellationToken.None,null,null);
            if(CombatSearchCoordinator.SmartPotionAuditMinimumMilliseconds(context with{Policy=exhausted with{IncludeTurnSetup=true}},baseline)<=0)
                throw new InvalidOperationException("Turn setup excluded from potion safety budget");
            checks.Add("TurnSetupQualifiesForPotionSafetyBudget");
            var result=await Task.Run(()=>CombatSearchCoordinator.SearchSmartPotionGradientWithMinimumBudget(context,baseline,new()));
            if(!result.Snapshot.AllEnemiesDead||result.ExplicitPotionCount!=2)
                throw new InvalidOperationException("Exhausted-budget rescue did not find two-potion victory");
            checks.Add("ExhaustedBudgetSearchesTwoPotionRescue");
            // Construct the phase boundary without changing the live turn loop.
            // Empty piles make the already-completed draw/reset idempotent here.
            CombatRootSnapshot startRoot;
            var phase=player.PlayerCombatState!.Phase;
            try
            {
                player.PlayerCombatState.Phase=PlayerTurnPhase.Start;
                startRoot=CombatRootSnapshot.Capture(state);
            }
            finally{player.PlayerCombatState!.Phase=phase;}
            var setupResult=await Task.Run(()=>CombatSearchCoordinator.Solve(startRoot,names,damage,
                policy with{IncludeTurnSetup=true},CancellationToken.None,null));
            if(!setupResult.Snapshot.AllEnemiesDead||setupResult.ExplicitPotionCount!=2)
                throw new InvalidOperationException("Full turn-setup coordinator skipped the two-potion rescue");
            checks.Add("FullTurnSetupCoordinatorFindsTwoSmartPotionVictory");
            bool won=false;
            SolverController.StartPredictedRouteForTesting(host,state,result,_=>won=true);
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(!won&&DateTime.UtcNow<deadline)await Task.Delay(50);
            if(!won||player.Creature.CurrentHp<=0)throw new InvalidOperationException("Two-potion native execution failed");
            checks.Add("TwoSmartPotionsExecuteToNativeVictory");
        }
        finally{CombatReplayRecording.TestCombatEndObserver=null;SolverSettings.ApplyForTesting(original);}
        File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!,"panel-sizes.json"),
            JsonSerializer.Serialize(sizes.Select(s=>new{width=s.X,height=s.Y})));
    }

    private static void CheckState(CombatRootSnapshot root,CombatSolver.Engine.InCombat.Simulation.CombatPredictionSimulator sim,
        CombatState state,Player player,string label,List<string> checks)
    {
        var expected=ContinuationStamp.CapturePredicted(player,sim,root.StartTurnNumber,root.Forecast,root.StartTurnNumber);
        var diff=expected.DescribeDifferences(ContinuationStamp.CaptureLive(state));
        if(diff.Count>0)throw new InvalidOperationException(label+": "+string.Join(" | ",diff.Take(4)));
        checks.Add(label);
    }
    private static async Task<CombatState> FreshCombat(Player player)
    {
        var encounter=ModelDb.AllEncounters.Single(e=>e.Id.Entry=="CORPSE_SLUGS_WEAK").ToMutable();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,MegaCrit.Sts2.Core.Map.MapPointType.Unassigned,encounter);
        var deadline=DateTime.UtcNow.AddSeconds(30);
        while(player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play&&DateTime.UtcNow<deadline)await Task.Delay(50);
        if(player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play)throw new InvalidOperationException("Turn initialization did not finish");
        var pcs=player.PlayerCombatState!;
        foreach(var pile in new[]{pcs.Hand,pcs.DrawPile,pcs.DiscardPile,pcs.ExhaustPile,pcs.PlayPile})
            foreach(var card in pile.Cards.ToArray())await CardPileCmd.RemoveFromCombat(card,skipVisuals:true);
        return CombatManager.Instance.DebugOnlyGetState()!;
    }
}
