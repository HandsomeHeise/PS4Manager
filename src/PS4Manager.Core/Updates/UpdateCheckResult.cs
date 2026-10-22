namespace PS4Manager.Core.Updates
{
    public sealed class UpdateCheckResult
    {
        public UpdateCheckResult(UpdateManifest manifest, UpdateValidationState state, string message)
        {
            Manifest = manifest;
            State = state;
            Message = message ?? "";
        }

        public UpdateManifest Manifest { get; }
        public UpdateValidationState State { get; }
        public string Message { get; }
        public bool IsUpdateAvailable { get { return State == UpdateValidationState.Valid; } }
    }
}
