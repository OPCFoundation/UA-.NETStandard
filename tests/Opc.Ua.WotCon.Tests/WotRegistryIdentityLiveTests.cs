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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.WotCon.Tests.Registry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture(false)]
    [TestFixture(true)]
    [NonParallelizable]
    [Category("WotCon")]
    [Category("Integration")]
    public sealed partial class WotRegistryIdentityLiveTests
    {
        public WotRegistryIdentityLiveTests(bool useDependencyInjection)
        {
            m_useDependencyInjection = useDependencyInjection;
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            TestContext.Out.WriteLine($"Runtime={Environment.Version}; framework={AppContext.TargetFrameworkName}");
            m_telemetry = NUnitTelemetryContext.Create();
            m_root = Path.Combine(Path.GetTempPath(), "wot-native-identities-" + Guid.NewGuid().ToString("N"));
            m_serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_serverFixture.StartAsync(m_root).ConfigureAwait(false);
            m_store = new NativeStore();
            WotRegistryServerOptions options;
            if (m_useDependencyInjection)
            {
                var services = new ServiceCollection();
                services.AddSingleton(m_telemetry);
                services.AddOpcUa().AddWotRegistryServer(Configure).AddWotRegistryClient();
                services.AddSingleton<IWotRegistryStore>(m_store);
                services.AddSingleton<IWotRegistryService>(provider =>
                {
                    WotRegistryServerOptions configured = provider.GetRequiredService<WotRegistryServerOptions>();
                    return new WotRegistryService(
                        provider.GetRequiredService<IWotRegistryStore>(),
                        configured.Bounds,
                        configured.IdentityBindings);
                });
                m_services = services.BuildServiceProvider();
                options = m_services.GetRequiredService<WotRegistryServerOptions>();
                m_registry = (WotRegistryService)m_services.GetRequiredService<IWotRegistryService>();
                Assert.That(m_services.GetRequiredService<IWotTypedRegistryService>(), Is.SameAs(m_registry));
            }
            else
            {
                options = new WotRegistryServerOptions();
                Configure(options);
                m_registry = new WotRegistryService(m_store, options.Bounds, options.IdentityBindings);
            }
            m_options = options;
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: new FakeWotDocumentConverter());
            var factory = new WotRegistryNodeManagerFactory(options, m_registry, m_coordinator);
            Ua.Server.NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(factory, null)
                .ConfigureAwait(false);
            m_registration = registration;
            m_manager = (WotRegistryNodeManager)registration.NodeManager;
            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_root).ConfigureAwait(false);
            m_bootstrapSession = await m_clientFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
            if (m_useDependencyInjection)
            {
                m_managed = await ManagedSession.CreateAsync(
                    m_clientFixture.Config, m_clientFixture.Endpoint, m_clientFixture.SessionFactory,
                    telemetry: m_telemetry).ConfigureAwait(false);
                m_client = await m_services!
                    .GetRequiredService<Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>()(
                        m_managed, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                m_client = await WotRegistryClient.ForServerAsync(m_bootstrapSession, m_telemetry)
                    .ConfigureAwait(false);
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_store?.ReleaseCommit();
            if (m_managed is not null)
            {
                await m_managed.DisposeAsync().ConfigureAwait(false);
            }
            if (m_bootstrapSession is not null)
            {
                await m_bootstrapSession.CloseAsync().ConfigureAwait(false);
                m_bootstrapSession.Dispose();
            }
            if (m_serverFixture is not null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();
            m_coordinator?.Dispose();
            if (m_typedServices is not null)
            {
                await m_typedServices.DisposeAsync().ConfigureAwait(false);
                m_typedServices = null;
            }
            if (m_services is not null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                m_registry?.Dispose();
            }
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task GeneratedTypedMethodsPublishCompleteAuthorityBeforeFileContent(WoTDocumentKindEnum kind)
        {
            const string catalogue = "https://Contoso.org/Plant%2FOne/?q=A#F";
            const string source = "https://Contoso.org/Thing%2fA/?q=One#ID";
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(kind, catalogue)
                .ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = kind == WoTDocumentKindEnum.ThingModel
                ? await group.CreateThingModelResourceAsync(source, "First").ConfigureAwait(false)
                : await group.CreateThingDescriptionResourceAsync(source, "First").ConfigureAwait(false);
            NodeId logical = allocation.LogicalResource.ResourceNodeId;
            NodeId version = allocation.Version.ResourceNodeId;
            string identityName = kind == WoTDocumentKindEnum.ThingModel ? "ModelId" : "ThingId";
            WotResource stored = m_registry.Current.FindResource(group.GroupId, allocation.Version.ResourceId)!;
            ResourceState logicalNode = m_manager.FindPredefinedNode<ResourceState>(logical)!;
            var expectedVersionsType = ExpandedNodeId.ToNodeId(
                kind == WoTDocumentKindEnum.ThingModel
                    ? ObjectTypeIds.ThingModelVersionsType : ObjectTypeIds.ThingDescriptionVersionsType,
                m_client.Session.NamespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(logical.IsNull || version.IsNull, Is.False);
                Assert.That(logical, Is.Not.EqualTo(version));
                Assert.That(allocation.FileHandle, Is.Zero);
                Assert.That(stored.SourceId, Is.EqualTo(source));
                Assert.That(stored.DefaultVersionId, Is.EqualTo("First"));
                Assert.That(stored.Versions[0].HasContent, Is.False);
                Assert.That(m_store.Blobs.Reads, Is.Zero);
                Assert.That(m_store.Blobs.Writes, Is.Zero);
                Assert.That(logicalNode.Versions!.TypeDefinitionId, Is.EqualTo(expectedVersionsType));
                Assert.That(logicalNode.Versions.BrowseName.NamespaceIndex,
                    Is.EqualTo(m_client.Session.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri)));
            });
            Assert.That(await ReadStringAsync(group.GroupNodeId, "CatalogUri", Namespaces.WotCon)
                .ConfigureAwait(false), Is.EqualTo(catalogue));
            Assert.That(await ReadStringAsync(group.GroupNodeId, "Name", XRegistryWellKnown.XRegistryNamespaceUri)
                .ConfigureAwait(false),
                Is.EqualTo(catalogue));
            Assert.That(await ReadStringAsync(logical, identityName, Namespaces.WotCon)
                .ConfigureAwait(false), Is.EqualTo(source));
            Assert.That(await ReadStringAsync(version, identityName, Namespaces.WotCon)
                .ConfigureAwait(false), Is.EqualTo(source));
            Assert.That(await ReadStringAsync(version, "Name", XRegistryWellKnown.XRegistryNamespaceUri)
                .ConfigureAwait(false),
                Is.EqualTo(source));
            if (kind == WoTDocumentKindEnum.ThingModel)
            {
                Assert.That(await new ThingModelFileTypeClient(m_client.Session, logical, m_telemetry)
                    .GetVersionsAsync(m_telemetry)
                    .ConfigureAwait(false), Is.Not.Null);
            }
            else
            {
                Assert.That(await new ThingDescriptionFileTypeClient(m_client.Session, logical, m_telemetry)
                    .GetVersionsAsync(m_telemetry)
                    .ConfigureAwait(false), Is.Not.Null);
            }
        }

        [Test]
        public async Task NativeGetOrCreatePreservesFlagsAndDefaultWithoutOpeningFiles()
        {
            (WotRegistryGroupClient group, bool createdGroup) = await m_client.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false);
            (_, bool repeatedGroup) = await m_client.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false);
            (WotRegistryResourceAllocation first, bool firstResource, bool firstVersion) =
                await group.GetOrCreateThingDescriptionResourceAsync("urn:thing", "v1").ConfigureAwait(false);
            (_, bool repeatedResource, bool repeatedVersion) =
                await group.GetOrCreateThingDescriptionResourceAsync("urn:thing", "v1").ConfigureAwait(false);
            (WotRegistryResourceAllocation next, bool newResource, bool newVersion) =
                await group.GetOrCreateThingDescriptionResourceAsync("urn:thing", "v2").ConfigureAwait(false);
            (WotRegistryResourceAllocation selected, _, _) =
                await group.GetOrCreateThingDescriptionResourceAsync("urn:thing").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(createdGroup, Is.True);
                Assert.That(repeatedGroup, Is.False);
                Assert.That(firstResource && firstVersion, Is.True);
                Assert.That(repeatedResource || repeatedVersion, Is.False);
                Assert.That(newResource, Is.False);
                Assert.That(newVersion, Is.True);
                Assert.That(next.LogicalResource.ResourceNodeId, Is.EqualTo(first.LogicalResource.ResourceNodeId));
                Assert.That(next.Version.ResourceNodeId, Is.Not.EqualTo(first.Version.ResourceNodeId));
                Assert.That(selected.Version.ResourceNodeId, Is.EqualTo(first.Version.ResourceNodeId));
                Assert.That(first.FileHandle | next.FileHandle | selected.FileHandle, Is.Zero);
                Assert.That(m_store.Blobs.Writes + m_store.Blobs.Reads, Is.Zero);
            });
        }

        [TestCase(WoTDocumentKindEnum.ThingDescription)]
        [TestCase(WoTDocumentKindEnum.ThingModel)]
        public async Task ReturnedHandleIsSessionOwnedAndCleanReopenPreservesCommittedBytes(WoTDocumentKindEnum kind)
        {
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(kind, "urn:catalogue")
                .ConfigureAwait(false);
            WotRegistryResourceAllocation allocation = await group.CreateDocumentResourceAsync(
                "urn:thing", "v1", requestFileOpen: true).ConfigureAwait(false);
            Assert.That(allocation.FileHandle, Is.Not.Zero);
            Assert.That(await allocation.Version.Proxy.GetPositionAsync(allocation.FileHandle)
                .ConfigureAwait(false), Is.Zero);
            Assert.That(m_registry.Current.FindResource(group.GroupId, allocation.Version.ResourceId)!.SourceId,
                Is.EqualTo("urn:thing"));
            using var otherFixture = new ClientFixture(false, false, m_telemetry);
            await otherFixture.LoadClientConfigurationAsync(m_root).ConfigureAwait(false);
            using ISession other = await otherFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                    .ConfigureAwait(false);
            var foreign = new WoTDocumentTypeClient(other, allocation.Version.ResourceNodeId, m_telemetry);
            ServiceResultException ownership = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await foreign.WriteAsync(allocation.FileHandle, ByteString.From(new byte[] { 1 }))
                    .ConfigureAwait(false))!;
            Assert.That(ownership.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            WotUpsertResourceRequest request = WotRegistryIdentityTests.Request(
                m_registry.Current.FindGroup(group.GroupId)!, allocation.Version.ResourceId, "urn:thing", "v1");
            await allocation.Version.Proxy.WriteAsync(allocation.FileHandle, request.Content).ConfigureAwait(false);
            await allocation.Version.Proxy.CloseAsync(allocation.FileHandle).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int writes = m_store.Blobs.Writes;
            (WotRegistryResourceAllocation reopened, bool createdResource, bool createdVersion) =
                await group.GetOrCreateDocumentResourceAsync("urn:thing", "v1", requestFileOpen: true)
                    .ConfigureAwait(false);
            Assert.That(await reopened.Version.Proxy.GetPositionAsync(reopened.FileHandle)
                .ConfigureAwait(false), Is.Zero);
            await reopened.Version.Proxy.CloseAsync(reopened.FileHandle).ConfigureAwait(false);
            (WotRegistryResourceAllocation nonTruncating, _, _) =
                await group.GetOrCreateDocumentResourceAsync("urn:thing", "v1", requestFileOpen: true)
                    .ConfigureAwait(false);
            await nonTruncating.Version.Proxy.WriteAsync(nonTruncating.FileHandle, ByteString.From("{"u8.ToArray()))
                .ConfigureAwait(false);
            await nonTruncating.Version.Proxy.CloseAsync(nonTruncating.FileHandle).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(createdResource || createdVersion, Is.False);
                Assert.That(reopened.Version.ResourceNodeId, Is.EqualTo(allocation.Version.ResourceNodeId));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.Blobs.Writes, Is.EqualTo(writes));
                Assert.That(m_store.Blobs.Reads, Is.GreaterThan(0));
            });
            Assert.That(await reopened.Version.DownloadAsync().ConfigureAwait(false), Is.EqualTo(request.Content));
            await other.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ReaderConflictAndFailedCommitCannotLeavePartialNativeAllocation()
        {
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false);
            WotRegistryResourceAllocation existing = await group.CreateDocumentResourceAsync("urn:existing", "v1")
                .ConfigureAwait(false);
            uint reader = await existing.Version.Proxy.OpenAsync(1).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ServiceResultException conflict = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await group.GetOrCreateDocumentResourceAsync("urn:existing", "v1", requestFileOpen: true)
                    .ConfigureAwait(false))!;
            Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(m_registry.Current, Is.SameAs(before));
            await existing.Version.Proxy.CloseAsync(reader).ConfigureAwait(false);

            m_store.FailNextCommit = true;
            ServiceResultException commit = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await group.CreateDocumentResourceAsync("urn:new", "v1", requestFileOpen: true).ConfigureAwait(false))!;
            Assert.That(commit.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(m_registry.Current, Is.SameAs(before));
            (WotRegistryResourceAllocation retry, bool createdResource, bool createdVersion) =
                await group.GetOrCreateDocumentResourceAsync("urn:new", "v1", requestFileOpen: true)
                    .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(createdResource && createdVersion, Is.True);
                Assert.That(retry.Version.ResourceId, Is.EqualTo("urn.new"));
                Assert.That(retry.FileHandle, Is.Not.Zero);
                Assert.That(m_store.Blobs.Writes + m_store.Blobs.Reads, Is.Zero);
            });
            await retry.Version.Proxy.CloseAsync(retry.FileHandle).ConfigureAwait(false);
        }

        [Test]
        public async Task DirtyCloseRejectsSourceMutationWithoutReplacingBytesOrMetadata()
        {
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:models").ConfigureAwait(false);
            WotRegistryResourceAllocation first = await group.CreateThingModelResourceAsync(
                "urn:thing", "v1", requestFileOpen: true).ConfigureAwait(false);
            WotResourceGroup storedGroup = m_registry.Current.FindGroup(group.GroupId)!;
            ByteString original = WotRegistryIdentityTests.Request(
                storedGroup, first.Version.ResourceId, "urn:thing", "v1").Content;
            await first.Version.Proxy.WriteAsync(first.FileHandle, original).ConfigureAwait(false);
            await first.Version.Proxy.CloseAsync(first.FileHandle).ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            int writes = m_store.Blobs.Writes;
            (WotRegistryResourceAllocation replacement, _, _) =
                await group.GetOrCreateThingModelResourceAsync("urn:thing", "v1", requestFileOpen: true)
                    .ConfigureAwait(false);
            ByteString other = WotRegistryIdentityTests.Request(
                storedGroup, first.Version.ResourceId, "urn:other", "v1").Content;
            await replacement.Version.Proxy.WriteAsync(replacement.FileHandle, other).ConfigureAwait(false);

            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await replacement.Version.Proxy.CloseAsync(replacement.FileHandle).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.Blobs.Writes, Is.EqualTo(writes));
            });
            Assert.That(await first.Version.DownloadAsync().ConfigureAwait(false), Is.EqualTo(original));
            Assert.That(await ReadStringAsync(first.Version.ResourceNodeId, "ModelId", Namespaces.WotCon)
                .ConfigureAwait(false),
                Is.EqualTo("urn:thing"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeInvalidKindAuthorityAndWrongReceiverHaveNoCreationSideEffects(bool modelOnDescription)
        {
            ServiceResultException all = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.Proxy.CreateDocumentGroupAsync(WoTDocumentKindEnum.All, "urn:catalogue")
                    .ConfigureAwait(false))!;
            ServiceResultException relative = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.Proxy.GetOrCreateDocumentGroupAsync(WoTDocumentKindEnum.ThingModel, "relative")
                    .ConfigureAwait(false))!;
            Assert.That(all.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(relative.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(m_store.CommitCount, Is.Zero);
            WotRegistryGroupClient td = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false);
            WotRegistryGroupClient tm = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:catalogue").ConfigureAwait(false);
            ThingModelGroupState tmState = m_manager.FindPredefinedNode<ThingModelGroupState>(tm.GroupNodeId)!;
            ThingDescriptionGroupState tdState =
                m_manager.FindPredefinedNode<ThingDescriptionGroupState>(td.GroupNodeId)!;
            WotRegistrySnapshot before = m_registry.Current;
            CallResponse call = await m_client.Session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = modelOnDescription ? td.GroupNodeId : tm.GroupNodeId,
                        MethodId = modelOnDescription
                            ? tmState.CreateThingModelResource!.NodeId : tdState.CreateThingDescriptionResource!.NodeId,
                        InputArguments = [Variant.From("urn:wrong"), Variant.From("v1"), Variant.From(true)]
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(call.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.Blobs.Reads + m_store.Blobs.Writes, Is.Zero);
            });
        }

        [TestCase("input")]
        [TestCase("output")]
        [TestCase("executable")]
        [TestCase("user")]
        public async Task ClientChecksNativeMethodContractBeforeCalling(string fault)
        {
            StatusCode status = fault switch
            {
                "executable" => StatusCodes.BadNotExecutable,
                "user" => StatusCodes.BadUserAccessDenied,
                _ => StatusCodes.BadTypeMismatch
            };
            WoTRegistryState registry = m_manager.FindPredefinedNode<WoTRegistryState>(m_client.RegistryNodeId)!;
            CreateDocumentGroupMethodState method = registry.CreateDocumentGroup!;
            ArrayOf<Argument> inputs = method.InputArguments!.Value;
            ArrayOf<Argument> outputs = method.OutputArguments!.Value;
            try
            {
                if (fault == "input")
                {
                    method.InputArguments.Value = [new Argument { Name = "Wrong" }];
                }
                else if (fault == "output")
                {
                    method.OutputArguments.Value = [new Argument { Name = "Wrong" }];
                }
                method.Executable = fault != "executable";
                method.UserExecutable = fault != "user";
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_client.CreateDocumentGroupAsync(WoTDocumentKindEnum.ThingDescription, "urn:catalogue")
                        .ConfigureAwait(false))!;
                Assert.Multiple(() =>
                {
                    Assert.That(failure.StatusCode, Is.EqualTo(status));
                    Assert.That(m_store.CommitCount, Is.Zero);
                    Assert.That(m_registry.Current.Groups, Is.Empty);
                });
            }
            finally
            {
                method.InputArguments.Value = inputs;
                method.OutputArguments.Value = outputs;
                method.Executable = method.UserExecutable = true;
            }
            Assert.That((await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue")
                    .ConfigureAwait(false)).GroupId, Does.StartWith("td."));
        }

        [Test]
        public async Task GenericConfiguredAliasesResolveTheSameTypedAllocation()
        {
            (WotRegistryGroupClient generic, bool createdGroup) = await m_client.GetOrCreateGroupAsync("configured")
                .ConfigureAwait(false);
            (WotRegistryGroupClient typed, bool repeatedGroup) = await m_client.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingModel, "urn:configured:catalogue").ConfigureAwait(false);
            (WotRegistryResourceClient exact, string version, bool created) =
                await generic.GetOrCreateResourceAsync("configured-model", "v1").ConfigureAwait(false);
            (WotRegistryResourceAllocation same, bool createdResource, bool createdVersion) =
                await typed.GetOrCreateThingModelResourceAsync("urn:configured:model", "v1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(createdGroup && created, Is.True);
                Assert.That(repeatedGroup || createdResource || createdVersion, Is.False);
                Assert.That(generic.GroupNodeId, Is.EqualTo(typed.GroupNodeId));
                Assert.That(generic.GroupId, Does.StartWith("tm."));
                Assert.That(exact.ResourceNodeId, Is.EqualTo(same.Version.ResourceNodeId));
                Assert.That(exact.ResourceId, Is.EqualTo(same.Version.ResourceId));
                Assert.That(version, Is.EqualTo("v1"));
                Assert.That(m_store.Blobs.Reads + m_store.Blobs.Writes, Is.Zero);
            });
        }

        [Test]
        public async Task MissingGenericAuthorityIsRejectedRatherThanInferredFromNames()
        {
            ServiceResultException groupFailure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_client.Proxy.CreateGroupAsync("thingmodels").ConfigureAwait(false))!;
            Assert.That(groupFailure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(m_store.CommitCount, Is.Zero);
            WotRegistryGroupClient group = await m_client.CreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:catalogue").ConfigureAwait(false);
            WotRegistrySnapshot before = m_registry.Current;
            ServiceResultException resourceFailure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await group.Proxy.CreateResourceAsync("urn-looking-name", "v1", true).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(resourceFailure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(m_store.Blobs.Reads + m_store.Blobs.Writes, Is.Zero);
            });
        }

        private async ValueTask<string> ReadStringAsync(NodeId receiver, string name, string namespaceUri)
        {
            NodeId property = await WotConBrowsePathResolver.ResolveChildAsync(
                m_client.Session, receiver, Ua.ReferenceTypeIds.HasProperty,
                m_client.Session.NamespaceUris.GetIndexOrAppend(namespaceUri), name,
                StatusCodes.BadNodeIdUnknown, "Required identity metadata is missing.", default).ConfigureAwait(false);
            DataValue value = await m_client.Session.ReadValueAsync(property).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out string result), Is.True);
            return result;
        }

        private static void Configure(WotRegistryServerOptions options)
        {
            options.AutoRefresh = false;
            options.ManagementAccess = new WotManagementAccessPolicy
            {
                MinimumSecurityMode = MessageSecurityMode.None,
                AllowAnonymous = true,
                RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
            };
            options.IdentityBindings.Groups =
            [
                new WotRegistryGroupIdentity("configured", WoTDocumentKindEnum.ThingModel, "urn:configured:catalogue")
            ];
            options.IdentityBindings.Resources =
            [
                new WotRegistryResourceIdentity("configured", "configured-model", "urn:configured:model")
            ];
        }

        private sealed class NativeStore : IWotRegistryStore, IWotRegistryResourceStoreProvider
        {
            public CountingBlobs Blobs { get; } = new();
            public IXRegistryResourceStore ResourceStore => Blobs;
            public bool FailNextCommit { get; set; }
            public int CommitCount { get; private set; }
            public int SuccessfulCommits { get; private set; }
            public Task CommitEntered => m_entered!.Task;

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.LoadAsync(cancellationToken);
            }

            public async ValueTask CommitAsync(
                WotRegistrySnapshot snapshot,
                CancellationToken cancellationToken = default)
            {
                CommitCount++;
                if (!m_pauseAfterCommit)
                {
                    await WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (FailNextCommit)
                {
                    FailNextCommit = false;
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable, "Injected non-commit.");
                }
                await m_inner.CommitAsync(snapshot, cancellationToken).ConfigureAwait(false);
                SuccessfulCommits++;
                if (m_pauseAfterCommit)
                {
                    await WaitForReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            public void PauseNextCommit(bool afterCommit = false)
            {
                m_pauseAfterCommit = afterCommit;
                m_entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void ReleaseCommit()
            {
                m_release?.TrySetResult(true);
            }

            private async ValueTask WaitForReleaseAsync(CancellationToken cancellationToken)
            {
                if (m_release is { } release)
                {
                    m_entered!.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_release = null;
                    }
                }
            }

            private readonly InMemoryWotRegistryStore m_inner = new();
            private TaskCompletionSource<bool>? m_entered;
            private TaskCompletionSource<bool>? m_release;
            private bool m_pauseAfterCommit;
        }

        private sealed class CountingBlobs : IXRegistryResourceStore
        {
            public int Reads => Volatile.Read(ref m_reads);
            public int Writes => Volatile.Read(ref m_writes);
            public bool FailNextRead { get; set; }

            public ValueTask<ByteString> ReadAsync(string resourceKey, long offset, int count, CancellationToken ct)
            {
                Interlocked.Increment(ref m_reads);
                if (FailNextRead)
                {
                    FailNextRead = false;
                    throw new ServiceResultException(
                        StatusCodes.BadResourceUnavailable, "Injected preserving-read fault.");
                }
                return m_inner.ReadAsync(resourceKey, offset, count, ct);
            }

            public ValueTask WriteAsync(string resourceKey, long offset, ByteString data, CancellationToken ct)
            {
                Interlocked.Increment(ref m_writes);
                return m_inner.WriteAsync(resourceKey, offset, data, ct);
            }

            public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct)
            {
                return m_inner.DeleteAsync(resourceKey, ct);
            }

            private readonly InMemoryResourceStore m_inner = new();
            private int m_reads;
            private int m_writes;
        }

        private readonly bool m_useDependencyInjection;
        private string m_root = null!;
        private ITelemetryContext m_telemetry = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ReferenceServer m_server = null!;
        private ClientFixture m_clientFixture = null!;
        private ISession m_bootstrapSession = null!;
        private ManagedSession? m_managed;
        private ServiceProvider? m_services;
        private WotRegistryService m_registry = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private WotRegistryNodeManager m_manager = null!;
        private WotRegistryClient m_client = null!;
        private NativeStore m_store = null!;
        private WotRegistryServerOptions m_options = null!;
        private Ua.Server.NodeManagerRegistration m_registration = null!;
        private ServiceProvider? m_typedServices;
    }
}
