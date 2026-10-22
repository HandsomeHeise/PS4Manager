using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App.Views
{
    /// <summary>
    /// Übertragungsleiste (Spec): Aufträge mit Status, Fortschritt als
    /// Textbalken, Rate, Restzeit; Abbrechen/Fortsetzen; einklappbar.
    /// </summary>
    internal sealed class TransferPanel : UiPanel
    {
        private const int GridHeightLogical = 150;

        private readonly List<TransferJob> _jobs = new List<TransferJob>();
        private readonly ListDataSource<TransferJob> _source;
        private readonly UiPanel _header = new UiPanel { Dock = DockStyle.Top };
        private bool _expanded;

        public TransferPanel()
        {
            Dock = DockStyle.Bottom;
            Toggle = new UiButton { Text = "▸ Transfers", Dock = DockStyle.Left };
            Summary = new UiLabel { Dock = DockStyle.Fill, AutoSize = false, Text = "No active transfers" };
            CancelJob = new UiButton { Text = "Cancel", Dock = DockStyle.Right };
            ResumeJob = new UiButton { Text = "Resume", Dock = DockStyle.Right };
            ShowFailures = new UiButton { Text = "Failures …", Dock = DockStyle.Right };
            ClearList = new UiButton { Text = "Clear list", Dock = DockStyle.Right };
            Grid = new AppGrid { Dock = DockStyle.Fill, Visible = false };

            Grid.Columns.Add(new GridColumn("Job", "Job") { Width = 340, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Status", "Status") { Width = 220, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Progress", "Progress") { Width = 240, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Rate", "Rate") { Width = 90, ReadOnly = true });
            Grid.Columns.Add(new GridColumn("Remaining", "Remaining") { Width = 100, ReadOnly = true });

            _source = new ListDataSource<TransferJob>(_jobs);
            _source.Map("Job", j => j.Title);
            _source.Map("Status", j => TransferText.State(j));
            _source.Map("Progress", j => TransferText.Progress(j));
            _source.Map("Rate", j => TransferText.Rate(j));
            _source.Map("Remaining", j => TransferText.Remaining(j));
            _source.Map("Job", j => j);
            Grid.DataSource = _source;

            // Zuletzt Hinzugefügtes dockt zuerst: Umschalter links, Knöpfe rechts, Text dazwischen.
            _header.Controls.Add(Summary);
            _header.Controls.Add(ClearList);
            _header.Controls.Add(ShowFailures);
            _header.Controls.Add(ResumeJob);
            _header.Controls.Add(CancelJob);
            _header.Controls.Add(Toggle);
            Controls.Add(Grid);
            Controls.Add(_header);

            Toggle.Click += (s, e) => Expanded = !Expanded;
            Relayout();
        }

        public UiButton Toggle { get; }
        public UiButton CancelJob { get; }
        public UiButton ResumeJob { get; }
        public UiButton ShowFailures { get; }
        public UiButton ClearList { get; }
        public UiLabel Summary { get; }
        public AppGrid Grid { get; }

        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Grid.Visible = value;
                Toggle.Text = (value ? "▾" : "▸") + " Transfers";
                Relayout();
            }
        }

        public TransferJob SelectedJob
        {
            get
            {
                int row = Grid.Selection.CurrentRow;
                return row >= 0 && row < _source.RowCount ? (TransferJob)_source.GetValue(row, "Job") : null;
            }
        }

        /// <summary>
        /// Neu binden nur, wenn sich die Anzahl ändert — sonst genügt Neuzeichnen,
        /// und die Auswahl (fürs Abbrechen/Fortsetzen) bleibt stehen.
        /// </summary>
        public void ShowJobs(IReadOnlyList<TransferJob> jobs)
        {
            bool sameCount = jobs.Count == _jobs.Count;
            _jobs.Clear();
            _jobs.AddRange(jobs);
            if (sameCount)
            {
                Grid.Invalidate();
            }
            else
            {
                Grid.DataSource = null;
                Grid.DataSource = _source;
            }
            Summary.Text = TransferText.Summary(jobs);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        private void Relayout()
        {
            int headerChrome = _header.Height - _header.DisplayRectangle.Height;
            _header.Height = Toggle.GetPreferredSize(Size.Empty).Height + headerChrome;
            int chrome = Height - DisplayRectangle.Height;
            Height = _header.Height + (_expanded ? DpiScale.Scale(GridHeightLogical, DeviceDpi) : 0) + chrome;
        }
    }
}
