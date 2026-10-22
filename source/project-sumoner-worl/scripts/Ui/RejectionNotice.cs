using Godot;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;

namespace SumonerWorld.Game.Ui;

/// <summary>Says on screen why the server refused the session (M2-15): most often an old package after the game data changed.</summary>
public static class RejectionNotice
{
    /// <summary>Shows the notice under <paramref name="parent"/> when the session was refused; true when it did.</summary>
    public static bool Show(Node parent, ClientEndpoint session)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(session);
        if (session.State != ClientSessionState.Rejected)
        {
            return false;
        }

        string text = session.RejectReason switch
        {
            RejectReason.DataMismatch => "This game package does not match the server.\nPlease get the new game package and start it again.",
            RejectReason.ServerFull => "The server is full. Try again later.",
            _ => $"The server did not let you in ({session.RejectReason}).",
        };
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", 8);
        var layer = new CanvasLayer { Layer = 20 };
        layer.AddChild(label);
        parent.AddChild(layer);
        return true;
    }
}
