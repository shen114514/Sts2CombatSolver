using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace CombatSolver;

// Separate from the solver's fixed-size calculation panel.
internal static class AutoNextRunBanner
{
    private static PanelContainer? panel;
    private static Label? label;
    internal static void Update(string text, bool visible)
    {
        var host = NGame.Instance;
        if (host == null) return;
        if (panel == null || !GodotObject.IsInstanceValid(panel))
        {
            if (!visible) return;
            var layer = new CanvasLayer { Name = "AutoNextRunBannerLayer", Layer = 249 };
            host.AddChild(layer);
            panel = new PanelContainer { Name = "AutoNextRunBanner", CustomMinimumSize = new Vector2(460, 48) };
            panel.AddThemeStyleboxOverride("panel", SolverUiTokens.CreateBox(SolverUiTokens.Palette.Surface,
                SolverUiTokens.Palette.BorderSubtle, SolverUiTokens.Radius.Medium, 12, 8));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
            label = SolverUiTokens.CreateLabel("", SolverUiTokens.Type.Caption, SolverUiTokens.Palette.TextPrimary);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var stop = SolverUiTokens.CreateButton("停止连开", SolverButtonStyle.Secondary);
            stop.Pressed += () => SolverSettings.Update(SolverSettings.Current with { AutoNextRunEnabled = false });
            row.AddChild(label); row.AddChild(stop); panel.AddChild(row); layer.AddChild(panel);
        }
        panel.Visible = visible;
        if (!visible) return;
        label!.Text = text;
        panel.Position = new Vector2(Math.Max(16, host.GetViewportRect().Size.X - panel.Size.X - 16), 80);
    }
}
