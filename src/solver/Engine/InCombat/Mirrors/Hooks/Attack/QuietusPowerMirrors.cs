using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver.Engine.InCombat.Mirrors.Hooks.Attack;

internal static class QuietusPowerMirrors
{
    internal static void BeforeAttack(QuietusPower power, BeforeAttackMirrorContext context)
    {
        var command = context.Command;
        if (power.Amount > 0 && command.Attacker == power.Owner && command.DamageProps.IsPoweredAttack()
            && command.DamageProps.HasFlag(ValueProp.FromArrow) && command.ModelSource is null or CardModel)
        {
            var state = context.StateStore.Get<State>(power);
            state.Command ??= command;
        }
    }
    internal static decimal ModifyDamage(QuietusPower power, ModifyDamageMirrorContext context)
    {
        if (power.Amount <= 0 || context.Dealer != power.Owner || !context.Props.IsPoweredAttack()
            || !context.Props.HasFlag(ValueProp.FromArrow)) return 1;
        var state = context.StateStore.Get<State>(power);
        return state.Command != null && context.CardSource != null && !context.CardSource.References(state.Command.ModelSource)
            ? 1 : power.Amount;
    }
    internal static void AfterAttack(QuietusPower power, AfterAttackMirrorContext context)
        => CompleteOrAbort(power, context, true);
    internal static void CompleteOrAbort(QuietusPower power, AfterAttackMirrorContext context, bool completed)
    {
        var state = context.StateStore.Get<State>(power);
        if (!ReferenceEquals(state.Command, context.Command)) return;
        if (completed) ((SimulatedCombatState)context.CombatState).SetPowerAmount(power, 0);
        state.Command = null;
    }
    internal static void ClearPair(CombatPredictionSimulator simulator, AttackCommand command)
    {
        if (!simulator.StateStore.HasEntries<State>()) return;
        foreach (var (_, state) in simulator.StateStore.ReadEntries<State>())
            if (ReferenceEquals(state.Command, command)) state.Command = null;
    }

    private sealed class State : IPredictionStateForkable, IPredictionForkBoundary
    {
        public AttackCommand? Command;
        public object Fork(PredictionForkContext context) { AssertForkable(); return MemberwiseClone(); }
        public void AssertForkable()
        {
            if (Command != null) throw new InvalidOperationException("Quietus attack is still resolving");
        }
    }
}
