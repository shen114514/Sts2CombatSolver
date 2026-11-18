namespace CombatSolver;

/// <summary>
/// How freely the solver may spend potions in this fight.
/// </summary>
/// <remarks>
/// The modes change what a potion costs a route, not what it does. A potion's benefit is always the
/// simulated difference it makes (see <see cref="ActEndingBossPolicy.StrategicHpDeficit"/>); this policy only
/// decides how much of that benefit has to stay unspent as a reserve for later fights.
/// </remarks>
internal enum PotionSpendMode
{
    /// <summary>Normal fight: keep a small reserve because the potion is worth something in a later fight.</summary>
    Normal,

    /// <summary>
    /// Act-ending boss with healing afterwards: the point is to clear the act, so a potion is only
    /// spent when the fight is actually in danger, not to shave HP off a win that is already secure.
    /// </summary>
    Conserve,

    /// <summary>First of two final bosses: HP carries forward alongside the remaining belt.</summary>
    DoubleBoss,

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
/// <item>Acts one and two end with <see cref="BossHpRelief.ActClearHeal"/>. Clearing the act heals back most of
/// the damage, so those fights are about getting through them, not about the HP left at the end: conserve.</item>
/// <item>Ascension 0-9: the act-three boss is the run's last fight, so it resolves to
/// <see cref="BossHpRelief.RunEnding"/> and every potion is spent.</item>
/// <item>Ascension 10: the first of the two act-three bosses resolves to <see cref="BossHpRelief.None"/> because
/// its HP carries into the second one, so full HP value and a small belt reserve are compared; only the second boss is
/// <see cref="BossHpRelief.RunEnding"/> and spends everything.</item>
/// </list>
/// </remarks>
internal static class PotionSpendPolicy
{
    /// <summary>
    /// HP of benefit a spent potion has to leave on the table in a normal fight, in the same units as
    /// <see cref="ActEndingBossPolicy.StrategicHpDeficit"/>.
    /// </summary>
    /// <remarks>
    /// This is a reserve, not a price ladder. It replaced the "worth saving" threshold that used to be scaled by
    /// five at an act-ending boss and to <c>int.MaxValue / 4</c> at the run-ending boss, which made a potion route
    /// that saved nineteen HP rank as infinitely worse than one that saved none.
    ///
    /// The value is deliberately below a typical early-fight swing: a potion is spent when it materially improves
    /// the fight, and kept when the improvement is noise.
    /// </remarks>
    public const int NormalReserveHp = 4;

    /// <summary>
    /// Reserve for a boss the player only has to clear. High enough that a comfortable win never drinks, low
    /// enough that a losing or knife-edge fight still gets the belt: the rescue paths and the "potion-free route
    /// loses" branch bypass the reserve entirely, so survival is never blocked by this number.
    /// </summary>
    public const int ConserveReserveHp = 12;

    public static int ReserveHp(PotionSpendMode mode) => mode switch
    {
        PotionSpendMode.SpendAll => 0,
        PotionSpendMode.Conserve => ConserveReserveHp,
        PotionSpendMode.DoubleBoss => NormalReserveHp,
        _ => NormalReserveHp,
    };

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
        // Act one and two bosses: clear the act, keep the belt for what follows.
        if (relief == BossHpRelief.ActClearHeal)
            return PotionSpendMode.Conserve;
        if (relief != BossHpRelief.RunEnding)
        {
            // A potion that preserves HP also preserves a resource for the second boss.
            // Do not apply the act-clear conserve premium to this pair.
            return ascensionLevel >= 10 && actHasSecondBoss && !isSecondBoss
                ? PotionSpendMode.DoubleBoss
                : PotionSpendMode.Normal;
        }
        // Defensive: a run-ending relief on the first of two bosses would be a run-state inconsistency; keep the
        // reserve rather than emptying the belt before the real last fight.
        if (ascensionLevel >= 10 && actHasSecondBoss && !isSecondBoss)
            return PotionSpendMode.DoubleBoss;
        return PotionSpendMode.SpendAll;
    }

    public static string Describe(PotionSpendMode mode) => mode switch
    {
        PotionSpendMode.SpendAll => "SpendAll",
        PotionSpendMode.Conserve => "Conserve",
        PotionSpendMode.DoubleBoss => "DoubleBoss",
        _ => "Normal",
    };

    /// <summary>
    /// Reserve for a belt that refills itself: Delicate Frond fills every open slot before each combat, and the
    /// White Beast Statue forces a potion reward after every combat, so a spent potion is not a resource that has
    /// to last the run - it is a resource that regenerates. The reserve stays above zero only so a potion is
    /// still not burned for noise.
    /// </summary>
    public static int RefreshReserveHp(PotionSpendMode mode) => mode switch
    {
        PotionSpendMode.SpendAll => 0,
        PotionSpendMode.Conserve => 5,
        PotionSpendMode.DoubleBoss => 1,
        _ => 1,
    };
}

/// <summary>
/// The potion-spend decision captured with the combat root, so every search pass sees one answer.
/// </summary>
internal readonly record struct PotionSpendContext(
    PotionSpendMode Mode,
    int AscensionLevel,
    bool PotionsRefreshEachCombat = false)
{
    public static PotionSpendContext Normal => new(PotionSpendMode.Normal, 0);
    public int ReserveHp => PotionsRefreshEachCombat
        ? PotionSpendPolicy.RefreshReserveHp(Mode)
        : PotionSpendPolicy.ReserveHp(Mode);
    public bool SpendsEverything => Mode == PotionSpendMode.SpendAll;
    public bool PlansForSecondBoss => Mode == PotionSpendMode.DoubleBoss;
}
