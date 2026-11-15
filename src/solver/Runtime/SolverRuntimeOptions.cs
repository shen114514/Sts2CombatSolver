namespace CombatSolver;

// Production defaults. The distributed mod has no external scenario/command server.
internal static class SolverRuntimeOptions
{
    public static bool IsActive => StandaloneSmokeTest.IsRunning;
    public static bool AutomaticTurnSearchEnabled => !StandaloneSmokeTest.PauseAutomaticSearch;
    public static bool IsReplayingRecordedInputs => false;
    public static DevelopmentSearchStrategy? CurrentDevelopmentStrategy => null;
    public static bool VerifyIncrementalSearch => false;
    public static bool FixedSearchBudget => false;
    public static bool MeasureSearchPhases => false;
    public static int? SearchBudgetOverrideMilliseconds => StandaloneSmokeTest.IsRunning?5000:null;
    public static int EarlyTurnExplorationDepth => 0;
    public static int EarlyTurnExplorationBudgetMilliseconds => 0;
    public static int? SearchMaxDegreeOfParallelismOverride => null;
    public static BeamWeightPerturbation? BeamWeightPerturbationOverride => null;
    public static SolverSearchProfile? CheckpointProfileOverride => null;
    public static bool UseNoveltyPortfolioOverride => false;
    public static bool UseBeamWidthPortfolioOverride => false;
    public static IReadOnlyList<int>? BeamWidthPortfolioWidthsOverride => null;
    public static bool BeamWidthPortfolioPlainBaselineMemberOverride => true;
    public static int PileOrderInvariantMaskOverride => 0;
    public static int StateKeySaltOverride => 0;
    public static int TranspositionPruningDisabledMaskOverride => 0;
    public static int? TranspositionEntryLimitOverride => null;
    public static int MemoryNoProgressRecoveryLimitOverride => 0;
    public static bool? Act3BossStrategyOverride => null;
}
