using System.Diagnostics;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver.Engine.InCombat.Mirrors;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal static class BattleLossNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "BATTLE_LOSS";

    internal static async Task RunAsync(NGame host, Player player, List<string> checks)
    {
        if (RunManager.Instance.ShouldSave) throw new InvalidOperationException("Saving fixture refused");
        int generation = SolverController.SearchGenerationForTesting;
        var results = new List<object>();
        foreach (string scenario in new[] { "ritual-empty", "ritual-upgraded-empty", "ritual-root-exhaust",
            "ritual-branch-exhaust", "ritual-power-is-not-exhaust", "stampede-one", "stampede-repeat", "stampede-empty",
            "ricochet", "ricochet-upgraded", "stampede-ricochet" })
        {
            var state = await Fresh();
            var pcs = player.PlayerCombatState!;
            bool stampede = scenario.StartsWith("stampede");
            CardModel? ritual = null, prior = null;
            if (stampede)
            {
                await PowerCmd.Apply<StampedePower>(new ThrowingPlayerChoiceContext(), player.Creature,
                    scenario == "stampede-repeat" ? 3 : 1, player.Creature, null);
                if (scenario != "stampede-empty")
                {
                    if (scenario == "stampede-ricochet") await Add<Ricochet>();
                    else { await Add<StrikeIronclad>(); await Add<Thunderclap>(); }
                }
            }
            else
            {
                ritual = scenario.StartsWith("ricochet") ? await Add<Ricochet>() : await Add<ForgottenRitual>();
                if (scenario.Contains("upgraded")) CardCmd.Upgrade(ritual);
                if (scenario.Contains("exhaust")) prior = await Add<DefendIronclad>();
                if (scenario == "ritual-root-exhaust")
                    await CardCmd.Exhaust(new ThrowingPlayerChoiceContext(), prior!);
                if (scenario == "ritual-power-is-not-exhaust")
                    await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), await Add<Inferno>(), null);
            }
            var root = CombatRootSnapshot.Capture(state);
            var sim = root.ForkSimulator();
            if (scenario == "ritual-branch-exhaust")
                sim.Exhaust(sim.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, prior)));
            var child = sim.Fork();
            if (stampede) HookMirrors.AfterAutoPostPlayPhaseEntered(child, player);
            else if (!child.AutoPlay(child.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, ritual))))
                throw new InvalidOperationException("Ritual prediction refused");
            if (scenario == "ritual-branch-exhaust")
                await CardCmd.Exhaust(new ThrowingPlayerChoiceContext(), prior!);
            if (stampede)
                await player.Creature.GetPower<StampedePower>()!.AfterAutoPostPlayPhaseEntered(new ThrowingPlayerChoiceContext(), player);
            else await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), ritual!, null);
            var differences = ContinuationStamp.CapturePredicted(player, child, root.StartTurnNumber, root.Forecast, root.StartTurnNumber)
                .DescribeDifferences(ContinuationStamp.CaptureLive(state));
            int actualEnergy = pcs.Energy;
            int expectedEnergy = scenario is "ritual-root-exhaust" or "ritual-branch-exhaust" ? 6 : 3;
            bool passed = differences.Count == 0 && actualEnergy == expectedEnergy;
            results.Add(new { scenario, passed, actualEnergy, expectedEnergy, differences });
            Save();
            if (!passed) throw new InvalidOperationException(scenario + ": " + string.Join(" | ", differences.Take(3)) + $" energy={actualEnergy}/{expectedEnergy}");
            checks.Add(scenario + ":FullStateForkAndRngMatch");

            async Task<T> Add<T>() where T : CardModel
            {
                var card = state.CreateCard<T>(player);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, null);
                return card;
            }
        }
        foreach (bool withHealing in new[] { true, false })
        {
            var state = await Fresh();
            if (!withHealing)
                foreach (var relic in player.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.BurningBlood>().ToArray())
                    await RelicCmd.Remove(relic);
            foreach (var enemy in state.Enemies) enemy.SetCurrentHpInternal(1);
            var hemo = state.CreateCard<Hemokinesis>(player);
            await CardPileCmd.AddGeneratedCardToCombat(hemo, PileType.Hand, null);
            var root = CombatRootSnapshot.Capture(state);
            var original = SolverSettings.Current;
            try
            {
                SolverSettings.ApplyForTesting(original with { PerformancePreset = SolverPerformancePreset.Custom,
                    SearchTimeLimitSeconds = 5, SearchBeamWidth = 12, SearchMaxExpandedNodes = 5000,
                    SearchMaxDegreeOfParallelism = 1, IgnoreLongTermRewards = true });
                var policy = SolverController.CaptureSearchPolicy(SolverSettings.Capture(), state, false, null) with
                    { QuickAcceptLowLoss = true, StopAtAcceptableBattleHpLoss = true };
                var clock = Stopwatch.StartNew();
                var names = SolverDisplayNames.Capture(state);
                var damage = BattleDamageTracker.Observe(state);
                var result = await Task.Run(() => CombatSearchCoordinator.Solve(root, names,
                    damage, policy, CancellationToken.None, null));
                int net = BattleHpStopPolicy.NetHpLoss(result.ProjectedBattleHpLost,
                    result.BattleHpRecoveredOrGainedSoFar + result.Snapshot.RecoveredPlayerHp, result.PostCombatRelicHeal);
                bool passed = result.Snapshot.AllEnemiesDead && !result.Snapshot.PlayerDead
                    && CombatSearchCoordinator.HasReachedAcceptableBattleHpLoss(policy, result);
                passed &= withHealing ? net == 0 : net is > 0 and <= 8;
                results.Add(new { scenario = withHealing ? "coordinator-healed-zero" : "coordinator-low-loss-without-healing", passed, net,
                    gross = result.ProjectedBattleHpLost, elapsedMs = clock.ElapsedMilliseconds });
                Save();
                if (!passed) throw new InvalidOperationException("Low loss coordinator did not settle");
                checks.Add(withHealing ? "CoordinatorHealedZeroSettles" : "CoordinatorSafeLowLossWithoutHealingSettles");
            }
            finally { SolverSettings.ApplyForTesting(original); }
        }
        if (SolverController.SearchGenerationForTesting != generation) throw new InvalidOperationException("Unexpected automatic search");
        checks.Add("BattleLossFixtureAutomaticSearchPaused");

        void Save() => File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!, "models.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        async Task<CombatState> Fresh()
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Unassigned,
                ModelDb.AllEncounters.Single(e => e.Id.Entry == "CORPSE_SLUGS_WEAK").ToMutable());
            var end = DateTime.UtcNow.AddSeconds(30);
            while (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play && DateTime.UtcNow < end) await Task.Delay(50);
            var pcs = player.PlayerCombatState ?? throw new InvalidOperationException("No player combat");
            if (pcs.Phase != PlayerTurnPhase.Play) throw new InvalidOperationException("Setup timed out");
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile, pcs.PlayPile })
                foreach (var card in pile.Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            foreach (var enemy in state.Enemies) { await CreatureCmd.SetMaxHp(enemy, 1000); enemy.SetCurrentHpInternal(1000); }
            return state;
        }
    }
}
