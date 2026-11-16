using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using SpireStrategist.Core;
using SpireStrategist.Mod;

namespace CombatSolver;

// Owns only end-of-run continuation. It never resumes, abandons, or replaces an
// unfinished run, and never advances an unrelated main-menu/profile submenu.
internal static class AutoNextRun
{
    private static RunState? owner;
    private static bool pending, faulted;
    private static string? configuredCharacter, plannedCharacter;
    private static long countdownEnds, nextClick, nextPoll;
    private static Task? operation;
    internal static int StartedRunsForTesting { get; private set; }
    internal static bool Pending => pending;
    internal static bool CountdownForTesting => countdownEnds != 0;
    internal static bool PauseForTesting { get; set; }
    internal static string Status { get; private set; } = "自动下一局已关闭";
    internal static event Action<string>? StatusChanged;

    internal static CharacterModel[] UnlockedCharacters()
        => new UnlockState(SaveManager.Instance.Progress).Characters
            .Where(c => c.GetType().Assembly == typeof(CharacterModel).Assembly).ToArray();

    internal static void Tick()
    {
        if (StandaloneSmokeTest.IsRunning && !AutoNextRunNativeTest.IsRequested) return;
        long now = System.Environment.TickCount64;
        if (now < nextPoll) return;
        nextPoll = now + 200;
        try
        {
            TickCore();
            AutoNextRunBanner.Update(Status, pending && SolverSettings.Current.AutoNextRunEnabled);
        }
        catch (Exception e)
        {
            faulted = true; ResetCountdown();
            SetStatus("自动下一局已暂停：" + e.GetBaseException().Message);
            Entry.Logger.Warn("[next-run] native_control=true " + e);
        }
    }

    private static void TickCore()
    {
        if (operation != null)
        {
            if (!operation.IsCompleted) return;
            var completed = operation; operation = null; completed.GetAwaiter().GetResult();
        }
        var settings = SolverSettings.Current;
        if (!settings.AutoNextRunEnabled)
        {
            owner = null; pending = false; faulted = false; ResetCountdown();
            configuredCharacter = null; SetStatus("自动下一局已关闭"); return;
        }
        if (configuredCharacter != settings.NextRunCharacterId)
        { configuredCharacter = settings.NextRunCharacterId; faulted = false; ResetCountdown(); }
        if (faulted) return;
        if (!Entry.Enabled || SolverController.IsMultiplayerSession)
        { owner = null; pending = false; ResetCountdown(); SetStatus("自动下一局仅支持单人标准模式"); return; }
        var manager = RunManager.Instance;
        var run = manager.DebugOnlyGetState();
        if (run != null)
        {
            if (run.Players.Count != 1 || run.GameMode != GameMode.Standard || run.IsCustomGame || manager.IsAbandoned)
            { owner = null; pending = false; ResetCountdown(); SetStatus("自动下一局仅支持单人标准模式"); return; }
            if (!ReferenceEquals(owner, run)) { owner = run; pending = false; ResetCountdown(); }
            if (!manager.HasRunEnded)
            { SetStatus("本局结束后自动开始下一局"); return; }
            pending = true;
        }
        if (!pending)
        { SetStatus("等待本局结束后续开"); return; }
        if (SolverOverlay.SettingsVisibleForAutomation || NModalContainer.Instance?.OpenModal != null ||
            (StandaloneSmokeTest.IsRunning && PauseForTesting))
        { ResetCountdown(); SetStatus("自动下一局已暂停，关闭设置或弹窗后继续"); return; }
        long now = System.Environment.TickCount64;
        var host = NGame.Instance;
        if (host == null) return;
        // These controls use native clicks and readiness checks,
        // preserving score, unlock and save completion instead of skipping it.
        var ending = GameActions.Descendants<NGameOverScreen>(host).FirstOrDefault(Ready);
        if (ending != null && ReferenceEquals(ending._runState, owner))
        {
            ResetCountdown(); SetStatus("正在完成本局结算");
            if (now < nextClick) return;
            var button = Ready(ending._mainMenuButton) ? (NClickableControl)ending._mainMenuButton : ending._continueButton;
            Click(button, now); return;
        }
        if (manager.IsInProgress) { ResetCountdown(); return; }
        if (host.MainMenu is not { } menu || !Ready(menu)) { ResetCountdown(); return; }
        var top = menu.SubmenuStack.Peek();
        if (top is NTimelineScreen timeline)
        { ResetCountdown(); HandleTimeline(timeline, now); return; }
        if (top != null)
        { ResetCountdown(); SetStatus("等待返回主菜单，当前页面保持手动操作"); return; }
        bool saved = SaveManager.Instance.HasRunSave || SaveManager.Instance.HasMultiplayerRunSave;
        if (AutoNextRunNativeTest.IsRequested && AutoNextRunNativeTest.ForceSaveGuardForTesting) saved = true;
        if (AutoNextRunNativeTest.IsRequested && AutoNextRunNativeTest.AllowSaveCopyFixture) saved = false;
        if (saved)
        { ResetCountdown(); SetStatus("检测到未完成存档，自动下一局不会覆盖它"); return; }
        if (SaveManager.Instance.CurrentRunSaveTask is { IsCompletedSuccessfully: false })
        { ResetCountdown(); SetStatus("等待本局存档处理完成"); return; }
        if (plannedCharacter == null)
        {
            var available = UnlockedCharacters();
            var ids = available.Select(c => c.Id.Entry).ToArray();
            plannedCharacter = AutoNextRunPolicy.SelectCharacter(settings.NextRunCharacterId, ids,
                ids.Length == 0 ? 0 : Random.Shared.Next(ids.Length));
            if (plannedCharacter == null)
            { SetStatus("所选角色未解锁或不可用，请在设置中重新选择"); return; }
            countdownEnds = now + 5000;
        }
        var character = UnlockedCharacters().FirstOrDefault(c => c.Id.Entry == plannedCharacter);
        if (character == null) { ResetCountdown(); return; }
        SetStatus($"{Math.Max(0, (countdownEnds - now + 999) / 1000)} 秒后开始下一局 · {character.Title.GetFormattedText()}");
        if (now < countdownEnds) return;
        // Claim before invoking asynchronous native start; ticks cannot launch twice.
        pending = false; owner = null; ResetCountdown();
        operation = StartAsync(host, character);
    }

