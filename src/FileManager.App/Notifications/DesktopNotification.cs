using System;
using FileManager.App.Dialogs;

namespace FileManager.App.Notifications
{
    public sealed class DesktopNotification
    {
        public DesktopNotification(string title, string message, MessageKind kind, string details = null, Action openDetails = null)
            : this(Guid.NewGuid(), title, message, kind, details, openDetails) { }
        internal DesktopNotification(Guid id, string title, string message, MessageKind kind, string details, Action openDetails)
        { Id = id; Title = title ?? "PS4 Manager"; Message = message ?? ""; Kind = kind; Details = details; OpenDetails = openDetails; }
        public Guid Id { get; }
        public string Title { get; }
        public string Message { get; }
        public MessageKind Kind { get; }
        public string Details { get; }
        public Action OpenDetails { get; }
    }
}
