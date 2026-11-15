using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver.Engine.InCombat.Mirrors.Hooks.Attack;

// 愚者的「瞄准」（AimPower，0.113 新增）。
//
// 游戏里的规则（Power 描述原文）：**你的下一支箭矢造成额外伤害**。它和「活力(VigorPower)」同构：
//   游戏侧 AimPower 覆写了 BeforeAttack / ModifyDamageAdditive / AfterAttack 三个钩子，
//   求解器这边照 VigorPowerMirrors 的写法建模，只是把触发条件从「任意攻击」收窄成「射箭」，
//   并且结算完把整份瞄准清掉（一次性加成，不是每击递减）。
//
// 为什么必须收窄：瞄准只作用于箭矢。如果像 Vigor 那样对任何攻击都加，愚者用普通攻击时会被多算伤害，
// 预测会比实际乐观 —— 那比不建模更坏。
internal static class AimPowerMirrors
{
    /// <summary>0.113 愚者卡池里真正「发射一支箭矢」的 12 张牌（按类型名）。</summary>
    private static readonly HashSet<string> ArrowShotCards =
    [
        nameof(DivineShot),
        nameof(FoolishShot),
        nameof(HeavyShot),
        nameof(QuickShot),
        nameof(RainShot),
        nameof(MelodicShot),
        nameof(RepeatShot),
        nameof(SkyShot),
        nameof(BlossomShot),
        nameof(HobblingShot),
        nameof(RapidShot),
        nameof(TrickShot),
    ];

    /// <summary>
    /// 卡牌是否「射箭」。游戏自己用 <see cref="CardTag.Shoot"/> 标记射击牌（0.113 实测），
    /// 所以优先认 tag；万一某张牌没打标签，再退回 0.113 卡池里那 12 张的白名单。
    /// </summary>
    public static bool IsArrowShot(CardModel? card)
    {
        if (card is null) return false;
        try { if (card.Tags.Contains(CardTag.Shoot)) return true; } catch { }
        return ArrowShotCards.Contains(card.GetType().Name);
    }

    /// <summary>预测层的卡（PredictedCard）版本。</summary>
    public static bool IsArrowShot(PredictedCard? card)
        => card is not null && IsArrowShot(card.Preview);

    public static void BeforeAttack(AimPower power, BeforeAttackMirrorContext context)
    {
        if (power.Amount > 0 && ShouldTrigger(power, context.Command))
        {
            var state = GetState(power, context);
            if (state.CommandToModify is null)
            {
                state.CommandToModify = context.Command;
                state.AmountWhenAttackStarted = power.Amount;
            }
        }
    }

    public static void AfterAttack(AimPower power, AfterAttackMirrorContext context)
        => CompleteOrAbort(power, context, completed: true);

    public static void CompleteOrAbort(AimPower power, AfterAttackMirrorContext context, bool completed)
    {
        var state = GetState(power, context);
        if (!ReferenceEquals(context.Command, state.CommandToModify)) return;
        if (completed && context.CombatState is ICombatPredictionEffectSink effects)
        {
            // 瞄准是「下一支箭」的一次性加成：这一箭打出去就整份清空。
            effects.SetPowerAmount(power, 0);
        }
        state.Clear();
    }

    internal static void ClearPair(CombatPredictionSimulator simulator, AttackCommand command)
    {
        // RainShot removes Aim before AfterAttack can enumerate it. The stored pair still needs cleanup.
        if (!simulator.StateStore.HasEntries<State>()) return;
        foreach (var (_, state) in simulator.StateStore.ReadEntries<State>())
            if (ReferenceEquals(state.CommandToModify, command)) state.Clear();
    }

    public static decimal ModifyDamageAdditive(AimPower power, ModifyDamageMirrorContext context)
    {
        if (power.Amount <= 0 || context.Dealer != power.Owner || !context.Props.IsPoweredAttack()) return 0;
        if (!IsArrowShot(context.CardSource)) return 0;

        var state = GetState(power, context);
        if (state.CommandToModify is not null
            && context.CardSource is not null
            && !context.CardSource.References(state.CommandToModify.ModelSource))
        {
            return 0;
        }

        // 「沉重射击」的卡面写着「瞄准对这张牌有 2 倍效果」，这一份翻倍由卡自己负责；
        // 那张卡的 OnPlay 求解器目前是 MethodMirrorIncomplete，所以在这里显式补上，
        // 免得把 2 倍漏算成 1 倍。
        return context.CardSource?.Original.GetType().Name == nameof(HeavyShot)
            ? power.Amount * 2
            : power.Amount;
    }

    private static State GetState(AimPower power, CombatMirrorContext context)
        => context.StateStore.Get<State>(power);

    private static bool ShouldTrigger(AimPower power, AttackCommand command)
    {
        return command.Attacker == power.Owner
            && command.DamageProps.IsPoweredAttack()
            && IsArrowShot(command.ModelSource as CardModel);
    }

    private sealed class State : IPredictionStateForkable, IPredictionForkBoundary
    {
        public AttackCommand? CommandToModify { get; set; }

        public int AmountWhenAttackStarted { get; set; }

        public void Clear() { CommandToModify = null; AmountWhenAttackStarted = 0; }

        public object Fork(PredictionForkContext context)
        {
            AssertForkable();
            return MemberwiseClone();
        }

        public void AssertForkable()
        {
            if (CommandToModify is not null || AmountWhenAttackStarted != 0)
                throw new InvalidOperationException("Cannot fork during Aim attack resolution.");
        }
    }
}
