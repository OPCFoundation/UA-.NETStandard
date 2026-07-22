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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon;
using Quickstarts.ReferenceServer;
using WotConModel = Opc.Ua.WotCon;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// End-to-end lifecycle test for the WoT Connectivity V2 registry hosted on a
    /// real running <see cref="ReferenceServer"/>. It registers a Thing
    /// Description, materializes it as a shadow-reloadable runtime projection,
    /// creates a real subscription and monitored item on the projected value,
    /// registers a compatible new version, refreshes into a new generation, and
    /// verifies that new Read/Browse observe the new generation while the existing
    /// monitored item is kept alive on the retained generation until the
    /// subscription is deleted and the retired projection is cleaned up.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("RuntimeNodeSet")]
    [Category("WotCon")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class WotRegistryLifecycleTests
    {
        private const double kMaxAge = 10000;
        private const string kModelNamespaceUri = "urn:wot:e2e:sensor";
        private const uint kRootNodeId = 5000;
        private const uint kValueNodeId = 5001;
        private const uint kGenChildBaseNodeId = 5100;
        private const string kValueBrowseName = "Value";

        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private RequestHeader m_requestHeader = null!;
        private SecureChannelContext m_secureChannelContext = null!;
        private ILogger m_logger = null!;

        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private NodeManagerRegistration m_registryRegistration = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotRegistryLifecycleTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create().CreateLogger<WotRegistryLifecycleTests>();

            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;

            // Host the WoT registry NodeManager on the running server with a
            // deterministic converter so the projected value node is predictable.
            var options = new WotRegistryServerOptions
            {
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.None,
                    AllowAnonymous = true,
                    RequiredRoleId = ObjectIds.WellKnownRole_Anonymous
                }
            };
            m_registry = new WotRegistryService();
            var host = new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle);
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SensorConverter());
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            m_registryRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(factory).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_coordinator?.Dispose();
            m_registry?.Dispose();
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

        [Test]
        public async Task RegisterMaterializeSubscribeRefreshAndRetireAsync()
        {
            IServerInternal server = m_server.CurrentInstance;

            // 1. Register and materialize the first generation of the Thing Description.
            await UpsertSensorAsync("sensor", generation: 1).ConfigureAwait(false);
            WotRefreshResult first = await m_coordinator
                .RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(first.Results.Any(r => r.LoadState == WoTLoadStateEnum.Active), Is.True,
                "The registered Thing Description must materialize into an active projection.");

            // The browseable registry projection exposes the group and resource.
            await AssertRegistryProjectionAsync(server).ConfigureAwait(false);

            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(ns, Is.GreaterThan(0), "The projected model namespace must be registered.");
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var gen1ChildNodeId = new NodeId(kGenChildBaseNodeId + 1u, ns);
            var gen2ChildNodeId = new NodeId(kGenChildBaseNodeId + 2u, ns);

            DataValue value1 = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(value1.StatusCode, Is.EqualTo(StatusCodes.Good),
                "The projected value node must be materialized and readable.");
            DataValue gen1Read = await ReadValueAsync(gen1ChildNodeId).ConfigureAwait(false);
            Assert.That(gen1Read.StatusCode, Is.EqualTo(StatusCodes.Good),
                "The first generation's node must be present after materialization.");

            // 2. Create a subscription and monitored item on the projected value.
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                .ConfigureAwait(false);
            ArrayOf<SubscriptionAcknowledgement> acks = default;
            (DataValue? initial, acks) = await PublishForDataChangeAsync(
                services, subscriptionId, acks, clientHandle: 1).ConfigureAwait(false);
            Assert.That(initial, Is.Not.Null,
                "The monitored item must deliver an initial data-change notification.");

            try
            {
                // 3. Register a compatible new version and refresh into a new generation.
                await UpsertSensorAsync("sensor", generation: 2).ConfigureAwait(false);
                WotRefreshResult second = await m_coordinator
                    .RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
                Assert.That(second.NewGeneration, Is.GreaterThan(first.NewGeneration),
                    "A compatible new version must advance the refresh generation.");

                // 4. New Read/Browse observe the new generation: new service requests
                // route to the replacement generation (which exposes the Gen2 node and
                // no longer the Gen1 node), while the value node persists across both.
                DataValue value2 = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(value2.StatusCode, Is.EqualTo(StatusCodes.Good));
                DataValue gen2Read = await ReadValueAsync(gen2ChildNodeId).ConfigureAwait(false);
                Assert.That(gen2Read.StatusCode, Is.EqualTo(StatusCodes.Good),
                    "A Read after the refresh must observe the new generation's node.");
                DataValue gen1AfterSwitch = await ReadValueAsync(gen1ChildNodeId).ConfigureAwait(false);
                Assert.That(
                    gen1AfterSwitch.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown).Or.EqualTo(StatusCodes.BadNodeIdInvalid),
                    "New requests must no longer resolve the retired generation's node.");

                BrowseResponse rootBrowse = await BrowseAsync(rootNodeId).ConfigureAwait(false);
                ArrayOf<ReferenceDescription> references = rootBrowse.Results[0].References;
                Assert.That(
                    references.Contains(r => r.BrowseName.Equals(
                        new QualifiedName(GenChildBrowseName(2), ns))),
                    Is.True, "Browse must observe the new generation's child.");
                Assert.That(
                    references.Contains(r => r.BrowseName.Equals(
                        new QualifiedName(GenChildBrowseName(1), ns))),
                    Is.False, "Browse must no longer observe the retired generation's child.");

                // 5. The existing monitored item remains alive across the switch: the
                // subscription still services publishes without invalidating the item.
                (_, acks) = await PublishKeepAliveAsync(services, subscriptionId, acks)
                    .ConfigureAwait(false);
            }
            finally
            {
                // 6. Delete the subscription, releasing the monitored item.
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            // 7. Remove the resource and refresh: the retired projection is cleaned up.
            await m_registry.DeleteResourceAsync(WotRegistryGroups.ThingDescriptions, "sensor")
                .ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            DataValue removed = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(
                removed.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown).Or.EqualTo(StatusCodes.BadNodeIdInvalid),
                "The retired projection's nodes must be cleaned up after the resource is removed.");
        }

        private async Task AssertRegistryProjectionAsync(IServerInternal server)
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, server.NamespaceUris);
            ushort v2Ns = (ushort)server.NamespaceUris.GetIndex(WotConModel.Namespaces.WotCon);
            var groupNodeId = new NodeId(
                "WoTRegistry/groups/" + WotRegistryGroups.ThingDescriptions, v2Ns);
            var resourceNodeId = new NodeId(
                $"WoTRegistry/groups/{WotRegistryGroups.ThingDescriptions}/resources/sensor", v2Ns);

            bool groupVisible = await WaitForConditionAsync(async () =>
            {
                BrowseResponse browse = await BrowseAsync(registryNodeId).ConfigureAwait(false);
                return browse.Results[0].References.Contains(r =>
                    ExpandedNodeId.ToNodeId(r.NodeId, server.NamespaceUris) == groupNodeId);
            }).ConfigureAwait(false);
            Assert.That(groupVisible, Is.True, "WoTRegistry must expose the Thing Description group.");

            bool resourceVisible = await WaitForConditionAsync(async () =>
            {
                BrowseResponse browse = await BrowseAsync(groupNodeId).ConfigureAwait(false);
                return browse.Results[0].References.Contains(r =>
                    ExpandedNodeId.ToNodeId(r.NodeId, server.NamespaceUris) == resourceNodeId);
            }).ConfigureAwait(false);
            Assert.That(resourceVisible, Is.True,
                "The group must expose the registered resource document.");
        }

        [Test]
        public async Task CrudMethodsAndFileUploadDriveTheRegistryAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, server.NamespaceUris);

            // 1. CreateGroup via the xRegistry CreateGroup Method on WoTRegistry.
            NodeId createGroupId = await FindChildAsync(registryNodeId, "CreateGroup")
                .ConfigureAwait(false);
            CallMethodResult createGroup = await CallAsync(
                registryNodeId, createGroupId, new Variant("sensors")).ConfigureAwait(false);
            Assert.That(createGroup.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_registry.Current.FindGroup("sensors"), Is.Not.Null);
            var groupNodeId = (NodeId)createGroup.OutputArguments[0]
                .AsBoxedObject(Variant.BoxingBehavior.Legacy);

            // 2. GetOrCreateResource with RequestFileOpen returns a write FileHandle.
            NodeId getOrCreateResourceId = await FindChildAsync(groupNodeId, "GetOrCreateResource")
                .ConfigureAwait(false);
            CallMethodResult createResource = await CallAsync(
                groupNodeId, getOrCreateResourceId,
                new Variant("thing1"), new Variant(string.Empty), new Variant(true))
                .ConfigureAwait(false);
            Assert.That(createResource.StatusCode, Is.EqualTo(StatusCodes.Good));
            var resourceNodeId = (NodeId)createResource.OutputArguments[0]
                .AsBoxedObject(Variant.BoxingBehavior.Legacy);
            uint fileHandle = createResource.OutputArguments[2].GetUInt32();
            Assert.That(fileHandle, Is.Not.Zero,
                "RequestFileOpen must return a non-zero write FileHandle.");

            byte[] td = Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"urn:thing1\",\"title\":\"thing1\"}");

            // 3. Write the document body through the inherited FileType and commit on Close.
            NodeId writeId = await FindChildAsync(resourceNodeId, "Write").ConfigureAwait(false);
            NodeId closeId = await FindChildAsync(resourceNodeId, "Close").ConfigureAwait(false);
            CallMethodResult write = await CallAsync(
                resourceNodeId, writeId,
                new Variant(fileHandle), new Variant(ByteString.From(td))).ConfigureAwait(false);
            Assert.That(write.StatusCode, Is.EqualTo(StatusCodes.Good));

            CallMethodResult close = await CallAsync(
                resourceNodeId, closeId, new Variant(fileHandle)).ConfigureAwait(false);
            Assert.That(close.StatusCode, Is.EqualTo(StatusCodes.Good));

            WotResource stored = m_registry.Current.FindResource("sensors", "thing1");
            Assert.That(stored?.DefaultVersion, Is.Not.Null,
                "Closing the write handle must commit the buffered document as a version.");

            // 4. Validate the stored document.
            NodeId validateId = await FindChildAsync(resourceNodeId, "Validate").ConfigureAwait(false);
            CallMethodResult validate = await CallAsync(resourceNodeId, validateId)
                .ConfigureAwait(false);
            Assert.That(validate.StatusCode, Is.EqualTo(StatusCodes.Good));
            object outcomeBoxed = validate.OutputArguments[0]
                .AsBoxedObject(Variant.BoxingBehavior.Legacy);
            Assert.That(outcomeBoxed, Is.InstanceOf<ExtensionObject>());
            Assert.That(((ExtensionObject)outcomeBoxed).TryGetValue(
                out WoTValidationOutcomeDataType outcome), Is.True);
            Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Success));

            // 5. SetEnabled(false) through the document Method.
            NodeId setEnabledId = await FindChildAsync(resourceNodeId, "SetEnabled")
                .ConfigureAwait(false);
            CallMethodResult setEnabled = await CallAsync(
                resourceNodeId, setEnabledId,
                new Variant(false), new Variant(0u)).ConfigureAwait(false);
            Assert.That(setEnabled.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_registry.Current.FindResource("sensors", "thing1")!.Enabled, Is.False);

            // 6. Delete the resource through the xRegistry Delete Method.
            NodeId deleteId = await FindChildAsync(resourceNodeId, "Delete").ConfigureAwait(false);
            CallMethodResult delete = await CallAsync(
                resourceNodeId, deleteId, new Variant(0u)).ConfigureAwait(false);
            Assert.That(delete.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_registry.Current.FindResource("sensors", "thing1"), Is.Null);
        }

        [Test]
        public async Task LabelsAddUpdateRemoveViaRealNodeManagerAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var registryNodeId = ExpandedNodeId.ToNodeId(
                WotConModel.ObjectIds.WoTRegistry, server.NamespaceUris);

            // ---- registry-level Labels ------------------------------------
            NodeId registryLabelsId = await FindChildAsync(registryNodeId, "Labels")
                .ConfigureAwait(false);
            NodeId registryAddId = await FindChildAsync(registryLabelsId, "AddAttribute")
                .ConfigureAwait(false);
            NodeId registryRemoveId = await FindChildAsync(registryLabelsId, "RemoveAttribute")
                .ConfigureAwait(false);

            CallMethodResult addRegistry = await CallAsync(
                registryLabelsId, registryAddId,
                new Variant("environment"), new Variant("production"), new Variant(0u))
                .ConfigureAwait(false);
            Assert.That(addRegistry.StatusCode, Is.EqualTo(StatusCodes.Good));

            NodeId envNodeId = await FindChildAsync(registryLabelsId, "environment")
                .ConfigureAwait(false);
            DataValue envValue = await ReadValueAsync(envNodeId).ConfigureAwait(false);
            Assert.That(envValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(envValue.GetValue<string>(null), Is.EqualTo("production"));

            // ---- group-level Labels ----------------------------------------
            NodeId createGroupId = await FindChildAsync(registryNodeId, "CreateGroup")
                .ConfigureAwait(false);
            CallMethodResult createGroup = await CallAsync(
                registryNodeId, createGroupId, new Variant("labelgroup")).ConfigureAwait(false);
            Assert.That(createGroup.StatusCode, Is.EqualTo(StatusCodes.Good));
            var groupNodeId = (NodeId)createGroup.OutputArguments[0]
                .AsBoxedObject(Variant.BoxingBehavior.Legacy);

            NodeId groupLabelsId = await FindChildAsync(groupNodeId, "Labels").ConfigureAwait(false);
            NodeId groupAddId = await FindChildAsync(groupLabelsId, "AddAttribute")
                .ConfigureAwait(false);

            CallMethodResult addGroupLabel = await CallAsync(
                groupLabelsId, groupAddId,
                new Variant("owner"), new Variant("team-iot"), new Variant(0u))
                .ConfigureAwait(false);
            Assert.That(addGroupLabel.StatusCode, Is.EqualTo(StatusCodes.Good));
            NodeId ownerNodeId = await FindChildAsync(groupLabelsId, "owner").ConfigureAwait(false);
            Assert.That(
                (await ReadValueAsync(ownerNodeId).ConfigureAwait(false)).GetValue<string>(null),
                Is.EqualTo("team-iot"));

            // Epoch mismatch is rejected with Bad_InvalidState and makes no change.
            NodeId groupEpochId = await FindChildAsync(groupNodeId, "Epoch").ConfigureAwait(false);
            uint groupEpoch = (await ReadValueAsync(groupEpochId).ConfigureAwait(false))
                .GetValue<uint>(0);
            CallMethodResult mismatchedGroup = await CallAsync(
                groupLabelsId, groupAddId,
                new Variant("owner"), new Variant("team-other"), new Variant(groupEpoch + 999))
                .ConfigureAwait(false);
            Assert.That(mismatchedGroup.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(
                (await ReadValueAsync(ownerNodeId).ConfigureAwait(false)).GetValue<string>(null),
                Is.EqualTo("team-iot"), "A rejected epoch mismatch must not change the label value.");

            // A key colliding with a fixed Labels container member is rejected.
            CallMethodResult collision = await CallAsync(
                groupLabelsId, groupAddId,
                new Variant("AddAttribute"), new Variant("x"), new Variant(0u))
                .ConfigureAwait(false);
            Assert.That(collision.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));

            // A key with a path-separator character is rejected.
            CallMethodResult invalidKey = await CallAsync(
                groupLabelsId, groupAddId,
                new Variant("a/b"), new Variant("x"), new Variant(0u)).ConfigureAwait(false);
            Assert.That(invalidKey.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

            // ---- resource-level Labels --------------------------------------
            NodeId getOrCreateResourceId = await FindChildAsync(groupNodeId, "GetOrCreateResource")
                .ConfigureAwait(false);
            CallMethodResult createResource = await CallAsync(
                groupNodeId, getOrCreateResourceId,
                new Variant("thing1"), new Variant(string.Empty), new Variant(false))
                .ConfigureAwait(false);
            Assert.That(createResource.StatusCode, Is.EqualTo(StatusCodes.Good));
            var resourceNodeId = (NodeId)createResource.OutputArguments[0]
                .AsBoxedObject(Variant.BoxingBehavior.Legacy);

            NodeId resourceLabelsId = await FindChildAsync(resourceNodeId, "Labels")
                .ConfigureAwait(false);
            NodeId resourceAddId = await FindChildAsync(resourceLabelsId, "AddAttribute")
                .ConfigureAwait(false);
            NodeId resourceRemoveId = await FindChildAsync(resourceLabelsId, "RemoveAttribute")
                .ConfigureAwait(false);

            CallMethodResult addResourceLabel = await CallAsync(
                resourceLabelsId, resourceAddId,
                new Variant("site"), new Variant("seattle"), new Variant(0u))
                .ConfigureAwait(false);
            Assert.That(addResourceLabel.StatusCode, Is.EqualTo(StatusCodes.Good));
            NodeId siteNodeId = await FindChildAsync(resourceLabelsId, "site").ConfigureAwait(false);
            Assert.That(
                (await ReadValueAsync(siteNodeId).ConfigureAwait(false)).GetValue<string>(null),
                Is.EqualTo("seattle"));

            // Remove the resource label; it must disappear from Browse.
            CallMethodResult removeResourceLabel = await CallAsync(
                resourceLabelsId, resourceRemoveId,
                new Variant("site"), new Variant(0u)).ConfigureAwait(false);
            Assert.That(removeResourceLabel.StatusCode, Is.EqualTo(StatusCodes.Good));
            BrowseResponse afterRemove = await BrowseAsync(resourceLabelsId).ConfigureAwait(false);
            Assert.That(
                afterRemove.Results[0].References.Contains(
                    r => string.Equals(r.BrowseName.Name, "site", StringComparison.Ordinal)),
                Is.False);

            // Removing an unknown label fails with a precise StatusCode.
            CallMethodResult removeUnknown = await CallAsync(
                resourceLabelsId, resourceRemoveId,
                new Variant("missing"), new Variant(0u)).ConfigureAwait(false);
            Assert.That(removeUnknown.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

            // ---- registry-level remove --------------------------------------
            CallMethodResult removeRegistry = await CallAsync(
                registryLabelsId, registryRemoveId,
                new Variant("environment"), new Variant(0u)).ConfigureAwait(false);
            Assert.That(removeRegistry.StatusCode, Is.EqualTo(StatusCodes.Good));
            BrowseResponse registryAfterRemove = await BrowseAsync(registryLabelsId)
                .ConfigureAwait(false);
            Assert.That(
                registryAfterRemove.Results[0].References.Contains(
                    r => string.Equals(r.BrowseName.Name, "environment", StringComparison.Ordinal)),
                Is.False);
        }

        private async Task<NodeId> FindChildAsync(NodeId parent, string browseName)
        {
            BrowseResponse browse = await BrowseAsync(parent).ConfigureAwait(false);
            foreach (ReferenceDescription reference in browse.Results[0].References)
            {
                if (string.Equals(reference.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(
                        reference.NodeId, m_server.CurrentInstance.NamespaceUris);
                }
            }
            Assert.Fail($"Child '{browseName}' was not found under {parent}.");
            return NodeId.Null;
        }

        private async Task<CallMethodResult> CallAsync(
            NodeId objectId, NodeId methodId, params Variant[] inputs)
        {
            ArrayOf<CallMethodRequest> methods =
            [
                new CallMethodRequest
                {
                    ObjectId = objectId,
                    MethodId = methodId,
                    InputArguments = inputs
                }
            ];
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse response = await m_server
                .CallAsync(m_secureChannelContext, requestHeader, methods, RequestLifetime.None)
                .ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<bool> WaitForConditionAsync(Func<Task<bool>> condition)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return false;
        }

        private async Task UpsertSensorAsync(string resourceId, int generation)
        {
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = SensorConverter.BuildContent(generation)
            }).ConfigureAwait(false);
        }

        private static string GenChildBrowseName(int generation)
            => "Gen" + generation.ToString(CultureInfo.InvariantCulture);

        // ---- deterministic converter -------------------------------------

        /// <summary>
        /// Emits a NodeSet2 whose model namespace is fixed and whose value node
        /// carries the generation number parsed from the document, plus a
        /// generation-specific child so a Browse can distinguish generations.
        /// </summary>
        private sealed class SensorConverter : IWotDocumentConverter
        {
            public static byte[] BuildContent(int generation)
                => Encoding.UTF8.GetBytes(
                    "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "\"@type\":\"uav:object\",\"id\":\"" + kModelNamespaceUri + "\"," +
                    "\"title\":\"sensor\",\"gen\":" +
                    generation.ToString(CultureInfo.InvariantCulture) + "}");

            public WotConversionOutput Convert(
                WotResource resource, ReadOnlyMemory<byte> content, WotRegistrySnapshot snapshot)
            {
                int generation = ParseGeneration(content.Span);
                uint childId = kGenChildBaseNodeId + (uint)generation;
                string childName = "Gen" + generation.ToString(CultureInfo.InvariantCulture);
                string xml = $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                               xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                      <NamespaceUris>
                        <Uri>{kModelNamespaceUri}</Uri>
                      </NamespaceUris>
                      <Models>
                        <Model ModelUri="{kModelNamespaceUri}" />
                      </Models>
                      <UAObject NodeId="ns=1;i={kRootNodeId}" BrowseName="1:Sensor">
                        <DisplayName>Sensor</DisplayName>
                        <References>
                          <Reference ReferenceType="i=40">i=58</Reference>
                          <Reference ReferenceType="i=35">ns=1;i={kValueNodeId}</Reference>
                          <Reference ReferenceType="i=35">ns=1;i={childId}</Reference>
                          <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                        </References>
                      </UAObject>
                      <UAVariable NodeId="ns=1;i={kValueNodeId}" BrowseName="1:{kValueBrowseName}"
                                  ParentNodeId="ns=1;i={kRootNodeId}" DataType="i=6" ValueRank="-1"
                                  AccessLevel="3" UserAccessLevel="3">
                        <DisplayName>{kValueBrowseName}</DisplayName>
                        <References>
                          <Reference ReferenceType="i=40">i=63</Reference>
                          <Reference ReferenceType="i=35" IsForward="false">ns=1;i={kRootNodeId}</Reference>
                        </References>
                        <Value><uax:Int32>{generation}</uax:Int32></Value>
                      </UAVariable>
                      <UAVariable NodeId="ns=1;i={childId}" BrowseName="1:{childName}"
                                  ParentNodeId="ns=1;i={kRootNodeId}" DataType="i=6" ValueRank="-1"
                                  AccessLevel="3" UserAccessLevel="3">
                        <DisplayName>{childName}</DisplayName>
                        <References>
                          <Reference ReferenceType="i=40">i=63</Reference>
                          <Reference ReferenceType="i=35" IsForward="false">ns=1;i={kRootNodeId}</Reference>
                        </References>
                        <Value><uax:Int32>{generation}</uax:Int32></Value>
                      </UAVariable>
                    </UANodeSet>
                    """;
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
                UANodeSet nodeSet = UANodeSet.Read(stream)!;
                return WotConversionOutput.Success(nodeSet);
            }

            private static int ParseGeneration(ReadOnlySpan<byte> content)
            {
                try
                {
                    var reader = new System.Text.Json.Utf8JsonReader(content);
                    while (reader.Read())
                    {
                        if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName &&
                            reader.GetString() == "gen" && reader.Read())
                        {
                            return reader.GetInt32();
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // fall through
                }
                return 1;
            }
        }

        // ---- client helpers (adapted from RuntimeNodeSetLifecycleTests) ----

        private async Task<DataValue> ReadValueAsync(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            ArrayOf<ReadValueId> readIds =
                [new ReadValueId { NodeId = nodeId, AttributeId = attributeId }];
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse response = await m_server.ReadAsync(
                m_secureChannelContext, requestHeader, kMaxAge,
                TimestampsToReturn.Neither, readIds, RequestLifetime.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private async Task<BrowseResponse> BrowseAsync(NodeId nodeId)
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId([nodeId], template);
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            return await services
                .BrowseAsync(requestHeader, view: null, requestedMaxReferencesPerNode: 0, nodesToBrowse)
                .ConfigureAwait(false);
        }

        private async Task<uint> CreateSubscriptionWithMonitoredItemAsync(
            ServerTestServices services, NodeId nodeId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscriptionResponse = await services
                .CreateSubscriptionAsync(requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            uint subscriptionId = subscriptionResponse.SubscriptionId;

            ArrayOf<MonitoredItemCreateRequest> monitoredItems =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1, SamplingInterval = 0, QueueSize = 1, DiscardOldest = true
                    }
                }
            ];
            requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse createItemsResponse = await services
                .CreateMonitoredItemsAsync(
                    requestHeader, subscriptionId, TimestampsToReturn.Both, monitoredItems)
                .ConfigureAwait(false);
            Assert.That(createItemsResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscriptionId;
        }

        private async Task DeleteSubscriptionAsync(ServerTestServices services, uint subscriptionId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ArrayOf<uint> subscriptionIds = [subscriptionId];
            DeleteSubscriptionsResponse response = await services
                .DeleteSubscriptionsAsync(requestHeader, subscriptionIds).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        private async Task<(DataValue? Value, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishForDataChangeAsync(
                ServerTestServices services, uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements, uint clientHandle)
        {
            const int MaxPublishAttempts = 20;
            DataValue? value = null;
            for (int attempt = 0; attempt < MaxPublishAttempts && value is null; attempt++)
            {
                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await services
                    .PublishAsync(requestHeader, acknowledgements, timeoutCts.Token).ConfigureAwait(false);
                acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(
                    sequenceNumber => new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId, SequenceNumber = sequenceNumber
                    });
                if (response.NotificationMessage is { } message)
                {
                    foreach (ExtensionObject notificationData in message.NotificationData)
                    {
                        if (notificationData.TryGetValue(out DataChangeNotification dcn))
                        {
                            foreach (MonitoredItemNotification item in dcn.MonitoredItems)
                            {
                                if (item.ClientHandle == clientHandle)
                                {
                                    value = item.Value;
                                }
                            }
                        }
                    }
                }
            }
            Assert.That(value, Is.Not.Null,
                $"No data-change notification for client handle {clientHandle} arrived.");
            return (value, acknowledgements);
        }

        private async Task<(bool Alive, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishKeepAliveAsync(
                ServerTestServices services, uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            PublishResponse response = await services
                .PublishAsync(requestHeader, acknowledgements, timeoutCts.Token).ConfigureAwait(false);
            Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId),
                "The subscription and its monitored item must stay alive across the shadow reload.");
            ArrayOf<SubscriptionAcknowledgement> acks = response.AvailableSequenceNumbers.ToArrayOf(
                sequenceNumber => new SubscriptionAcknowledgement
                {
                    SubscriptionId = subscriptionId, SequenceNumber = sequenceNumber
                });
            return (true, acks);
        }
    }
}
