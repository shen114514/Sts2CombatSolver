using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;

internal static class StretchPowerMirrors
{
    internal static void BeforeCardPlayed(StretchPower power, BeforeCardPlayedMirrorContext context)
    {
        if (context.CardPlay.Player.Creature != power.Owner || !context.PreviewCard.GainsBlock) return;
        var state = context.StateStore.Get<State>(power);
        if (state.Play != null) return;
        // Paired state is atomic. A nested choice uses existing whole-action replay.
        context.Simulator.RejectExecutionContinuation();
        state.Play = context.CardPlay;
        state.StartAmount = power.Amount;
    }

    internal static decimal ModifyBlockAdditive(StretchPower power, CombatPredictionSimulator simulator,
        ValueProp props, PredictedCard? source, CardPlay? play)
    {
        if (source?.Preview.Owner.Creature != power.Owner) return 0;
        var state = simulator.StateStore.Get<State>(power);
        if (state.Play != null && state.Play != play) return 0;
        if (!props.IsPoweredCardOrMonsterMoveBlock()) return 0;
        if (play == null) return power.Amount;
        if (state.Play == null || state.Play.Player != source.Preview.Owner) return 0;
        state.GaveBlock = true;
        return power.Amount;
    }

    internal static void AfterCardPlayed(StretchPower power, AfterCardPlayedMirrorContext context)
    {
        var state = context.StateStore.Get<State>(power);
        if (state.Play != context.CardPlay) return;
        int consumed = state.GaveBlock ? state.StartAmount : 0;
        state.Clear();
        if (consumed != 0) ((SimulatedCombatState)context.CombatState).SetPowerAmount(power, power.Amount-consumed);
    }

    internal static void ClearPair(CombatPredictionSimulator simulator, CardPlay play)
    {
        if (!simulator.StateStore.HasEntries<State>()) return;
        foreach (var (_, state) in simulator.StateStore.ReadEntries<State>())
            if (state.Play == play) state.Clear();
    }

    private sealed class State : IPredictionStateForkable, IPredictionForkBoundary
    {
        public CardPlay? Play;
        public int StartAmount;
        public bool GaveBlock;
        public void Clear() { Play = null; StartAmount = 0; GaveBlock = false; }
        public void AssertForkable()
        {
            if (Play != null) throw new InvalidOperationException("Cannot fork while Stretch block-card resolution is pending");
        }
        public object Fork(PredictionForkContext context) { AssertForkable(); return new State(); }
    }
}
