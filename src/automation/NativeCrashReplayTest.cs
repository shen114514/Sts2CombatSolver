using System.Text.Json;
using Godot;
using HarmonyLib;
using CombatSolver.Patching;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Rooms;
namespace CombatSolver;
internal static class NativeCrashReplayTest
{
    internal static bool IsRequested=>StandaloneSmokeTest.IsRunning&&System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") is "CRASH_REPLAY" or "CRASH_CONTINUE" or "CENTIPEDE_REPLAY";
    private static async Task Until(Func<bool> condition,string label)
    {var end=DateTime.UtcNow.AddSeconds(30);while(!condition()&&DateTime.UtcNow<end)await Task.Delay(100);if(!condition())throw new InvalidOperationException(label+": "+RoomAutomation.Status);}
    internal static async Task RunAsync(NGame host,List<string> checks)
    {
        string path=System.Environment.GetEnvironmentVariable("COMBATSOLVER_REPLAY_SAVE")??throw new InvalidOperationException("Saved-run copy required");
        if(!Path.IsPathFullyQualified(path))throw new InvalidOperationException("Absolute copy path required");
        var save=JsonSerializer.Deserialize(File.ReadAllBytes(path),JsonSerializationUtility.GetTypeInfo<SerializableRun>())
            ??throw new InvalidOperationException("Saved-run copy invalid");
        save=RunManager.CanonicalizeSave(save,1UL);
        var run=RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run,save);
        if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saved-run replay must not save");
        checks.Add("SavedRunCloneReloadWithoutSavingOrIncrementingOfficialReloads");
        SolverSettings.ApplyForTesting(SolverSettings.Current with{AutoRewardsEnabled=true,AutoCampEnabled=true,
            AutoEventsEnabled=false,AutoShopEnabled=false});
        int before=run.Players[0].Relics.Count;
        await host.LoadRun(run,save.PreFinishedRoom);
        await Until(()=>NMapScreen.Instance?.IsOpen==true,"saved treasure pickup and proceed");
        if(run.Players[0].Relics.Count!=before+1)throw new InvalidOperationException("Saved treasure reward mismatch");
        checks.Add("OriginalSavedTreasureRewardAndProceedCompleted");
        if(System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE")=="CRASH_CONTINUE")
        {
            SolverSettings.ApplyForTesting(SolverSettings.Current with{AutoEventsEnabled=true,AutoShopEnabled=true});
            StandaloneSmokeTest.EnableContinuationReplay();
            var initial=run.CurrentMapCoord;int act=run.CurrentActIndex;
            var deadline=DateTime.UtcNow.AddMinutes(4);
            while(DateTime.UtcNow<deadline&&!run.IsGameOver&&run.CurrentActIndex==act)
            {
                await Task.Delay(250);
                if(SolverController.PredictionFailure!=null)
                {checks.Add("PredictionIsolatedDuringContinuation:"+SolverController.PredictionFailure);break;}
            }
            checks.Add($"ContinuationObservationEnded:From={initial}:To={run.CurrentMapCoord}:Act={run.CurrentActIndex}:Hp={run.Players[0].Creature.CurrentHp}");
            return;
        }
        // Repeat real room transitions and cleanup, where the native heap failure was observed.
        for(int i=0;i<8;i++)
        {
            NMapScreen.Instance?.Close(false);
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);
            await Until(()=>NMapScreen.Instance?.IsOpen==true,"treasure transition "+i);
            GC.Collect();await Task.Delay(250);
        }
        checks.Add("EightTreasureTransitionsWithNativeCleanupCompleted");
    }
}
internal sealed class NativeReplayNoReloadWritePatch:IPatchMethod
{
    public static ModPatchTarget[] GetTargets()=>[new(typeof(SaveManager),"IncrementNumReloads",
        AccessTools.Method(typeof(SaveManager),"IncrementNumReloads").GetParameters().Select(p=>p.ParameterType).ToArray())];
    public static bool Prefix(ref Task __result)
    {if(!NativeCrashReplayTest.IsRequested)return true;__result=Task.CompletedTask;return false;}
}
internal sealed class NativeReplayNoSavingPatch:IPatchMethod
{
    public static ModPatchTarget[] GetTargets()=>[new(typeof(RunManager),"InitializeShared",
        AccessTools.Method(typeof(RunManager),"InitializeShared").GetParameters().Select(p=>p.ParameterType).ToArray())];
    public static void Prefix(ref bool shouldSave){if(NativeCrashReplayTest.IsRequested)shouldSave=false;}
}
