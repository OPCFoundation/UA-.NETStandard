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
 *
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("WoT")]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotProjectionViewCanonicalLiveTests
    {
        [Test]
        public async Task CanonicalViewIdentityCannotBeTakenByAnotherResourceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("canonical-owned-view", harness.NamespaceIndex);
            var first = new WotViewProjectionRequest(
                "first", "resource:first", NodeId.Null, identity, LegacyPlan(101));
            await harness.Views.ApplyAsync(first).ConfigureAwait(false);

            ServiceResultException? rejection = null;
            try
            {
                var conflicting = new WotViewProjectionRequest(
                    "second", "resource:second", NodeId.Null, identity, LegacyPlan(202));
                await harness.Views.ApplyAsync(conflicting).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            uint retainedVersion = await harness.ReadViewVersionAsync(identity).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "A canonical View cannot be reassigned to another Resource.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(retainedVersion, Is.EqualTo(101u),
                    "Rejected ownership must not replace the original native publication.");
            });
        }

        [Test]
        public async Task AuthoredCanonicalViewIsBrowsableInItsOwnNamespaceAsync()
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            NodeId identity = harness.Identity("urn:c2:authored-views", "Authored");
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "authored", "resource:authored", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);

            ReadResponse read = await harness.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = identity, AttributeId = Attributes.NodeClass }],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(read.Results, Has.Count.EqualTo(1));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.View));
            Assert.That(await harness.ReadViewVersionAsync(identity).ConfigureAwait(false), Is.EqualTo(1u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanonicalViewCannotReplaceAnExistingNodeRoleAsync(bool ownedProperty)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var original = new NodeId("role-owner", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "original", "resource:original", NodeId.Null, original, LegacyPlan(101))).ConfigureAwait(false);
            NodeId occupied = ownedProperty
                ? await harness.ViewVersionIdAsync(original).ConfigureAwait(false)
                : Ua.VariableIds.Server_ServerStatus_StartTime;
            DataValue before = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResultException? rejection = null;
            try
            {
                await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                    "collision", "resource:collision", NodeId.Null, occupied, LegacyPlan(202)))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                rejection = exception;
            }

            DataValue after = await harness.Session.ReadValueAsync(occupied).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Is.Not.Null, "An existing node's owner and role must not be overwritten.");
                Assert.That(rejection?.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(after.WrappedValue, Is.EqualTo(before.WrappedValue));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeViewPropertyPathsResolveWithoutInventingNodesAsync(bool existing)
        {
            await using NativeHarness harness = await NativeHarness.CreateAsync().ConfigureAwait(false);
            var identity = new NodeId("translated-view", harness.NamespaceIndex);
            await harness.Views.ApplyAsync(new WotViewProjectionRequest(
                "translated", "resource:translated", NodeId.Null, identity, LegacyPlan(1))).ConfigureAwait(false);
            NodeId propertyId = await harness.ViewVersionIdAsync(identity).ConfigureAwait(false);
            TranslateBrowsePathsToNodeIdsResponse translated = await harness.Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = identity,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                    IncludeSubtypes = false,
                                    TargetName = new QualifiedName(existing ? "ViewVersion" : "Missing")
                                }
                            ]
                        }
                    }
                ], CancellationToken.None).ConfigureAwait(false);

            Assert.That(translated.Results, Has.Count.EqualTo(1));
            BrowsePathResult result = translated.Results[0];
            if (!existing)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(result.Targets, Is.Empty);
                return;
            }
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets, Has.Count.EqualTo(1));
            Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            Assert.That(ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, harness.Session.NamespaceUris),
                Is.EqualTo(propertyId));
        }

        private static WotViewProjectionPlan LegacyPlan(uint version)
        {
            return new WotViewProjectionPlan(
                "urn:canonical-view-test", WotDocumentKind.ThingDescription, [], [], version, []);
        }

        private sealed class NativeHarness : IAsyncDisposable
        {
            private NativeHarness()
            {
                m_directory = Path.Combine(
                    Path.GetTempPath(), nameof(WotProjectionViewCanonicalLiveTests), Guid.NewGuid().ToString("N"));
                m_fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    AutoAccept = true,
                    SecurityNone = false
                };
            }

            public ISession Session { get; private set; } = null!;

            public LifecycleWotViewProjectionHost Views { get; private set; } = null!;

            public ushort NamespaceIndex { get; private set; }

            public static async Task<NativeHarness> CreateAsync()
            {
                var harness = new NativeHarness();
                try
                {
                    await harness.StartAsync().ConfigureAwait(false);
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public NodeId Identity(string namespaceUri, string identifier)
            {
                return new NodeId(
                    identifier, m_server!.CurrentInstance.NamespaceUris.GetIndexOrAppend(namespaceUri));
            }

            public async Task<uint> ReadViewVersionAsync(NodeId viewId)
            {
                NodeId propertyId = await ViewVersionIdAsync(viewId).ConfigureAwait(false);
                DataValue value = await Session.ReadValueAsync(propertyId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out uint version), Is.True);
                return version;
            }

            public async Task<NodeId> ViewVersionIdAsync(NodeId viewId)
            {
                BrowseResponse browse = await Session.BrowseAsync(
                    null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = viewId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                            IncludeSubtypes = false,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(browse.Results, Has.Count.EqualTo(1));
                Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                ReferenceDescription[] references = browse.Results[0].References.ToArray()
                    ?? throw new InvalidOperationException("The native View returned no property references.");
                ReferenceDescription property = references.Single(
                    reference => reference.BrowseName.Name == "ViewVersion");
                return ExpandedNodeId.ToNodeId(property.NodeId, Session.NamespaceUris);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (Session is not null)
                    {
                        await Session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        Session?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (m_client is not null)
                            {
                                await m_client.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                Views?.Dispose();
                            }
                            finally
                            {
                                try
                                {
                                    await m_fixture.StopAsync().ConfigureAwait(false);
                                }
                                finally
                                {
                                    try
                                    {
                                        m_coordinator?.Dispose();
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            m_registry?.Dispose();
                                        }
                                        finally
                                        {
                                            try
                                            {
                                                m_server?.Dispose();
                                            }
                                            finally
                                            {
                                                if (Directory.Exists(m_directory))
                                                {
                                                    Directory.Delete(m_directory, recursive: true);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private async Task StartAsync()
            {
                m_server = await m_fixture.StartAsync(m_directory).ConfigureAwait(false);
                m_registry = new WotRegistryService();
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
                var options = new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                };
                await m_server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator), callerContext: null)
                    .ConfigureAwait(false);
                Views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
                m_client = new ClientFixture(false, false, NUnitTelemetryContext.Create());
                await m_client.LoadClientConfigurationAsync(m_directory).ConfigureAwait(false);
                Session = await m_client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.That(Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                NamespaceIndex = (ushort)Session.NamespaceUris.GetIndex(Namespaces.WotCon);
                Assert.That(NamespaceIndex, Is.GreaterThan(0));
            }

            private readonly string m_directory;
            private readonly ServerFixture<ReferenceServer> m_fixture;
            private ReferenceServer? m_server;
            private ClientFixture? m_client;
            private WotRegistryService? m_registry;
            private WotMaterializationCoordinator? m_coordinator;
        }
    }
}
