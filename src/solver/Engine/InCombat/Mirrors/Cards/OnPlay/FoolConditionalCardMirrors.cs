using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using CombatSolver.Engine.Common;

namespace CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;

internal static class FoolConditionalCardMirrors
{
    internal static void SteadyCadenceOnPlay(SteadyCadence card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        context.AttackSingle();
        if (context.Simulator.HasPendingChoice) return;
        var variable = (MegaCrit.Sts2.Core.Localization.DynamicVars.CalculatedVar)card.DynamicVars["CalculatedCards"];
        if (!CalculatedVarSpecRegistry.TryCalculate(variable, context.Simulator, context.Card,
                context.Target, out decimal count))
            throw new InvalidOperationException("SteadyCadence has no branch-local draw calculation.");
        context.Simulator.Draw(card.Owner, count);
    }

    internal static void GrowthOnPlay(Growth card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        // Native captures both the cards and the count before any exhaust/draw hooks run.
        var sadness = context.OwnerState.Hand.Cards
            .Where(candidate => candidate.Preview is Sadness).ToArray();
        foreach (var candidate in sadness)
        {
            context.Simulator.Exhaust(candidate);
            if (context.Simulator.HasPendingChoice) return;
        }
        ((SimulatedCombatState)context.CombatState).ApplyPowerFromSource(typeof(StretchPower),
            card.Owner.Creature, (int)(card.DynamicVars.CalculationExtra.BaseValue * sadness.Length),
            card.Owner.Creature, card);
    }

