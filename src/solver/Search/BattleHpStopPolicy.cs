namespace CombatSolver;

// Satisficing only: this does not alter route ranking or certify optimality.
internal static class BattleHpStopPolicy
{
    internal const int QuickAcceptNetHpLoss = 8;

    internal static int NetHpLoss(int grossLoss, int recoveredHp, int relicHeal)
        => Math.Max(0, grossLoss - Math.Max(0, recoveredHp) - Math.Max(0, relicHeal));

    internal static bool MeetsTarget(bool completeVictory, int netLoss, int configuredTarget,
        bool quickAccept, int finalHp, int finalMaxHp)
        => completeVictory && (netLoss <= configuredTarget
            || quickAccept && netLoss <= QuickAcceptNetHpLoss
                && finalHp >= Math.Max(12, finalMaxHp / 5));
}
