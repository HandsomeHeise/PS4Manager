using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;

namespace PS4Manager.Core.Games
{
    public enum InstalledState { Unknown, Installed, NotInstalled }
    public enum TrainerAction { EnableGodmode, DisableGodmode, RefillAmmo, GiveRk5 }
    public enum ActionOutcome { Success, NoChange, Blocked, Cancelled, Partial, Unknown }
    public enum GodmodeState { Unknown, Off, OnExternal, OnOwned }

    public sealed class TrainerIdentity : IEquatable<TrainerIdentity>
    {
        public TrainerIdentity(long generation, string host, int pid, string titleId, ulong moduleBase, string profileId)
        {
            Generation = generation; Host = (host ?? "").Trim().ToLowerInvariant(); Pid = pid;
            TitleId = (titleId ?? "").Trim().ToUpperInvariant(); ModuleBase = moduleBase; ProfileId = profileId ?? "";
        }
        public long Generation { get; }
        public string Host { get; }
        public int Pid { get; }
        public string TitleId { get; }
        public ulong ModuleBase { get; }
        public string ProfileId { get; }
        public bool Equals(TrainerIdentity other) => other != null && Generation == other.Generation &&
            Host == other.Host && Pid == other.Pid && TitleId == other.TitleId && ModuleBase == other.ModuleBase && ProfileId == other.ProfileId;
        public override bool Equals(object obj) => Equals(obj as TrainerIdentity);
        public override int GetHashCode() => Generation.GetHashCode() ^ Host.GetHashCode() ^ Pid ^ TitleId.GetHashCode() ^ ModuleBase.GetHashCode() ^ ProfileId.GetHashCode();
    }

    public sealed class GameEntry
    {
        public GameEntry(string id, string name, string titleId, string profileId, string mode, bool experimental)
        { Id = id; Name = name; TitleId = titleId; ProfileId = profileId; Mode = mode; Experimental = experimental; }
        public string Id { get; }
        public string Name { get; }
        public string TitleId { get; }
        public string ProfileId { get; }
        public string Mode { get; }
        public bool Experimental { get; }
    }

    public sealed class InstalledGame
    {
        public InstalledGame(string titleId, string name, string version, InstalledState state, string reason)
        { TitleId = titleId; Name = name; Version = version; State = state; Reason = reason; }
        public string TitleId { get; }
        public string Name { get; }
        public string Version { get; }
        public InstalledState State { get; }
        public string Reason { get; }
    }

    public sealed class TrainerStatus
    {
        public TrainerStatus(TrainerIdentity identity, bool ready, string reason, GodmodeState godmode, string playerName, string weaponName, string contextKey = null)
        { Identity = identity; Ready = ready; Reason = reason; Godmode = godmode; PlayerName = playerName; WeaponName = weaponName; ContextKey = contextKey; }
        public TrainerIdentity Identity { get; }
        public bool Ready { get; }
        public string Reason { get; }
        public GodmodeState Godmode { get; }
        public string PlayerName { get; }
        public string WeaponName { get; }
        public string ContextKey { get; }
    }

    public sealed class TrainerResult
    {
        public TrainerResult(TrainerIdentity identity, TrainerAction action, ActionOutcome outcome, string message)
        { Identity = identity; Action = action; Outcome = outcome; Message = message; }
        public TrainerIdentity Identity { get; }
        public TrainerAction Action { get; }
        public ActionOutcome Outcome { get; }
        public string Message { get; }
    }

    public sealed class TrainerTarget
    {
        public TrainerTarget(long generation, Frame4Endpoint endpoint, Func<bool> isCurrent, CancellationToken sessionToken)
        { Generation = generation; Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint)); IsCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent)); SessionToken = sessionToken; }
        public long Generation { get; }
        public Frame4Endpoint Endpoint { get; }
        public Func<bool> IsCurrent { get; }
        public CancellationToken SessionToken { get; }
    }

    public sealed class ConfirmationRequest
    {
        public ConfirmationRequest(TrainerIdentity identity, TrainerAction action, bool needsOfflineConsent, string message)
        { Identity = identity; Action = action; NeedsOfflineConsent = needsOfflineConsent; Message = message; }
        public TrainerIdentity Identity { get; }
        public TrainerAction Action { get; }
        public bool NeedsOfflineConsent { get; }
        public string Message { get; }
    }

    public interface IGameTrainer
    {
        Task<TrainerStatus> ProbeAsync(TrainerTarget target, CancellationToken ct);
        Task<TrainerResult> ExecuteAsync(TrainerTarget target, TrainerIdentity expected, TrainerAction action, CancellationToken ct, string expectedContext = null);
    }
    public interface IInstalledGames
    {
        Task<IReadOnlyList<InstalledGame>> DiscoverAsync(CancellationToken ct);
    }
}
