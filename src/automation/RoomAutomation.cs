using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Mod;

namespace CombatSolver;

internal static class RoomAutomation
{
    private static RoomAutomationOptions previous;
    private static long nextPoll;
    private static long faultedAt;
    internal static event Action<string>? StatusChanged;
    internal static string Status {get;private set;}="自动房间操作已关闭";
    internal static int SubmittedActions {get;private set;}
    internal static bool IsBusy=>AdvisorService.Current?.Autopilot.IsBusy==true;
    internal static RoomAutomationOptions Options=>new(SolverSettings.Current.AutoRewardsEnabled,SolverSettings.Current.AutoShopEnabled,
        SolverSettings.Current.AutoEventsEnabled,SolverSettings.Current.AutoCampEnabled);
    internal static void SetStatus(string text){if(Status==text)return;Status=text;StatusChanged?.Invoke(text);}
    internal static void ObserveSubmittedAction()=>SubmittedActions++;
    internal static RoomAutomationFeature FeatureForScreen(object? screen,AbstractRoom? room)=>screen switch
    {
        NRewardsScreen or NCardRewardSelectionScreen or NTreasureRoom=>RoomAutomationFeature.Rewards,
        _ when room is MerchantRoom=>RoomAutomationFeature.Shop,
        _ when room is RestSiteRoom=>RoomAutomationFeature.Camp,
        _ when room is EventRoom=>RoomAutomationFeature.Events,
        _=>RoomAutomationFeature.None
    };
    internal static bool AllowsCurrentScreen(RunState run)=>!CombatManager.Instance.IsInProgress&&
        !run.IsGameOver&&Options.Allows(FeatureForScreen(ActiveScreenContext.Instance?.GetCurrentScreen(),run.CurrentRoom));
    internal static void Tick()
    {
        try{TickCore();}
        catch(Exception ex)
        {
            AdvisorService.Current?.Autopilot.Pause("自动操作已暂停："+ex.GetBaseException().Message);
            AdvisorService.Current?.RecordAuto("error",new{error=ex.ToString()});
            Entry.Logger.Warn("[automation] native_control=true "+ex);
        }
    }
    private static void TickCore()
    {
        if(!Entry.Enabled||SolverController.IsMultiplayerSession||CombatManager.Instance.IsInProgress)return;
        if(SolverOverlay.SettingsVisibleForAutomation){AdvisorService.Current?.Autopilot.ResetPendingForManual();return;}
        if(ActiveScreenContext.Instance?.GetCurrentScreen() is not Node foreground
            ||!GodotObject.IsInstanceValid(foreground)||!foreground.IsInsideTree()||!foreground.IsNodeReady())return;
        var manager=RunManager.Instance;
        if(manager==null||!manager.IsInProgress||manager.DebugOnlyGetState() is not {} run||run.Players.Count!=1||run.IsGameOver)
        {AdvisorService.Clear();previous=default;return;}
        var options=Options;
        if(AdvisorService.Current is not {} service||!ReferenceEquals(service.State,run))
        {
            AdvisorService.Clear();
            var player=LocalContext.GetMe(run);
            if(player==null)return;
            service=new(run,player);previous=default;
        }
        if(previous!=options)
        {
            service.Autopilot.Pause("自动选项已更新");
            service.ClearFault();
            if(options.Any)service.Autopilot.Enable();else SetStatus("自动房间操作已关闭");
            previous=options;
        }
        if(options.Any&&!service.Autopilot.Enabled)
        {
            // 同一个房间里出错后不再永久停摆：等 15 秒再试一次，仍然失败才继续等（换房会立刻重试）。
            if(ReferenceEquals(service.FaultedRoom,run.CurrentRoom)
                && System.Environment.TickCount64-faultedAt<15000)return;
            if(!ReferenceEquals(service.FaultedRoom,run.CurrentRoom))faultedAt=System.Environment.TickCount64;
            service.ClearFault();service.Autopilot.Enable();
        }
        if(!options.Any||!AllowsCurrentScreen(run))
        {
            service.Autopilot.ResetPendingForManual();
            var feature=FeatureForScreen(foreground,run.CurrentRoom);
            if(options.Any&&feature!=RoomAutomationFeature.None)SetStatus("当前房间的自动操作已关闭，等待手动操作");
            return;
        }
        if(System.Environment.TickCount64<nextPoll)return;
        nextPoll=System.Environment.TickCount64+200;
        service.Guard(service.Poll);
        service.Autopilot.Tick();
    }
}
