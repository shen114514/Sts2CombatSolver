using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

/// <summary>
/// Exact branch-local multipliers for vanilla calculated card variables. This replaces invocation
/// of delegates bound to the live combat graph, which is not valid after a prediction branch diverges.
/// </summary>
internal static class CalculatedVarSpecRegistry
{
    public static IReadOnlyCollection<Type> SupportedTypes { get; } =
    [
        typeof(MyShinies), typeof(PreciseCut), typeof(Stack), typeof(Squeeze), typeof(Mirage), typeof(Rattle), typeof(MindBlast),
        typeof(GangUp), typeof(Mimic), typeof(KnifeTrap), typeof(Unleash), typeof(Radiate), typeof(PerfectedStrike),
        typeof(SovereignBlade), typeof(Supermassive), typeof(Sacrifice), typeof(TimesUp), typeof(MementoMori),
        typeof(SoulStorm), typeof(Voltaic), typeof(TearAsunder), typeof(ExpectAFight), typeof(HelixDrill),
        typeof(PullFromBelow), typeof(Normality), typeof(Synchronize), typeof(Protector), typeof(NoEscape),
        typeof(Flechettes), typeof(Rend), typeof(GoldAxe), typeof(FlakCannon), typeof(LunarBlast), typeof(Murder),
        typeof(CompileDriver), typeof(Finisher), typeof(Bully), typeof(BeatIntoShape), typeof(DeathMarch),
        typeof(DemonicShield), typeof(Barrage), typeof(CrescentSpear), typeof(BodySlam), typeof(AshenStrike),
        // 愚者（0.113）：7 张卡用了计算型数值，不登记就会让整场搜索 faulted。
        typeof(HarmonicShield), typeof(MelodicShot), typeof(RhythmStrike), typeof(SteadyCadence),
        typeof(WahSymphony), typeof(Anguish), typeof(Growth), typeof(TheShot),
    ];

    public static IReadOnlyDictionary<Type, string> EvidenceByType { get; }
        = SupportedTypes.ToDictionary(type => type, type => type == typeof(Murder)
            ? "MURDER-ROOT-HISTORY"
            : "CALCULATED-CARD-BATCH-136");

    public static bool TryCalculate(
        CalculatedVar calculatedVar,
        CombatPredictionSimulator simulator,
        PredictedCard card,
        Creature? target,
        out decimal value)
    {
        string key = card.Preview.DynamicVars
            .FirstOrDefault(pair => ReferenceEquals(pair.Value, calculatedVar)).Key;
        if (string.IsNullOrEmpty(key)
            || !TryMultiplier(simulator, card, target, out decimal multiplier))
        {
            value = 0m;
            return false;
        }
        DynamicVar baseVar = card.Preview.DynamicVars.CalculationBase;
        DynamicVar extraVar = card.Preview.DynamicVars.TryGetValue("CalculationExtra", out DynamicVar? extra)
            ? extra
            : card.Preview.DynamicVars.ExtraDamage;
        value = baseVar.BaseValue + extraVar.BaseValue * multiplier;
        return true;
    }

