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

internal static class CatastropheNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "CATASTROPHE";
    internal static async Task RunAsync(NGame host, Player player, List<string> checks)
    {
        if (RunManager.Instance.ShouldSave) throw new InvalidOperationException("Saving fixture refused");
        var results = new List<object>();
        int generation = SolverController.SearchGenerationForTesting;
        foreach (var scenario in new[] { "basic", "upgraded", "nested", "echo-nested", "echo-upgraded-nested", "unplayable", "empty", "empty-upgraded", "uproar", "beatdown" })
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Unassigned,
                ModelDb.AllEncounters.Single(e => e.Id.Entry == "CORPSE_SLUGS_WEAK").ToMutable());
            var end = DateTime.UtcNow.AddSeconds(30);
            while (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play && DateTime.UtcNow < end) await Task.Delay(50);
            var pcs = player.PlayerCombatState ?? throw new InvalidOperationException("Native combat missing");
            if (pcs.Phase != PlayerTurnPhase.Play) throw new InvalidOperationException("Native setup timed out");
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile, pcs.PlayPile })
                foreach (var card in pile.Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            foreach (var enemy in state.Enemies) { await CreatureCmd.SetMaxHp(enemy, 1000); enemy.SetCurrentHpInternal(1000); }
            CardModel outer = scenario == "uproar" ? state.CreateCard<Uproar>(player) :
                scenario == "beatdown" ? state.CreateCard<BeatDown>(player) : state.CreateCard<Catastrophe>(player);
            var target = scenario == "uproar" ? state.Enemies.First() : null;
            if (scenario.Contains("upgraded")) CardCmd.Upgrade(outer);
            await CardPileCmd.AddGeneratedCardToCombat(outer, PileType.Hand, null);
            bool nested = scenario.Contains("nested"), echo = scenario.Contains("echo");
            if (echo) await PowerCmd.Apply<EchoFormPower>(new ThrowingPlayerChoiceContext(), player.Creature, 1, player.Creature, null);
            if (nested)
            {
                for (int i = 0; i < 6; i++) await Add<Hologram>(PileType.Draw);
                for (int i = 0; i < 3; i++) { await Add<Sunder>(PileType.Discard); await Add<Overclock>(PileType.Discard); await Add<Smokestack>(PileType.Discard); }
            }
            else if (scenario == "unplayable") { await Add<Burn>(PileType.Draw); await Add<Dazed>(PileType.Draw); }
            else if (!scenario.StartsWith("empty")) for (int i = 0; i < 8; i++) await Add<StrikeDefect>(scenario == "beatdown" ? PileType.Discard : PileType.Draw);

            var root = CombatRootSnapshot.Capture(state);
            var names = SolverDisplayNames.Capture(state);
            var plans = new List<PlanCardChoice>();
            CombatPredictionSimulator sim;
            while (true)
            {
                if (plans.Count > 12) throw new InvalidOperationException("Nested choice replay did not finish");
                sim = root.ForkSimulator();
                var combat = (SimulatedCombatState)sim.State.CombatState;
                combat.BeginActionChoices(plans);
                sim.AutoPlay(sim.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, outer)), target);
                combat.EndActionChoices();
                if (combat.PendingTurnStartChoice is not { } request) break;
                plans.Add(CardChoiceSupport.BuildChoices(request.Spec!, names, 12, 12).First() with
                { SourceId = request.SourceId, ContextId = request.ContextId, Timing = request.Timing });
            }
            var fork = sim.Fork();
            var expected = ContinuationStamp.CapturePredicted(player, fork, root.StartTurnNumber, root.Forecast, root.StartTurnNumber);
            using (var session = NativeChoiceRuntime.Begin(state, player, "catastrophe-differential:" + scenario))
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                session.SetPlanAndStartDriving(host, plans, cancellation.Token);
                await session.AwaitProducerAndCompleteAsync(CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), outer, target))
                    .WaitAsync(cancellation.Token);
            }
            var differences = expected.DescribeDifferences(ContinuationStamp.CaptureLive(state));
            int outerPlays = sim.History.Entries.OfType<CombatPredictionCardPlayStartedEntry>().Count(p => p.Card.Id == "CATASTROPHE");
            bool passed = differences.Count == 0 && (!nested || plans.Count == outer.DynamicVars.Cards.IntValue * (echo ? 2 : 1));
            results.Add(new { scenario, passed, choices = plans.Count, outerPlays, differences });
            File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!, "models.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException(scenario + ": " + string.Join(" | ", differences.Take(3)) + $" choices={plans.Count}");
            checks.Add("Catastrophe:" + scenario + ":FullStateForkChoicesAndRngMatch");

            async Task Add<T>(PileType pile) where T : CardModel
            { await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<T>(player), pile, null); }
        }
        if (SolverController.SearchGenerationForTesting != generation) throw new InvalidOperationException("Automatic search contaminated fixture");
        checks.Add("CatastropheFixtureAutomaticSearchPaused");
    }
}
