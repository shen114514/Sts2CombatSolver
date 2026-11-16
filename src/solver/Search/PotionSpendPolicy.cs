namespace CombatSolver;

/// <summary>
/// How freely the solver may spend potions in this fight.
/// </summary>
/// <remarks>
/// The two modes change what a potion costs a route, not what it does. A potion's benefit is always the
/// simulated difference it makes (see <see cref="ActEndingBossPolicy.StrategicHpDeficit"/>); this policy only
/// decides how much of that benefit has to stay unspent as a reserve for later fights.
/// </remarks>
internal enum PotionSpendMode
{
    /// <summary>Normal fight: keep a small reserve because the potion is worth something in a later fight.</summary>
    Normal,

    /// <summary>The run ends with this fight: nothing follows, so every potion is spent if it helps at all.</summary>
    SpendAll,
}

/// <summary>
/// Which potion-spend mode applies to a fight, derived from where the fight sits in the run.
/// </summary>
/// <remarks>
/// The run-ending rule is what the player's Ascension level changes, and the game already encodes that
/// difference structurally: <see cref="MegaCrit.Sts2.Core.Models.ActModel.SecondBossEncounter"/> exists only in
/// Double Boss mode (Ascension 10+).
///
/// <list type="bullet">
/// <item>Ascension 0-9: the act-three boss is the run's last fight, so it resolves to
/// <see cref="BossHpRelief.RunEnding"/> and every potion is spent.</item>
/// <item>Ascension 10: the first of the two act-three bosses resolves to <see cref="BossHpRelief.None"/> because
/// its HP carries into the second one, so that fight keeps the normal reserve; only the second boss is
/// <see cref="BossHpRelief.RunEnding"/> and spends everything.</item>
/// </list>
///
/// Acts one and two end with <see cref="BossHpRelief.ActClearHeal"/> and stay in
/// <see cref="PotionSpendMode.Normal"/>: the potion still has two more acts of work to do.
/// </remarks>
internal static class PotionSpendPolicy
{
    /// <summary>
    /// HP of benefit a spent potion has to leave on the table in a normal fight, in the same units as
    /// <see cref="ActEndingBossPolicy.StrategicHpDeficit"/>.
    /// </summary>
    /// <remarks>
    /// This is a reserve, not a price ladder. It used to be the same number as the "worth saving" threshold,
    /// scaled by five at an act-ending boss and to <c>int.MaxValue / 4</c> at the run-ending boss, which made a
    /// potion route that saved nineteen HP rank as infinitely worse than one that saved none. One flat number
    /// keeps the term comparable with the deficit axis it is added to, and zero at the run's last fight keeps
    /// "nothing follows this" meaning "spend it", not "it is worthless".
    ///
    /// The value is deliberately below a typical early-fight swing: a potion is spent when it materially
    /// improves the fight, and kept when the improvement is noise.
    /// </remarks>
    public const int NormalReserveHp = 4;

    public static int ReserveHp(PotionSpendMode mode)
        => mode == PotionSpendMode.SpendAll ? 0 : NormalReserveHp;

    /// <summary>
    /// Resolves the mode from the fight's post-combat relief plus the Ascension-10 double-boss layout.
    /// </summary>
    /// <param name="relief">Post-combat HP relief for this encounter, from the live run state.</param>
    /// <param name="ascensionLevel">Current run Ascension level; logged and used by the defensive branch below.</param>
    /// <param name="actHasSecondBoss">Whether this act is in Double Boss mode.</param>
    /// <param name="isSecondBoss">Whether this encounter is that second boss.</param>
    public static PotionSpendMode Resolve(
        BossHpRelief relief,
        int ascensionLevel,
        bool actHasSecondBoss,
        bool isSecondBoss)
    {
        if (relief != BossHpRelief.RunEnding)
            return PotionSpendMode.Normal;
        // Defensive: a run-ending relief on the first of two bosses would be a run-state inconsistency; keep the
        // reserve rather than emptying the belt before the real last fight.
        if (ascensionLevel >= 10 && actHasSecondBoss && !isSecondBoss)
            return PotionSpendMode.Normal;
        return PotionSpendMode.SpendAll;
    }

    public static string Describe(PotionSpendMode mode)
        => mode == PotionSpendMode.SpendAll ? "SpendAll" : "Normal";
}

/// <summary>
/// The potion-spend decision captured with the combat root, so every search pass sees one answer.
/// </summary>
internal readonly record struct PotionSpendContext(PotionSpendMode Mode, int AscensionLevel)
{
    public static PotionSpendContext Normal => new(PotionSpendMode.Normal, 0);
    public int ReserveHp => PotionSpendPolicy.ReserveHp(Mode);
    public bool SpendsEverything => Mode == PotionSpendMode.SpendAll;
}
