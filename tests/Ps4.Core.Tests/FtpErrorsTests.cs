using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ps4.Core.Ftp;
using Xunit;

namespace Ps4.Core.Tests
{
    public class FtpErrorsTests
    {
        [Theory]
        [InlineData(550, FtpErrorKind.Permanent)]
        [InlineData(553, FtpErrorKind.Permanent)]
        [InlineData(451, FtpErrorKind.Permanent)]
        [InlineData(452, FtpErrorKind.Permanent)]
        [InlineData(502, FtpErrorKind.Permanent)]
        [InlineData(421, FtpErrorKind.Transient)]
        [InlineData(425, FtpErrorKind.Transient)]
        [InlineData(426, FtpErrorKind.Transient)]
        public void Ftp_codes_are_classified(int code, FtpErrorKind expected)
        {
            Assert.Equal(expected, FtpErrors.Classify(WebExceptionStatus.ProtocolError, code));
        }

        [Theory]
        [InlineData(WebExceptionStatus.ConnectFailure)]
        [InlineData(WebExceptionStatus.Timeout)]
        [InlineData(WebExceptionStatus.RequestCanceled)]
        [InlineData(WebExceptionStatus.ReceiveFailure)]
        [InlineData(WebExceptionStatus.ConnectionClosed)]
        public void Transport_failures_without_an_ftp_answer_are_transient(WebExceptionStatus status)
        {
            Assert.Equal(FtpErrorKind.Transient, FtpErrors.Classify(status, 0));
        }

        [Fact]
        public void Known_codes_get_plain_german_text()
        {
            Assert.Equal("Not found or access denied", FtpErrors.Describe(550, "550 Requested action not taken."));
            Assert.Equal("Name not allowed", FtpErrors.Describe(553, "x"));
            Assert.Equal("Console error", FtpErrors.Describe(451, "x"));
            Assert.Equal("Not enough space on the console", FtpErrors.Describe(452, "x"));
        }

        [Fact]
        public void Unknown_codes_keep_code_and_server_text()
        {
            Assert.Equal("FTP error 502: 502 Command not implemented.",
                FtpErrors.Describe(502, "502 Command not implemented.\r\n"));
        }

        [Fact]
        public void User_cancellation_wins_over_everything()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var result = FtpErrors.Translate(new WebException("abgebrochen", WebExceptionStatus.RequestCanceled), cts.Token);
                Assert.IsAssignableFrom<OperationCanceledException>(result);
            }
        }

        [Fact]
        public void An_aborted_request_without_user_cancellation_is_a_transient_timeout()
        {
            // Unsere Frist bricht die Anfrage per request.Abort() ab — das ist
            // eine Zeitüberschreitung, kein Abbruch durch den Anwender.
            var result = FtpErrors.Translate(new WebException("x", WebExceptionStatus.RequestCanceled), CancellationToken.None);
            var ftp = Assert.IsType<FtpException>(result);
            Assert.Equal(FtpErrorKind.Transient, ftp.Kind);
            Assert.Equal(0, ftp.StatusCode);
            Assert.Contains("Timeout", ftp.Message);
        }

        [Fact]
        public void A_refused_connection_says_so()
        {
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(
                new WebException("x", WebExceptionStatus.ConnectFailure), CancellationToken.None));
            Assert.True(ftp.IsTransient);
            Assert.Contains("GoldHEN", ftp.Message);
        }

        [Fact]
        public void The_idle_timeout_of_a_copy_is_transient()
        {
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(new OperationCanceledException(), CancellationToken.None));
            Assert.True(ftp.IsTransient);
        }

        [Fact]
        public void Socket_and_io_failures_are_transient_and_keep_the_cause()
        {
            var socket = new SocketException(10054);
            var ftp = Assert.IsType<FtpException>(FtpErrors.Translate(socket, CancellationToken.None));
            Assert.True(ftp.IsTransient);
            Assert.Same(socket, ftp.InnerException);

            Assert.True(Assert.IsType<FtpException>(FtpErrors.Translate(new IOException("weg"), CancellationToken.None)).IsTransient);
        }

        [Fact]
        public void Local_file_problems_pass_through_unchanged()
        {
            var missing = new FileNotFoundException("lokal");
            Assert.Same(missing, FtpErrors.Translate(missing, CancellationToken.None));
            var denied = new UnauthorizedAccessException("lokal");
            Assert.Same(denied, FtpErrors.Translate(denied, CancellationToken.None));
        }

        [Fact]
        public void A_local_file_exception_passes_through_unchanged()
        {
            // Ein Lese-/Schreibfehler auf der lokalen Datei (gesperrt, Platte voll)
            // ist kein Verbindungsabbruch zur PS4 (Finding A, Review Task 6).
            var local = new LocalFileException("Platte voll", new IOException("voll"));
            Assert.Same(local, FtpErrors.Translate(local, CancellationToken.None));
        }

        [Fact]
        public void An_ftp_exception_passes_through_and_foreign_exceptions_stay_untouched()
        {
            var own = new FtpException("x", FtpErrorKind.Permanent, 550);
            Assert.Same(own, FtpErrors.Translate(own, CancellationToken.None));
            var foreign = new InvalidOperationException("x");
            Assert.Same(foreign, FtpErrors.Translate(foreign, CancellationToken.None));
        }
    }
}
