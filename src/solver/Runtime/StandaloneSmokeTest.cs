using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver.Patching;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Saves;

namespace CombatSolver;

// Explicit local developer validation; no HTTP input and no production activation.
internal static class StandaloneSmokeTest
{
    private static readonly string? DirectoryPath=System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR");
    internal static bool IsRunning => !string.IsNullOrWhiteSpace(DirectoryPath);
    internal static bool PauseAutomaticSearch {get;private set;}=IsRunning;
    internal static bool PauseRouting {get;private set;}=IsRunning;
    internal static void SetRoutingForFlowTest(bool enabled)
    {if(!IsRunning)throw new InvalidOperationException("Developer flow test required");PauseRouting=!enabled;}
    internal static void EnableContinuationReplay()
    {if(!NativeCrashReplayTest.IsRequested)throw new InvalidOperationException("Saved-run diagnostic required");PauseRouting=false;PauseAutomaticSearch=false;}
    internal static void TryStart(NGame? host)
    {
        if(IsRunning&&host!=null)_=RunAsync(host);
    }
    private static async Task RunAsync(NGame host)
    {
        string stage="startup";
        var evidence=new List<string>();
        try
        {
            if(!Path.IsPathFullyQualified(DirectoryPath!))throw new InvalidOperationException("Absolute evidence path required.");
            Directory.CreateDirectory(DirectoryPath!);
            await host.GameStartupComplete;
            SolverSettings.ApplyForTesting(SolverSettings.Current with
            {AutoRewardsEnabled=false,AutoShopEnabled=false,AutoEventsEnabled=false,AutoCampEnabled=false});
            if(RunManager.Instance.IsInProgress)throw new InvalidOperationException("Preserve existing run.");
            if(!Entry.Enabled)throw new InvalidOperationException("Required patch installation failed.");
            var expected=new[]{"0Harmony","GodotSharp","sts2"};
            if(typeof(Entry).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.Contains("Ritsu",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Forbidden framework reference.");
            if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.Contains("Ritsu",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Framework still loaded.");
            evidence.Add("NoFrameworkAssemblyReferenceOrLoadedAssembly");
            if(CentipedeNativeTest.IsRequested)
            {
                stage="centipede_native";
                await CentipedeNativeTest.RunAsync(host,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            if(NativeCrashReplayTest.IsRequested)
            {
                stage="saved_crash_replay";
                await NativeCrashReplayTest.RunAsync(host,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            if(RoomFlowNativeTest.IsRequested)await RoomFlowNativeTest.CheckMenuAsync(host,evidence);
            stage="start_non_saving_run";
            string character=System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_CHARACTER")??"FOOL";
            CharacterModel model=character=="IRONCLAD"?ModelDb.Character<Ironclad>():ModelDb.Character<Fool>();
            var testPlayer=Player.CreateForNewRun(model,new MegaCrit.Sts2.Core.Unlocks.UnlockState(SaveManager.Instance.Progress),1UL);
            await host.StartNewSingleplayerRun(testPlayer,false,ActModel.GetDefaultList(),[],"STANDALONE-20261115",GameMode.Standard,isCustomGame:false,ascensionLevel:2);
            if(RunManager.Instance.ShouldSave)throw new InvalidOperationException("Saving test refused.");
            var run=RunManager.Instance.DebugOnlyGetState()!;
            var player=LocalContext.GetMe(run)!;
            if(ModelRepairsNativeTest.IsRequested)
            {
                stage="model_repairs";
                await ModelRepairsNativeTest.RunAsync(host,run,player,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            if(CampRestoreNativeTest.IsRequested)
            {
                stage="camp_restore";
                await CampRestoreNativeTest.RunAsync(run,player,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            if(RoomFlowNativeTest.IsRequested)
            {
                stage="room_flow";
                await RoomFlowNativeTest.RunAsync(host,run,player,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            if(RoomOptionsNativeTest.IsRequested)
            {
                stage="room_options";
                await RoomOptionsNativeTest.RunAsync(host,run,player,evidence);
                Write("Passed",stage,evidence,null);
                return;
            }
            stage="native_combat";
            PauseAutomaticSearch=false;
            var encounter=ModelDb.AllEncounters.Single(e=>e.Id.Entry=="CORPSE_SLUGS_WEAK").ToMutable();
            int generation=SolverController.SearchGenerationForTesting;
            bool won=false;
            CombatManager.Instance.CombatWon+=OnWon;
            void OnWon(CombatRoom room){won=true;}
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster,MegaCrit.Sts2.Core.Map.MapPointType.Unassigned,encounter);
            var deadline=DateTime.UtcNow.AddMinutes(4);
            int maxTurn=0;
            while(!won&&DateTime.UtcNow<deadline)
            {
                maxTurn=Math.Max(maxTurn,player.PlayerCombatState?.TurnNumber??0);
                await Task.Delay(100);
                if(SolverController.PredictionFailure!=null)throw new InvalidOperationException("Prediction isolated: "+SolverController.PredictionFailure);
            }
            CombatManager.Instance.CombatWon-=OnWon;
            PauseAutomaticSearch=true;
            if(!won||player.Creature.CurrentHp<=0)throw new InvalidOperationException("Native combat did not finish with player alive.");
            if(SolverController.SearchGenerationForTesting<=generation)throw new InvalidOperationException("No solver search occurred.");
            evidence.Add($"NativeCombatWon:Character={character}:Turns={maxTurn}:Hp={player.Creature.CurrentHp}:Searches={SolverController.SearchGenerationForTesting-generation}:ManualResponses=0");
            stage="native_map_travel";
            await RunManager.Instance.EnterRoomDebug(RoomType.Map);
            var map=NMapScreen.Instance!;
            map.Open();
            var before=run.CurrentMapCoord;
            PauseRouting=false;
            deadline=DateTime.UtcNow.AddSeconds(30);
            while(run.CurrentMapCoord==before&&DateTime.UtcNow<deadline)await Task.Delay(100);
            PauseRouting=true;
            PauseAutomaticSearch=true;
            if(run.CurrentMapCoord==before)throw new InvalidOperationException("Native map did not travel.");
            evidence.Add($"NativeMapTravel:From={before}:To={run.CurrentMapCoord}:ManualResponses=0");
            Write("Passed",stage,evidence,null);
        }
        catch(Exception ex){Entry.Logger.Error("[selftest] "+stage+" "+ex);Write("Failed",stage,evidence,ex.ToString());}
        finally{host.GetTree().Quit();}
    }
    private static void Write(string status,string stage,List<string> checks,string? error)
    {
        File.WriteAllText(Path.Combine(DirectoryPath!,"result.json"),JsonSerializer.Serialize(new{status,stage,checks,error,shouldSave=RunManager.Instance.ShouldSave,finishedAt=DateTimeOffset.Now},new JsonSerializerOptions{WriteIndented=true}));
    }
}

internal sealed class StandaloneTestProgressIsolationPatch:IPatchMethod
{
    public static ModPatchTarget[] GetTargets()=>[
        new(typeof(SaveManager),nameof(SaveManager.UpdateProgressAfterCombatWon),[typeof(Player),typeof(CombatRoom)]),
        new(typeof(SaveManager),nameof(SaveManager.SaveProgressFile),[])];
    public static bool Prefix()=>!StandaloneSmokeTest.IsRunning;
}
