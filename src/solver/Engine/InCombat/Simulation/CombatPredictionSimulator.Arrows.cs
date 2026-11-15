using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Arrows;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CombatSolver.Engine.InCombat.Simulation;

internal sealed partial class CombatPredictionSimulator
{
    internal int TryShootArrow(Player player, PredictedCard source)
    {
        // A nested native choice replays the whole action with its resolved choices. Never
        // fork a half-completed arrow command or lose its post-choice retrieval/impaling.
        RejectExecutionContinuation();
        int index = State.GetPlayerCombatState(player).Arrows.TryShoot();
        ArrowEventMirrors.Invoke(ArrowEventKind.Shot, this, player, null, index, source);
        return index;
    }

    internal void CompleteArrowShot(Player player, Creature target, int index, PredictedCard source)
    {
        RejectExecutionContinuation();
        var combat = (SimulatedCombatState)State.CombatState;
        var arrows = State.GetPlayerCombatState(player).Arrows;
        uint id = target.CombatId ?? throw new InvalidOperationException("Arrow target has no combat identity");
        arrows.Impale(id, index);
        if (State.GetCreature(target).IsAlive && combat.GetAmount<ImpaledPower>(target) == 0)
            combat.Apply<ImpaledPower>(target, 1, player.Creature);
        SyncImpaled(player, target);
        // The AttackCommand overload passes null to this hook; RainShot's callback overload passes its card.
        ArrowEventMirrors.Invoke(ArrowEventKind.Impaled, this, player, target, index,
            source.Preview is MegaCrit.Sts2.Core.Models.Cards.RainShot ? source : null);
        if (HasPendingChoice) return;
        if (arrows[index].DestroyAfterImpaled)
        {
            arrows.Destroy(index);
            SyncImpaled(player, target);
        }
        else if (State.GetCreature(target).IsDead && arrows.ArrowsOn(id).Contains(index))
            DislodgeArrow(player, target, source);
    }

    internal void DislodgeArrow(Player player, Creature target, PredictedCard? source)
    {
        RejectExecutionContinuation();
        var arrows = State.GetPlayerCombatState(player).Arrows;
        int index = arrows.DetachFirst(target.CombatId!.Value);
        if (index < 0) return;
        if (arrows[index].DestroyAfterImpaled)
            arrows.Destroy(index);
        else
        {
            ArrowEventMirrors.Invoke(ArrowEventKind.Dislodged, this, player, target, index, source);
            if (HasPendingChoice) return;
            arrows.Retrieve(index);
        }
        SyncImpaled(player, target);
    }

    internal void SyncImpaled(Player player, Creature target)
    {
        var combat = (SimulatedCombatState)State.CombatState;
        if (combat.GetMutablePower<ImpaledPower>(target) is not { } power) return;
        var arrows = State.GetPlayerCombatState(player).Arrows;
        uint id = target.CombatId!.Value;
        if (arrows.ArrowsOn(id).Count == 0)
            combat.SetPowerAmount(power, 0);
        else
            power.HitCounter = arrows.HitCounter(id);
    }
}
