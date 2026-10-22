using System;

namespace FileManager.Core.Transfers
{
    public enum StepKind
    {
        CreateRemoteFolder, UploadFile, CreateLocalFolder, DownloadFile, DeleteRemoteFile, RemoveRemoteFolder, RenameRemote, Transaction
    }

    public enum StepState { Pending, Done, Skipped, Failed }

    /// <summary>
    /// Eine Datei- oder Ordneroperation eines Auftrags. Zielpfade sind
    /// änderbar, weil „Beide behalten" das Ziel erst bei der Ausführung
    /// umbenennt: Upload → RemotePath, Download → LocalPath,
    /// Umbenennen/Verschieben → TargetRemotePath.
    /// </summary>
    public sealed class TransferStep
    {
        public TransferStep(StepKind kind, string remotePath, string localPath, string targetRemotePath, long size)
        {
            Kind = kind;
            RemotePath = remotePath;
            LocalPath = localPath;
            TargetRemotePath = targetRemotePath;
            Size = size;
            State = StepState.Pending;
        }

        public StepKind Kind { get; }
        public string RemotePath { get; internal set; }
        public string LocalPath { get; internal set; }
        public string TargetRemotePath { get; internal set; }
        public long Size { get; internal set; }

        /// <summary>Nur für RenameRemote relevant: verschiebt einen Ordner.</summary>
        public bool IsDirectory { get; internal set; }

        /// <summary>Rückschreiben einer geöffneten Datei: ersetzt ohne Konfliktfrage.</summary>
        public bool OverwriteWithoutAsking { get; internal set; }

        /// <summary>
        /// A missing remote file completes this step as skipped. Used only for
        /// optional companion files whose absence does not invalidate the
        /// primary operation.
        /// </summary>
        public bool MissingIsSuccess { get; set; }

        public StepState State { get; internal set; }
        public long Transferred { get; internal set; }
        public string Error { get; internal set; }

        /// <summary>Die Übertragung hat begonnen; Größe und Quelle werden vor einem Resume erneut geprüft.</summary>
        internal bool Started { get; set; }

        /// <summary>Der Anwender bzw. der Rückschreib-Ablauf hat das Ersetzen des Ziels erlaubt.</summary>
        internal bool TargetReplacementApproved { get; set; }

        /// <summary>Job-eindeutiger Upload-Zwischenpfad; verhindert Kollisionen mit fremden .part-Dateien.</summary>
        internal string RemotePartPath { get; set; }

        /// <summary>Job-eindeutiger Download-Zwischenpfad im Zielordner.</summary>
        internal string LocalPartPath { get; set; }

        /// <summary>Fingerabdruck der lokalen Upload-Quelle beim ersten Versuch.</summary>
        internal byte[] LocalSourceHash { get; set; }

        /// <summary>Änderungszeit der Remote-Quelle beim ersten Download-Versuch.</summary>
        internal DateTime? RemoteSourceModified { get; set; }

        /// <summary>Inhaltsfingerabdruck der Remote-Quelle beim ersten Download-Versuch.</summary>
        internal byte[] RemoteSourceHash { get; set; }

        /// <summary>Vor RNTO muss das Ziel noch exakt dem bestätigten Stand entsprechen.</summary>
        internal bool TargetIdentityRequired { get; set; }
        internal bool ExpectedTargetExists { get; set; }
        internal byte[] ExpectedTargetHash { get; set; }

        public bool IsTransfer
        {
            get { return Kind == StepKind.UploadFile || Kind == StepKind.DownloadFile || Kind == StepKind.Transaction; }
        }

        /// <summary>Was in der Bilanz als „Element" zählt — Ordner-Hilfsschritte nicht.</summary>
        public bool CountsAsItem
        {
            get { return IsTransfer || Kind == StepKind.DeleteRemoteFile || Kind == StepKind.RenameRemote; }
        }
    }
}
