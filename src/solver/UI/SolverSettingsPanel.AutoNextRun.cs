using Godot;

namespace CombatSolver;

internal sealed partial class SolverSettingsPanel
{
    private CheckButton _autoNextRun = null!;
    private OptionButton _nextRunCharacter = null!;
    private readonly List<string> _nextRunCharacterIds = [];
    private Label _nextRunStatus = null!;

    private void AddAutoNextRunSettings(VBoxContainer content)
    {
        var grid = CreateSettingsGrid();
        _autoNextRun = CreateToggle();
        _reloadInputs.Add(data => _autoNextRun.SetPressedNoSignal(data.AutoNextRunEnabled));
        _autoNextRun.Toggled += enabled =>
        {
            if (_loading) return;
            SolverSettings.Update(SolverSettings.Current with { AutoNextRunEnabled = enabled });
            SetStatus(enabled ? "已开启，本局结束后生效" : "已关闭自动下一局", SolverUiTokens.Palette.Success);
        };
        AddBasicRow(grid, "本局结束后自动下一局", _autoNextRun,
            "胜利或死亡后完成结算，返回主菜单并等待 5 秒续开；可随时关闭，不会覆盖未完成的存档。");
        _nextRunCharacter = CreateOptionInput(260);
        _reloadInputs.Add(data =>
        {
            _nextRunCharacter.Clear(); _nextRunCharacterIds.Clear();
            _nextRunCharacter.AddItem("每局随机角色"); _nextRunCharacterIds.Add(AutoNextRunPolicy.RandomCharacter);
            foreach (var character in AutoNextRun.UnlockedCharacters())
            {
                _nextRunCharacter.AddItem("固定 · " + character.Title.GetFormattedText());
                _nextRunCharacterIds.Add(character.Id.Entry);
            }
            int index = _nextRunCharacterIds.IndexOf(data.NextRunCharacterId);
            if (index < 0)
            {
                index = _nextRunCharacterIds.Count;
                _nextRunCharacterIds.Add(data.NextRunCharacterId);
                _nextRunCharacter.AddItem("所选角色当前不可用");
            }
            _nextRunCharacter.Selected = index;
        });
        _nextRunCharacter.ItemSelected += index =>
        {
            if (_loading) return;
            SolverSettings.Update(SolverSettings.Current with { NextRunCharacterId = _nextRunCharacterIds[(int)index] });
            SetStatus("已保存，下一局使用此选择", SolverUiTokens.Palette.Success);
        };
        AddBasicRow(grid, "下一局角色", _nextRunCharacter,
            "随机仅从已解锁的原版角色中选择，可连续重复同一角色；固定角色会一直沿用。难度采用该角色记住的进阶等级。");
        AddSettingsSection(content, "连续开局", "默认关闭。战斗自动、选路及房间操作沿用各自开关；打开设置期间暂停结算和续开。", grid);
        _nextRunStatus = SolverUiTokens.CreateLabel(AutoNextRun.Status, SolverUiTokens.Type.Caption, SolverUiTokens.Palette.TextSecondary);
        _nextRunStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_nextRunStatus);
        AutoNextRun.StatusChanged += UpdateNextRunStatus;
        TreeExiting += () => AutoNextRun.StatusChanged -= UpdateNextRunStatus;
    }
    private void UpdateNextRunStatus(string text)
    { if (GodotObject.IsInstanceValid(_nextRunStatus)) _nextRunStatus.Text = text; }
    internal bool AutoNextRunUiForTesting => _autoNextRun.ButtonPressed == SolverSettings.Current.AutoNextRunEnabled &&
        _nextRunCharacterIds[_nextRunCharacter.Selected] == SolverSettings.Current.NextRunCharacterId;
    internal void SetNextRunOptionsForTesting(bool enabled, string character)
    {
        _autoNextRun.ButtonPressed = enabled;
        int index = _nextRunCharacterIds.IndexOf(character);
        if (index < 0) throw new InvalidOperationException("Unavailable fixture character");
        _nextRunCharacter.Selected = index;
        _nextRunCharacter.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }
    internal void ScrollNextRunIntoViewForTesting()
    {
        if (_generalPage is ScrollContainer scroll && scroll.GetChildCount() > 0)
            scroll.ScrollVertical = Math.Max(0, (int)(_autoNextRun.GlobalPosition.Y - scroll.GetChild<Control>(0).GlobalPosition.Y) - 120);
    }
}
