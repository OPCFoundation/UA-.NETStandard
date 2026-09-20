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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.WotCon.Tests.Registry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Coordinated mutation contracts over the stock projection host, file store and UA-TCP client.
    /// </summary>
    [TestFixture]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Category("WotCon")]
    [Category("Integration")]
    [Category("NativeTcp")]
    [NonParallelizable]
    public sealed class WotCoordinatedMutationTests : IAsyncDisposable
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotCoordinatedMutationTests),
                Guid.NewGuid().ToString("N"));
            try
            {
                m_store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
                var options = new WotRegistryServerOptions
                {
                    AutoRefresh = false,
                    IdentityBindings = WotRegistryTestAuthorities.ForResources("sensor")
                };
                m_registry = new WotRegistryService(m_store, options.Bounds, options.IdentityBindings);
                await m_registry.InitializeAsync().ConfigureAwait(false);
                WotRegistryMutationResult created = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "sensor",
                    VersionId = "v1",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:sensor"))
                }).ConfigureAwait(false);
                Assert.That(created.Changed, Is.True, created.Message);
                Assert.That(created.Resource, Is.Not.Null);
                m_identity = created.Resource!;
                m_modelUri = $"urn:wot:{m_identity.GroupId}/{m_identity.ResourceId}";

                m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    SecurityNone = true,
                    AutoAccept = true
                };
                m_server = await m_fixture.StartAsync(Path.Combine(m_root, "pki")).ConfigureAwait(false);
                var converter = new FakeWotDocumentConverter();
                converter.SetRootNodeId(m_identity.ResourceId, new ExpandedNodeId(5000u, m_modelUri));
                m_coordinator = new WotMaterializationCoordinator(
                    m_registry,
                    new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                    documentConverter: converter);
                await m_server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator),
                    callerContext: null).ConfigureAwait(false);
                Assert.That(m_coordinator.Generation, Is.EqualTo(1u));

                m_client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
                await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "pki")).ConfigureAwait(false);
                Assert.That(m_client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
                m_session = await m_client.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"),
                    SecurityPolicies.None).ConfigureAwait(false);
                m_active = Resource();
                m_projectedNodeId = m_active.RootNodeId;
                Assert.That(m_projectedNodeId.IsNull, Is.False);
                DataValue before = await ReadProjectedNodeClassAsync().ConfigureAwait(false);
                Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(before.WrappedValue.TryGetValue(out int nodeClass), Is.True);
                Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object));
                m_projectionOwner = m_server.NodeManagerLifecycle.Registrations.ToList().Single(
                    registration => registration.NamespaceUris.Contains(m_modelUri));
            }
            catch (Exception setupFailure) when (setupFailure is not OutOfMemoryException)
            {
                var failures = new List<Exception> { setupFailure };
                await CleanupAsync(failures).ConfigureAwait(false);
                throw new AggregateException("Coordinated-mutation test setup failed.", failures);
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            var failures = new List<Exception>();
            await CleanupAsync(failures).ConfigureAwait(false);
            if (failures.Count != 0)
            {
                throw new AggregateException("Coordinated-mutation test cleanup failed.", failures);
            }
        }

        [Test]
        public async Task ProgrammaticDisableWithoutAutoRefreshRetiresTheLiveOwner()
        {
            long versionEpoch = m_active.FindVersion("v1")!.Epoch;

            WotRegistryMutationResult mutation = await m_registry.SetEnabledAsync(
                m_identity.GroupId,
                m_identity.ResourceId,
                false,
                m_active.MetaEpoch).ConfigureAwait(false);

            Assert.That(mutation.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), mutation.Message);
            DataValue after = await ReadProjectedNodeClassAsync().ConfigureAwait(false);
            Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList().Any(
                registration => registration.Id == m_projectionOwner.Id), Is.False);
            Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
            WotResource disabled = Resource();
            Assert.That(disabled.Enabled, Is.False);
            Assert.That(disabled.ActiveVersionId, Is.Null.Or.Empty);
            Assert.That(disabled.RootNodeId.IsNull, Is.True);
            Assert.That(disabled.RefreshGeneration, Is.EqualTo(2u));
            Assert.That(disabled.FindVersion("v1")!.Epoch, Is.EqualTo(versionEpoch));
            Assert.That(disabled.FindVersion("v1")!.Digest, Is.EqualTo(m_active.FindVersion("v1")!.Digest));
        }

        [Test]
        public async Task ProgrammaticDeleteWithoutAutoRefreshRetiresTheLiveOwner()
        {
            WotRegistryMutationResult mutation = await m_registry.DeleteResourceAsync(
                m_identity.GroupId,
                m_identity.ResourceId,
                m_active.MetaEpoch).ConfigureAwait(false);

            Assert.That(mutation.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), mutation.Message);
            Assert.That(m_registry.Current.FindResource(m_identity.GroupId, m_identity.ResourceId), Is.Null);
            DataValue after = await ReadProjectedNodeClassAsync().ConfigureAwait(false);
            Assert.That(after.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList().Any(
                registration => registration.Id == m_projectionOwner.Id), Is.False);
            Assert.That(m_coordinator.Generation, Is.EqualTo(2u));
        }

        private async Task<DataValue> ReadProjectedNodeClassAsync()
        {
            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = m_projectedNodeId, AttributeId = Attributes.NodeClass }
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private WotResource Resource()
        {
            return m_registry.Current.FindResource(m_identity.GroupId, m_identity.ResourceId) ??
                throw new InvalidOperationException("The test resource unexpectedly disappeared.");
        }

        private async Task CleanupAsync(List<Exception> failures)
        {
            await CaptureCleanupAsync(async () =>
            {
                if (m_session is not null)
                {
                    try
                    {
                        await m_session.CloseAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_session.Dispose();
                        m_session = null!;
                    }
                }
            }, failures).ConfigureAwait(false);
            await CaptureCleanupAsync(async () =>
            {
                if (m_fixture is not null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }, failures).ConfigureAwait(false);
            CaptureCleanup(() => m_server?.Dispose(), failures);
            await CaptureCleanupAsync(async () =>
            {
                if (m_client is not null)
                {
                    await m_client.DisposeAsync().ConfigureAwait(false);
                }
            }, failures).ConfigureAwait(false);
            CaptureCleanup(() => m_coordinator?.Dispose(), failures);
            CaptureCleanup(() => m_registry?.Dispose(), failures);
            CaptureCleanup(() => m_store?.Dispose(), failures);
            if (failures.Count == 0 && !string.IsNullOrEmpty(m_root) && Directory.Exists(m_root))
            {
                CaptureCleanup(() => Directory.Delete(m_root, recursive: true), failures);
            }
        }

        private static async Task CaptureCleanupAsync(Func<Task> cleanup, List<Exception> failures)
        {
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
        }

        private static void CaptureCleanup(Action cleanup, List<Exception> failures)
        {
            try
            {
                cleanup();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
        }

        private string m_root = null!;
        private string m_modelUri = null!;
        private FileWotRegistryStore m_store = null!;
        private WotRegistryService m_registry = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private ClientFixture m_client = null!;
        private Opc.Ua.Client.ISession m_session = null!;
        private WotResource m_identity = null!;
        private WotResource m_active = null!;
        private NodeManagerRegistration m_projectionOwner = null!;
        private NodeId m_projectedNodeId;
        private int m_disposed;
    }
}
