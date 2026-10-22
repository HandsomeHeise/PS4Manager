namespace PS4Manager.Core.Mods
{
    public enum ModActivationOutcomeKind { Deferred, Enabled, ActivationFailed }
    public sealed class ModActivationOutcome
    {
        public ModActivationOutcome(ModActivationOutcomeKind kind, string message) { Kind = kind; Message = message; }
        public ModActivationOutcomeKind Kind { get; }
        public string Message { get; }
    }
}
