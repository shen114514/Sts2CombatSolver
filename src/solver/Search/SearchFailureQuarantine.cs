using System.Collections.Frozen;

namespace CombatSolver;

// Request-local, immutable snapshots are passed into every search member and branch.
// No live cards, powers, settings or real action queues are modified.
internal sealed class SearchFailureQuarantine(int budgetMilliseconds, IEnumerable<string>? alreadyExcluded = null)
{
    internal const int MaximumExcludedCards = 3;
    private readonly HashSet<string> _excluded = new(alreadyExcluded ?? [], StringComparer.Ordinal);
    public IReadOnlySet<string> Excluded => _excluded.ToFrozenSet(StringComparer.Ordinal);
    public int Remaining(long elapsedMilliseconds)
        => (int)Math.Max(0, budgetMilliseconds - Math.Min(int.MaxValue, elapsedMilliseconds));

    internal static bool IsCompatibilityFailure(Exception exception)
    {
        Exception cause = exception.GetBaseException();
        return cause is MissingMemberException or KeyNotFoundException
            || cause.GetType().FullName == "CombatSolver.Engine.Common.PredictionUnsupportedException";
    }

    public bool TryExclude(string cardId, Exception exception, long elapsedMilliseconds)
        => !string.IsNullOrWhiteSpace(cardId)
           && IsCompatibilityFailure(exception)
           && _excluded.Count < MaximumExcludedCards
           && Remaining(elapsedMilliseconds) >= 1_000
           && _excluded.Add(cardId);
}
