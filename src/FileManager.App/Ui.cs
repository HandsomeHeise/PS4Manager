using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileManager.App
{
    /// <summary>
    /// Kleine Helfer der App-Schicht. Öffentlich, damit das Dashboard
    /// (PS4Manager.App) Fehlertexte gleich formuliert und seine Dialoge ebenso
    /// auf den UI-Thread bringt.
    /// </summary>
    public static class Ui
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>
        /// Text einer Ausnahme für den Anwender. FtpException und
        /// ZoneViolationException tragen schon deutschen Klartext — anders als
        /// in der Gallery zählt die äußere Nachricht, nicht die innerste Ursache
        /// (Plan-Entscheidung 9).
        /// </summary>
        public static string Describe(Exception ex)
        {
            var aggregate = ex as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count == 1) ex = aggregate.InnerExceptions[0];
            return ex.Message;
        }

        /// <summary>
        /// Führt show auf dem UI-Thread aus — der Kern ruft Rückfragen und
        /// Konfliktdialoge aus Hintergrund-Threads. Auf dem UI-Thread selbst
        /// läuft show sofort (modal).
        /// </summary>
        public static Task<T> OnUiAsync<T>(Control owner, Func<T> show)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action run = () =>
            {
                try { tcs.SetResult(show()); }
                catch (Exception ex) { tcs.SetException(ex); }
            };
            if (owner.InvokeRequired) owner.BeginInvoke(run);
            else run();
            return tcs.Task;
        }

        /// <summary>Hat ein Textfeld den Fokus? Dann gehören Entf, Rücktaste und Strg+C/X/V/A ihm.</summary>
        public static bool IsTextInputFocused()
        {
            for (var control = Control.FromHandle(GetFocus()); control != null; control = control.Parent)
                if (control is TextBoxBase || control is DevExpress.XtraEditors.BaseEdit) return true;
            return false;
        }
    }
}
