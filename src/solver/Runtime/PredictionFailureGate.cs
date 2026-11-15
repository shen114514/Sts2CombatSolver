namespace CombatSolver;

// A fault is owned by one real combat and turn. Repeated bridge ticks must not retry
// an identical deterministic failure. Native input, a new turn or manual search can recover.
internal sealed class PredictionFailureGate
{
    private object? _combat;
    private int _turn;
    private string? _failure;

    public void Record(object combat, int turn, string failure)
        => (_combat, _turn, _failure) = (combat, turn, failure);

    public string? GetFailure(object? combat, int turn)
        => combat != null && ReferenceEquals(_combat, combat) && turn == _turn ? _failure : null;

    public void Clear() => (_combat, _turn, _failure) = (null, -1, null);
}
