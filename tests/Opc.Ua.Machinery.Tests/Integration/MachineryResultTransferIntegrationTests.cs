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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Verifies immediate, owner-scoped transient-file removal through real OPC TCP sessions.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineryResultTransferIntegrationTests
    {
        /// <summary>
        /// Client close with either subscription policy, and server termination, preserve the surviving owner.
        /// </summary>
        [TestCase("delete")]
        [TestCase("retain")]
        [TestCase("server")]
        public async Task SessionTerminationRemovesOnlyItsFilesBeforeReturning(string termination)
        {
            await using var fixture = new Fixture();
            await fixture.StartAsync();
            ClientSession first = await fixture.ConnectAsync();
            ClientSession second = await fixture.ConnectAsync();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            NodeId transferId = fixture.Manager.ResultManagement!.ResultTransfer!.NodeId;
            var firstTransfer = new TemporaryFileTransferTypeClient(first, transferId, telemetry);
            var secondTransfer = new TemporaryFileTransferTypeClient(second, transferId, telemetry);
            Variant options = Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "file" });
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = cancellation.Token;
            (NodeId firstFile, uint firstHandle, _) = await firstTransfer.GenerateFileForReadAsync(options, ct);
            (NodeId secondFile, uint secondHandle, _) = await secondTransfer.GenerateFileForReadAsync(options, ct);
            var forbidden = new FileTypeClient(second, firstFile, telemetry);
            await Assert.ThatAsync(async () => await forbidden.ReadAsync(firstHandle, 3, ct),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
            await Assert.ThatAsync(async () => await secondTransfer.GenerateFileForReadAsync(options, ct),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));

            if (termination == "server")
            {
                // The expiry/termination pipeline closes the session without a client CloseSession request.
                await fixture.Server.CurrentInstance.CloseSessionAsync(null!, first.SessionId, false, ct);
            }
            else
            {
                await first.CloseAsync(1000, termination == "delete", ct);
            }

            ReadResponse observed = await second.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = firstFile, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = secondFile, AttributeId = Attributes.NodeClass }
                ], ct);
            Assert.That(observed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(observed.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            var survivor = new FileTypeClient(second, secondFile, telemetry);
            Assert.That((await survivor.ReadAsync(secondHandle, 3, ct)).ToArray(),
                Is.EqualTo(new byte[] { 2, 4, 6 }));

            (NodeId replacementFile, uint replacementHandle, _) =
                await secondTransfer.GenerateFileForReadAsync(options, ct);
            var replacement = new FileTypeClient(second, replacementFile, telemetry);
            Assert.That((await replacement.ReadAsync(replacementHandle, 3, ct)).ToArray(),
                Is.EqualTo(new byte[] { 2, 4, 6 }));
            await replacement.CloseAsync(replacementHandle, ct);
            ReadResponse removed = await second.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = replacementFile, AttributeId = Attributes.NodeClass }], ct);
            Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            await survivor.SetPositionAsync(secondHandle, 0, ct);
            Assert.That((await survivor.ReadAsync(secondHandle, 3, ct)).ToArray(),
                Is.EqualTo(new byte[] { 2, 4, 6 }));
            await survivor.CloseAsync(secondHandle, ct);
        }

        /// <summary>
        /// Hosts the stock stand-alone Machinery result owner without substituting transfer behavior.
        /// </summary>
        private sealed class Fixture : IAsyncDisposable
        {
            /// <summary>
            /// Configures a bounded result store and real listener.
            /// </summary>
            public Fixture()
            {
                var store = new InMemoryMachineryResultStore();
                store.Add(new MachineryResult(
                    new ResultDataType { ResultMetaData = new ResultMetaDataType { ResultId = "file" } },
                    ByteString.From(2, 4, 6)));
                m_factory = new MachineryResultNodeManagerFactory(
                    new MachineryResultServerOptions { MaxConcurrentResultTransfers = 2 }, store);
                m_server = new ServerFixture<StandardServer>(telemetry =>
                {
                    var server = new StandardServer(telemetry);
                    server.AddNodeManager(m_factory);
                    return server;
                })
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
            }

            /// <summary>
            /// Gets the live namespace owner.
            /// </summary>
            public MachineryResultNodeManager Manager => m_factory.Manager!;

            /// <summary>
            /// Gets the real server for server-initiated termination.
            /// </summary>
            public StandardServer Server { get; private set; } = null!;

            /// <summary>
            /// Starts the listener with short repository-owned certificate paths.
            /// </summary>
            public async Task StartAsync()
            {
                await m_server.LoadConfigurationAsync(NewPkiPath());
                Server = await m_server.StartAsync();
            }

            /// <summary>
            /// Creates another independent TCP session.
            /// </summary>
            public async Task<ClientSession> ConnectAsync()
            {
                var client = new ClientFixture(NUnitTelemetryContext.Create());
                m_clients.Add(client);
                client.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
                await client.LoadClientConfigurationAsync(NewPkiPath(), "TransferClient");
                ClientSession session = await client.ConnectAsync(
                    new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None);
                m_sessions.Add(session);
                return session;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                foreach (ClientSession session in m_sessions)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync(1000, true);
                    }
                    if (session is IAsyncDisposable asynchronous)
                    {
                        await asynchronous.DisposeAsync();
                    }
                    else
                    {
                        session.Dispose();
                    }
                }
                foreach (ClientFixture client in m_clients)
                {
                    await client.DisposeAsync();
                }
                await m_server.StopAsync();
                foreach (string path in m_pkiPaths)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                }
            }

            /// <summary>
            /// Keeps test-only PKI inside the checkout rather than the external build directory.
            /// </summary>
            private string NewPkiPath()
            {
                string root = Environment.GetEnvironmentVariable("OPCUA_GMS_TEST_ROOT") ?? Environment.CurrentDirectory;
                string path = Path.GetFullPath(
                    Path.Combine(root, "artifacts", "rt", Guid.NewGuid().ToString("N")[..8]));
                m_pkiPaths.Add(path);
                return path;
            }

            /// <summary>The real node-manager factory.</summary>
            private readonly MachineryResultNodeManagerFactory m_factory;

            /// <summary>The listener fixture.</summary>
            private readonly ServerFixture<StandardServer> m_server;

            /// <summary>The owned client fixtures.</summary>
            private readonly List<ClientFixture> m_clients = [];

            /// <summary>The owned sessions.</summary>
            private readonly List<ClientSession> m_sessions = [];

            /// <summary>Test-only certificate paths removed on teardown.</summary>
            private readonly List<string> m_pkiPaths = [];
        }
    }
}