    private static async Task StartAsync(NGame host, CharacterModel character)
    {
        var stats = SaveManager.Instance.Progress.GetStatsForCharacter(character.Id);
        int ascension = Math.Clamp(stats?.PreferredAscension ?? 0, 0, stats?.MaxAscension ?? 0);
        string seed = SeedHelper.GetRandomSeed();
        var player = Player.CreateForNewRun(character, new UnlockState(SaveManager.Instance.Progress),
            (ulong)NetSingleplayerGameService.defaultNetId);
        Entry.Logger.Info($"[next-run] START character={character.Id.Entry} ascension={ascension} saving={!StandaloneSmokeTest.IsRunning}");
        try
        {
            await host.StartNewSingleplayerRun(player, !StandaloneSmokeTest.IsRunning, ActModel.GetDefaultList(), [], seed,
                GameMode.Standard, isCustomGame: false, ascensionLevel: ascension);
        }
        catch (Exception error)
        {
            await host.ReturnToMainMenuWithInternalError(error);
            throw;
        }
        StartedRunsForTesting++;
        SetStatus("下一局已开始 · " + character.Title.GetFormattedText());
    }

    private static void HandleTimeline(NTimelineScreen timeline, long now)
    {
        SetStatus("正在完成本局解锁");
        if (now < nextClick) return;
        var unlock = GameActions.Descendants<NUnlockScreen>(timeline).FirstOrDefault(Ready);
        if (unlock != null) { Click(unlock._unlockConfirmButton, now); return; }
        if (Ready(timeline._inspectScreen)) { Click(timeline._inspectScreen._closeButton, now); return; }
        var slot = GameActions.Descendants<NEpochSlot>(timeline).FirstOrDefault(s => Ready(s) && s.State == EpochSlotState.Obtained);
        if (slot != null) { Click(slot, now); return; }
        Click(timeline._backButton, now);
    }

    private static bool Ready(Control? control) => control != null && GodotObject.IsInstanceValid(control) &&
        control.IsInsideTree() && control.IsNodeReady() && control.IsVisibleInTree();
    private static void Click(NClickableControl? button, long now)
    {
        if (!Ready(button) || !button!.IsEnabled) return;
        nextClick = now + 1000;
        Entry.Logger.Info("[next-run] CONTINUE screen_button=" + button.GetType().Name);
        button.ForceClick();
    }
    private static void ResetCountdown() { plannedCharacter = null; countdownEnds = 0; }
    private static void SetStatus(string text)
    { if (Status == text) return; Status = text; StatusChanged?.Invoke(text); }
}
