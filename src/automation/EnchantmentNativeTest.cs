using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal static class EnchantmentNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "ENCHANTMENT_CONTRACT";

    internal static async Task RunAsync(NGame host, RunState run, Player player, List<string> checks)
    {
        if (RunManager.Instance.ShouldSave) throw new InvalidOperationException("Saving fixture refused");
        string directory = System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!;
        ContentCompatibilityAudit.Export(directory, player, checks);
        int generation = SolverController.SearchGenerationForTesting;
        var results = new List<object>();
        var deckCard = player.Deck.Cards.First();
        CardCmd.Enchant<Glam>(deckCard, 1m);
        var state = await Fresh();
        Check(SolverController.PredictionFailure == null, "GlamDeckNativeCombatEntryAndFirstPlayPhase", checks);
        _ = CombatRootSnapshot.Capture(state);
        Check(true, "GlamDeckRootCaptureWithoutObsoleteField", checks);

        // Same native card is played twice. The second play must not replay Glam.
        var card = state.CreateCard<DefendIronclad>(player);
        CardCmd.Enchant<Glam>(card, 1m);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, null);
        await Compare(card, "GlamFirstPlay", 10);
        Check(card.Enchantment!.Status == EnchantmentStatus.Disabled, "GlamDisablesAfterFirstSeries", checks);
        var disabled = EnchantmentStateSupport.Describe(card.Enchantment);
        var unused = ModelDb.Enchantment<Glam>().ToMutable(); unused.Status = EnchantmentStatus.Normal;
        Check(disabled != EnchantmentStateSupport.Describe(unused), "GlamUsedStateDistinguishesFingerprints", checks);
        await CardPileCmd.Add(card, PileType.Hand, skipVisuals: true);
        await Compare(card, "GlamReturnedCardSecondPlay", 15);

        // Momentum's genuinely mutable private field remains part of the key.
        var momentum = (Momentum)ModelDb.Enchantment<Momentum>().ToMutable();
        string before = EnchantmentStateSupport.Describe(momentum);
        momentum._extraDamage++;
        Check(before != EnchantmentStateSupport.Describe(momentum), "MomentumExtraDamageDistinguishesFingerprints", checks);

        // Inject the original exception family at the optional checkpoint boundary.
        bool checkpointThrown = false;
        CombatBugReportExporter.TestBeforeCheckpointCapture = label =>
        {
            if (label != "combat_start") return;
            checkpointThrown = true;
            throw new TypeInitializationException("InjectedDiagnosticSchema", new MissingFieldException("InjectedMissingField"));
        };
        try { state = await Fresh(); }
        finally { CombatBugReportExporter.TestBeforeCheckpointCapture = null; }
        Check(checkpointThrown && SolverController.PredictionFailure == null,
            "CheckpointTypeInitializerFailureDoesNotFaultNativeCombatEntry", checks);

        // Independently fail essential solver setup inside the real native event.
        bool setupThrown = false;
        SolverController.TestBeforeBeginCombat = () =>
        {
            setupThrown = true;
            throw new MissingFieldException("InjectedSolverSetupFailure");
        };
        try { state = await Fresh(); }
        finally { SolverController.TestBeforeBeginCombat = null; }
        Check(setupThrown && !SolverController.FullAutoEnabled && !SolverController.IsDeploying,
            "SolverSetupFailureIsolatedNativeCombatStillReachesPlay", checks);
        var manual = state.CreateCard<DefendIronclad>(player);
        await CardPileCmd.AddGeneratedCardToCombat(manual, PileType.Hand, null);
        await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), manual, null);
        Check(player.Creature.Block == 5, "NativeCardPlayStillWorksAfterSolverSetupFailure", checks);

        state = await Fresh();
        Check(SolverController.PredictionFailure == null, "NextCombatClearsPriorFailureWithoutReusingPlan", checks);
        Check(player.PlayerCombatState!.AllCards.Any(c => c.Enchantment is Glam { Status: EnchantmentStatus.Normal }),
            "GlamResetsInNextNativeCombat", checks);
        Check(SolverController.SearchGenerationForTesting == generation, "ContractFixtureAutomaticSearchPaused", checks);

        // Real relic modification -> three-card UI -> autopilot -> next combat.
        // Use the same three base cards as the reported reward, not a one-card shortcut.
        await RunManager.Instance.EnterRoomDebug(RoomType.Map, showTransition: false);
        NMapScreen.Instance?.Close(false);
        var relic = await RelicCmd.Obtain<SilkenTress>(player);
        var creations = new CardModel[] { run.CreateCard<GunkUp>(player), run.CreateCard<Feral>(player), run.CreateCard<CompileDriver>(player) }
            .Select(c => new CardCreationResult(c)).ToList();
        var creationOptions = CardCreationOptions.ForRoom(player, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
        Check(relic.TryModifyCardRewardOptionsLate(player, creations, creationOptions), "SilkenTressNativeRewardModificationApplied", checks);
        await relic.AfterModifyingCardRewardOptions();
        var offered = creations.Select(c => c.Card).ToArray();
        Check(offered.All(c => c.Enchantment is Glam), "AllThreeNativeRewardCandidatesHaveGlam", checks);
        int deckCount = player.Deck.Cards.Count;
        SolverSettings.ApplyForTesting(SolverSettings.Current with { AutoRewardsEnabled = true });
        try
        {
            var rewardTask = new RewardsSet(player).WithCustomRewards([
                new CardReward(offered, CardCreationSource.Encounter, player, creationOptions)]).Offer();
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (!rewardTask.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(100);
            if (!rewardTask.IsCompleted) throw new InvalidOperationException("Glam reward automation timed out: " + RoomAutomation.Status);
            await rewardTask;
            Check(player.Deck.Cards.Count == deckCount + 1 && offered.Any(player.Deck.Cards.Contains),
                "ThreeGlamRewardsAutomaticallyChooseAndAddNativeCardManualResponsesZero", checks);
        }
        finally { SolverSettings.ApplyForTesting(SolverSettings.Current with { AutoRewardsEnabled = false }); }
        NMapScreen.Instance?.Close(false);
        state = await Fresh();
        _ = CombatRootSnapshot.Capture(state);
        Check(SolverController.PredictionFailure == null, "AfterGlamRewardNextNativeCombatReachesPlayAndCapturesRoot", checks);
        File.WriteAllText(Path.Combine(directory, "enchantment-models.json"), JsonSerializer.Serialize(results,
            new JsonSerializerOptions { WriteIndented = true }));

        async Task<CombatState> Fresh()
        {
            var encounter = ModelDb.AllEncounters.Single(e => e.Id.Entry == "CORPSE_SLUGS_WEAK").ToMutable();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Unassigned, encounter);
            var end = DateTime.UtcNow.AddSeconds(30);
            while (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play && DateTime.UtcNow < end) await Task.Delay(50);
            if (player.PlayerCombatState?.Phase != PlayerTurnPhase.Play) throw new InvalidOperationException("Native Play not reached");
            return CombatManager.Instance.DebugOnlyGetState()!;
        }
        async Task Compare(CardModel candidate, string name, int expectedBlock)
        {
            var pcs = player.PlayerCombatState!;
            // Keep all native initial piles: compare the entire continuation stamp.
            var root = CombatRootSnapshot.Capture(state);
            var sim = root.ForkSimulator();
            var predicted = sim.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, candidate));
            if (!sim.AutoPlay(predicted)) throw new InvalidOperationException(name + " predicted play refused");
            var fork = sim.Fork();
            var expected = ContinuationStamp.CapturePredicted(player, fork, root.StartTurnNumber, root.Forecast, root.StartTurnNumber);
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), candidate, null);
            var differences = expected.DescribeDifferences(ContinuationStamp.CaptureLive(state));
            results.Add(new { name, differences, block = player.Creature.Block, expectedBlock });
            Check(differences.Count == 0 && player.Creature.Block == expectedBlock, name + ":FullStateForkAndRngMatch", checks);
        }
    }

    private static void Check(bool passed, string name, List<string> checks)
    { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
}
