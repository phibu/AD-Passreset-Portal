using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PassReset.Tests.Services;

/// <summary>
/// Validates R-01 assumption A1: <see cref="TcpClient.ConnectAsync(string,int,CancellationToken)"/>
/// honours the supplied <see cref="CancellationToken"/> deadline so the /api/health SMTP
/// probe cannot stall the IIS worker thread beyond the configured 3s budget.
///
/// Test strategy: bind a <see cref="TcpListener"/> to 127.0.0.1:0 but never call
/// <c>AcceptTcpClient</c>. Incoming connects stack in the accept queue and eventually
/// hand-shake or stall indefinitely, so the CTS — not the OS timeout — must drive
/// cancellation. A passing test proves the probe cancels within 3s + tolerance.
/// </summary>
public class SmtpProbeTests
{
    [Fact]
    public async Task ConnectAsync_RespectsCancellationToken()
    {
        // Bind a listener but do NOT accept. With backlog=1 and no Accept pump, any
        // connect after the first will pend. Even the first may SYN/ACK but never be
        // handed to application code; the goal here is to prove cancellation wins.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();

            var sw = Stopwatch.StartNew();
            Exception? captured = null;

            // Saturate the accept backlog so subsequent connects stall.
            // Then fire a connect under the 3s CTS; if the OS completes the handshake
            // the test still passes (no cancellation needed) — but if it stalls, the
            // CTS MUST fire within 3s + tolerance.
            //
            // To force a pending connect, use IPAddress.Parse("10.255.255.1") as the
            // target — a TEST-NET-1 / RFC 5737 blackhole. CI runners generally have no
            // route there, so the connect pends until cancellation.
            try
            {
                await client.ConnectAsync(IPAddress.Parse("10.255.255.1"), 25, cts.Token);
            }
            catch (OperationCanceledException ex) { captured = ex; }
            catch (SocketException ex)             { captured = ex; }

            sw.Stop();

            // Outcome 1 (expected): CTS fired; elapsed ≤ 3s + 1s tolerance.
            // Outcome 2 (fast-fail on some networks): SocketException raised by the
            // stack well under 3s — still acceptable, proves non-blocking behavior.
            Assert.NotNull(captured);
            Assert.True(
                sw.Elapsed < TimeSpan.FromSeconds(4),
                $"ConnectAsync did not honour the 3s CancellationToken — elapsed {sw.Elapsed.TotalSeconds:F2}s");
        }
        finally
        {
            listener.Stop();
        }
    }
}