    private static bool TryMultiplier(
        CombatPredictionSimulator simulator,
        PredictedCard card,
        Creature? target,
        out decimal multiplier)
    {
        CardModel model = card.Preview;
        SimPlayerCombatState playerState = simulator.State.GetPlayerCombatState(model.Owner);
        SimulatedCombatState combat = (SimulatedCombatState)simulator.State.CombatState;
        Creature owner = card.Preview.Owner.Creature;
        multiplier = model switch
        {
            TheShot shot => shot.CardsExhaustedByFlorescence,
            MyShinies => (decimal)combat.GetPlayerGold(model.Owner) / model.DynamicVars.Gold.BaseValue,
            PreciseCut => -playerState.Hand.Cards.Count,
            Stack => playerState.DiscardPile.Cards.Count,
            Squeeze => playerState.AllCards.Count(candidate =>
                candidate.Preview.Tags.Contains(CardTag.OstyAttack) && !candidate.References(model)),
            Mirage => combat.Enemies
                .Where(enemy => simulator.State.GetCreature(enemy).IsAlive)
                .Sum(enemy => combat.GetAmount<PoisonPower>(enemy)),
            Rattle => 1 + (simulator.State.GetOsty(model.Owner) is { } osty
                ? combat.GetCreatureAttacksThisTurn(osty)
                : 0),
            MindBlast => playerState.DrawPile.Cards.Count,
            GangUp => target == null ? 0 : combat.Creatures
                .Where(creature => creature != owner && creature.Side == owner.Side)
                .Sum(creature => combat.GetPoweredAttackHitsThisTurn(creature, target)),
            Mimic => target == null ? 0 : simulator.State.GetCreature(target).Block,
            KnifeTrap => playerState.ExhaustPile.Cards.Count(candidate => candidate.Preview.Tags.Contains(CardTag.Shiv)),
            Unleash => simulator.State.GetOsty(model.Owner) is { } unleashOsty
                && simulator.State.GetCreature(unleashOsty).IsAlive
                ? simulator.State.GetCreature(unleashOsty).CurrentHp
                : 0,
            Radiate => combat.GetStarsGainedThisTurn(model.Owner),
            PerfectedStrike => playerState.AllCards.Count(candidate => candidate.Preview.Tags.Contains(CardTag.Strike)),
            SovereignBlade => combat.GetAmount<ParryPower>(owner),
            Supermassive => CountGeneratedCards(simulator, model.Owner),
            Sacrifice => simulator.State.GetOsty(model.Owner) is { } sacrificeOsty
                && simulator.State.GetCreature(sacrificeOsty).IsAlive
                ? combat.GetOstyMaxHp(simulator, model.Owner) * 3
                : 0,
            TimesUp => target == null ? 0 : combat.GetAmount<DoomPower>(target),
            MementoMori => combat.GetCardsDiscardedThisTurn(owner),
            SoulStorm => playerState.ExhaustPile.Cards.Count(candidate => candidate.Preview is Soul),
            Voltaic => CountLightningChannels(simulator, model.Owner),
            TearAsunder => 1 + CountUnblockedDamageEvents(simulator, owner),
            ExpectAFight => Math.Max(0, combat.GetAmount<StrengthPower>(owner)),
            HelixDrill => Math.Max(0, combat.GetEnergySpentThisTurn(model.Owner)
                - card.GetEnergyCostWithModifiers(simulator, playerState)),
            PullFromBelow => CountEtherealPlays(simulator, model.Owner),
            Normality => Math.Min(3, combat.GetCardPlayStartsThisTurn(owner)),
            Synchronize or CompileDriver => playerState.OrbQueue.Orbs.Select(orb => orb.Id).Distinct().Count(),
            Protector => simulator.State.GetOsty(model.Owner) is { } protectorOsty
                && simulator.State.GetCreature(protectorOsty).IsAlive
                ? combat.GetOstyMaxHp(simulator, model.Owner)
                : 0,
            NoEscape => target == null
                ? 0
                : Math.Floor((decimal)combat.GetAmount<DoomPower>(target)
                    / model.DynamicVars["DoomThreshold"].BaseValue),
            Flechettes => playerState.Hand.Cards.Count(candidate => candidate.Preview.Type == CardType.Skill),
            Rend => target == null ? 0 : combat.EffectivePowers().Count(power =>
                power.Owner == target
                && power.TypeForCurrentAmount == PowerType.Debuff
                && power is not ITemporaryPower),
            GoldAxe => CountFinishedCardPlays(simulator),
            FlakCannon => playerState.AllCards.Count(candidate =>
                candidate.Preview.Type == CardType.Status
                && candidate.GetPile(simulator.State)?.Type != PileType.Exhaust),
            LunarBlast => combat.GetSkillCardsPlayedThisTurn(owner),
            Murder => CountDrawnCards(simulator, model.Owner),
            Finisher => combat.GetAttacksPlayedThisTurn(owner),
            Bully => target == null ? 0 : combat.GetAmount<VulnerablePower>(target),
            BeatIntoShape => target == null ? 0 : combat.GetPoweredAttackHitsThisTurn(owner, target),
            DeathMarch => combat.GetNonHandDrawsThisTurn(model.Owner),
            DemonicShield or BodySlam => simulator.State.GetCreature(owner).Block,
            Barrage => playerState.OrbQueue.Orbs.Count,
            CrescentSpear => playerState.AllCards.Count(candidate =>
                candidate.Preview.CanonicalStarCost >= 0 || candidate.Preview.HasStarCostX),
            AshenStrike => playerState.ExhaustPile.Cards.Count,
            // ---- 愚者（0.113）----
            // 谐波护盾(格挡) / 旋律射击(命中) / 节奏打击(命中) / 平稳韵律(抽牌)：都按「本回合打出的旋律种类数」。
            HarmonicShield or MelodicShot or RhythmStrike or SteadyCadence
                => combat.GetMelodiesPlayedThisTurn(model.Owner),
            // 哇 交响乐：牌堆里每有一张带哇/啾/叽的牌就 +4 伤害。
            WahSymphony => playerState.AllCards.Count(candidate => HasMelodyTag(candidate.Preview)),
            // 苦恼：本场战斗里每生成过一张悲伤，伤害 +6。
            Anguish => CountGeneratedSadness(simulator, model.Owner),
            // 生长：消耗手牌里的所有悲伤，每张获得拉伸。
            Growth => playerState.Hand.Cards.Count(candidate => candidate.Preview is Sadness),
            _ => 0m,
        };
        return model is MyShinies or PreciseCut or Stack or Squeeze or Mirage or Rattle or MindBlast or GangUp
            or Mimic or KnifeTrap or Unleash or Radiate or PerfectedStrike or SovereignBlade
            or Supermassive or Sacrifice or TimesUp or MementoMori or SoulStorm or Voltaic
            or TearAsunder or ExpectAFight or HelixDrill or PullFromBelow or Normality
            or Synchronize or Protector or NoEscape or Flechettes or Rend or GoldAxe or FlakCannon
            or LunarBlast or Murder or CompileDriver or Finisher or Bully or BeatIntoShape
            or DeathMarch or DemonicShield or Barrage or CrescentSpear or BodySlam or AshenStrike
            or HarmonicShield or MelodicShot or RhythmStrike or SteadyCadence or WahSymphony
            or Anguish or Growth or TheShot;
    }

