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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchRevealsSelfRegisteredNamespaceOnlyOnCommitAsync(
            bool publish, bool registerDuringFactory)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IAsyncNodeManager? candidate = null;
            RuntimeNodeSetOptions options = CreateOptions(kModelNamespaceUri, kFirstRegistrationValue);
            Action<INodeManagerBuilder>? configure = options.Configure;
            options.Configure = builder =>
            {
                configure?.Invoke(builder);
                if (!registerDuringFactory)
                {
                    m_server.CurrentInstance.NodeManager.RegisterNamespaceManager(
                        kModelNamespaceUri, RequireLifecycleValue(candidate));
                }
            };
            var runtimeFactory = new RuntimeNodeSetNodeManagerFactory(options);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateCandidateAsync(server, configuration, token));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            NodeId valueId;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], null, timeout.Token).ConfigureAwait(false))
            {
                valueId = new NodeId(kValueNodeId,
                    (ushort)ChangePreparer.GetServer(RequireLifecycleValue(candidate))!
                        .NamespaceUris.GetIndex(kModelNamespaceUri));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
                ReadResponse hidden = await ReadAsync(valueId).ConfigureAwait(false);
                Assert.That(hidden.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            ReadResponse final = await ReadAsync(valueId).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(final.Results[0].StatusCode,
                    Is.EqualTo(publish ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(publish ? 1 : 0));
                if (publish)
                {
                    Assert.That(final.Results[0].WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
                }
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateCandidateAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                candidate = await runtimeFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
                if (registerDuringFactory)
                {
                    server.NodeManager.RegisterNamespaceManager(kModelNamespaceUri, candidate);
                }
                return candidate;
            }

            async Task<ReadResponse> ReadAsync(NodeId id)
            {
                return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = id, AttributeId = Attributes.Value }], timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchPublishesReferencesToSurvivingOwnersAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null, timeout.Token)
                .ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri);
            NodeId root = new(kRootNodeId, ns);
            NodeId ordinaryRoot = new(kRootNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            Assert.That(await ObjectsContainsRootAsync(ordinaryRoot).ConfigureAwait(false), Is.True,
                "The same native Browse predicate must find an ordinarily published root.");
            bool whilePrepared;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null, timeout.Token)
                .ConfigureAwait(false))
            {
                whilePrepared = await ObjectsContainsRootAsync(root).ConfigureAwait(false);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            bool afterDecision = await ObjectsContainsRootAsync(root).ConfigureAwait(false);
            ReadResponse direct = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = root, AttributeId = Attributes.BrowseName }], timeout.Token)
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(direct.Results[0].StatusCode,
                    Is.EqualTo(publish ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown));
                if (publish)
                {
                    Assert.That(direct.Results[0].WrappedValue,
                        Is.EqualTo(new Variant(new QualifiedName(kRootBrowseName, ns))),
                        "The committed candidate is routable even if its surviving owner's reference is missing.");
                }
                Assert.That(whilePrepared, Is.False, "Preparation must not mutate a serving owner's references.");
                Assert.That(afterDecision, Is.EqualTo(publish),
                    "The surviving Objects owner must expose the newly committed manager's root.");
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async Task<bool> ObjectsContainsRootAsync(NodeId target)
            {
                BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], timeout.Token).ConfigureAwait(false);
                BrowseResult page = response.Results[0];
                bool found = false;
                while (true)
                {
                    Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                    for (int index = 0; index < page.References.Count; index++)
                    {
                        found |= page.References[index].NodeId == new ExpandedNodeId(target);
                    }
                    if (page.ContinuationPoint.Length == 0)
                    {
                        return found;
                    }
                    BrowseNextResponse next = await session.BrowseNextAsync(
                        null, false, [page.ContinuationPoint], timeout.Token).ConfigureAwait(false);
                    page = next.Results[0];
                }
            }
        }

        /// <summary>
        /// Verifies that concurrent namespace additions invalidate preparation before the durable decision.
        /// </summary>
        [Test]
        public async Task PreparedBatchRejectsChangedNamespacePrefixBeforeDecisionAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null).ConfigureAwait(false);
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            Assert.That(namespaces.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            ushort concurrent = namespaces.GetIndexOrAppend("urn:opcfoundation.org:Tests:ConcurrentNamespace");
            int decisions = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(namespaces.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            Assert.That(namespaces.GetString(concurrent),
                Is.EqualTo("urn:opcfoundation.org:Tests:ConcurrentNamespace"));
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that cached manager contexts and builders follow canonical additive metadata across later commits.
        /// </summary>
        [Test]
        public async Task PublishedManagerCachesObserveLaterMetadataAndDetachBeforeRetirementAsync()
        {
            TrackingLifecycleNodeManager? firstManager = null;
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => firstManager = manager), null)
                .ConfigureAwait(false);
            TrackingLifecycleNodeManager firstOwner = RequireLifecycleValue(firstManager);
            var cachedTypes = (TypeTable)firstOwner.SystemContext.TypeTable;
            NamespaceTable cachedNamespaces = firstOwner.SystemContext.NamespaceUris;
            IEncodeableFactory cachedFactory = firstOwner.SystemContext.EncodeableFactory;
            IEncodeableFactoryBuilder cachedBuilder = cachedFactory.Builder;
            ushort ns = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            NodeId added = new(8410, ns);
            ExpandedNodeId alias = new(8411, kSecondModelNamespaceUri);
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? activator), Is.True);
            IEncodeableType structure = RequireLifecycleValue(activator);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(kSecondRegistrationValue, manager =>
                {
                    manager.Server.TypeTree.AddSubtype(added, Ua.ObjectTypeIds.BaseObjectType);
                    manager.Server.Factory.Builder.AddEncodeableType(alias, structure).Commit();
                }, kSecondModelNamespaceUri))], null).ConfigureAwait(false);
            Assert.That(cachedTypes.IsKnown(added), Is.False);
            Assert.That(cachedFactory.TryGetEncodeableType(alias, out _), Is.False);
            await prepared.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(cachedTypes.FindSuperType(added), Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
            Assert.That(cachedNamespaces.GetString(ns), Is.EqualTo(kSecondModelNamespaceUri));
            Assert.That(cachedFactory.TryGetEncodeableType(alias, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(structure));
            ExpandedNodeId cachedAlias = new(8412, kSecondModelNamespaceUri);
            cachedBuilder.AddEncodeableType(cachedAlias, structure).Commit();
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(cachedAlias, out _), Is.True);
            await m_server.NodeManagerLifecycle.RemoveAsync(first, null).ConfigureAwait(false);
            cachedTypes.Remove(added);
            cachedNamespaces.Append("urn:opcfoundation.org:Tests:DetachedCleanup");
            Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(added), Is.True);
            Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(
                "urn:opcfoundation.org:Tests:DetachedCleanup"), Is.EqualTo(-1));
            Assert.That(m_server.CurrentInstance.Factory.TryGetEncodeableType(alias, out _), Is.True);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedBatchRejectsChangedTypeInputsBeforeDecisionAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null).ConfigureAwait(false);
            NodeId unrelated = new(8301,
                m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(kModelNamespaceUri));
            m_server.CurrentInstance.TypeTree.AddSubtype(unrelated, Ua.ObjectTypeIds.BaseObjectType);
            int decisions = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations(lifecycle).IsEmpty, Is.True);
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(unrelated),
                Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
        }

        [Test]
        public async Task PreparedBatchDoesNotLoseTypeWritesDuringDecisionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], null, timeout.Token)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId unrelated = new(8302, 0);
            Task<NodeManagerBatchResult> commit = prepared.CommitAsync(async token =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).AsTask();
            bool accepted = false;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    m_server.CurrentInstance.TypeTree.AddSubtype(unrelated, Ua.ObjectTypeIds.BaseObjectType);
                    accepted = true;
                }
                catch (InvalidOperationException)
                {
                    Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(unrelated), Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            NodeManagerBatchResult result = await commit.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(unrelated),
                Is.EqualTo(accepted ? Ua.ObjectTypeIds.BaseObjectType : NodeId.Null),
                "A concurrent type mutation must either be retained or fail before it changes the serving image.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchTypesRemainPrivateUntilPublicationAsync(bool replace, bool publish)
        {
            const string modelUri = kModelNamespaceUri + ":BatchTypes";
            const uint typeIdentifier = 8100;
            ushort namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(modelUri);
            NodeId typeId = new(typeIdentifier, namespaceIndex);
            NodeId addedTypeId = new(typeIdentifier + 1, namespaceIndex);
            NodeManagerRegistration? previous = null;
            if (replace)
            {
                previous = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                    Options(Ua.ObjectTypeIds.BaseObjectType), null).ConfigureAwait(false);
            }
            NodeId originalParent = replace ? Ua.ObjectTypeIds.BaseObjectType : NodeId.Null;
            Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId), Is.EqualTo(originalParent));
            NodeId proposedParent = replace ? originalParent : Ua.ObjectTypeIds.FolderType;
            var factory = new RuntimeNodeSetNodeManagerFactory(Options(proposedParent, includeChild: true));
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            NodeId whilePrepared;
            await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [replace
                    ? NodeManagerBatchChange.Replace(RequireLifecycleValue(previous), factory)
                    : NodeManagerBatchChange.Add(factory)], null)
                .ConfigureAwait(false))
            {
                whilePrepared = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
                Assert.That(m_server.CurrentInstance.TypeTree.IsKnown(addedTypeId), Is.False);
                if (publish)
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(whilePrepared, Is.EqualTo(originalParent),
                    "Private preparation must not add or replace a serving type relationship.");
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                    Is.EqualTo(publish ? proposedParent : originalParent),
                    "Publication preserves serving definitions and adds compatible declarations.");
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(addedTypeId),
                    Is.EqualTo(publish ? typeId : NodeId.Null));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(replace || publish ? 1 : 0));
                if (replace && !publish)
                {
                    Assert.That(GetBranchRegistration(lifecycle, RequireLifecycleValue(previous).Id),
                        Is.SameAs(previous));
                }
            }

            RuntimeNodeSetOptions Options(NodeId parent, bool includeChild = false)
            {
                string child = includeChild ? $$"""
                      <UAObjectType NodeId="ns=1;i={{typeIdentifier + 1}}" BrowseName="1:BatchChildType">
                        <DisplayName>BatchChildType</DisplayName>
                        <References>
                          <Reference ReferenceType="i=45" IsForward="false">ns=1;i={{typeIdentifier}}</Reference>
                        </References>
                      </UAObjectType>
                    """ : string.Empty;
                string xml = $$"""
                    <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                      <NamespaceUris><Uri>{{modelUri}}</Uri></NamespaceUris>
                      <Models><Model ModelUri="{{modelUri}}" Version="1.0.0" /></Models>
                      <UAObjectType NodeId="ns=1;i={{typeIdentifier}}" BrowseName="1:BatchType">
                        <DisplayName>BatchType</DisplayName>
                        <References>
                          <Reference ReferenceType="i=45" IsForward="false">{{parent}}</Reference>
                        </References>
                      </UAObjectType>
                      {{child}}
                    </UANodeSet>
                    """;
                return new RuntimeNodeSetOptions
                {
                    Sources =
                    [
                        RuntimeNodeSetSource.FromStream("batch-types",
                            _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))), [modelUri])
                    ]
                };
            }
        }

        /// <summary>
        /// Verifies that incompatible metadata aborts before the durable decision and preserves the live owner.
        /// </summary>
        /// <param name="changeFactory">Whether to change an activator rather than a type relationship.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchRejectsIncompatibleMetadataBeforeDecisionAsync(bool changeFactory)
        {
            NodeManagerRegistration previous = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, _ => { }), null).ConfigureAwait(false);
            ushort namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            NodeId typeId = new(8400, namespaceIndex);
            m_server.CurrentInstance.TypeTree.AddSubtype(typeId, Ua.ObjectTypeIds.BaseObjectType);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? serving), Is.True);
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.EUInformation, out IEncodeableType? proposed),
                Is.True);
            AssertLifecycleValue(proposed);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(previous,
                        CreateTrackingNodeManagementFactory(303, manager =>
                        {
                            if (changeFactory)
                            {
                                manager.Server.Factory.Builder.AddEncodeableType(DataTypeIds.Range, proposed).Commit();
                            }
                            else
                            {
                                manager.Server.TypeTree.AddSubtype(typeId, Ua.ObjectTypeIds.FolderType);
                            }
                        }))
                ], null).ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(GetBranchRegistration(lifecycle, previous.Id), Is.SameAs(previous));
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                    Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
                Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? retained), Is.True);
                Assert.That(retained, Is.SameAs(serving));
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchNativeReadRetainsOneGenerationAcrossDecisionAsync(bool rejectDecision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            TrackingLifecycleNodeManager? firstManager = null;
            TrackingLifecycleNodeManager? secondManager = null;
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kFirstRegistrationValue, manager => firstManager = manager),
                null, timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration second = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kSecondRegistrationValue, manager => secondManager = manager,
                    kSecondModelNamespaceUri), null, timeout.Token).ConfigureAwait(false);
            AssertLifecycleValue(firstManager);
            AssertLifecycleValue(secondManager);
            NodeId typeId = new(8303,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            NodeId addedTypeId = new(8304, typeId.NamespaceIndex);
            m_server.CurrentInstance.TypeTree.AddSubtype(typeId, Ua.ObjectTypeIds.BaseObjectType);
            IEncodeableFactory factory = m_server.CurrentInstance.Factory;
            Assert.That(factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? structure), Is.True);
            AssertLifecycleValue(structure);
            ExpandedNodeId alias = new(8307, "urn:opcfoundation.org:Tests:RetainedFactory");
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first,
                        CreateTrackingNodeManagementFactory(303, manager =>
                        {
                            manager.Server.TypeTree.AddSubtype(addedTypeId, typeId);
                            manager.Server.Factory.Builder.AddEncodeableType(alias, structure).Commit();
                        })),
                    NodeManagerBatchChange.Replace(second,
                        CreateTrackingNodeManagementFactory(404, _ => { }, kSecondModelNamespaceUri))
                ], null, timeout.Token).ConfigureAwait(false);
            await using var client = new ClientFixture(false, true, NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId firstId = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            NodeId secondId = new(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri));
            NodeId beforePublication = NodeId.Null;
            NodeId afterPublication = NodeId.Null;
            bool factoryBeforePublication = false;
            firstManager.ReadCallbackNodeId = firstId;
            firstManager.ReadCallback = async (context, token) =>
            {
                beforePublication = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
                factoryBeforePublication = factory.TryGetEncodeableType(alias, out _);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                afterPublication = m_server.CurrentInstance.TypeTree.FindSuperType(typeId);
            };
            Task<ReadResponse> pendingRead = ReadPairAsync();
            Task<NodeManagerBatchResult>? pendingCommit = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                pendingCommit = prepared.CommitAsync(
                    _ =>
                    {
                        decided.TrySetResult(true);
                        if (rejectDecision)
                        {
                            throw new IOException("Confirmed noncommit while a native read owns the old image.");
                        }
                        return default;
                    },
                    () => published.TrySetResult(true),
                    timeout.Token).AsTask();
                await decided.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!rejectDecision)
                {
                    await published.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(pendingRead.IsCompleted, Is.False);
                    Assert.That(firstManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(secondManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(firstManager.DisposeCount, Is.Zero);
                    Assert.That(secondManager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            ReadResponse oldRead = await pendingRead.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(pendingCommit, Is.Not.Null);
            AssertLifecycleValue(pendingCommit);
            if (rejectDecision)
            {
                await Assert.ThatAsync(() => pendingCommit, Throws.TypeOf<IOException>()).ConfigureAwait(false);
            }
            else
            {
                NodeManagerBatchResult committed = await pendingCommit.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(committed.CleanupFailure, Is.Null);
                Assert.That(committed.Retired, Is.EqualTo(2u));
            }
            ReadResponse current = await ReadPairAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(oldRead.Results.Count, Is.EqualTo(2));
                Assert.That(oldRead.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(oldRead.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(oldRead.Results[0].WrappedValue, Is.EqualTo(new Variant(kFirstRegistrationValue)));
                Assert.That(oldRead.Results[1].WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
                Assert.That(current.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Results[0].WrappedValue,
                    Is.EqualTo(new Variant(rejectDecision ? kFirstRegistrationValue : 303)));
                Assert.That(current.Results[1].WrappedValue,
                    Is.EqualTo(new Variant(rejectDecision ? kSecondRegistrationValue : 404)));
                Assert.That(prepared.IsCommitted, Is.EqualTo(!rejectDecision));
                Assert.That(firstManager.DisposeCount, Is.EqualTo(rejectDecision ? 0 : 1));
                Assert.That(secondManager.DisposeCount, Is.EqualTo(rejectDecision ? 0 : 1));
                Assert.That(beforePublication, Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
                Assert.That(afterPublication, Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType),
                    "An additive publication cannot change a serving type's definition.");
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(typeId),
                    Is.EqualTo(Ua.ObjectTypeIds.BaseObjectType));
                Assert.That(m_server.CurrentInstance.TypeTree.FindSuperType(addedTypeId),
                    Is.EqualTo(rejectDecision ? NodeId.Null : typeId));
                Assert.That(factoryBeforePublication, Is.False);
                Assert.That(factory.TryGetEncodeableType(alias, out _), Is.EqualTo(!rejectDecision));
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);

            async Task<ReadResponse> ReadPairAsync()
            {
                return await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = firstId, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = secondId, AttributeId = Attributes.Value }
                    ], timeout.Token).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchOwnsItsChangesAcrossAwaitAsync(bool mutateCallerArray)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            NodeManagerRegistration unrelated = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kReadinessProbeNamespaceUri, 707), null, timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var delayedFactory = new Mock<IAsyncNodeManagerFactory>();
            delayedFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateDelayedAsync(server, configuration, token));
            NodeManagerBatchChange[] changes =
            [
                NodeManagerBatchChange.Add(delayedFactory.Object),
                NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
            ];
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(changes, null, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (mutateCallerArray)
                {
                    changes[1] = NodeManagerBatchChange.Remove(unrelated);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            await using IPreparedNodeManagerBatch prepared = await pending.WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            DataValue second = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)))
                .ConfigureAwait(false);
            DataValue original = await ReadValueAsync(new NodeId(kValueNodeId,
                (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kReadinessProbeNamespaceUri)))
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Registrations.Count, Is.EqualTo(2));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(3));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(unrelated));
                Assert.That(result.Retired, Is.Zero);
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(second.WrappedValue, Is.EqualTo(new Variant(kSecondRegistrationValue)));
                Assert.That(original.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(original.WrappedValue, Is.EqualTo(new Variant(707)));
            }

            async ValueTask<IAsyncNodeManager> CreateDelayedAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PreparedBatchPostdecisionFailuresStillReconcileAsync(
            bool failPublication, bool failReadiness)
        {
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager), null)
                .ConfigureAwait(false);
            var publicationFailure = new IOException("Post-decision publication callback failed.");
            var readinessFailure = new InvalidOperationException("Committed readiness failed.");
            ReadinessLifecycleNodeManager? replacement = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken _) =>
                {
                    replacement = new ReadinessLifecycleNodeManager(server, configuration, m_logger, kGeneration2Value)
                    {
                        ReadinessCallback = token =>
                        {
                            Assert.That(token.CanBeCanceled, Is.False);
                            if (failReadiness)
                            {
                                throw readinessFailure;
                            }
                            return default;
                        }
                    };
                    return new ValueTask<IAsyncNodeManager>(replacement);
                });
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(original, factory.Object)], null).ConfigureAwait(false);
            AssertLifecycleValue(originalManager);
            AssertLifecycleValue(replacement);
            int decisions = 0;
            int publications = 0;
            NodeManagerBatchResult? result = null;
            IOException? escaped = null;
            try
            {
                result = await prepared.CommitAsync(
                    _ =>
                    {
                        decisions++;
                        return default;
                    },
                    () =>
                    {
                        publications++;
                        Assert.That(GetBranchRegistration(lifecycle, original.Id),
                            Is.SameAs(prepared.Registrations[0]));
                        if (failPublication)
                        {
                            throw publicationFailure;
                        }
                    }).ConfigureAwait(false);
            }
            catch (IOException failure)
            {
                escaped = failure;
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(escaped, Is.Null, "A post-decision callback failure must remain a committed outcome.");
                Assert.That(result, Is.Not.Null);
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(decisions, Is.EqualTo(1));
                Assert.That(publications, Is.EqualTo(1));
                Assert.That(GetBranchRegistrations(lifecycle).Count, Is.EqualTo(1));
                Assert.That(GetBranchRegistration(lifecycle, original.Id).Generation,
                    Is.EqualTo(original.Generation + 1));
                Assert.That(replacement.ReadinessCount, Is.EqualTo(1));
                Assert.That(replacement.ReadinessCompletedCount, Is.EqualTo(failReadiness ? 0 : 1));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                if (result is not null)
                {
                    Assert.That(result.Retired, Is.EqualTo(1u));
                    if (failPublication || failReadiness)
                    {
                        Assert.That(result.CleanupFailure, Is.TypeOf<AggregateException>());
                        var failures = RequireLifecycleValue(result.CleanupFailure as AggregateException)
                            .Flatten().InnerExceptions;
                        Assert.That(failures, Has.Count.EqualTo((failPublication ? 1 : 0) + (failReadiness ? 1 : 0)));
                        if (failPublication)
                        {
                            Assert.That(failures, Does.Contain(publicationFailure));
                        }
                        if (failReadiness)
                        {
                            Assert.That(failures, Does.Contain(readinessFailure));
                        }
                    }
                    else
                    {
                        Assert.That(result.CleanupFailure, Is.Null);
                    }
                }
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [Category("NativeTcp")]
        public async Task PreparedBatchRejectsChangedRoutingInputsOverNativeTcp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(false, true, telemetry);
            await client.LoadClientConfigurationAsync(m_pkiRoot);
            Assert.That(client.SessionFactory, Is.TypeOf<DefaultSessionFactory>());
            using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None);
            try
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                            CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                    ], null);
                NodeManagerRegistration intervening = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
                int decisions = 0;

                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await prepared.CommitAsync(_ =>
                    {
                        decisions++;
                        return default;
                    });
                }).ConfigureAwait(false);

                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
                ArrayOf<ReadValueId> nodes =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = new NodeId(kValueNodeId,
                            (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)),
                        AttributeId = Attributes.Value
                    }
                ];
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodes, CancellationToken.None);
                Assert.That(response.Results.Count, Is.EqualTo(2));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(202));
            }
            finally
            {
                await session.CloseAsync();
            }
        }

        [Test]
        public async Task PreparedBatchRejectsChangedRoutingInputsBeforeDecision()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ], null);
            NodeManagerRegistration intervening = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
            int decisions = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                });
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(intervening));
            DataValue unpublished = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue retained = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(unpublished.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(retained.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retained.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(202));
        }

        [Test]
        public async Task PreparedBatchDecisionFailureLeavesBothExistingGenerationsActive()
        {
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null);
            NodeManagerRegistration second = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303))),
                    NodeManagerBatchChange.Replace(second, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, 404)))
                ], null);

            await Assert.ThrowsAsync<IOException>(async () =>
            {
                await prepared.CommitAsync(_ => throw new IOException("Confirmed decision noncommit."));
            }).ConfigureAwait(false);

            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(first));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(second));
            DataValue firstValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue secondValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.WrappedValue.TryGetValue(out int firstNumber), Is.True);
            Assert.That(secondValue.WrappedValue.TryGetValue(out int secondNumber), Is.True);
            Assert.That(firstNumber, Is.EqualTo(101));
            Assert.That(secondNumber, Is.EqualTo(202));

            await prepared.DisposeAsync().ConfigureAwait(false);
            await using IPreparedNodeManagerBatch followup = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 606)))
                ], null);
            await followup.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(followup.IsCommitted, Is.True);
            DataValue followupValue = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(followupValue.WrappedValue.TryGetValue(out int followupNumber), Is.True);
            Assert.That(followupNumber, Is.EqualTo(606));
        }

        [Test]
        public async Task PreparedBatchDecisionCallbackRejectsNestedLifecycleOperationsAsync()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ], null);
            await using IPreparedNodeManagerBatch nestedCommit = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ], null);

            await prepared.CommitAsync(async _ =>
            {
                await Task.Run(async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await lifecycle.PrepareAsync(
                            [
                                NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                                    CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                            ], null, timeout.Token).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await nestedCommit.CommitAsync(_ => default).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await nestedCommit.DisposeAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }, _).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(1));
            await nestedCommit.DisposeAsync().ConfigureAwait(false);

            await using IPreparedNodeManagerBatch followup = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ], null);
            await followup.CommitAsync(_ => default).ConfigureAwait(false);
            Assert.That(followup.IsCommitted, Is.True);
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(2));
        }

        [Test]
        public async Task PreparedBatchRejectsStaleRegistrationBeforeDecision()
        {
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null);
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Replace(first, new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, 303)))
                ], null);
            NodeManagerRegistration winner = await m_server.NodeManagerLifecycle.ShadowReloadRuntimeNodeSetAsync(
                first, CreateOptions(kModelNamespaceUri, 505));
            int decisions = 0;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                });
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(winner));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(505));
        }

        [Test]
        public async Task PreparedBatchPredecisionCancellationLeavesNoPublishedOwner()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ], null);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int decisions = 0;

            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, cancellation.Token);
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(GetBranchRegistrations(), Is.Empty);
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task PreparedBatchPostdecisionCancellationKeepsCommittedOwner()
        {
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                ], null);
            using var cancellation = new CancellationTokenSource();
            NodeManagerBatchResult result = await prepared.CommitAsync(_ =>
            {
                cancellation.Cancel();
                return default;
            }, cancellation.Token);

            Assert.That(prepared.IsCommitted, Is.True);
            Assert.That(result.Registrations.Count, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(result.Registrations[0]));
            DataValue value = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(101));
        }

        [Test]
        public async Task PreparedBatchRemainsPrivateUntilDecision()
        {
            Assert.That(m_server.NodeManagerLifecycle, Is.InstanceOf<INodeManagerBatchLifecycle>());
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))),
                    NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))
                ], null);
            Assert.That(GetBranchRegistrations(), Is.Empty);
            int decisions = 0;
            NodeManagerBatchResult result = await prepared.CommitAsync(async _ =>
            {
                decisions++;
                DataValue first = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
                DataValue second = await ReadValueAsync(new NodeId(
                    kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(GetBranchRegistrations(), Is.Empty);
            });
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(result.Registrations.Count, Is.EqualTo(2));
            Assert.That(GetBranchRegistrations().Count, Is.EqualTo(2));
            DataValue publishedFirst = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri)));
            DataValue publishedSecond = await ReadValueAsync(new NodeId(
                kValueNodeId, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri)));
            Assert.That(publishedFirst.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedSecond.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(publishedFirst.WrappedValue.TryGetValue(out int firstValue), Is.True);
            Assert.That(publishedSecond.WrappedValue.TryGetValue(out int secondValue), Is.True);
            Assert.That(firstValue, Is.EqualTo(kFirstRegistrationValue));
            Assert.That(secondValue, Is.EqualTo(kSecondRegistrationValue));
        }
    }
}
