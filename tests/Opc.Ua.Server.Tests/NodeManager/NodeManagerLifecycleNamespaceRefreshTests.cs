/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.ModelChange;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Regression coverage for
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/issues/4100"/>:
    /// registering a NodeManager on a running Server appends a namespace
    /// uri and bumps <c>UrisVersion</c>. A Client with model change
    /// tracking enabled must re-read its namespace table when it observes
    /// the resulting model change, otherwise it resolves NodeIds from the
    /// new namespace against a stale <c>NamespaceUris</c> table.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ModelChange")]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class NodeManagerLifecycleNamespaceRefreshTests
    {
        private const string kLiveNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeManagerLifecycleNamespaceRefresh";

        private const uint kRootNodeId = 8100;
        private const string kRootBrowseName = "NamespaceRefreshRoot";

        /// <summary>
        /// Starts a fresh <see cref="ReferenceServer"/> on opc.tcp and loads a
        /// client configuration that shares its PKI root.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(NodeManagerLifecycleNamespaceRefreshTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server and cleans up PKI artefacts.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            m_clientFixture?.Dispose();
            m_server?.Dispose();

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// A live NodeManager add appends a namespace uri on the Server. A
        /// tracking Client must pick the new uri up and be able to resolve a
        /// NodeId from it, while a subsequent same-uri reload must leave the
        /// Client table unchanged.
        /// </summary>
        [Test]
        [CancelAfter(120_000)]
        public async Task LiveNodeManagerAddRefreshesTheClientNamespaceTable(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectTrackingSessionAsync(ct)
                .ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                var refreshed = new TaskCompletionSource<ModelChangedEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.That(session.ModelChange, Is.Not.Null);
                session.ModelChange!.ModelChanged += (_, e) =>
                {
                    if (e.NamespaceTableRefreshed)
                    {
                        refreshed.TrySetResult(e);
                    }
                };

                int namespaceCountBefore = session.NamespaceUris.Count;
                Assert.That(session.NamespaceUris.GetIndex(kLiveNamespaceUri),
                    Is.EqualTo(-1),
                    "the live namespace must not be known before the add");

                NodeManagerRegistration registration = await m_server
                    .NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(CreateOptions(), null, ct)
                    .ConfigureAwait(false);

                ModelChangedEventArgs observed = await refreshed.Task
                    .WaitAsync(TimeSpan.FromSeconds(60), ct)
                    .ConfigureAwait(false);

                Assert.That(observed.NamespaceTableRefreshed, Is.True);
                Assert.That(session.NamespaceUris.Count,
                    Is.EqualTo(namespaceCountBefore + 1));

                int liveIndex = session.NamespaceUris.GetIndex(kLiveNamespaceUri);
                Assert.That(liveIndex, Is.EqualTo(namespaceCountBefore),
                    "the live namespace uri must be appended to the client table");

                // The whole point of the refresh: a NodeId expressed in the
                // new namespace must now resolve client side.
                var expanded = new ExpandedNodeId(kRootNodeId, 0, kLiveNamespaceUri, 0);
                NodeId resolved = ExpandedNodeId.ToNodeId(expanded, session.NamespaceUris);
                Assert.That(resolved.IsNull, Is.False);
                Assert.That(resolved.NamespaceIndex, Is.EqualTo((ushort)liveIndex));

                // A same-uri reload must not grow the table any further.
                await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(registration, CreateOptions(), null, ct)
                    .ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

                Assert.That(session.NamespaceUris.Count,
                    Is.EqualTo(namespaceCountBefore + 1));
                await session.CloseAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task<ManagedSession> ConnectTrackingSessionAsync(
            CancellationToken ct)
        {
            var url = new Uri(
                Utils.UriSchemeOpcTcp +
                "://localhost:" +
                m_fixture.Port.ToString(CultureInfo.InvariantCulture));

            ConfiguredEndpoint endpoint = await m_clientFixture
                .GetEndpointAsync(url, SecurityPolicies.None)
                .ConfigureAwait(false);

            return await new ManagedSessionBuilder(m_clientFixture.Config, m_telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(NodeManagerLifecycleNamespaceRefreshTests))
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .WithModelChangeTracking()
                .ConnectAsync(ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the runtime NodeSet options for a model that owns
        /// <see cref="kLiveNamespaceUri"/> and contributes a single object
        /// organized under the Objects folder.
        /// </summary>
        private static RuntimeNodeSetOptions CreateOptions()
        {
            string xml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{kLiveNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kLiveNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={kRootNodeId}" BrowseName="1:{kRootBrowseName}">
                    <DisplayName>{kRootBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;

            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        $"NamespaceRefreshTests-{kLiveNamespaceUri}",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [kLiveNamespaceUri])
                ]
            };
        }

        private ITelemetryContext m_telemetry;
        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
    }
}
