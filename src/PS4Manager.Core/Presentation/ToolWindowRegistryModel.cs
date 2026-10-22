using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.Presentation
{
    public interface IToolWindow
    {
        event EventHandler Closed;

        bool IsClosed { get; }

        void Activate();
        void Close();
    }

    public interface IToolWindowFactory<out TWindow> where TWindow : class, IToolWindow
    {
        TWindow Create();
    }

    /// <summary>
    /// Headless single-instance registry used by the WinForms adapter. Window
    /// lifecycle and activation stay observable without constructing native UI.
    /// </summary>
    public sealed class ToolWindowRegistryModel<TWindow> where TWindow : class, IToolWindow
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, TWindow> _windows =
            new Dictionary<string, TWindow>(StringComparer.Ordinal);

        public TWindow Open(string key, IToolWindowFactory<TWindow> factory)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A stable tool key is required.", nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            TWindow existing;
            lock (_sync)
            {
                if (_windows.TryGetValue(key, out existing) && existing.IsClosed)
                {
                    _windows.Remove(key);
                    existing = null;
                }

                if (existing == null)
                {
                    var created = factory.Create();
                    if (created == null)
                        throw new InvalidOperationException("The tool-window factory returned null.");

                    EventHandler closed = null;
                    closed = (sender, args) =>
                    {
                        lock (_sync)
                        {
                            TWindow current;
                            if (_windows.TryGetValue(key, out current) &&
                                ReferenceEquals(current, created))
                                _windows.Remove(key);
                        }
                        created.Closed -= closed;
                    };
                    created.Closed += closed;
                    _windows[key] = created;
                    return created;
                }
            }

            existing.Activate();
            return existing;
        }

        /// <summary>
        /// Die offenen Fenster als Momentaufnahme — das Dashboard fragt sie vor
        /// dem Schließen nach ihren Verlustwarnungen.
        /// </summary>
        public IReadOnlyList<TWindow> Windows
        {
            get { lock (_sync) return _windows.Values.ToArray(); }
        }

        public void CloseAll()
        {
            TWindow[] windows;
            lock (_sync) windows = _windows.Values.Distinct().ToArray();
            foreach (var window in windows)
            {
                if (!window.IsClosed) window.Close();
            }
        }
    }
}
