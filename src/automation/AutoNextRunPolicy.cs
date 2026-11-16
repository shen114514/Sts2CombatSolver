namespace CombatSolver;

public static class AutoNextRunPolicy
{
    public const string RandomCharacter = "RANDOM";
    public static string? SelectCharacter(string requested, IReadOnlyList<string> unlocked, int randomIndex)
    {
        if (unlocked.Count == 0) return null;
        if (requested != RandomCharacter) return unlocked.Contains(requested, StringComparer.Ordinal) ? requested : null;
        if ((uint)randomIndex >= (uint)unlocked.Count) throw new ArgumentOutOfRangeException(nameof(randomIndex));
        return unlocked[randomIndex];
    }
}
