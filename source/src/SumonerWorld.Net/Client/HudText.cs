namespace SumonerWorld.Net.Client;

/// <summary>
/// Words for the HUD (M2-17), kept short and plain so children can read them: the key list (F1), the hint lines under the bars and
/// the companions, warnings for low vitals and the companion cards.
/// </summary>
public static class HudText
{
    /// <summary>Below this share of 255 a vital counts as low.</summary>
    public const int LowShare = 64;

    /// <summary>The key list (F1): groups of a title and its keys with what they do.</summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<(string Keys, string Does)> Keys)> KeyList { get; } =
    [
        ("Moving", [("W A S D", "walk"), ("Hold right mouse", "turn the camera"), ("Mouse wheel", "camera closer or further")]),
        ("Fighting", [("Left click", "attack (hold: strong attack)"), ("Shift", "block"), ("Space", "dodge"), ("T or middle click", "lock on to a creature")]),
        ("Creatures", [("E", "feed"), ("F", "hit"), ("R", "free from a snare"), ("P", "offer a Pact"), ("Q", "shout to scare away"), ("H", "gather berries and branches")]),
        ("Your companions", [("Z", "call a companion"), ("X", "send it back"), ("K", "its skills"), ("7 8 9 0", "attack, guard, hold, come back")]),
        ("Windows", [("1 to 0", "action bar"), ("L", "abilities"), ("I", "bag and gear"), ("B", "crafting"), ("J", "quests"), ("F1", "this key list"), ("Esc", "settings, or close a window")]),
    ];

    /// <summary>The one hint line under the bars.</summary>
    public const string BarHint = "F1: keys   Esc: settings";

    /// <summary>The combat keys over the bars.</summary>
    public const string CombatHint = "Left click: attack   Shift: block   Space: dodge";

    /// <summary>The lock-on key, added to <see cref="CombatHint"/> when lock-on is allowed.</summary>
    public const string LockOnHint = "   T: lock on";

    /// <summary>The companion keys under the companion cards.</summary>
    public const string CompanionHint = "Z: call   X: send back   K: skills";

    /// <summary>The key list as text, one key per line under each group's title.</summary>
    public static string KeyListText()
    {
        var text = new System.Text.StringBuilder();
        foreach ((string title, IReadOnlyList<(string Keys, string Does)> keys) in KeyList)
        {
            text.Append(title).Append('\n');
            foreach ((string key, string does) in keys)
            {
                text.Append("  ").Append(key).Append(": ").Append(does).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>A warning for low health, stamina or mana (health first), or empty.</summary>
    public static string Warning(byte health, byte stamina, byte mana, bool down) =>
        down ? string.Empty
        : health < LowShare ? "Low health! Step back, eat or block."
        : stamina < LowShare ? "Tired: wait a moment to dodge and block again."
        : mana < LowShare ? "Low mana: it comes back slowly."
        : string.Empty;

    /// <summary>A companion card's top line, e.g. <c>Mossback · Level 3 · young</c>.</summary>
    public static string CompanionTitle(string creature, int level, Core.Progression.LifeStage stage) =>
        $"{creature} · Level {level} · {stage.ToString().ToLowerInvariant()}";

    /// <summary>A companion card's state: its health in words when it is with the player, else <c>away (Z to call)</c>.</summary>
    public static string CompanionState(bool summoned, byte? health) =>
        !summoned ? "away (Z to call)"
        : health is not byte shown ? "with you"
        : shown == 0 ? "with you · knocked out"
        : shown < LowShare ? "with you · badly hurt"
        : shown < 192 ? "with you · hurt"
        : "with you · well";
}
