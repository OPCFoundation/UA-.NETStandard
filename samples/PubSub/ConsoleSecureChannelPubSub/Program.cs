/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Finite brokerless PubSub over real confidential UASC connections.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs both peers by default, or an explicitly selected process/setup role.
        /// Returns nonzero on mismatched data, authorization failure, cancellation or failed teardown.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var options = SampleOptions.Parse(args);
                if (options.Help)
                {
                    Console.WriteLine(
                        """
                        ConsoleSecureChannelPubSub [demo|setup|server|client]
                          --shared                       Services + PubSub on one authenticated UASC transport
                          --encoding uadp|json           Independently negotiated payload (default uadp)
                          --authorization configured|session
                          --reverse                      Client publishes; server subscribes
                          --count 4..1000                Samples per writer (default 8)
                          --timeout 5..300               Finite deadline in seconds (default 45)
                          --port 0..65535                Server data port (default automatically selected)
                          --pki PATH                     New demo/setup PKI, or existing process-mode PKI
                          --ready PATH                   Fresh shared readiness path for server/client
                          --result PATH                  Exact NetworkMessage and decoded-value evidence

                        No arguments provisions two owned, explicitly trusted identities and runs real sockets.
                        Separate processes require setup and OPCUA_SAMPLE_KEY_PASSWORD (16+ characters).
                        The same encoding, authorization, role and count options must be used for both processes.
                        """);
                    return 0;
                }
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
                void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                }
                Console.CancelKeyPress += Cancel;
                try
                {
                    using ILoggerFactory logging = LoggerFactory.Create(builder =>
                        builder.SetMinimumLevel(LogLevel.Warning).AddSimpleConsole());
                    var telemetry = new SampleTelemetry(logging);
                    RunEvidence evidence = await RunAsync(options, telemetry, cancellation.Token).ConfigureAwait(false);
                    if (options.ResultFile != null)
                    {
                        await EvidenceFiles.WriteAsync(options.ResultFile,
                            JsonSerializer.Serialize(evidence, EvidenceJsonContext.Default.RunEvidence),
                            cancellation.Token).ConfigureAwait(false);
                    }
                    foreach (EndpointEvidence endpoint in evidence.Endpoints)
                    {
                        string channels = endpoint.Role == "client"
                            ? $" secureChannel={endpoint.SecureChannelId}" +
                                $" serviceChannel={endpoint.ServiceSecureChannelId}" +
                                $" sameTransport={endpoint.SameServiceTransport}"
                            : string.Empty;
                        Console.WriteLine(
                            $"SUCCESS {endpoint.Role} publisher={endpoint.Publisher} " +
                            $"samples={options.Count} messages={endpoint.Sent.Length + endpoint.Received.Length} " +
                            $"revision={endpoint.Revision} sessions={endpoint.Sessions} " +
                            $"reads={endpoint.Reads} publishes={endpoint.Publishes} " +
                            $"notifications={endpoint.Notifications} calls={endpoint.Calls}{channels}");
                    }
                    return 0;
                }
                finally
                {
                    Console.CancelKeyPress -= Cancel;
                }
            }
            catch (OperationCanceledException)
            {
                await Console.Error.WriteLineAsync(
                    "FAILED: cancelled or exceeded the finite deadline; no transport fallback was used.")
                    .ConfigureAwait(false);
                return 2;
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"FAILED: {exception}").ConfigureAwait(false);
                return 1;
            }
        }

        /// <summary>
        /// Finishes resource cleanup before publishing a success report.
        /// </summary>
        private static async Task<RunEvidence> RunAsync(
            SampleOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            SamplePki pki = await SamplePki.CreateAsync(options, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable pkiLifetime = pki.ConfigureAwait(false);
            if (options.Mode is "setup" or "demo")
            {
                await pki.ProvisionAsync(telemetry, cancellationToken).ConfigureAwait(false);
            }
            switch (options.Mode)
            {
                case "setup":
                    Console.WriteLine($"SETUP complete: {pki.Root}; exactly two owned application identities.");
                    return new RunEvidence();
                case "server":
                    EndpointEvidence server = await SecureChannelSample.RunServerAsync(
                        options, pki, telemetry, null, cancellationToken).ConfigureAwait(false);
                    return new RunEvidence { Endpoints = [server] };
                case "client":
                    ServerReady ready = await EvidenceFiles.ReadReadyAsync(options, cancellationToken)
                        .ConfigureAwait(false);
                    EndpointEvidence client = await SecureChannelSample.RunClientAsync(
                        options, pki, telemetry, ready, cancellationToken).ConfigureAwait(false);
                    return new RunEvidence { Endpoints = [client] };
                default:
                    return await SecureChannelSample.RunDemoAsync(options, pki, telemetry, cancellationToken)
                        .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Supplies the existing stack telemetry surface from the application's logger factory.
    /// </summary>
    internal sealed class SampleTelemetry(ILoggerFactory logging) : TelemetryContextBase(logging);
}
