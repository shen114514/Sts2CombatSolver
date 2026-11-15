using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace CombatSolver.Engine.InCombat.Simulation;

internal static class ArrowStateCapture
{
    internal static bool ReadYahTriggered(YahPower power)
    {
        object? data = typeof(PowerModel).GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(power);
        if (data == null) return false;
        return (bool)(data.GetType().GetField("triggeredThisTurn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException("YahPower.Data.triggeredThisTurn")).GetValue(data)!;
    }
    public static ArrowBranchState Capture(PlayerCombatState state)
    {
        var live = state.Arrows;
        ArrowBranchState result = new(live.Select(arrow => new ArrowBranchState.Slot(
            Enum.Parse<BranchArrowStatus>(arrow.Status.ToString()), arrow.DestroyAfterImpaled)));
        var player = state._player;
        result.YahTriggered = player.Creature.Powers.OfType<YahPower>().Any(ReadYahTriggered);
        if (player.Creature.CombatState is { } combat)
            foreach (var creature in combat.Creatures)
                foreach (ImpaledPower power in creature.Powers.OfType<ImpaledPower>().Where(p => p.Applier == player.Creature))
                    foreach (var arrow in power.Arrows)
                    {
                        int index = -1;
                        for (int i = 0; i < live.Count; i++) if (ReferenceEquals(live[i], arrow)) { index = i; break; }
                        if (index < 0 || creature.CombatId == null)
                            throw new InvalidOperationException("Impaled arrow is absent from the owner's captured slots");
                        result.Impale(creature.CombatId.Value, index, power.HitCounter);
                    }
        return result;
    }
}
