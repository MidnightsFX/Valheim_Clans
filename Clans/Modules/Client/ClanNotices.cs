namespace Clans.Client {
    // Short messages to the player.
    internal static class ClanNotices {
        internal static void ShowInfo(string text) {
            if (string.IsNullOrEmpty(text)) { return; }
            Logger.LogDebug("Clans notice: " + text);
            if (MessageHud.instance != null) { MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text); }
        }

        internal static void ShowError(string text) {
            if (string.IsNullOrEmpty(text)) { return; }
            Logger.LogDebug("Clans error: " + text);
            if (MessageHud.instance != null) { MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text); }
        }
    }
}
