namespace CombatSolver;

// Derived search guidance only. Native death, revival timing and victory checks remain authoritative.
internal readonly record struct CentipedeProgress(int Segments,int Living,int Reviving,int Hp,int MaximumHp,
    int EffectiveHp,int MaximumEffectiveHp,long SquareHp)
{
    public bool CanPrepare=>Segments>1&&Living==Segments&&Reviving==0;
    public CentipedeProgress Add(int hp,int effectiveHp)=>new(Segments+1,Living+(hp>0?1:0),
        Reviving+(hp<=0&&effectiveHp>0?1:0),Hp+Math.Max(0,hp),Math.Max(MaximumHp,hp),
        EffectiveHp+Math.Max(0,effectiveHp),Math.Max(MaximumEffectiveHp,effectiveHp),SquareHp+(long)Math.Max(0,hp)*Math.Max(0,hp));
    // Equal damage should not systematically favor tunneling the weakest segment. Bound
    // this guidance to four player HP so it cannot replace survival or actual terminal ranking.
    public double BeamAdjustment=>Segments>1&&Living>1
        ?-Math.Min(48,Math.Max(0,Segments*MaximumEffectiveHp-EffectiveHp))*2500d:0;
    public int PreparationValue=>CanPrepare?-(int)Math.Min(int.MaxValue-1,(long)MaximumHp*1024+SquareHp):int.MinValue;
    public static bool IsPreparation(CentipedeProgress before,CentipedeProgress after)=>
        before.CanPrepare&&after.CanPrepare&&before.Segments==after.Segments&&after.Hp<before.Hp;
}

internal sealed partial class CombatBeamSolver
{
    // Explicit developer A/B control; never disabled by a normal player setting.
    private readonly bool _coordinateCentipede=!StandaloneSmokeTest.IsRunning||
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_CENTIPEDE_BASELINE")!="1";
}
