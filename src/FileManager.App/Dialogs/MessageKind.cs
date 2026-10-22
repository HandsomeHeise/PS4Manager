namespace FileManager.App.Dialogs
{
    public enum MessageKind { Information, Success, Warning, Error }
    internal static class MessageKinds
    {
        public static string Caption(MessageKind kind)
        {
            switch (kind) {
                case MessageKind.Success: return "✓  Completed";
                case MessageKind.Warning: return "!  Attention required";
                case MessageKind.Error: return "×  Action failed";
                default: return "i  Information";
            }
        }
    }
}
