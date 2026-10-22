using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Übersetzt, was FtpWebRequest wirft, in die eine Unterscheidung, die
    /// darüber gebraucht wird: transient (Verbindung — Auftrag unterbrechen,
    /// später fortsetzen) oder permanent (Server sagt nein — Datei als
    /// fehlgeschlagen notieren, weitermachen). Spec „Fehlerbehandlung".
    /// </summary>
    public static class FtpErrors
    {
        public static FtpErrorKind Classify(WebExceptionStatus status, int ftpStatusCode)
        {
            if (ftpStatusCode > 0)
            {
                // 421 Dienst schließt, 425/426 Datenverbindung kaputt — das
                // sind Verbindungs-, keine Sachfehler.
                return ftpStatusCode == 421 || ftpStatusCode == 425 || ftpStatusCode == 426
                    ? FtpErrorKind.Transient
                    : FtpErrorKind.Permanent;
            }
            return FtpErrorKind.Transient;
        }

        public static string Describe(int statusCode, string serverText)
        {
            switch (statusCode)
            {
                case 550: return "Not found or access denied";
                case 553: return "Name not allowed";
                case 451: return "Console error";
                case 452: return "Not enough space on the console";
                case 421:
                case 425:
                case 426: return "Connection to the PS4 was interrupted";
            }
            var text = string.IsNullOrWhiteSpace(serverText) ? "" : ": " + serverText.Trim();
            return "FTP error " + statusCode + text;
        }

        public static Exception Translate(Exception ex, CancellationToken ct)
        {
            // Hat der Anwender abgebrochen, ist jede Folgeausnahme (meist eine
            // WebException aus request.Abort()) nur ein Symptom davon.
            if (ct.IsCancellationRequested) return new OperationCanceledException(ct);

            if (ex is FtpException) return ex;
            if (IsLocal(ex)) return ex;

            var web = ex as WebException;
            if (web != null)
            {
                var response = web.Response as FtpWebResponse;
                int code = response != null ? (int)response.StatusCode : 0;
                string message = code > 0
                    ? Describe(code, response.StatusDescription)
                    : DescribeTransport(web.Status);
                return new FtpException(message, Classify(web.Status, code), code, ex);
            }

            // Unsere Untätigkeits-Frist (FtpTransfer) — nicht der Anwender.
            if (ex is OperationCanceledException)
                return new FtpException("Timeout — the PS4 is not responding", FtpErrorKind.Transient, 0, ex);

            if (ex is IOException || ex is SocketException || ex is TimeoutException)
                return new FtpException("Connection to the PS4 was interrupted", FtpErrorKind.Transient, 0, ex);

            return ex;
        }

        /// <summary>
        /// Lokale Dateifehler (Quelle weg, Ziel gesperrt, Platte voll) sind
        /// keine Verbindungsprobleme — sie dürfen keinen Auftrag
        /// „unterbrechen". LocalFileException/FileNotFound/DirectoryNotFound/
        /// PathTooLong erben von IOException, darum VOR dem IOException-Zweig
        /// prüfen.
        /// </summary>
        private static bool IsLocal(Exception ex)
        {
            return ex is LocalFileException
                || ex is FileNotFoundException
                || ex is DirectoryNotFoundException
                || ex is PathTooLongException
                || ex is UnauthorizedAccessException;
        }

        internal static string DescribeTransport(WebExceptionStatus status)
        {
            switch (status)
            {
                case WebExceptionStatus.Timeout:
                case WebExceptionStatus.RequestCanceled:
                    return "Timeout — the PS4 is not responding";
                case WebExceptionStatus.ConnectFailure:
                    return "Cannot connect to the PS4 — is GoldHEN's FTP server running?";
                case WebExceptionStatus.NameResolutionFailure:
                    return "The PS4 address could not be resolved";
                default:
                    return "Connection to the PS4 was interrupted (" + status + ")";
            }
        }
    }
}
