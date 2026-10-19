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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using RecoverableServiceDemo.Model;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Demonstrates normal typed calls and explicit acceptance using real local UA-TCP connections.
    /// </summary>
    public static class RecoveryDemo
    {
        /// <summary>
        /// Hosts isolated peers, completes one delayed increment, and verifies recovery and acknowledgement.
        /// </summary>
        public static async Task<RecoveryDemoResult> RunAsync(
            bool interruptNetwork, TextWriter output, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            ct.ThrowIfCancellationRequested();
            string root = Path.Combine(Path.GetTempPath(), "ua-rse-demo-" + Guid.NewGuid().ToString("N"));
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using var telemetryLifetime = telemetry as IDisposable;
            try
            {
                await output.WriteLineAsync(
                    "Local demo only: temporary certificate stores, auto-trust, and loopback-only endpoints.")
                    .ConfigureAwait(false);
                return await RunCoreAsync(interruptNetwork, output, root, telemetry, ct).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task<RecoveryDemoResult> RunCoreAsync(
            bool interruptNetwork, TextWriter output, string root, ITelemetryContext telemetry, CancellationToken ct)
        {
            var work = new DemoWork();
            int port = GetFreeLoopbackPort();
            string serverRoot = Path.Combine(root, "server");
            var serverApplication = new ApplicationInstance(telemetry)
            {
                ApplicationName = "RecoverableDemoServer",
                ApplicationType = ApplicationType.Server
            };
            await using var serverLifetime = serverApplication.ConfigureAwait(false);
            _ = await serverApplication
                .Build("urn:localhost:OPCFoundation:RecoverableDemoServer", "urn:OPCFoundation:RecoverableDemo")
                .SetMaxMessageSize(131_072)
                .AsServer([$"opc.tcp://127.0.0.1:{port}/RecoverableDemo"])
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                .AddSecurityConfiguration(CreateCertificates("RecoverableDemoServer", serverRoot), serverRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync(ct).ConfigureAwait(false);
            await CheckCertificateAsync(serverApplication, ct).ConfigureAwait(false);
            // StartAsync transfers ownership to the application. TODO: Model this async transfer in CA2000.
#pragma warning disable CA2000
            var server = new StandardServer(telemetry)
            {
                AsyncServices = new AsyncServiceExecutionOptions { Enabled = true }
            };
#pragma warning restore CA2000
            try
            {
                server.AddNodeManager(new DemoWorkNodeManagerFactory(work));
                await serverApplication.StartAsync(server, ct).ConfigureAwait(false);
            }
            catch
            {
                if (!ReferenceEquals(serverApplication.Server, server))
                {
                    server.Dispose();
                }
                throw;
            }

            var relay = new LoopbackOutageRelay(port, telemetry);
            await using var relayLifetime = relay.ConfigureAwait(false);
            string clientRoot = Path.Combine(root, "client");
            var clientApplication = new ApplicationInstance(telemetry)
            {
                ApplicationName = "RecoverableDemoClient",
                ApplicationType = ApplicationType.Client
            };
            await using var clientLifetime = clientApplication.ConfigureAwait(false);
            ApplicationConfiguration configuration = await clientApplication
                .Build("urn:localhost:OPCFoundation:RecoverableDemoClient", "urn:OPCFoundation:RecoverableDemo")
                .SetMaxMessageSize(131_072)
                .SetOperationTimeout(5000)
                .AsClient()
                .AddSecurityConfiguration(CreateCertificates("RecoverableDemoClient", clientRoot), clientRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync(ct).ConfigureAwait(false);
            await CheckCertificateAsync(clientApplication, ct).ConfigureAwait(false);
            EndpointDescription description = CoreUtils.Clone(server.GetEndpoints().Find(endpoint =>
                endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256))
                ?? throw new InvalidOperationException("The sample's secure endpoint is missing.");
            description.EndpointUrl = relay.EndpointUrl.ToString();
            description.Server.DiscoveryUrls = [description.EndpointUrl];
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(configuration))
            {
                UpdateBeforeConnect = false
            };

            ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(endpoint)
                .WithCheckDomain()
                .PreferRecoverableExecution()
                .ConnectAsync(ct).ConfigureAwait(false);
            await using var sessionLifetime = session.ConfigureAwait(false);
            NodeId originalSessionId = session.SessionId;
            var device = new DemoDeviceTypeClient(
                session, ExpandedNodeId.ToNodeId(Model.ObjectIds.DemoDevice, session.NamespaceUris), telemetry);

            RecoverableAcknowledgementScope acknowledgement =
                session.BeginAcknowledgementScope(requireRecoverableReceipt: true);
            await using var acknowledgementLifetime = acknowledgement.ConfigureAwait(false);
            Task<int> operation = device.SlowIncrementAsync(7, ct).AsTask();
            try
            {
                Task first = await Task.WhenAny(work.Entered, operation).WaitAsync(ct).ConfigureAwait(false);
                if (ReferenceEquals(first, operation))
                {
                    _ = await operation.ConfigureAwait(false);
                    throw new InvalidOperationException("The call completed without entering the Server Method.");
                }
                await work.Entered.WaitAsync(ct).ConfigureAwait(false);
                await output.WriteLineAsync("Method started once; five seconds of Server work remain.")
                    .ConfigureAwait(false);
                if (interruptNetwork)
                {
                    relay.Interrupt();
                    await output.WriteLineAsync(
                        "Network interrupted: existing TCP connections closed; reconnects are rejected.")
                        .ConfigureAwait(false);
                }
                work.Release();
                await work.Completed.WaitAsync(ct).ConfigureAwait(false);
                if (interruptNetwork)
                {
                    if (operation.IsCompleted || relay.InterruptedConnections == 0)
                    {
                        throw new InvalidOperationException(
                            "The native connection was not interrupted before delivery.");
                    }
                    await output.WriteLineAsync("Server finished while disconnected; the result is retained.")
                        .ConfigureAwait(false);
                    relay.Restore();
                    await output.WriteLineAsync("Network restored; awaiting the original typed call, not repeating it.")
                        .ConfigureAwait(false);
                }
                int result = await operation.WaitAsync(ct).ConfigureAwait(false);
                bool sameSession = session.SessionId == originalSessionId;
                RecoverableReceipt receipt = acknowledgement.Receipt
                    ?? throw new InvalidOperationException("The required recoverable receipt was not produced.");
                if (result != 7 || work.Value != 7 || work.Invocations != 1 || !sameSession || receipt.IsAcknowledged)
                {
                    throw new InvalidOperationException("The original invocation or its acceptance boundary changed.");
                }

                string resultPath = Path.Combine(root, "accepted-result.txt");
                await File.WriteAllTextAsync(resultPath, result.ToString(CultureInfo.InvariantCulture), ct)
                    .ConfigureAwait(false);
                await output.WriteLineAsync("Typed result written to the sample acceptance file; acknowledging now.")
                    .ConfigureAwait(false);
                await acknowledgement.AcknowledgeAsync(ct).ConfigureAwait(false);
                if (!receipt.IsAcknowledged)
                {
                    throw new InvalidOperationException("Explicit acknowledgement did not release the result.");
                }
                await output.WriteLineAsync(
                    $"PASS: value={result}, invocations={work.Invocations}, sameSession={sameSession}, " +
                    $"acknowledged={receipt.IsAcknowledged}.").ConfigureAwait(false);
                return new RecoveryDemoResult(result, work.Invocations, sameSession,
                    receipt.IsAcknowledged, interruptNetwork && relay.InterruptedConnections > 0);
            }
            finally
            {
                work.Release();
                relay.Restore();
            }
        }

        private static ArrayOf<CertificateIdentifier> CreateCertificates(string name, string root)
        {
            return ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                $"CN={name}, O=OPC Foundation, DC=localhost", CertificateStoreType.Directory, root);
        }

        private static async Task CheckCertificateAsync(ApplicationInstance application, CancellationToken ct)
        {
            if (!await application.CheckApplicationInstanceCertificatesAsync(
                true, CertificateFactory.DefaultLifeTime, ct: ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The local sample certificate could not be created.");
            }
        }

        private static int GetFreeLoopbackPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }

    /// <summary>
    /// Records the observable result of the local demonstration.
    /// </summary>
    /// <param name="Value">The result returned by the ordinary typed Method.</param>
    /// <param name="Invocations">The number of real Server Method invocations.</param>
    /// <param name="SameSession">Whether the original Session was retained.</param>
    /// <param name="Acknowledged">Whether explicit acceptance released the result.</param>
    /// <param name="NetworkInterrupted">Whether an established TCP connection was actually interrupted.</param>
    public sealed record RecoveryDemoResult(
        int Value, int Invocations, bool SameSession, bool Acknowledged, bool NetworkInterrupted);
}
