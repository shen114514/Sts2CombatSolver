using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
namespace CombatSolver;
internal static class CentipedeNativeTest
{
    internal static bool IsRequested=>StandaloneSmokeTest.IsRunning&&System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="CENTIPEDE_REPLAY";
    internal static async Task RunAsync(NGame host,List<string> checks)
    {
        string path=System.Environment.GetEnvironmentVariable("COMBATSOLVER_REPLAY_SAVE")??throw new InvalidOperationException("Save copy required");
        var save=JsonSerializer.Deserialize(File.ReadAllBytes(path),JsonSerializationUtility.GetTypeInfo<SerializableRun>())??throw new InvalidOperationException("Invalid copy");
        save=RunManager.CanonicalizeSave(save,1UL);var run=RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run,save);
        if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving centipede fixture refused");
        SolverSettings.ApplyForTesting(SolverSettings.Current with{PerformancePreset=SolverPerformancePreset.Custom,
            SearchTimeLimitSeconds=5,SearchBeamWidth=12,SearchMaxExpandedNodes=15000,SearchMaxCardBranchesPerNode=8,
            SearchMaxDegreeOfParallelism=1,AutoRouteEnabled=false,AutoRewardsEnabled=false,AutoCampEnabled=false,
            AutoEventsEnabled=false,AutoShopEnabled=false,AutoEnableFullAuto=true,AutomaticCalculationEnabled=true,
            StopFullAutoOnDeathTurn=false,StopFullAutoOnWorseRecalculation=false,StopFullAutoOnCombatEnd=false,
            IgnoreLongTermRewards=true});
        SolverController.ApplyPersistentSettings(SolverSettings.Capture());
        bool won=false;CombatManager.Instance.CombatWon+=OnWon;
        void OnWon(MegaCrit.Sts2.Core.Rooms.CombatRoom room){won=true;}
        var samples=new List<object>();var prior=new Dictionary<uint,int>();int revives=0,turn=0;
        StandaloneSmokeTest.EnableContinuationReplay();
        try
        {
            await host.LoadRun(run,save.PreFinishedRoom);
            var state=CombatManager.Instance.DebugOnlyGetState()??throw new InvalidOperationException("No combat in saved copy");
            if(!state.Enemies.Any(e=>e.Monster is DecimillipedeSegment))throw new InvalidOperationException("Saved copy is not centipede encounter");
            var player=LocalContext.GetMe(run)!;int hp=player.Creature.CurrentHp;
            var deadline=DateTime.UtcNow.AddMinutes(3);string last="";
            while(!won&&player.Creature.CurrentHp>0&&DateTime.UtcNow<deadline)
            {
                turn=Math.Max(turn,player.PlayerCombatState?.TurnNumber??0);
                var parts=state.Enemies.Where(e=>e.Monster is DecimillipedeSegment).Select(e=>new{Id=e.CombatId??0,Hp=e.CurrentHp}).ToArray();
                foreach(var part in parts){if(prior.TryGetValue(part.Id,out int previous)&&previous<=0&&part.Hp>0)revives++;prior[part.Id]=part.Hp;}
                string key=turn+":"+string.Join(',',parts.Select(p=>p.Hp));
                if(key!=last){samples.Add(new{turn,parts,hp=player.Creature.CurrentHp});last=key;}
                if(SolverController.PredictionFailure!=null)throw new InvalidOperationException(SolverController.PredictionFailure);
                await Task.Delay(50);
            }
            var result=new{baseline=System.Environment.GetEnvironmentVariable("COMBATSOLVER_CENTIPEDE_BASELINE")=="1",won,
                turns=turn,startHp=hp,endHp=player.Creature.CurrentHp,revives,samples};
            File.WriteAllText(Path.Combine(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!,"centipede.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            checks.Add($"NativeCentipede:Won={won}:Turns={turn}:Hp={player.Creature.CurrentHp}:Revives={revives}");
            if(!won)throw new InvalidOperationException("Native centipede did not finish with victory");
        }
        finally{CombatManager.Instance.CombatWon-=OnWon;}
    }
}
