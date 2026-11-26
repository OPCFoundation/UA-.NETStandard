// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Configuration
{
    /// <summary>
    /// Tests for the server-wide cache of static namespace defaults read by long-lived operation
    /// contexts (monitored items, subscribers) and by service requests.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.None)]
    public class NamespaceDefaultPermissionCacheTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_server = server.CurrentInstance;
            m_configuration = (ConfigurationNodeManager)m_server.ConfigurationNodeManager!;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture!.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// A long-lived monitored item context reads static defaults from the server-wide cache,
        /// and a reported change applies to its very next check.
        /// </summary>
        [Test]
        public async Task ReportedChangeAppliesToNextCheckOfMonitoredItemContextAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/Reported").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            ServerSystemContext itemContext = CreateMonitoredItemContext();

            NamespaceDefaultPermissions first = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(first.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(Permissions(first), Is.EqualTo((uint)PermissionType.Browse));
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.True, "static defaults are cached server-wide");

            SetRoles(metadata, PermissionType.Read, report: true);

            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.False, "a reported change retires the entry");
            NamespaceDefaultPermissions second = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(Permissions(second), Is.EqualTo((uint)PermissionType.Read));
            NamespaceDefaultPermissions asyncRead = await NamespaceDefaultPermissionReader.ReadAsync(
                m_server!, itemContext, namespaceIndex).ConfigureAwait(false);
            Assert.That(Permissions(asyncRead), Is.EqualTo((uint)PermissionType.Read));
        }

        /// <summary>
        /// A value assigned in process without <c>ClearChangeMasks</c> raises no notification;
        /// it still applies to the next check of a long-lived context, as before the cache.
        /// </summary>
        [Test]
        public async Task UnreportedChangeAppliesToNextCheckAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/Unreported").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            ServerSystemContext itemContext = CreateMonitoredItemContext();
            NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.True);

            SetRoles(metadata, PermissionType.Write, report: false);

            NamespaceDefaultPermissions changed = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(Permissions(changed), Is.EqualTo((uint)PermissionType.Write));
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.False, "a pending change is read live");

            metadata.DefaultRolePermissions!.ClearChangeMasks(m_server!.DefaultSystemContext, false);
            NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);
            Assert.That(Cache.TryGet(namespaceIndex, out NamespaceDefaultPermissions cached), Is.True,
                "the namespace is cached again once the change is reported");
            Assert.That(Permissions(cached), Is.EqualTo((uint)PermissionType.Write));
        }

        /// <summary>
        /// Defaults produced by a read callback depend on the caller and are never cached
        /// server-wide; a long-lived context invokes the callback on every check.
        /// </summary>
        [Test]
        public async Task CallbackDefaultsAreReadOnEveryCheckAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/Callback").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            int calls = 0;
            metadata.DefaultRolePermissions!.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
            {
                Interlocked.Increment(ref calls);
                return ServiceResult.Good;
            };
            try
            {
                ServerSystemContext itemContext = CreateMonitoredItemContext();
                NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);
                NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);

                Assert.That(calls, Is.EqualTo(2));
                Assert.That(Cache.TryGet(namespaceIndex, out _), Is.False);
            }
            finally
            {
                metadata.DefaultRolePermissions!.OnSimpleReadValue = null;
            }
        }

        /// <summary>
        /// Assigning a read callback to a cached property takes effect at once, although it
        /// raises no notification.
        /// </summary>
        [Test]
        public async Task CallbackAssignedLaterRetiresEntryAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/LateCallback").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            ServerSystemContext itemContext = CreateMonitoredItemContext();
            NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.True);

            metadata.DefaultRolePermissions!.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant value) =>
            {
                value = Variant.FromStructure(Roles(PermissionType.Call));
                return ServiceResult.Good;
            };
            try
            {
                NamespaceDefaultPermissions read = NamespaceDefaultPermissionReader.Read(
                    m_server!, itemContext, namespaceIndex);
                Assert.That(Permissions(read), Is.EqualTo((uint)PermissionType.Call));
            }
            finally
            {
                metadata.DefaultRolePermissions!.OnSimpleReadValue = null;
            }
        }

        /// <summary>
        /// A read callback that removes itself while it runs produced a caller-specific result,
        /// which must not be shared server-wide.
        /// </summary>
        [Test]
        public async Task SelfRemovingCallbackResultIsNotCachedAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/SelfRemoving").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            PropertyState<ArrayOf<RolePermissionType>> property = metadata.DefaultRolePermissions!;
            property.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant value) =>
            {
                property.OnSimpleReadValue = null;
                value = Variant.FromStructure(Roles(PermissionType.Call));
                return ServiceResult.Good;
            };

            ServerSystemContext itemContext = CreateMonitoredItemContext();
            NamespaceDefaultPermissions first = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(Permissions(first), Is.EqualTo((uint)PermissionType.Call));
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.False, "the callback result is not cached");

            NamespaceDefaultPermissions second = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(Permissions(second), Is.EqualTo((uint)PermissionType.Browse));
        }

        /// <summary>
        /// Changing the role entries a caller received does not change the cached defaults.
        /// </summary>
        [Test]
        public async Task ReturnedRolesAreCopiesAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/Copies").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            ServerSystemContext itemContext = CreateMonitoredItemContext();

            NamespaceDefaultPermissions first = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            first.DefaultRolePermissions[0].Permissions = (uint)PermissionType.Write;
            NamespaceDefaultPermissions hit = NamespaceDefaultPermissionReader.Read(
                m_server!, itemContext, namespaceIndex);
            Assert.That(Permissions(hit), Is.EqualTo((uint)PermissionType.Browse));

            hit.DefaultRolePermissions[0].Permissions = (uint)PermissionType.Write;
            Assert.That(Cache.TryGet(namespaceIndex, out NamespaceDefaultPermissions cached), Is.True);
            Assert.That(Permissions(cached), Is.EqualTo((uint)PermissionType.Browse));
        }

        /// <summary>
        /// Removing a namespace's metadata, as when its node manager is removed or reloaded,
        /// leaves no stale defaults behind.
        /// </summary>
        [Test]
        public async Task RemovedMetadataLeavesNoStaleDefaultsAsync()
        {
            (ushort namespaceIndex, NamespaceMetadataState metadata) = await CreateNamespaceAsync(
                "http://test.org/UA/DefaultsCache/Removed").ConfigureAwait(false);
            SetRoles(metadata, PermissionType.Browse, report: true);
            ServerSystemContext itemContext = CreateMonitoredItemContext();
            NamespaceDefaultPermissionReader.Read(m_server!, itemContext, namespaceIndex);
            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.True);

            var namespaces = (NamespacesState)metadata.Parent!;
            namespaces.RemoveChild(metadata);
            await m_configuration!.DeleteNodeAsync(
                m_configuration.SystemContext, metadata.NodeId).ConfigureAwait(false);
            namespaces.ClearChangeMasks(m_server!.DefaultSystemContext, false);

            Assert.That(Cache.TryGet(namespaceIndex, out _), Is.False);
            NamespaceDefaultPermissions read = await NamespaceDefaultPermissionReader.ReadAsync(
                m_server!, itemContext, namespaceIndex).ConfigureAwait(false);
            Assert.That(read.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.HasDefaultRolePermissions, Is.False, "the removed namespace has no defaults");
        }

        /// <summary>
        /// Detaching the registry, as on a configuration node manager reload, drops the cache.
        /// </summary>
        [Test]
        public void ClearDropsEveryEntry()
        {
            var cache = new NamespaceDefaultPermissionCache();
            var metadata = NamespaceMetadataNode.From(new NamespaceMetadataState(null))!;
            cache.TryAdd(metadata, null, null, null, new NamespaceDefaultPermissions(
                300, AccessRestrictionType.None, default, default), true);
            Assert.That(cache.TryGet(300, out _), Is.True, "indices beyond the initial size grow the cache");

            cache.Clear();

            Assert.That(cache.TryGet(300, out _), Is.False);
        }

        private NamespaceDefaultPermissionCache Cache =>
            ((INamespaceDefaultPermissionCacheProvider)m_configuration!).DefaultPermissionCache;

        private async Task<(ushort, NamespaceMetadataState)> CreateNamespaceAsync(string namespaceUri)
        {
            ushort namespaceIndex = m_server!.NamespaceUris.GetIndexOrAppend(namespaceUri);
            NamespaceMetadataState? metadata = await m_configuration!
                .CreateNamespaceMetadataStateAsync(namespaceUri).ConfigureAwait(false);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata!.DefaultRolePermissions, Is.Not.Null);
            return (namespaceIndex, metadata);
        }

        private void SetRoles(NamespaceMetadataState metadata, PermissionType permissions, bool report)
        {
            metadata.DefaultRolePermissions!.Value = Roles(permissions);
            if (report)
            {
                metadata.DefaultRolePermissions.ClearChangeMasks(m_server!.DefaultSystemContext, false);
            }
        }

        private static ArrayOf<RolePermissionType> Roles(PermissionType permissions)
        {
            return
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)permissions
                }
            ];
        }

        private static uint Permissions(NamespaceDefaultPermissions defaults)
        {
            Assert.That(defaults.DefaultRolePermissions.Count, Is.EqualTo(1));
            return defaults.DefaultRolePermissions[0].Permissions;
        }

        private ServerSystemContext CreateMonitoredItemContext()
        {
            var item = new Mock<IMonitoredItem>();
            var operation = new OperationContext(item.Object);
            return m_server!.DefaultSystemContext.Copy(operation);
        }

        private ServerFixture<StandardServer>? m_fixture;
        private IServerInternal? m_server;
        private ConfigurationNodeManager? m_configuration;
    }
}
