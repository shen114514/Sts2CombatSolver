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

namespace CombatSolver;

internal static class ShiniesDoubleBossNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "SHINIES_DOUBLE_BOSS";
    internal static async Task RunAsync(NGame host, RunState run, Player player, List<string> checks)
    {
        if (RunManager.Instance.ShouldSave || run.AscensionLevel != 10) throw new InvalidOperationException("Non-saving A10 fixture required");
        var results = new List<object>();
        int generation = SolverController.SearchGenerationForTesting;
        foreach (var sample in new[] { (0,false,0), (19,false,0), (20,false,0), (39,false,0), (40,false,0),
            (199,false,0), (200,false,0), (9,true,0), (10,true,0), (19,true,0), (20,true,0), (200,true,3) })
        {
            var state = await Fresh("CORPSE_SLUGS_WEAK", RoomType.Monster);
            await PlayerCmd.LoseGold(player.Gold, player);
            await PlayerCmd.GainGold(sample.Item1, player);
            if (player.Gold != sample.Item1) throw new InvalidOperationException("Gold fixture modified by relics");
            var pcs = player.PlayerCombatState!;
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile, pcs.PlayPile })
                foreach (var card in pile.Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals:true);
            var target = state.Enemies.First();
            await CreatureCmd.SetMaxHp(target, 1000); target.SetCurrentHpInternal(1000);
            if (sample.Item3 > 0) await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), player.Creature, sample.Item3, player.Creature, null);
            var shinies = state.CreateCard<MyShinies>(player);
            if (sample.Item2) CardCmd.Upgrade(shinies);
            await CardPileCmd.AddGeneratedCardToCombat(shinies, PileType.Hand, null);
            var root = CombatRootSnapshot.Capture(state);
            var sim = root.ForkSimulator();
            if (!sim.AutoPlay(sim.State.GetPlayerCombatState(player).Hand.Cards.Single(c => ReferenceEquals(c.Original, shinies)), target))
                throw new InvalidOperationException("Shinies simulation refused");
            var expected = ContinuationStamp.CapturePredicted(player, sim.Fork(), root.StartTurnNumber, root.Forecast, root.StartTurnNumber);
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), shinies, target);
            var differences = expected.DescribeDifferences(ContinuationStamp.CaptureLive(state));
            int hits = sample.Item1 / (sample.Item2 ? 10 : 20), damage = 1000 - target.CurrentHp;
            bool passed = differences.Count == 0 && damage == hits * (1 + sample.Item3);
            results.Add(new { gold=sample.Item1, upgraded=sample.Item2, strength=sample.Item3, hits, damage, passed, differences });
            Save();
            if (!passed) throw new InvalidOperationException("MyShinies " + sample + ": " + string.Join(" | ", differences.Take(2)));
            checks.Add($"MyShinies:Gold={sample.Item1}:Upgrade={sample.Item2}:Strength={sample.Item3}:NativeDamageForkAndRngMatch");
        }
        run.CurrentActIndex = 2;
        var first = ModelDb.AllEncounters.Single(e => e.Id.Entry == "TEST_SUBJECT_BOSS").ToMutable();
        var second = ModelDb.AllEncounters.Single(e => e.Id.Entry == "AEONGLASS_BOSS").ToMutable();
        run.Act.SetBossEncounter(first); run.Act.SetSecondBossEncounter(second);
        foreach (var sample in new[] { ("CORPSE_SLUGS_WEAK",RoomType.Monster,PotionSpendMode.Normal,true),
            (first.Id.Entry,RoomType.Boss,PotionSpendMode.DoubleBoss,false),
            (second.Id.Entry,RoomType.Boss,PotionSpendMode.SpendAll,true) })
        {
            var state = await Fresh(sample.Item1,sample.Item2);
            var root = CombatRootSnapshot.Capture(state);
            var policy = SolverController.CaptureSearchPolicy(SolverSettings.Capture(),state,false,null);
            bool passed = root.PotionSpendMode == sample.Item3 && policy.StopAtAcceptableBattleHpLoss == sample.Item4;
            results.Add(new { encounter=sample.Item1, expected=sample.Item3.ToString(), actual=root.PotionSpendMode.ToString(),
                reserve=root.PotionSpend.ReserveHp, stopAtTarget=policy.StopAtAcceptableBattleHpLoss, passed });
            Save();
            if (!passed) throw new InvalidOperationException("Double boss context mismatch: " + sample.Item1);
            if (sample.Item3==PotionSpendMode.DoubleBoss && (root.BossHpRelief!=BossHpRelief.None || root.PotionSpend.ReserveHp>=PotionSpendPolicy.ConserveReserveHp))
                throw new InvalidOperationException("First boss still discounts HP or prioritizes conserving potions");
            checks.Add("A10:"+sample.Item1+":HpAndPotionPolicyMatched");
        }
        if(SolverController.SearchGenerationForTesting!=generation)throw new InvalidOperationException("Automatic search contaminated fixture");
        checks.Add("ShiniesDoubleBossAutomaticSearchPaused");
        void Save()=>File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!,"models.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        async Task<CombatState> Fresh(string encounter,RoomType room)
        {
            await RunManager.Instance.EnterRoomDebug(room,MegaCrit.Sts2.Core.Map.MapPointType.Unassigned,
                ModelDb.AllEncounters.Single(e=>e.Id.Entry==encounter).ToMutable());
            var end=DateTime.UtcNow.AddSeconds(30);
            while(player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play&&DateTime.UtcNow<end)await Task.Delay(50);
            if(player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play)throw new InvalidOperationException("Combat setup timeout");
            return CombatManager.Instance.DebugOnlyGetState()!;
        }
    }
}
