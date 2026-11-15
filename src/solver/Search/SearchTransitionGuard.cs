namespace CombatSolver;

internal static class SearchTransitionGuard
{
    private static readonly string? InjectedFailureCard =
        System.Environment.GetEnvironmentVariable("STS2_WHALE_TEST_BAD_CARD");
    public static TResult Execute<TResult>(
        PlanAction action,
        StateFingerprint parentState,
        int parentActionCount,
        Func<TResult> transition)
    {
        try
        {
            // Process-only fault injection for the real recovery path, never a saved setting.
            if (action.Kind == PlanActionKind.PlayCard && action.CardId == InjectedFailureCard)
                throw new MissingMemberException("Injected simulated card compatibility failure: " + action.CardId);
            return transition();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidPlannedChoiceBranchException)
        {
            throw;
        }
        catch (SearchTransitionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SearchTransitionException(
                action,
                parentState,
                parentActionCount,
                ex);
        }
    }
}
