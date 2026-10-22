using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PS4Manager.Core.Presentation;

namespace PS4Manager.App
{
    /// <summary>
    /// Ein Fenster je Werkzeug (Spec „Dashboard"): Erneutes Öffnen aktiviert das
    /// vorhandene statt ein zweites zu bauen. Welche Instanz noch lebt,
    /// entscheidet das testbare Modell; diese Klasse kleidet es in WinForms.
    /// </summary>
    internal sealed class ToolWindowRegistry
    {
        private readonly Func<string, Form> _create;
        private readonly ToolWindowRegistryModel<Window> _model = new ToolWindowRegistryModel<Window>();

        public ToolWindowRegistry(Func<string, Form> create)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
        }

        /// <summary>Die offenen Werkzeugfenster — das Dashboard fragt sie vor dem Schließen.</summary>
        public IReadOnlyList<Form> OpenForms
        {
            get
            {
                var forms = new List<Form>();
                foreach (var window in _model.Windows)
                {
                    if (!window.Form.IsDisposed) forms.Add(window.Form);
                }
                return forms;
            }
        }

        public Form Open(string key)
        {
            return _model.Open(key, new WindowFactory(this, key)).Form;
        }

        public void CloseAll()
        {
            _model.CloseAll();
        }

        private sealed class WindowFactory : IToolWindowFactory<Window>
        {
            private readonly ToolWindowRegistry _registry;
            private readonly string _key;

            public WindowFactory(ToolWindowRegistry registry, string key)
            {
                _registry = registry;
                _key = key;
            }

            public Window Create()
            {
                var form = _registry._create(_key);
                if (form == null) throw new InvalidOperationException("No window is registered for tool “" + _key + "”.");
                return new Window(form);
            }
        }

        /// <summary>Ein Formular als Fenster des Modells: Schließen ist das Ereignis, das es kennt.</summary>
        private sealed class Window : IToolWindow
        {
            public Window(Form form)
            {
                Form = form;
                form.FormClosed += (s, e) =>
                {
                    var closed = Closed;
                    if (closed != null) closed(this, EventArgs.Empty);
                };
            }

            public Form Form { get; }

            public event EventHandler Closed;

            public bool IsClosed { get { return Form.IsDisposed; } }

            public void Activate()
            {
                if (Form.IsDisposed) return;
                if (Form.WindowState == FormWindowState.Minimized) Form.WindowState = FormWindowState.Normal;
                Form.Show();
                Form.Activate();
                Form.BringToFront();
            }

            public void Close()
            {
                if (!Form.IsDisposed) Form.Close();
            }
        }
    }
}
