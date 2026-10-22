namespace PS4Manager.Core.Updates
{
    public enum UpdateValidationState
    {
        Valid,
        InvalidManifest,
        NotNewer,
        ChannelMismatch,
        InsecureUrl,
        InvalidHash,
        MissingSignature,
        InvalidSignature
    }
}
