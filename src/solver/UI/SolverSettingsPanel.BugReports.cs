using Godot;
namespace CombatSolver;
internal sealed partial class SolverSettingsPanel
{
    private CheckButton _detailedDiagnosticLogs=null!;
    private CheckButton _retainAllCombatLogs=null!;
    private Control CreateBugReportsPage()
    {
        var content=CreatePageContent("LocalDiagnostics");
        var grid=CreateSettingsGrid();
        _detailedDiagnosticLogs=CreateToggle();
        _detailedDiagnosticLogs.Toggled+=enabled=>{if(!_loading)SolverSettings.Update(SolverSettings.Current with{EnableDetailedDiagnosticLogs=enabled});};
        _retainAllCombatLogs=CreateToggle();
        _retainAllCombatLogs.Toggled+=enabled=>{if(!_loading)SolverSettings.Update(SolverSettings.Current with{RetainAllCombatLogs=enabled});};
        AddBasicRow(grid,"搜索详细日志",_detailedDiagnosticLogs,"仅写入本机日志，开启会增加搜索开销。");
        AddBasicRow(grid,"保留战斗日志",_retainAllCombatLogs,"保留本进程的战斗诊断记录。");
        content.AddChild(grid);
        return CreatePageScroll(content);
    }
    private void ReloadBugReportsPage(SolverSettingsData data)
    {
        _detailedDiagnosticLogs.SetPressedNoSignal(data.EnableDetailedDiagnosticLogs);
        _retainAllCombatLogs.SetPressedNoSignal(data.RetainAllCombatLogs);
    }
}
