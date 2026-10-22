using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PS4Manager.Core.Mods
{
    public enum ModInstallState { Prepared, BackingUp, Staging, Replacing, Incomplete, Installed, RollingBack, Uninstalling, RolledBack, Uninstalled, Superseded }
    public enum ModFileState { Pending, Staged, MoveOriginalIntent, OriginalMoved, InstallIntent, Installed, RestoreIntent, Restored }
    [DataContract]
    public sealed class ModReceipt
    {
        [DataMember] public int SchemaVersion=1;
        [DataMember] public Guid TransactionId;
        [DataMember] public Guid ProfileId;
        [DataMember] public Guid ModId;
        [DataMember] public Guid ReleaseId;
        [DataMember] public Guid? PredecessorId;
        [DataMember] public string TitleId;
        [DataMember] public string GameVersion;
        [DataMember] public string Title;
        [DataMember] public string ReleaseVersion;
        [DataMember] public string ReleaseJson;
        [DataMember] public string PackageHash;
        [DataMember] public ModInstallState State;
        [DataMember] public string RecoveryAction;
        [DataMember] public string LastError;
        [DataMember] public DateTime UpdatedUtc=DateTime.UtcNow;
        [DataMember] public List<ModReceiptFile> Files=new List<ModReceiptFile>();
    }
    [DataContract]
    public sealed class ModReceiptFile
    {
        [DataMember] public string Source;
        [DataMember] public string Target;
        [DataMember] public string DesiredHash;
        [DataMember] public long DesiredSize;
        [DataMember] public bool PriorExists;
        [DataMember] public string PriorHash;
        [DataMember] public long PriorSize;
        [DataMember] public bool OriginalExists;
        [DataMember] public string OriginalHash;
        [DataMember] public long OriginalSize;
        [DataMember] public string StagePath;
        [DataMember] public string RemoteOriginalPath;
        [DataMember] public ModFileState State;
        [DataMember] public string Verification;
    }
}