    /// <summary>牌上是否带「旋律」标签（哇/啾/叽）。</summary>
    private static bool HasMelodyTag(CardModel card)
    {
        try
        {
            return card.Tags.Contains(CardTag.Wah)
                || card.Tags.Contains(CardTag.Cho)
                || card.Tags.Contains(CardTag.Ki);
        }
        catch { return false; }
    }

    /// <summary>本场战斗里已经生成过多少张「悲伤」（战前快照 + 本次搜索的分叉历史）。</summary>
    private static int CountGeneratedSadness(CombatPredictionSimulator simulator, Player player)
        => ((SimulatedCombatState)simulator.State.CombatState)
               .CountGeneratedCardsBeforePrediction(player, card => card is Sadness)
           + simulator.History.OfType<CombatPredictionCardGeneratedEntry>()
               .Count(entry => entry.Creator == player && entry.Card.Original is Sadness);

    private static int CountGeneratedCards(CombatPredictionSimulator simulator, Player player)
        => ((SimulatedCombatState)simulator.State.CombatState).GetCardsGeneratedBeforePrediction(player)
           + simulator.History.OfType<CombatPredictionCardGeneratedEntry>().Count(entry => entry.Creator == player);

    internal static int CountLightningChannels(CombatPredictionSimulator simulator, Player player)
        => ((SimulatedCombatState)simulator.State.CombatState).GetLightningChannelsBeforePrediction(player)
           + simulator.History.OfType<CombatPredictionOrbChanneledEntry>()
               .Count(entry => entry.Orb is LightningOrb && entry.Orb.Owner == player);

    private static int CountUnblockedDamageEvents(CombatPredictionSimulator simulator, Creature owner)
        => ((SimulatedCombatState)simulator.State.CombatState).GetUnblockedHitsBeforePrediction(owner)
           + simulator.History.OfType<CombatPredictionDamageReceivedEntry>()
               .Count(entry => entry.Receiver == owner && entry.Result.UnblockedDamage > 0);

    private static int CountEtherealPlays(CombatPredictionSimulator simulator, Player player)
        => ((SimulatedCombatState)simulator.State.CombatState).GetEtherealPlaysBeforePrediction(player)
           + simulator.History.OfType<CombatPredictionCardPlayFinishedEntry>()
               .Count(entry => entry.CardPlay.Player == player && entry.WasEthereal);

    private static int CountFinishedCardPlays(CombatPredictionSimulator simulator)
        => ((SimulatedCombatState)simulator.State.CombatState).GetFinishedPlaysBeforePrediction()
           + simulator.History.OfType<CombatPredictionCardPlayFinishedEntry>().Count();

    private static int CountDrawnCards(CombatPredictionSimulator simulator, Player player)
        => ((SimulatedCombatState)simulator.State.CombatState).GetCardsDrawnBeforePrediction(player)
           + simulator.History.OfType<CombatPredictionCardDrawnEntry>()
               .Count(entry => entry.Card.Owner == player);
}
