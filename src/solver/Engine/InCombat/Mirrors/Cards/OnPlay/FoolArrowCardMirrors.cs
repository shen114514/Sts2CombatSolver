using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;

internal static class FoolArrowCardMirrors
{
    internal static void MourningBlowOnPlay(MourningBlow card, CardOnPlayMirrorContext context)
    {
        // Transform after AfterCardPlayed, on the last play only. Choices replay the full action.
        context.Simulator.RejectExecutionContinuation();
        context.AttackSingle();
        if (!context.Simulator.HasPendingChoice && context.CardPlay.IsLastInSeries)
            card.TransformIntoAfterPlay = PredictedCard.Create(CanonicalModels.Card<Sadness>(), card.Owner).Preview;
    }

    internal static void FlorescenceOnPlay(Florescence card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        var shots = context.OwnerState.AllCards.Where(c => c.Preview.Tags.Contains(CardTag.Shoot)
            && c.GetPile(context.State)?.Type != PileType.Exhaust).ToArray();
        foreach (var shot in shots)
        {
            context.Simulator.Exhaust(shot);
            if (context.Simulator.HasPendingChoice) return;
        }
        var generated = PredictedCard.Create(CanonicalModels.Card<TheShot>(), card.Owner);
        if (card.IsUpgraded) generated.Upgrade();
        ((TheShot)generated.MutablePreview).BuffFromFlorescence(shots.Length);
        context.Simulator.AddGeneratedCardToCombat(generated, PileType.Hand, card.Owner,
            resultKind: CardGenerationResultKind.Fixed);
    }
    internal static void QuiverOnPlay(Quiver card, CardOnPlayMirrorContext context)
    {
        for (int i = 0; i < card.DynamicVars.Repeat.BaseValue; i++) context.OwnerState.Arrows.GainSlot();
    }

    internal sealed class RecallChoiceState : IPredictionStateForkable
    {
        public bool Recalled;
        public object Fork(PredictionForkContext context) => MemberwiseClone();
    }

    internal static void RecallOnPlay(CardModel card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        var arrows = context.OwnerState.Arrows;
        uint target = context.Target.CombatId!.Value;
        bool hadArrows = arrows.ArrowsOn(target).Count > 0;
        if (card is Recall)
            context.Simulator.StateStore.Get<RecallChoiceState>(context.Card.Original).Recalled = hadArrows;
        if (!hadArrows) return;
        while (arrows.ArrowsOn(target).Count > 0)
        {
            context.Simulator.DislodgeArrow(card.Owner, context.Target, context.Card);
            if (context.Simulator.HasPendingChoice) return;
        }
        if (card is Uproot)
            ((SimulatedCombatState)context.CombatState).Apply<VulnerablePower>(context.Target,
                card.DynamicVars.Vulnerable.IntValue, card.Owner.Creature);
        else if (card is Wrest)
            context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.IntValue);
    }

    internal static void ChoFirmOnPlay(ChoFirm card, CardOnPlayMirrorContext context)
    {
        int count = context.OwnerState.Arrows.AvailableCount > 0 ? 2 : 1;
        for (int i = 0; i < count && !context.Simulator.HasPendingChoice; i++) context.GainBlock(card.Owner.Creature);
    }

    internal static void BowSmackOnPlay(BowSmack card, CardOnPlayMirrorContext context)
    {
        bool impaled = ((SimulatedCombatState)context.CombatState).GetAmount<ImpaledPower>(context.Target) > 0;
        context.AttackSingle();
        if (impaled && !context.Simulator.HasPendingChoice)
            context.Simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.IntValue);
    }

    internal static void ShootOnPlay(CardModel card, CardOnPlayMirrorContext context)
    {
        var sim = context.Simulator;
        var combat = (SimulatedCombatState)context.CombatState;
        if (sim.State.GetCreature(context.Target).IsDead) return;
        int arrow = sim.TryShootArrow(card.Owner, context.Card);
        if (arrow < 0 || sim.HasPendingChoice) return; // Shot hooks still fire with no arrow.
        int historyStart = sim.History.Entries.Count;
        if (card is RainShot)
            RainDamage(card, context);
        else
        {
            var command = (card is TheShot ? DamageCmd.Attack(card.DynamicVars.CalculatedDamage)
                : DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)).FromArrow()
                .FromCard(card, context.CardPlay).Targeting(context.Target);
            if (card is RapidShot) command.WithHitCount(card.DynamicVars.HitCount.IntValue);
            else if (card is MelodicShot) command.WithHitCount((int)context.Calculate(card.DynamicVars["CalculatedHits"]));
            else if (card is RepeatShot) command.WithHitCount(context.Card.ResolveEnergyXValue(context.State));
            command.Simulate(sim);
        }
        if (sim.HasPendingChoice) return; // TryShootArrow rejected partial-command capture.
        if (card is BlossomShot)
        {
            int dealt = 0;
            foreach (var entry in sim.History.EntriesFrom(historyStart))
                if (entry is CombatPredictionDamageReceivedEntry damage && damage.CardSource?.Original == context.Card.Original)
                    dealt += damage.Result.TotalDamage + damage.Result.OverkillDamage;
            combat.Apply<StretchPower>(card.Owner.Creature, dealt, card.Owner.Creature);
        }
        if (sim.HasPendingChoice) return;
        sim.CompleteArrowShot(card.Owner, context.Target, arrow, context.Card);
        if (sim.HasPendingChoice) return;
        if (card is HobblingShot)
        {
            combat.Apply<WeakPower>(context.Target, card.DynamicVars["Power"].IntValue, card.Owner.Creature);
            if (!sim.HasPendingChoice)
                combat.Apply<VulnerablePower>(context.Target, card.DynamicVars["Power"].IntValue, card.Owner.Creature);
        }
    }

    private static void RainDamage(CardModel card, CardOnPlayMirrorContext context)
    {
        var sim = context.Simulator;
        var combat = (SimulatedCombatState)context.CombatState;
        var attack = sim.BeginAttackContext(context.Card, context.CardPlay);
        bool completed = false;
        try
        {
            if (sim.HasPendingChoice) return;
            var first = sim.Damage([context.Target], card.DynamicVars.Damage.BaseValue,
                ValueProp.Move | ValueProp.FromArrow, card.Owner.Creature, context.Card, context.CardPlay);
            if (sim.HasPendingChoice) return;
            sim.AddAttackContextHit(attack, first);
            if (combat.GetPower<AimPower>(card.Owner.Creature) is { } aim) combat.SetPowerAmount(aim, 0);
            if (combat.GetPower<QuietusPower>(card.Owner.Creature) is { } quietus) combat.SetPowerAmount(quietus, 0);
            if (first.FirstOrDefault() is { } hit)
            {
                var targets = combat.HittableEnemies.Where(enemy => enemy != context.Target).ToArray();
                if (targets.Length > 0)
                {
                    var splash = sim.Damage(targets, hit.TotalDamage + hit.OverkillDamage, ValueProp.Move,
                        card.Owner.Creature, context.Card, context.CardPlay, capOnly: true);
                    if (sim.HasPendingChoice) return;
                    sim.AddAttackContextHit(attack, splash);
                }
            }
            completed = true;
        }
        finally { sim.EndAttackContext(attack, completed); }
    }
}
