using CombatSolver.Engine.Common;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace CombatSolver.Engine.InCombat.Mirrors.Hooks.Arrows;
using Registry = MethodMirrorRegistry<AbstractModel, ArrowEventMirrorContext>;

internal enum ArrowEventKind { Shot, Impaled, Dislodged }

internal static class ArrowEventMirrors
{
    private static readonly MirrorMethodSpec AfterArrowShot = MirrorMethodSpec.Hook(
        nameof(AbstractModel.AfterArrowShot), [typeof(Player), typeof(ArrowSlot), typeof(PlayerChoiceContext), typeof(CardModel)]);
    private static readonly MirrorMethodSpec AfterArrowImpaled = MirrorMethodSpec.Hook(
        nameof(AbstractModel.AfterArrowImpaled), [typeof(ImpaledPower), typeof(ArrowSlot), typeof(PlayerChoiceContext), typeof(CardModel)]);
    private static readonly MirrorMethodSpec AfterArrowDislodged = MirrorMethodSpec.Hook(
        nameof(AbstractModel.AfterArrowDislodged), [typeof(ImpaledPower), typeof(ArrowSlot), typeof(PlayerChoiceContext), typeof(CardModel)]);
    private static readonly Registry Shot = CreateShot();
    private static readonly Registry Impaled = CreateImpaled();
    private static readonly Registry Dislodged = CreateDislodged();

    internal static void Invoke(ArrowEventKind kind, CombatPredictionSimulator simulator, Player shooter,
        Creature? target, int index, PredictedCard? source)
    {
        var combat = (SimulatedCombatState)simulator.State.CombatState;
        var context = new ArrowEventMirrorContext { Simulator = simulator, Shooter = shooter,
            Target = target, Index = index, Source = source };
        var registry = kind == ArrowEventKind.Shot ? Shot : kind == ArrowEventKind.Impaled ? Impaled : Dislodged;
        foreach (var listener in combat.IterateHookListeners().ToArray())
        {
            registry.Invoke(listener, context);
            if (simulator.HasPendingChoice) return;
        }
    }

    private static Registry CreateShot()
    {
        var registry = new Registry(AfterArrowShot);
        registry.Register<QuietusPower>((power, c) => {
            if (c.Index >= 0 && c.Source != null && power.Owner == c.Shooter.Creature)
                c.State.GetPlayerCombatState(c.Shooter).Arrows.MarkForDestruction(c.Index);
        });
        // The solver admits one player only. Vanilla CoveringFire affects other players.
        registry.RegisterIgnored<CoveringFirePower>();
        registry.Register<WhistlingArrows>((card, c) => {
            var predicted = c.State.FindCard(card);
            if (c.Index >= 0 && card.Owner == c.Shooter && predicted != null
                && c.State.GetPlayerCombatState(card.Owner).DiscardPile.Cards.Contains(predicted))
                c.Simulator.AddToPile(predicted, PileType.Hand);
        });
        return registry;
    }
    private static Registry CreateImpaled()
    {
        var registry = new Registry(AfterArrowImpaled);
        registry.Register<ExplosiveArrowsPower>((power, c) => {
            if (power.Owner == c.Shooter.Creature)
                c.Simulator.Damage(((SimulatedCombatState)c.CombatState).HittableEnemies,
                    power.Amount, ValueProp.Unpowered, power.Owner);
        });
        return registry;
    }
    private static Registry CreateDislodged()
    {
        var registry = new Registry(AfterArrowDislodged);
        registry.Register<Twine>((relic, c) => {
            if (!relic.IsMelted && relic.Owner == c.Shooter)
                c.Simulator.GainBlock(relic.Owner.Creature, relic.DynamicVars.Block);
        });
        registry.Register<EpicRetrieval>((card, c) => {
            if (card.Owner == c.Shooter && c.Source?.References(card) == true)
                c.Simulator.GainEnergy(card.Owner, card.DynamicVars.Energy.BaseValue);
        });
        registry.Register<YahPower>((power, c) => {
            var state = c.State.GetPlayerCombatState(c.Shooter);
            if (power.Owner == c.Shooter.Creature && !state.Arrows.YahTriggered)
            {
                state.Arrows.YahTriggered = true;
                c.Simulator.GainEnergy(c.Shooter, power.Amount);
            }
        });
        return registry;
    }
}

internal sealed class ArrowEventMirrorContext : CombatMirrorContext
{
    public required Player Shooter { get; init; }
    public required Creature? Target { get; init; }
    public required int Index { get; init; }
    public required PredictedCard? Source { get; init; }
}