    internal static void JoyOnPlay(Joy card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        context.Simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.IntValue);
        if (!context.Simulator.HasPendingChoice)
            context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.BaseValue);
    }

    internal static void PunchOfJoyOnPlay(PunchOfJoy card, CardOnPlayMirrorContext context)
    {
        // Damage and transform hooks can suspend: replay the complete branch-local action.
        context.Simulator.RejectExecutionContinuation();
        context.AttackSingle();
        if (context.Simulator.HasPendingChoice) return;

        var candidates = context.OwnerState.Hand.Cards
            .Where(candidate => candidate.Preview is Sadness).ToList();
        if (candidates.Count == 0) return;
        // Native StableShuffle sorts before shuffling and consumes the full selection stream.
        CombatBeamSolver.StableShuffleProjection(candidates, context.Rng.CombatCardSelection);
        CardChoiceSupport.ReplaceTransformedCard(context.Simulator, candidates[0],
            PredictedCard.Create(CanonicalModels.Card<Joy>(), card.Owner), CardGenerationResultKind.Fixed);
    }

    internal static void KiRestOnPlay(KiRest card, CardOnPlayMirrorContext context)
    {
        var combat = (SimulatedCombatState)context.CombatState;
        context.Simulator.RejectExecutionContinuation();
        combat.Apply<EnergyNextTurnPower>(card.Owner.Creature, card.DynamicVars.Energy.IntValue, card.Owner.Creature);
        if (!context.Simulator.HasPendingChoice
            && (combat.HasFinishedMelodyThisTurn(card.Owner, CardTag.Wah)
                || combat.HasFinishedMelodyThisTurn(card.Owner, CardTag.Cho)))
            combat.Apply<RetainHandPower>(card.Owner.Creature, 1, card.Owner.Creature);
    }

    internal static void SorrowfulStrikeOnPlay(SorrowfulStrike card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        context.AttackSingle();
        if (!context.Simulator.HasPendingChoice)
            context.Simulator.AddToCombat<Sadness>(card.Owner.Creature, PileType.Hand,
                card.DynamicVars.Cards.IntValue, card.Owner);
    }

    internal static void ApplyOwnerPower<T>(CardModel card, CardOnPlayMirrorContext context, string variable) where T : PowerModel
        => ((SimulatedCombatState)context.CombatState).Apply<T>(card.Owner.Creature,
            card.DynamicVars[variable].IntValue, card.Owner.Creature);

    internal static void YogaOnPlay(Yoga card, CardOnPlayMirrorContext context)
    {
        context.Simulator.RejectExecutionContinuation();
        foreach (var ally in context.State.GetTeammatesOf(card.Owner.Creature))
        {
            if (ally.IsPlayer && context.State.GetCreature(ally).IsAlive)
                ((SimulatedCombatState)context.CombatState).Apply<StretchPower>(ally, card.DynamicVars["StretchPower"].IntValue, card.Owner.Creature);
            if (context.Simulator.HasPendingChoice) return;
        }
    }

    internal static void DeepStretchOnPlay(DeepStretch card, CardOnPlayMirrorContext context)
    {
        // A draw hook can suspend. Replay the complete action rather than lose the preceding buff.
        context.Simulator.RejectExecutionContinuation();
        ApplyOwnerPower<StretchPower>(card, context, "StretchPower");
        if (!context.Simulator.HasPendingChoice)
            context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.IntValue);
    }

    internal static void ChoKiOnPlay(ChoKi card, CardOnPlayMirrorContext context)
    {
        var combat = (SimulatedCombatState)context.CombatState;
        int stretchBeforeBlock = combat.GetAmount<StretchPower>(card.Owner.Creature);
        context.Simulator.RejectExecutionContinuation();
        context.GainBlock(card.Owner.Creature);
        if (!context.Simulator.HasPendingChoice && stretchBeforeBlock > 0)
            combat.Apply<StretchPower>(card.Owner.Creature, stretchBeforeBlock, card.Owner.Creature);
    }

    internal static void BraceOnPlay(Brace card, CardOnPlayMirrorContext context)
    {
        int count = context.Card.ResolveEnergyXValue(context.State);
        context.Simulator.RejectExecutionContinuation();
        // Preserve native repeated applications, including per-application listeners and X modifiers.
        for (int index = 0; index < count; index++)
        {
            ApplyOwnerPower<AimPower>(card, context, "AimPower");
            if (context.Simulator.HasPendingChoice) return;
        }
    }

    private static readonly CardEffectRecipe WahChoRecipe = new([CardEffectKind.Block, CardEffectKind.Attack]);

    public static void WahChoOnPlay(WahCho card, CardOnPlayMirrorContext context)
        => WahChoRecipe.Execute(card, context);

    public static void AllIsWellOnPlay(AllIsWell card, CardOnPlayMirrorContext context)
        => ((SimulatedCombatState)context.CombatState).Apply<AllIsWellPower>(card.Owner.Creature,
            card.DynamicVars["AllIsWellPower"].IntValue, card.Owner.Creature);

    public static void RummageOnPlay(Rummage card, CardOnPlayMirrorContext context)
    {
        // Draw hooks may open a choice. Replay the whole action with the recorded choice;
        // never retain only the draw continuation and drop the following generated card.
        context.Simulator.RejectExecutionContinuation();
        context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.IntValue);
        if (!context.Simulator.HasPendingChoice)
            context.Simulator.AddToCombat<Sadness>(card.Owner.Creature,
                MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand, 1, card.Owner);
    }

    public static void JestOnPlay(Jest card, CardOnPlayMirrorContext context)
    {
        context.GainBlock(card.Owner.Creature);
        if (!context.Simulator.HasPendingChoice)
            context.Simulator.AddToCombat<Sadness>(card.Owner.Creature,
                MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand,
                card.DynamicVars.Cards.IntValue, card.Owner);
    }

    public static void ChoReadyOnPlay(ChoReady card, CardOnPlayMirrorContext context)
    {
        var combat = context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("ChoReady requires simulated combat state.");
        context.GainBlock(card.Owner.Creature);
        if (!context.Simulator.HasPendingChoice)
            combat.Apply<AimPower>(card.Owner.Creature,
                card.DynamicVars["AimPower"].IntValue, card.Owner.Creature);
    }

    public static void KiWhipOnPlay(KiWhip card, CardOnPlayMirrorContext context)
    {
        var combat = context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("KiWhip requires simulated combat state.");
        context.AttackSingle();
        if (!context.Simulator.HasPendingChoice)
            combat.Apply<EnergyNextTurnPower>(card.Owner.Creature,
                card.DynamicVars.Energy.IntValue, card.Owner.Creature);
    }

    public static void NailOnPlay(Nail card, CardOnPlayMirrorContext context)
    {
        var combat = context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("Nail requires simulated combat state.");
        // Vanilla checks before attacking: without an impaled target the entire card does nothing.
        if (combat.GetAmount<ImpaledPower>(context.Target) <= 0)
            return;
        context.AttackSingle();
        if (context.Simulator.HasPendingChoice)
            return;
        int amount = card.DynamicVars["Power"].IntValue;
        combat.Apply<WeakPower>(context.Target, amount, card.Owner.Creature);
        combat.Apply<VulnerablePower>(context.Target, amount, card.Owner.Creature);
    }

    public static void KiHopOnPlay(KiHop card, CardOnPlayMirrorContext context)
    {
        var combat = context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("KiHop requires simulated combat state.");
        // Capture before damage: the hit may dislodge the last arrow or kill its target.
        bool wasImpaled = combat.GetAmount<ImpaledPower>(context.Target) > 0;
        context.AttackSingle();
        if (!context.Simulator.HasPendingChoice && wasImpaled)
            context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.IntValue);
    }
}
