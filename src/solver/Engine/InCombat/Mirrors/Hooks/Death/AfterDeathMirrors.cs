using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.Common.Mirrors;

namespace CombatSolver.Engine.InCombat.Mirrors.Hooks.Death;

using Registry = MethodMirrorRegistry<AbstractModel, AfterDeathMirrorContext>;

// Mirrors the prediction-relevant parts of Hook.AfterDeath.
internal static class AfterDeathMirrors
{
    // 0.113：AbstractModel.AfterDeath 多了一个 source（CardModel）参数：
    //   0.111  (PlayerChoiceContext, Creature, bool wasRemovalPrevented, float deathAnimLength)
    //   0.113  (PlayerChoiceContext, Creature, CardModel source, bool wasRemovalPrevented, float deathAnimLength)
    // 少了它，MirrorMethodSpec 构造时就抛 MissingMethodException（编译期看不出来，全是按名+签名反射找），
    // 表现是「战斗中搜索一崩到底、游戏站着不动」——见 2026-11-14 02:31 的定位记录。
    private static readonly MirrorMethodSpec AfterDeath = MirrorMethodSpec.Hook(
        nameof(AbstractModel.AfterDeath),
        [
            typeof(PlayerChoiceContext),
            typeof(Creature),
            typeof(CardModel),
            typeof(bool),
            typeof(float)
        ]);

    private static readonly Registry Registry = CreateRegistry();

    public static void Invoke(AbstractModel listener, AfterDeathMirrorContext context)
    {
        Registry.Invoke(listener, context);
    }

    private static Registry CreateRegistry()
    {
        var registry = new Registry(AfterDeath);
        registry.Register<ImpaledPower>((power, context) => {
            if (context.WasRemovalPrevented || context.Creature != power.Owner) return;
            var player = context.CombatState.Players.FirstOrDefault(p => p.Creature == power.Applier);
            if (player == null || context.State.GetCreature(player.Creature).IsDead) return;
            var arrows = context.State.GetPlayerCombatState(player).Arrows;
            while (arrows.ArrowsOn(power.Owner.CombatId!.Value).Count != 0)
            {
                context.Simulator.DislodgeArrow(player, power.Owner, context.Source);
                if (context.Simulator.HasPendingChoice) return;
            }
        });

        registry.RegisterIgnored<Aeonglass>();
        registry.RegisterIgnored<DecimillipedeSegment>();
        registry.RegisterIgnored<KinPriest>();
        registry.RegisterIgnored<LagavulinMatriarch>();
        registry.Register<Queen>(HandleQueen);
        registry.RegisterIgnored<SoulFysh>();
        registry.RegisterIgnored<TestSubject>();
        registry.RegisterIgnored<TheInsatiable>();
        registry.RegisterIgnored<Vantom>();
        registry.RegisterIgnored<WaterfallGiant>();

        registry.Register<GremlinHorn>(HandleGremlinHorn);
        registry.Register<Melancholy>(HandleMelancholy);
        registry.Register<StockPower>(HandleStock);
        registry.Register<CrabRagePower>(HandleCrabRage);
        registry.Register<DampenPower>(HandleDampen);

        return registry;
    }

    private static void HandleDampen(DampenPower power, AfterDeathMirrorContext context)
    {
        if (!context.WasRemovalPrevented)
        {
            if (context.CombatState is not SimulatedCombatState combat)
                throw new InvalidOperationException("Dampen death requires captured caster and card state.");
            combat.RemoveDampenCaster(context.Creature);
        }
    }

    private static void HandleCrabRage(CrabRagePower power, AfterDeathMirrorContext context)
    {
        if (context.Creature != power.Owner && context.Creature.Side == power.Owner.Side)
        {
            if (context.CombatState is not ICombatPredictionEffectSink effects)
                throw new InvalidOperationException("Crab rage requires writable branch state.");
            effects.ApplyPowerFromSource(typeof(StrengthPower), power.Owner,
                power.DynamicVars.Strength.IntValue, power.Owner, null);
            context.Simulator.GainBlock(power.Owner, power.DynamicVars.Block.BaseValue, ValueProp.Unpowered);
            effects.SetPowerAmount(power, 0);
        }
    }

    private static void HandleStock(StockPower power, AfterDeathMirrorContext context)
    {
        if (!context.WasRemovalPrevented && context.Creature == power.Owner && power.Amount > 0)
        {
            if (context.CombatState is not ICombatPredictionEffectSink effects)
                throw new InvalidOperationException("补货效果缺少可写的预测状态。");
            effects.SpawnStockReplacement(context.Simulator, power);
        }
    }

    private static void HandleGremlinHorn(GremlinHorn relic, AfterDeathMirrorContext context)
    {
        if (context.Creature.Side != relic.Owner.Creature.Side)
        {
            context.Simulator.GainEnergy(relic.Owner, relic.DynamicVars.Energy.BaseValue);
            context.Simulator.Draw(relic.Owner, relic.DynamicVars.Cards.BaseValue);
        }
    }

    private static void HandleQueen(Queen queen, AfterDeathMirrorContext context)
    {
        if (context.Creature.Monster is not TorchHeadAmalgam ||
            !context.State.GetCreature(queen.Creature).IsAlive)
        {
            return;
        }
        if (context.CombatState is not ICombatPredictionMonsterStateSink monsterState)
            throw new InvalidOperationException("女王死亡联动缺少可写的预测怪物状态。");
        monsterState.SetMonsterBool(queen.Creature, "_hasAmalgamDied", true);
        if (monsterState.GetPredictedMoveId(queen.Creature) == "BURN_BRIGHT_FOR_ME_MOVE")
            monsterState.ForceMonsterMove(queen.Creature, "ENRAGE_MOVE");
    }

    private static void HandleMelancholy(Melancholy card, AfterDeathMirrorContext context)
    {
        if (!context.WasRemovalPrevented)
        {
            var previewCard = context.State.FindCard(card)?.MutablePreview;
            previewCard?.EnergyCost.AddThisCombat(-previewCard.DynamicVars.Energy.IntValue);
        }
    }
}

internal sealed class AfterDeathMirrorContext : CombatMirrorContext
{
    public required Creature Creature { get; init; }

    public required bool WasRemovalPrevented { get; init; }
    public PredictedCard? Source { get; init; }
}
