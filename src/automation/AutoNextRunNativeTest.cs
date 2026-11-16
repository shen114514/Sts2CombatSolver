using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace CombatSolver;

internal static class AutoNextRunNativeTest
{
    internal static bool IsRequested => StandaloneSmokeTest.IsRunning &&
        System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_MODE") == "AUTO_NEXT";
    internal static bool AllowSaveCopyFixture { get; set; }
    internal static bool ForceSaveGuardForTesting { get; set; }
    internal static async Task RunAsync(NGame host, List<string> checks)
    {
        var original = SolverSettings.Current;
        var expectedDifficulty = AutoNextRun.UnlockedCharacters().ToDictionary(c => c.Id.Entry,
            c => SaveManager.Instance.Progress.GetStatsForCharacter(c.Id)?.PreferredAscension ?? 0);
        var layer = new CanvasLayer { Layer = 250 }; host.AddChild(layer);
        var panel = new SolverSettingsPanel { Position = new Vector2(60,40), Size = new Vector2(1120,850) };
        layer.AddChild(panel);
        try
        {
            var old = SolverSettings.DeserializeForTesting("{}");
            Check(!old.AutoNextRunEnabled && old.NextRunCharacterId == "IRONCLAD", "OldSettingsDefaultOffAndFixedIronclad", checks);
            panel.SetNextRunOptionsForTesting(true, "DEFECT");
            panel.Reload();
            Check(panel.AutoNextRunUiForTesting && SolverSettings.Current.NextRunCharacterId == "DEFECT",
                "SettingsUiEnablesFixedDefect", checks);
            panel.SetNextRunOptionsForTesting(true, AutoNextRunPolicy.RandomCharacter); panel.Reload();
            var roundTrip = SolverSettings.RoundTripForTesting(SolverSettings.Current);
            Check(roundTrip.AutoNextRunEnabled && roundTrip.NextRunCharacterId == "RANDOM" && panel.AutoNextRunUiForTesting,
                "RandomChoiceAndEnabledFlagRoundTrip", checks);
            panel.SetNextRunOptionsForTesting(true, "DEFECT"); panel.Reload();
            await Task.Delay(300); panel.ScrollNextRunIntoViewForTesting(); await Task.Delay(200);
            Screenshot("settings");
            layer.QueueFree();
            await Task.Delay(1200);
            Check(!AutoNextRun.Pending && !AutoNextRun.CountdownForTesting && AutoNextRun.StartedRunsForTesting == 0,
                "EnablingInMainMenuNeverStartsOverExistingSave", checks);

            var player = Player.CreateForNewRun(ModelDb.Character<Ironclad>(), new UnlockState(SaveManager.Instance.Progress), 1UL);
            var initial = await host.StartNewSingleplayerRun(player, false, ActModel.GetDefaultList(), [],
                "AUTONEXT-20261116", GameMode.Standard, false, 0);
            await Task.Delay(500);
            Check(!AutoNextRun.Pending && !RunManager.Instance.ShouldSave, "LiveRunNotInterruptedAndFixtureDoesNotSave", checks);

            AutoNextRun.PauseForTesting = true;
            await CreatureCmd.Kill(player.Creature, force: true);
            await Until(() => AutoNextRun.Pending, "death observed");
            SolverSettings.Update(SolverSettings.Current with { AutoNextRunEnabled = false });
            AutoNextRun.PauseForTesting = false;
            await Task.Delay(1300);
            Check(AutoNextRun.StartedRunsForTesting == 0 && RunManager.Instance.HasRunEnded,
                "SwitchOffDuringDeathScreenPreventsContinuation", checks);
            SolverSettings.Update(SolverSettings.Current with { AutoNextRunEnabled = true });
            ForceSaveGuardForTesting = true;
            await Until(() => host.MainMenu?.SubmenuStack.Peek() == null && !RunManager.Instance.IsInProgress,
                "death summary returns to menu");
            await Task.Delay(400);
            Check(AutoNextRun.Pending && !AutoNextRun.CountdownForTesting && AutoNextRun.StartedRunsForTesting == 0,
                "UnfinishedSaveConditionPreventsNewRunWithoutFileMutation", checks);
            // This developer-only fixture permits a new NON-SAVING run while the
            // official save remains present. Production never bypasses this guard.
            AllowSaveCopyFixture = true;
            ForceSaveGuardForTesting = false;
            // Exercise the real timeline submenu before countdown, preserving all
            // progress writes through the developer isolation prefixes.
            host.MainMenu!.SubmenuStack.PushSubmenuType<NTimelineScreen>();
            await Until(() => host.MainMenu?.SubmenuStack.Peek() == null, "timeline unlock and native back");
            checks.Add("TimelineNativeUnlockAndBackBeforeNextRun");
            await Until(() => AutoNextRun.CountdownForTesting, "first countdown");
            Screenshot("countdown");
            AutoNextRun.PauseForTesting = true; await Task.Delay(400);
            Check(!AutoNextRun.CountdownForTesting && AutoNextRun.StartedRunsForTesting == 0,
                "OpenSettingsOrModalConditionCancelsCountdown", checks);
            AutoNextRun.PauseForTesting = false;
            await Until(() => AutoNextRun.StartedRunsForTesting == 1, "fixed Defect next run");
            var first = RunManager.Instance.DebugOnlyGetState()!;
            Check(first.Players.Single().Character.Id.Entry == "DEFECT" && first.AscensionLevel == expectedDifficulty["DEFECT"] &&
                !RunManager.Instance.ShouldSave, "DeathToFixedDefectUsesRememberedDifficultyWithoutSaving", checks);
            await Task.Delay(1300);
            Check(AutoNextRun.StartedRunsForTesting == 1, "NoDuplicateLaunchAfterFirstStart", checks);

            await RunManager.Instance.WinRun();
            await Until(() => AutoNextRun.StartedRunsForTesting == 2, "fixed Defect repeated after win");
            var second = RunManager.Instance.DebugOnlyGetState()!;
            Check(second.Players.Single().Character.Id.Entry == "DEFECT" && second.Rng.StringSeed != first.Rng.StringSeed,
                "WinToRepeatedFixedDefectUsesFreshSeed", checks);
            SolverSettings.Update(SolverSettings.Current with { NextRunCharacterId = "RANDOM" });
            await Task.Delay(300);
            await CreatureCmd.Kill(second.Players.Single().Creature, force: true);
            await Until(() => AutoNextRun.StartedRunsForTesting == 3, "random next run");
            var random = RunManager.Instance.DebugOnlyGetState()!;
            string selected = random.Players.Single().Character.Id.Entry;
            Check(AutoNextRun.UnlockedCharacters().Any(c => c.Id.Entry == selected) && random.AscensionLevel == expectedDifficulty[selected] &&
                SolverSettings.Current.NextRunCharacterId == "RANDOM", "RandomNextRunIsUnlockedAndUsesItsOwnRememberedDifficulty", checks);
            await RunManager.Instance.WinRun();
            await Until(() => AutoNextRun.CountdownForTesting, "cancellable countdown");
            SolverSettings.Update(SolverSettings.Current with { AutoNextRunEnabled = false });
            await Task.Delay(6000);
            Check(AutoNextRun.StartedRunsForTesting == 3 && !RunManager.Instance.IsInProgress && !AutoNextRun.Pending,
                "SwitchOffDuringCountdownCancelsNextLaunch", checks);
        }
        finally
        {
            AllowSaveCopyFixture = false; ForceSaveGuardForTesting = false; AutoNextRun.PauseForTesting = false;
            SolverSettings.ApplyForTesting(original);
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
        }
        void Screenshot(string name) => host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(
            System.Environment.GetEnvironmentVariable("COMBATSOLVER_SELF_TEST_DIR")!, name + ".png"));
    }
    private static void Check(bool value, string name, List<string> checks)
    { if (!value) throw new InvalidOperationException(name); checks.Add(name); Entry.Logger.Info("[auto-next-test] PASS " + name); }
    private static async Task Until(Func<bool> condition, string stage)
    {
        var end = DateTime.UtcNow.AddSeconds(100);
        while (!condition() && DateTime.UtcNow < end) await Task.Delay(100);
        if (!condition()) throw new InvalidOperationException(stage + ": " + AutoNextRun.Status);
    }
}
