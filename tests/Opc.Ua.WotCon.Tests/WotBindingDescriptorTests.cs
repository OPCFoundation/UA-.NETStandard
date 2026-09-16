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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using Opc.Ua.WotCon.Tests.Support;
using Quickstarts.ReferenceServer;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class WotBindingDescriptorTests
    {
        [SetUp]
        public void RecordExecutionRuntime()
        {
            using Process process = Process.GetCurrentProcess();
            TestContext.Out.WriteLine(
                $"WOT-R36-RUNTIME;pid={process.Id};framework={RuntimeInformation.FrameworkDescription};" +
                $"clr={Environment.Version};entry={typeof(WotBindingDescriptorTests).Assembly.Location};" +
                $"core={typeof(object).Assembly.Location}");
        }

        [Test]
        public async Task RegisteredBindingHasBrowseableIdentityAndPolicyBeforeSelection()
        {
            await WithRegistryAsync((manager, root) =>
            {
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                NodeState? folder = root.FindChild(manager.SystemContext, new QualifiedName("SupportedBindings", ns));
                Assert.That(folder, Is.Not.Null);
                var children = new List<BaseInstanceState>();
                folder!.GetChildren(manager.SystemContext, children);
                Assert.That(children, Has.Count.EqualTo(1));
                Assert.That(children[0], Is.TypeOf<WoTBindingState>());
                var binding = (WoTBindingState)children[0];
                Assert.That(binding.BindingUri!.Value, Is.EqualTo(MemoryWotBinder.BindingUri));
                Assert.That(binding.Title!.Value, Is.EqualTo("Sample In-Memory Binding"));
                Assert.That(binding.ProfileVersion!.Value, Is.EqualTo("1.0"));
                Assert.That(binding.DraftMaturity!.Value, Is.Not.Null.And.Not.Empty);
                Assert.That(binding.Enabled!.Value, Is.False);
                Assert.That(binding.ContentTypes!.Value.ToArray(),
                    Is.EqualTo(s_memoryContentTypes));
                Assert.That(binding.Capabilities, Is.Not.Null);
                Assert.That(binding.Capabilities!.Value.BindingUri, Is.EqualTo(MemoryWotBinder.BindingUri));
                Assert.That(manager.Find(binding.NodeId), Is.SameAs(binding));
                Assert.That(manager.Find(binding.BindingUri.NodeId), Is.SameAs(binding.BindingUri));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisteredButUnusedBindingIsNotReportedAsSelected()
        {
            await WithRegistryAsync(async (manager, root) =>
            {
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                NodeState? selected = root.FindChild(manager.SystemContext, new QualifiedName("SelectedBindings", ns));
                Assert.That(selected, Is.Not.Null);
                (ServiceResult status, DataValue value) = await selected!.ReadAttributeAsync(
                    manager.SystemContext, Attributes.Value, default, QualifiedName.Null, default)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(status), Is.True);
                Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> snapshot), Is.True);
                Assert.That(snapshot.Count, Is.Zero);
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedBindingsTrackPublishedPlansAndClearAfterRetirement()
        {
            await WithRegistryAsync(async (manager, root) =>
            {
                ByteString content = ByteString.From(Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/ """
                    {
                      "@context":"https://www.w3.org/2022/wot/td/v1.1",
                      "@type":"uav:object",
                      "id":"urn:test:binding-descriptors:reading",
                      "title":"Descriptor selection",
                      "security":"none",
                      "securityDefinitions":{"none":{"scheme":"nosec"}},
                      "properties":{
                        "reading":{"type":"number","forms":[{"href":"mem://store/reading"}]}
                      }
                    }
                    """));
                WotRegistryMutationResult uploaded = await manager.Registry.UpsertResourceAsync(
                    new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "descriptor-reading",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = content,
                        SetAsDefault = true
                    }).ConfigureAwait(false);
                Assert.That(uploaded.Outcome,
                    Is.Not.EqualTo(WoTOutcomeEnum.Failed).And.Not.EqualTo(WoTOutcomeEnum.Rejected));
                WotRefreshResult refresh = await manager.Coordinator.RefreshAsync(new WotRefreshRequest())
                    .ConfigureAwait(false);
                Assert.That(refresh.Results, Has.Length.EqualTo(1));
                Assert.That(refresh.Results[0].LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                ArrayOf<WoTBindingCapabilityDataType> selected = await ReadSelectedAsync(manager, root)
                    .ConfigureAwait(false);
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].BindingUri, Is.EqualTo(MemoryWotBinder.BindingUri));
                Assert.That(selected[0].ProfileVersion, Is.EqualTo("1.0"));

                await manager.Coordinator.RemoveAllAsync().ConfigureAwait(false);

                selected = await ReadSelectedAsync(manager, root).ConfigureAwait(false);
                Assert.That(selected.Count, Is.Zero);
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                NodeState folder = root.FindChild(manager.SystemContext,
                    new QualifiedName("SupportedBindings", ns))!;
                var descriptors = new List<BaseInstanceState>();
                folder.GetChildren(manager.SystemContext, descriptors);
                Assert.That(descriptors, Has.Count.EqualTo(1));
            }, executable: true).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BindingEnabledReflectsEffectiveRuntimeOperations(bool executable)
        {
            await WithRegistryAsync((manager, root) =>
            {
                WoTBindingState descriptor = Descriptors(manager, root)[0];
                Assert.That(descriptor.Enabled!.Value, Is.EqualTo(executable));
                Assert.That(descriptor.Capabilities!.Value.Capabilities.Count > 0, Is.EqualTo(executable));
                return Task.CompletedTask;
            }, executable).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisteredVersionsHaveDistinctIndexedDescriptorProperties()
        {
            var first = new MemoryWotBinder().Capability.ToDataType();
            var second = new WoTBindingCapabilityDataType
            {
                BindingUri = first.BindingUri,
                Title = "Second version",
                ProfileVersion = "2.0",
                DraftMaturity = first.DraftMaturity,
                Capabilities = first.Capabilities,
                ContentTypes = first.ContentTypes
            };
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(new[] { first, second });
            await WithRegistryAsync((manager, root) =>
            {
                List<WoTBindingState> descriptors = Descriptors(manager, root);
                Assert.That(descriptors, Has.Count.EqualTo(2));
                var identities = new HashSet<NodeId>();
                foreach (WoTBindingState descriptor in descriptors)
                {
                    Assert.That(identities.Add(descriptor.NodeId), Is.True);
                    Assert.That(manager.Find(descriptor.NodeId), Is.SameAs(descriptor));
                    var properties = new List<BaseInstanceState>();
                    descriptor.GetChildren(manager.SystemContext, properties);
                    Assert.That(properties, Has.Count.EqualTo(7));
                    foreach (BaseInstanceState property in properties)
                    {
                        Assert.That(identities.Add(property.NodeId), Is.True);
                        Assert.That(manager.Find(property.NodeId), Is.SameAs(property));
                    }
                }
                Assert.That(identities, Has.Count.EqualTo(16));
                return Task.CompletedTask;
            }, registeredBinders: binders.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task BindingDescriptorDoesNotReuseItsTypeDeclarationNodeId()
        {
            await WithRegistryAsync((manager, root) =>
            {
                WoTBindingState descriptor = Descriptors(manager, root)[0];
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                var declarationId = new NodeId(64578u, ns);
                Assert.That(descriptor.BindingUri!.NodeId, Is.Not.EqualTo(declarationId));
                NodeState? declaration = manager.Find(declarationId);
                Assert.That(declaration, Is.Not.SameAs(descriptor.BindingUri));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task EmptyBinderRegistryExposesEmptySupportedAndSelectedBindings()
        {
            await WithRegistryAsync(async (manager, root) =>
            {
                Assert.That(Descriptors(manager, root), Is.Empty);
                ArrayOf<WoTBindingCapabilityDataType> selected = await ReadSelectedAsync(manager, root)
                    .ConfigureAwait(false);
                Assert.That(selected.Count, Is.Zero);
            }, registeredBinders: NullWotBinderRegistry.Instance).ConfigureAwait(false);
        }

        [TestCase("Title")]
        [TestCase("ProfileVersion")]
        [TestCase("DraftMaturity")]
        public async Task OptionalBindingMetadataMayBeAbsent(string absentMember)
        {
            WoTBindingCapabilityDataType original = new MemoryWotBinder().Capability.ToDataType();
            var capability = new WoTBindingCapabilityDataType
            {
                BindingUri = original.BindingUri,
                Title = absentMember == "Title" ? null : original.Title,
                ProfileVersion = absentMember == "ProfileVersion" ? null : original.ProfileVersion,
                DraftMaturity = absentMember == "DraftMaturity" ? null : original.DraftMaturity,
                Capabilities = original.Capabilities,
                ContentTypes = original.ContentTypes
            };
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(new[] { capability });
            await WithRegistryAsync((manager, root) =>
            {
                WoTBindingState descriptor = Descriptors(manager, root).Single();
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                Assert.That(descriptor.FindChild(manager.SystemContext, new QualifiedName(absentMember, ns)),
                    Is.Null, "An absent optional field must not become a fabricated metadata value.");
                Assert.That(descriptor.BindingUri!.Value, Is.EqualTo(original.BindingUri));
                Assert.That(descriptor.Capabilities!.Value, Is.EqualTo(capability));
                return Task.CompletedTask;
            }, registeredBinders: binders.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task IdenticalRegisteredCapabilitiesShareOneDescriptor()
        {
            WoTBindingCapabilityDataType capability = new MemoryWotBinder().Capability.ToDataType();
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(new[] { capability, capability });
            await WithRegistryAsync((manager, root) =>
            {
                WoTBindingState descriptor = Descriptors(manager, root).Single();
                Assert.That(descriptor.Capabilities!.Value, Is.EqualTo(capability));
                return Task.CompletedTask;
            }, registeredBinders: binders.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task AbsentAndEmptyVersionsRetainDistinctDescriptorIdentities()
        {
            WoTBindingCapabilityDataType original = new MemoryWotBinder().Capability.ToDataType();
            string?[] versions = [null, string.Empty, "unversioned"];
            WoTBindingCapabilityDataType[] capabilities = versions.Select(version =>
                new WoTBindingCapabilityDataType
                {
                    BindingUri = original.BindingUri,
                    Title = original.Title,
                    ProfileVersion = version,
                    DraftMaturity = original.DraftMaturity,
                    Capabilities = original.Capabilities,
                    ContentTypes = original.ContentTypes
                }).ToArray();
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(capabilities);
            await WithRegistryAsync((manager, root) =>
            {
                List<WoTBindingState> descriptors = Descriptors(manager, root);
                Assert.That(descriptors, Has.Count.EqualTo(3));
                Assert.That(descriptors.Select(descriptor => descriptor.Capabilities!.Value.ProfileVersion),
                    Is.EquivalentTo(versions));
                var identities = new HashSet<NodeId>();
                foreach (WoTBindingState descriptor in descriptors)
                {
                    Assert.That(identities.Add(descriptor.NodeId), Is.True);
                    Assert.That(manager.Find(descriptor.NodeId), Is.SameAs(descriptor));
                    var properties = new List<BaseInstanceState>();
                    descriptor.GetChildren(manager.SystemContext, properties);
                    foreach (BaseInstanceState property in properties)
                    {
                        Assert.That(identities.Add(property.NodeId), Is.True);
                        Assert.That(manager.Find(property.NodeId), Is.SameAs(property));
                    }
                }
                return Task.CompletedTask;
            }, registeredBinders: binders.Object).ConfigureAwait(false);
        }

        [Test]
        public async Task ConflictingRegisteredCapabilityIdentitiesAreRejected()
        {
            WoTBindingCapabilityDataType first = new MemoryWotBinder().Capability.ToDataType();
            var second = new WoTBindingCapabilityDataType
            {
                BindingUri = first.BindingUri,
                ProfileVersion = first.ProfileVersion,
                Title = "Conflicting title",
                DraftMaturity = first.DraftMaturity,
                Capabilities = first.Capabilities,
                ContentTypes = first.ContentTypes
            };
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(new[] { first, second });
            await Assert.ThatAsync(
                () => WithRegistryAsync((_, _) => Task.CompletedTask, registeredBinders: binders.Object),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public async Task MissingBindingUriIsRejected(string? bindingUri)
        {
            WoTBindingCapabilityDataType original = new MemoryWotBinder().Capability.ToDataType();
            var capability = new WoTBindingCapabilityDataType
            {
                BindingUri = bindingUri,
                Title = original.Title,
                ProfileVersion = original.ProfileVersion,
                DraftMaturity = original.DraftMaturity,
                Capabilities = original.Capabilities,
                ContentTypes = original.ContentTypes
            };
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(new[] { capability });
            await Assert.ThatAsync(
                () => WithRegistryAsync((_, _) => Task.CompletedTask, registeredBinders: binders.Object),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError)).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedSnapshotRetainsOptionalMetadataAndDeduplicatesPlanIdentities()
        {
            var capability = new WoTBindingCapabilityDataType
            {
                BindingUri = MemoryWotBinder.BindingUri,
                Capabilities = [WoTBindingCapabilityEnum.ReadProperty],
                ContentTypes = ["application/json"]
            };
            await WithPublishedCapabilityPlanAsync([capability, capability], async coordinator =>
            {
                ArrayOf<WoTBindingCapabilityDataType> selected = await coordinator.GetSelectedBindingCapabilitiesAsync()
                    .ConfigureAwait(false);
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].IsEqual(capability), Is.True);
                WoTBindingCapabilityDataType[] callerArray = selected.ToArray()
                    ?? throw new InvalidOperationException("The selected snapshot returned no array.");
                callerArray[0] = new WoTBindingCapabilityDataType { BindingUri = "urn:test:caller-only" };
                selected = await coordinator.GetSelectedBindingCapabilitiesAsync().ConfigureAwait(false);
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].IsEqual(capability), Is.True);
                await coordinator.RemoveAllAsync().ConfigureAwait(false);
                selected = await coordinator.GetSelectedBindingCapabilitiesAsync().ConfigureAwait(false);
                Assert.That(selected.Count, Is.Zero);
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedSnapshotMutationCannotChangePublishedPlans()
        {
            WoTBindingCapabilityDataType capability = new MemoryWotBinder().Capability.ToDataType();
            await WithPublishedCapabilityPlanAsync([capability], async coordinator =>
            {
                ArrayOf<WoTBindingCapabilityDataType> selected = await coordinator.GetSelectedBindingCapabilitiesAsync()
                    .ConfigureAwait(false);
                Assert.That(selected.Count, Is.EqualTo(1));
                var callerValue = new WoTBindingCapabilityDataType
                {
                    BindingUri = "urn:test:caller-only",
                    Title = "Caller mutation",
                    ProfileVersion = "caller",
                    Capabilities = [],
                    ContentTypes = []
                };
                IServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
                using var encoder = new BinaryEncoder(context);
                callerValue.Encode(encoder);
                byte[] bytes = encoder.CloseAndReturnBuffer()
                    ?? throw new InvalidOperationException("The caller value was not encoded.");
                using var decoder = new BinaryDecoder(bytes, context);
                selected[0].Decode(decoder);

                ArrayOf<WoTBindingCapabilityDataType> current = await coordinator.GetSelectedBindingCapabilitiesAsync()
                    .ConfigureAwait(false);
                Assert.That(current.Count, Is.EqualTo(1));
                Assert.That(current[0].BindingUri, Is.EqualTo(MemoryWotBinder.BindingUri));
                Assert.That(current[0].Title, Is.EqualTo("Sample In-Memory Binding"));
                Assert.That(current[0].ProfileVersion, Is.EqualTo("1.0"));
                Assert.That(current[0].Capabilities.Count, Is.GreaterThan(0));
                Assert.That(current[0].ContentTypes.ToArray(), Is.EqualTo(s_memoryContentTypes));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedSnapshotsUseDeterministicIdentityOrdering()
        {
            var alphaVersion2 = new WoTBindingCapabilityDataType
            {
                BindingUri = "urn:test:alpha",
                ProfileVersion = "2.0"
            };
            var alphaUnversioned = new WoTBindingCapabilityDataType
            {
                BindingUri = "urn:test:alpha"
            };
            var beta = new WoTBindingCapabilityDataType
            {
                BindingUri = "urn:test:beta",
                ProfileVersion = "1.0"
            };
            await WithPublishedCapabilityPlanAsync(
                [beta, alphaVersion2, alphaUnversioned, alphaVersion2], async coordinator =>
                {
                    ArrayOf<WoTBindingCapabilityDataType> selected = await coordinator
                        .GetSelectedBindingCapabilitiesAsync().ConfigureAwait(false);
                    Assert.That(selected.Count, Is.EqualTo(3));
                    Assert.That(selected[0].IsEqual(alphaUnversioned), Is.True);
                    Assert.That(selected[1].IsEqual(alphaVersion2), Is.True);
                    Assert.That(selected[2].IsEqual(beta), Is.True);
                }).ConfigureAwait(false);
        }

        [Test]
        public async Task SelectedSnapshotChecksCancellationBeforeReading()
        {
            using var registry = new WotRegistryService();
            using var coordinator = new WotMaterializationCoordinator(registry,
                new FakeWotProjectionHost(), documentConverter: new FakeWotDocumentConverter());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(() => coordinator.GetSelectedBindingCapabilitiesAsync(cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            ArrayOf<WoTBindingCapabilityDataType> selected = await coordinator.GetSelectedBindingCapabilitiesAsync()
                .ConfigureAwait(false);
            Assert.That(selected.Count, Is.Zero);
        }

        [Test]
        public async Task SelectedSnapshotRejectsReadsAfterDisposal()
        {
            using var registry = new WotRegistryService();
            using var coordinator = new WotMaterializationCoordinator(registry,
                new FakeWotProjectionHost(), documentConverter: new FakeWotDocumentConverter());
            coordinator.Dispose();
            await Assert.ThatAsync(() => coordinator.GetSelectedBindingCapabilitiesAsync().AsTask(),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task BindingIdentityAndUnusedSelectionAreReadableOverEncryptedTransport()
        {
            await WithRegistryTransportAsync(async (_, _, endpoint, directory) =>
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                await using var fixture = new ClientFixture(false, false, telemetry);
                await fixture.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                using ClientSession session = await fixture.ConnectAsync(endpoint, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, telemetry)
                        .ConfigureAwait(false);
                    ReferenceDescription[] registryChildren = (await BrowseAsync(session, client.RegistryNodeId)
                        .ConfigureAwait(false)).ToArray()
                        ?? throw new InvalidOperationException("The registry returned no child reference array.");
                    ReferenceDescription folder = registryChildren.Single(
                        reference => reference.BrowseName.Name == "SupportedBindings");
                    NodeId folderId = ExpandedNodeId.ToNodeId(folder.NodeId, session.NamespaceUris);
                    ArrayOf<ReferenceDescription> descriptors = await BrowseAsync(session, folderId)
                        .ConfigureAwait(false);
                    Assert.That(descriptors.Count, Is.EqualTo(1));
                    Assert.That(ExpandedNodeId.ToNodeId(descriptors[0].TypeDefinition, session.NamespaceUris),
                        Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.WoTBindingType, session.NamespaceUris)));
                    NodeId descriptorId = ExpandedNodeId.ToNodeId(descriptors[0].NodeId, session.NamespaceUris);
                    ReferenceDescription[] properties = (await BrowseAsync(session, descriptorId)
                        .ConfigureAwait(false)).ToArray()
                        ?? throw new InvalidOperationException("The descriptor returned no property reference array.");
                    Assert.That(properties, Has.Length.EqualTo(7));
                    string[] expectedNames =
                    [
                        "BindingUri", "Title", "ProfileVersion", "DraftMaturity",
                        "Enabled", "ContentTypes", "Capabilities"
                    ];
                    Assert.That(properties.Select(property => property.BrowseName.Name),
                        Is.EquivalentTo(expectedNames));
                    var reads = expectedNames.Select(name => new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(properties.Single(property =>
                            property.BrowseName.Name == name).NodeId, session.NamespaceUris),
                        AttributeId = Attributes.Value
                    }).ToArrayOf();
                    ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        reads, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(7));
                    for (int i = 0; i < response.Results.Count; i++)
                    {
                        Assert.That(response.Results[i].StatusCode, Is.EqualTo(StatusCodes.Good), expectedNames[i]);
                    }
                    Assert.That(response.Results[0].WrappedValue.TryGetValue(out string uri), Is.True);
                    Assert.That(uri, Is.EqualTo(MemoryWotBinder.BindingUri));
                    Assert.That(response.Results[1].WrappedValue.TryGetValue(out string title), Is.True);
                    Assert.That(title, Is.EqualTo("Sample In-Memory Binding"));
                    Assert.That(response.Results[2].WrappedValue.TryGetValue(out string version), Is.True);
                    Assert.That(version, Is.EqualTo("1.0"));
                    Assert.That(response.Results[4].WrappedValue.TryGetValue(out bool enabled), Is.True);
                    Assert.That(enabled, Is.True);
                    Assert.That(response.Results[5].WrappedValue.TryGetValue(out ArrayOf<string> contentTypes),
                        Is.True);
                    Assert.That(contentTypes.ToArray(), Is.EqualTo(s_memoryContentTypes));
                    ReferenceDescription selected = registryChildren.Single(
                        reference => reference.BrowseName.Name == "SelectedBindings");
                    DataValue value = await session.ReadValueAsync(
                        ExpandedNodeId.ToNodeId(selected.NodeId, session.NamespaceUris)).ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> snapshot), Is.True);
                    Assert.That(snapshot.Count, Is.Zero);
                }
                finally
                {
                    await session.CloseAsync().ConfigureAwait(false);
                }
            }, executable: true).ConfigureAwait(false);
        }

        [Test]
        public async Task BindingDescriptorsExposeTypedReadOnlyFieldsOverEncryptedTransport()
        {
            await WithRegistryTransportAsync(async (_, _, endpoint, directory) =>
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                await using var fixture = new ClientFixture(false, false, telemetry);
                await fixture.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                using ClientSession session = await fixture.ConnectAsync(endpoint, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    WotRegistryClient client = await WotRegistryClient.ForServerAsync(session, telemetry)
                        .ConfigureAwait(false);
                    ReferenceDescription[] registryChildren = (await BrowseAsync(session, client.RegistryNodeId)
                        .ConfigureAwait(false)).ToArray()
                        ?? throw new InvalidOperationException("The registry returned no child reference array.");
                    ReferenceDescription folder = registryChildren.Single(
                        reference => reference.BrowseName.Name == "SupportedBindings");
                    ArrayOf<ReferenceDescription> descriptors = await BrowseAsync(session,
                        ExpandedNodeId.ToNodeId(folder.NodeId, session.NamespaceUris)).ConfigureAwait(false);
                    Assert.That(descriptors.Count, Is.EqualTo(1));
                    ReferenceDescription[] properties = (await BrowseAsync(session,
                        ExpandedNodeId.ToNodeId(descriptors[0].NodeId, session.NamespaceUris))
                        .ConfigureAwait(false)).ToArray()
                        ?? throw new InvalidOperationException("The descriptor returned no property reference array.");
                    NodeId capabilityType = ExpandedNodeId.ToNodeId(
                        DataTypeIds.WoTBindingCapabilityDataType, session.NamespaceUris);
                    (string Name, NodeId DataType, int Rank)[] expected =
                    [
                        ("BindingUri", Ua.DataTypeIds.String, ValueRanks.Scalar),
                        ("Title", Ua.DataTypeIds.String, ValueRanks.Scalar),
                        ("ProfileVersion", Ua.DataTypeIds.String, ValueRanks.Scalar),
                        ("DraftMaturity", Ua.DataTypeIds.String, ValueRanks.Scalar),
                        ("Enabled", Ua.DataTypeIds.Boolean, ValueRanks.Scalar),
                        ("ContentTypes", Ua.DataTypeIds.String, ValueRanks.OneDimension),
                        ("Capabilities", capabilityType, ValueRanks.Scalar),
                        ("SelectedBindings", capabilityType, ValueRanks.OneDimension)
                    ];
                    var writes = new List<WriteValue>();
                    foreach ((string name, NodeId dataType, int rank) in expected)
                    {
                        ReferenceDescription property = (name == "SelectedBindings" ? registryChildren : properties)
                            .Single(reference => reference.BrowseName.Name == name);
                        NodeId nodeId = ExpandedNodeId.ToNodeId(property.NodeId, session.NamespaceUris);
                        uint[] attributes =
                        [
                            Attributes.DataType, Attributes.ValueRank,
                            Attributes.AccessLevel, Attributes.UserAccessLevel, Attributes.Value
                        ];
                        ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                            attributes.Select(attribute => new ReadValueId
                            {
                                NodeId = nodeId,
                                AttributeId = attribute
                            }).ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
                        Assert.That(read.Results.Count, Is.EqualTo(5), name);
                        for (int i = 0; i < read.Results.Count; i++)
                        {
                            Assert.That(read.Results[i].StatusCode, Is.EqualTo(StatusCodes.Good), name);
                        }
                        Assert.That(read.Results[0].WrappedValue.TryGetValue(out NodeId actualType), Is.True, name);
                        Assert.That(actualType, Is.EqualTo(dataType), name);
                        Assert.That(read.Results[1].WrappedValue.TryGetValue(out int actualRank), Is.True, name);
                        Assert.That(actualRank, Is.EqualTo(rank), name);
                        Assert.That(read.Results[2].WrappedValue.TryGetValue(out byte access), Is.True, name);
                        Assert.That(access, Is.EqualTo(AccessLevels.CurrentRead), name);
                        Assert.That(read.Results[3].WrappedValue.TryGetValue(out byte userAccess), Is.True, name);
                        Assert.That(userAccess, Is.EqualTo(AccessLevels.CurrentRead), name);
                        if (name == "Capabilities")
                        {
                            Assert.That(read.Results[4].WrappedValue.TryGetValue(out ExtensionObject encoded),
                                Is.True);
                            var expectedCapability = new MemoryWotBinder().Capability.ToDataType();
                            Assert.That(ExpandedNodeId.ToNodeId(encoded.TypeId, session.NamespaceUris),
                                Is.EqualTo(ExpandedNodeId.ToNodeId(
                                    expectedCapability.BinaryEncodingId, session.NamespaceUris)));
                            session.MessageContext.Factory.Builder
                                .AddEncodeableType<WoTBindingCapabilityDataType>().Commit();
                            Assert.That(read.Results[4].WrappedValue.TryGetValue<WoTBindingCapabilityDataType>(
                                out WoTBindingCapabilityDataType? capability, session.MessageContext), Is.True);
                            Assert.That(capability, Is.Not.Null);
                            Assert.That(capability!.BindingUri, Is.EqualTo(expectedCapability.BindingUri));
                            Assert.That(capability.Title, Is.EqualTo(expectedCapability.Title));
                            Assert.That(capability.ProfileVersion, Is.EqualTo(expectedCapability.ProfileVersion));
                            Assert.That(capability.DraftMaturity, Is.EqualTo(expectedCapability.DraftMaturity));
                            Assert.That(capability.Capabilities.ToArray(),
                                Is.EqualTo(expectedCapability.Capabilities.ToArray()));
                            Assert.That(capability.ContentTypes.ToArray(),
                                Is.EqualTo(expectedCapability.ContentTypes.ToArray()));
                        }
                        else if (name == "DraftMaturity")
                        {
                            Assert.That(read.Results[4].WrappedValue.TryGetValue(out string maturity), Is.True);
                            Assert.That(maturity,
                                Is.EqualTo(new MemoryWotBinder().Capability.ToDataType().DraftMaturity));
                        }
                        writes.Add(new WriteValue
                        {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = read.Results[4]
                        });
                    }
                    WriteResponse write = await session.WriteAsync(null, writes.ToArrayOf(), CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(write.Results.Count, Is.EqualTo(8));
                    for (int i = 0; i < write.Results.Count; i++)
                    {
                        Assert.That(write.Results[i], Is.EqualTo(StatusCodes.BadNotWritable), expected[i].Name);
                    }
                }
                finally
                {
                    await session.CloseAsync().ConfigureAwait(false);
                }
            }, executable: true).ConfigureAwait(false);
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseAsync(ClientSession session, NodeId nodeId)
        {
            BrowseResponse response = await session.BrowseAsync(null, new ViewDescription(), 0,
                [
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.Length, Is.Zero);
            return response.Results[0].References;
        }

        private static async Task WithPublishedCapabilityPlanAsync(
            ArrayOf<WoTBindingCapabilityDataType> capabilities,
            Func<WotMaterializationCoordinator, Task> inspect)
        {
            WoTBindingCapabilityDataType[] planned = capabilities.ToArray()
                ?? throw new InvalidOperationException("The test requires a capability array.");
            var binders = new Mock<IWotBinderRegistry>(MockBehavior.Strict);
            binders.SetupGet(registry => registry.Capabilities).Returns(planned.Distinct().ToArray());
            binders.Setup(registry => registry.Prepare(It.IsAny<WotBindingPlanRequest>()))
                .Returns((WotBindingPlanRequest request) =>
                    new WotBindingPlan(request.ResourceXid, [.. planned], [], [], []));
            binders.Setup(registry => registry.ActivateAsync(
                It.IsAny<WotBindingPlan>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            binders.Setup(registry => registry.DeactivateAsync(
                It.IsAny<WotBindingPlan>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            using var registry = new WotRegistryService();
            using var coordinator = new WotMaterializationCoordinator(registry,
                new FakeWotProjectionHost(), binders.Object, documentConverter: new FakeWotDocumentConverter());
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "optional-binding",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes(
                    /*lang=json,strict*/ """
                    {
                      "@context":"https://www.w3.org/2022/wot/td/v1.1",
                      "@type":"uav:object",
                      "id":"urn:test:optional-binding",
                      "title":"Optional binding metadata",
                      "security":"none",
                      "securityDefinitions":{"none":{"scheme":"nosec"}}
                    }
                    """))
            }).ConfigureAwait(false);
            WotRefreshResult refresh = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            Assert.That(refresh.Results.Single().LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
            await inspect(coordinator).ConfigureAwait(false);
        }

        private static List<WoTBindingState> Descriptors(WotRegistryNodeManager manager, NodeState root)
        {
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            NodeState? folder = root.FindChild(manager.SystemContext, new QualifiedName("SupportedBindings", ns));
            Assert.That(folder, Is.Not.Null);
            var children = new List<BaseInstanceState>();
            folder!.GetChildren(manager.SystemContext, children);
            var descriptors = new List<WoTBindingState>(children.Count);
            foreach (BaseInstanceState child in children)
            {
                Assert.That(child, Is.TypeOf<WoTBindingState>());
                descriptors.Add((WoTBindingState)child);
            }
            return descriptors;
        }

        private static async Task<ArrayOf<WoTBindingCapabilityDataType>> ReadSelectedAsync(
            WotRegistryNodeManager manager, NodeState root)
        {
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            NodeState selected = root.FindChild(manager.SystemContext,
                new QualifiedName("SelectedBindings", ns))!;
            (ServiceResult status, DataValue value) = await selected.ReadAttributeAsync(
                manager.SystemContext, Attributes.Value, default, QualifiedName.Null, default).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(value.WrappedValue.TryGetValue(
                out ArrayOf<WoTBindingCapabilityDataType> capabilities, manager.Server.MessageContext), Is.True);
            return capabilities;
        }

        private static Task WithRegistryAsync(
            Func<WotRegistryNodeManager, NodeState, Task> inspect, bool executable = false,
            IWotBinderRegistry? registeredBinders = null)
        {
            return WithRegistryTransportAsync(
                (manager, root, _, _) => inspect(manager, root), executable, registeredBinders);
        }

        private static async Task WithRegistryTransportAsync(
            Func<WotRegistryNodeManager, NodeState, Uri, string, Task> inspect, bool executable = false,
            IWotBinderRegistry? registeredBinders = null)
        {
            string directory = Path.Combine(
                Path.GetTempPath(), nameof(WotBindingDescriptorTests), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                AutoAccept = true,
                SecurityNone = false
            };
            ReferenceServer? server = null;
            WotRegistryService? registry = null;
            WotMaterializationCoordinator? coordinator = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                registry = new WotRegistryService();
                IWotBinderRegistry binders = registeredBinders ?? new WotProtocolBinderRegistry([new MemoryWotBinder()],
                    executable ? [new MemoryWotBindingExecutor(new MemoryWotStore())] : null);
                coordinator = new WotMaterializationCoordinator(
                    registry, new LifecycleWotProjectionHost(server.NodeManagerLifecycle), binders);
                NodeManagerRegistration registration = await server.NodeManagerLifecycle.AddAsync(
                    new WotRegistryNodeManagerFactory(
                        new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator),
                    callerContext: null).ConfigureAwait(false);
                var manager = (WotRegistryNodeManager)registration.NodeManager;
                ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
                NodeState? root = manager.Find(new NodeId(64100u, ns));
                Assert.That(root, Is.Not.Null);
                await inspect(manager, root!,
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"), directory).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        server?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            coordinator?.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                registry?.Dispose();
                            }
                            finally
                            {
                                if (Directory.Exists(directory))
                                {
                                    Directory.Delete(directory, recursive: true);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static readonly string[] s_memoryContentTypes = ["application/json", "text/plain"];
    }
}
