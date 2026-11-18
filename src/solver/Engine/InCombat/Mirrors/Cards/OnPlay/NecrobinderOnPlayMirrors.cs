using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;

/// <summary>
/// Exact OnPlay mirrors for the Necrobinder cards that previously fell back to inference.
/// </summary>
/// <remarks>
/// Every handler here is a direct transcript of the native body: one attack command, one block command or one
/// draw. Cards whose native body needs mechanics this simulator does not model (replay-into-hand, choice
/// cursors) are deliberately left on inference rather than modelled approximately - a wrong exact mirror is
/// worse than an inferred one because it stops the risk marker from firing.
/// </remarks>
internal static class NecrobinderOnPlayMirrors
{
    // BansheesCry: 33 damage (36 upgraded) to every opponent.
    public static void BansheesCryOnPlay(BansheesCry _, CardOnPlayMirrorContext context)
        => context.AttackAllOpponents();

    // Bury: single-target 52 damage (63 upgraded).
    public static void BuryOnPlay(Bury _, CardOnPlayMirrorContext context)
        => context.AttackSingle();

    // Defile: single-target 13 damage (17 upgraded), Ethereal.
    public static void DefileOnPlay(Defile _, CardOnPlayMirrorContext context)
        => context.AttackSingle();

    // Eradicate: X-cost, 11 damage (14 upgraded) per energy, Retain.
    public static void EradicateOnPlay(Eradicate card, CardOnPlayMirrorContext context)
    {
        int hits = context.Card.ResolveEnergyXValue(context.State);
        if (hits <= 0)
            return;
        context.AttackSingle(hitCount: hits);
    }

    // Flatten: Osty attack for 12 (16 upgraded), single target.
    public static void FlattenOnPlay(Flatten card, CardOnPlayMirrorContext context)
        => OstyAttack(card, card.DynamicVars.OstyDamage.BaseValue, context);

    // Melancholy: 13 block (upgraded more). Its AfterDeath cost reduction is a separate hook.
    public static void MelancholyOnPlay(Melancholy _, CardOnPlayMirrorContext context)
        => context.GainBlock(context.PreviewCard.Owner.Creature);

    // Parse: draw 3 (4 upgraded).
    public static void ParseOnPlay(Parse card, CardOnPlayMirrorContext context)
        => context.Simulator.Draw(card.Owner, card.DynamicVars.Cards.BaseValue);

    // Poke: Osty attack for 6 (8 upgraded), single target.
    public static void PokeOnPlay(Poke card, CardOnPlayMirrorContext context)
        => OstyAttack(card, card.DynamicVars.OstyDamage.BaseValue, context);

    // Reap: single-target 27 damage (31 upgraded).
    public static void ReapOnPlay(Reap _, CardOnPlayMirrorContext context)
        => context.AttackSingle();

    // Sow: 8 damage (10 upgraded) to every opponent.
    public static void SowOnPlay(Sow _, CardOnPlayMirrorContext context)
        => context.AttackAllOpponents();

    /// <summary>One Osty-sourced attack, skipped entirely when the Osty is missing or dead.</summary>
    private static void OstyAttack(
        CardModel card,
        decimal amount,
        CardOnPlayMirrorContext context)
    {
        if (context.State.GetOsty(card.Owner) is not { } osty
            || context.State.GetCreature(osty).IsDead)
        {
            return;
        }

        DamageCmd.Attack(amount)
            .FromOsty(osty, card, context.CardPlay)
            .Targeting(context.Target)
            .Simulate(context.Simulator);
    }
}
