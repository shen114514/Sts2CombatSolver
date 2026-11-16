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
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

// Explicit no-save fixtures. A full continuation stamp checks pile order and RNG,
// rather than accepting equal card counts as evidence of a correct mirror.
internal static class ModelRepairsNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "MODEL_REPAIRS";

    internal static async Task RunAsync(NGame host, RunState run, Player player, List<string> checks)
    {
        if (RunManager.Instance.ShouldSave) throw new InvalidOperationException("Saving model fixture refused");
        int generation = SolverController.SearchGenerationForTesting;
        var results = new List<object>();
        var failures = new List<string>();
        {
            var state = await FreshCombat(player);
            var mantle = await Add<CrimsonMantle>(state, player, PileType.Hand);
            var second = await Add<CrimsonMantle>(state, player, PileType.Hand);
            var sim = await Compare(state, player, mantle, "CrimsonMantle");
            var child = sim.Fork();
            child.AutoPlay(child.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, second)));
            var parentPower = ((SimulatedCombatState)sim.State.CombatState).GetPower<CrimsonMantlePower>(player.Creature)!;
            var childPower = ((SimulatedCombatState)child.State.CombatState).GetPower<CrimsonMantlePower>(player.Creature)!;
            var livePower = player.Creature.GetPower<CrimsonMantlePower>()!;
            bool isolated = parentPower.DynamicVars["SelfDamage"].BaseValue == 1 &&
                childPower.DynamicVars["SelfDamage"].BaseValue == 2 && livePower.DynamicVars["SelfDamage"].BaseValue == 1;
            var combat = (SimulatedCombatState)sim.State.CombatState;
            TurnStartPowerSupport.ApplyAfterPlayerTurnStartPower(sim, combat, player, new TurnStartChoiceCursor(null), parentPower);
            await livePower.AfterPlayerTurnStart(new ThrowingPlayerChoiceContext(), player);
            var expected = ContinuationStamp.CapturePredicted(player, sim, player.PlayerCombatState!.TurnNumber,
                IntentForecaster.Build(state, 1), player.PlayerCombatState.TurnNumber);
            var differences = expected.DescribeDifferences(ContinuationStamp.CaptureLive(state));
            bool passed = isolated && differences.Count == 0;
            results.Add(new { name = "CrimsonMantleForkAndTurnStart", passed, isolated, differences });
            if (passed) checks.Add("CrimsonMantleForkAndNativeTurnStartMatch");
            else failures.Add("CrimsonMantleForkAndTurnStart:" + differences.FirstOrDefault());
        }

        foreach (bool upgraded in new[] { false, true })
        {
            var state = await FreshCombat(player);
            var shield = await Add<MightyShield>(state, player, PileType.Hand);
            if (upgraded) CardCmd.Upgrade(shield);
            await Add<StrikeIronclad>(state, player, PileType.Draw);
            await Add<DefendIronclad>(state, player, PileType.Draw);
            await Add<PommelStrike>(state, player, PileType.Draw);
            await Compare(state, player, shield, "MightyShield" + (upgraded ? "+" : ""));
        }

        {
            var state = await FreshCombat(player);
            await PowerCmd.Apply<HellraiserPower>(new ThrowingPlayerChoiceContext(), player.Creature, 1, player.Creature, null);
            var draw = await Add<BattleTrance>(state, player, PileType.Hand);
            for (int i = 0; i < 3; i++)
            {
                var strike = await Add<PommelStrike>(state, player, PileType.Draw);
                CardCmd.Upgrade(strike);
            }
            await Add<StrikeIronclad>(state, player, PileType.Draw);
            await Add<StrikeIronclad>(state, player, PileType.Draw);
            await Compare(state, player, draw, "HellraiserPommelDrawChain");
        }

        {
            var state = await FreshCombat(player);
            foreach (var enemy in state.Enemies) enemy.SetCurrentHpInternal(1);
            await Add<Hemokinesis>(state, player, PileType.Hand);
            await Add<Hemokinesis>(state, player, PileType.Hand);
            var original = SolverSettings.Current;
            try
            {
                SolverSettings.ApplyForTesting(original with
                {
                    PerformancePreset = SolverPerformancePreset.Custom,
                    SearchTimeLimitSeconds = 3, SearchBeamWidth = 12,
                    SearchMaxExpandedNodes = 3000, SearchMaxCardBranchesPerNode = 8,
                    SearchMaxDegreeOfParallelism = 1, IgnoreLongTermRewards = true
                });
                var root = CombatRootSnapshot.Capture(state);
                var names = SolverDisplayNames.Capture(state);
                var damage = BattleDamageTracker.Observe(state);
                var policy = SolverController.CaptureSearchPolicy(SolverSettings.Capture(), state, false, null);
                var result = await Task.Run(() => new CombatBeamSolver(root, names, damage, policy).Solve());
                result.PreviousProjectedBattleHpLost = 0;
                if (result.ProjectedBattleHpLossIncrease <= 0 || result.CombatEndedTurn != root.StartTurnNumber)
                    throw new InvalidOperationException("Worse-route fixture must find a one-turn win with self damage");
                bool won = false;
                SolverController.SetStopFullAutoOnWorseRecalculation(true, persist: false);
                SolverController.StartPredictedRouteForTesting(host, state, result, _ => won = true);
                if (!SolverController.LastFullAutoStoppedForWorseRecalculationForTesting)
                    throw new InvalidOperationException("Explicit pause preference was ignored");
                checks.Add("ExplicitWorseRoutePauseStillAvailable");
                SolverController.SetStopFullAutoOnWorseRecalculation(false, persist: false);
                SolverController.SetFullAuto(host, state, true);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!won && DateTime.UtcNow < deadline) await Task.Delay(50);
                if (!won || player.Creature.CurrentHp <= 0)
                    throw new InvalidOperationException("Unattended worse route failed to execute to victory");
                checks.Add("WorseRecalculationContinuesToNativeVictoryWhenPauseDisabled");
                results.Add(new { name = "WorseRouteUnattended", passed = true, result.ProjectedBattleHpLossIncrease, won });
            }
            finally
            {
                CombatReplayRecording.TestCombatEndObserver = null;
                SolverSettings.ApplyForTesting(original);
                SolverController.SetStopFullAutoOnWorseRecalculation(original.StopFullAutoOnWorseRecalculation, persist: false);
            }
        }

        File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!, "models.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        if (SolverController.SearchGenerationForTesting != generation)
            throw new InvalidOperationException("Automatic search contaminated model fixture");
        checks.Add("ModelFixtureAutomaticSearchPaused");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));

        async Task<CombatPredictionSimulator> Compare(CombatState state, Player owner, CardModel card, string name)
        {
            var root = CombatRootSnapshot.Capture(state);
            var sim = root.ForkSimulator();
            var predicted = sim.State.GetPlayerCombatState(owner).Hand.Cards.Single(c => ReferenceEquals(c.Original, card));
            if (!sim.AutoPlay(predicted)) throw new InvalidOperationException(name + " simulated play refused");
            var expected = ContinuationStamp.CapturePredicted(owner, sim, root.StartTurnNumber, root.Forecast, root.StartTurnNumber);
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), card, null);
            var actual = ContinuationStamp.CaptureLive(state);
            var differences = expected.DescribeDifferences(actual);
            var pcs = owner.PlayerCombatState!;
            int generatedDazed = pcs.DrawPile.Cards.Count(c => c is Dazed) + pcs.Hand.Cards.Count(c => c is Dazed);
            int autoPlays = sim.History.Entries.OfType<CombatPredictionCardPlayStartedEntry>().Count() - 1;
            bool passed = differences.Count == 0 && (!name.StartsWith("MightyShield") || generatedDazed == 3)
                && (name != "HellraiserPommelDrawChain" || autoPlays >= 5);
            results.Add(new { name, passed, generatedDazed, autoPlays, differences });
            if (passed) checks.Add(name + ":FullStateAndRngMatch");
            else failures.Add(name + ":" + differences.FirstOrDefault());
            return sim;
        }
    }

    private static async Task<CombatState> FreshCombat(Player player)
    {
        var encounter = ModelDb.AllEncounters.Single(e => e.Id.Entry == "CORPSE_SLUGS_WEAK").ToMutable();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Unassigned, encounter);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        if (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play)
            throw new InvalidOperationException("Native fixture player turn did not finish setup");
        var pcs = player.PlayerCombatState!;
        foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile, pcs.PlayPile })
            foreach (var card in pile.Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        foreach (var enemy in state.Enemies) { await CreatureCmd.SetMaxHp(enemy, 500); enemy.SetCurrentHpInternal(500); }
        return state;
    }

    private static async Task<T> Add<T>(CombatState state, Player player, PileType pile) where T : CardModel
    {
        var card = state.CreateCard<T>(player);
        await CardPileCmd.AddGeneratedCardToCombat(card, pile, null);
        return card;
    }
}
