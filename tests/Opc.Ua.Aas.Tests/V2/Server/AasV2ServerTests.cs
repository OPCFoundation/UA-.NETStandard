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
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Assets;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Aas.Server.V2;
using Opc.Ua.Aas.Server.V2.Hosting;
using Opc.Ua.Aas.Tests.Server;
using Opc.Ua.Aas.V2;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Aas.Tests.V2.Server
{
    /// <summary>
    /// Tests the OPC 30270 AAS V2 server half.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasV2ServerTests
    {
        [Test]
        public async Task NodeManagerProjectsEveryProvidedEnvironmentAsync()
        {
            AasEnvironment first = CreateEnvironment("one");
            AasEnvironment second = CreateEnvironment("two");
            var host = new RecordingProjectionHost();
            using AasV2EnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasV2EnvironmentProvider(new ArrayOf<AasEnvironment>(new[] { first, second })),
                new RecordingValueProvider(),
                new DefaultAasOperationHandler(),
                host);

            await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.That(host.V2Environments, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public async Task DocumentValueProviderIsSeededAndWritableAsync()
        {
            var provider = new DocumentAasV2ValueProvider();
            NodeId valueNodeId = MemberNodeId(ElementNodeId("one", "Temperature"), "Value");
            provider.AddEnvironment(CreateEnvironment("one"));

            AasValueReadResult before = await provider.ReadValueAsync(valueNodeId).ConfigureAwait(false);
            ServiceResult write = await provider.WriteValueAsync(valueNodeId, Variant.From("43"))
                .ConfigureAwait(false);
            AasValueReadResult after = await provider.ReadValueAsync(valueNodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(before.StatusCode), Is.True);
                Assert.That(before.Value.TryGetValue(out string? original), Is.True);
                Assert.That(original, Is.EqualTo("42"));
                Assert.That(ServiceResult.IsGood(write), Is.True);
                Assert.That(after.Value.TryGetValue(out string? updated), Is.True);
                Assert.That(updated, Is.EqualTo("43"));
            });
        }

        [Test]
        public async Task RuntimeReadAndWriteReachValueProviderAsync()
        {
            var valueProvider = new Mock<IAasValueProvider>(MockBehavior.Strict);
            valueProvider.Setup(p => p.ReadValueAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasValueReadResult(ServiceResult.Good, Variant.From("before"),
                    StatusCodes.Good, DateTime.UtcNow));
            valueProvider.Setup(p => p.WriteValueAsync(It.IsAny<NodeId>(), It.IsAny<Variant>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(valueProvider.Object,
                new DefaultAasOperationHandler()).ConfigureAwait(false);
            NodeId valueNodeId = MemberNodeId(ElementNodeId("one", "Temperature"), "Value");

            AttributeReadResult read = await callbacks.Reads[valueNodeId](null!, null!, NumericRange.Null,
                QualifiedName.Null, CancellationToken.None).ConfigureAwait(false);
            AttributeWriteResult write = await callbacks.Writes[valueNodeId](null!, null!, NumericRange.Null,
                Variant.From("after"), CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(read.Result), Is.True);
                Assert.That(read.Value.TryGetValue(out string? readValue), Is.True);
                Assert.That(readValue, Is.EqualTo("before"));
                Assert.That(ServiceResult.IsGood(write.Result), Is.True);
            });
            valueProvider.Verify(p => p.ReadValueAsync(valueNodeId, It.IsAny<CancellationToken>()), Times.Once);
            valueProvider.Verify(p => p.WriteValueAsync(valueNodeId, It.IsAny<Variant>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task OperationMethodInvokesHandlerAndRejectsArgumentsAsync()
        {
            var handler = new Mock<IAasOperationHandler>(MockBehavior.Strict);
            handler.Setup(h => h.InvokeAsync(It.IsAny<AasOperationInvokeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasOperationInvokeResult([], [], true, string.Empty));
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(new RecordingValueProvider(), handler.Object)
                .ConfigureAwait(false);
            NodeId operationNodeId = MemberNodeId(ElementNodeId("one", "Calibrate"), "Operation");

            var outputs = new List<Variant>();
            ServiceResult good = await callbacks.Calls[operationNodeId](null!, null!, NodeId.Null, [], outputs,
                CancellationToken.None).ConfigureAwait(false);
            ServiceResult bad = await callbacks.Calls[operationNodeId](null!, null!, NodeId.Null,
                new ArrayOf<Variant>(new[] { Variant.From(1) }), [], CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(good), Is.True);
                Assert.That(outputs, Is.Empty);
                Assert.That(bad.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            });
            handler.Verify(h => h.InvokeAsync(It.Is<AasOperationInvokeRequest>(r => r.InputValues.Count == 0 &&
                r.InoutputValues.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task OperationFailureReturnsBadInvalidStateAsync()
        {
            var handler = new Mock<IAasOperationHandler>(MockBehavior.Strict);
            handler.Setup(h => h.InvokeAsync(It.IsAny<AasOperationInvokeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasOperationInvokeResult([], [], false, "failed"));
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(new RecordingValueProvider(), handler.Object)
                .ConfigureAwait(false);

            ServiceResult result = await callbacks.Calls[MemberNodeId(ElementNodeId("one", "Calibrate"), "Operation")](
                null!, null!, NodeId.Null, [], [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task EmbeddedFileOpenReadAndCloseServeBlobContentAsync()
        {
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(new RecordingValueProvider(),
                new DefaultAasOperationHandler()).ConfigureAwait(false);
            NodeId fileNodeId = MemberNodeId(ElementNodeId("one", "Manual"), "File");

            var openOutputs = new List<Variant>();
            ServiceResult open = await callbacks.Calls[MemberNodeId(fileNodeId, "Open")](null!, null!, NodeId.Null,
                new ArrayOf<Variant>(new[] { Variant.From(AasElementFileManager.ReadMode) }), openOutputs,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(openOutputs[0].TryGetValue(out uint handle), Is.True);
            var readOutputs = new List<Variant>();
            ServiceResult read = await callbacks.Calls[MemberNodeId(fileNodeId, "Read")](null!, null!, NodeId.Null,
                new ArrayOf<Variant>(new[] { Variant.From(handle), Variant.From(16) }), readOutputs,
                CancellationToken.None).ConfigureAwait(false);
            ServiceResult close = await callbacks.Calls[MemberNodeId(fileNodeId, "Close")](null!, null!, NodeId.Null,
                new ArrayOf<Variant>(new[] { Variant.From(handle) }), [], CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(open), Is.True);
                Assert.That(ServiceResult.IsGood(read), Is.True);
                Assert.That(ServiceResult.IsGood(close), Is.True);
                Assert.That(readOutputs[0].TryGetValue(out ByteString content), Is.True);
                Assert.That(Encoding.UTF8.GetString(content.Span.ToArray()), Is.EqualTo("manual"));
            });
        }

        [Test]
        public void ConformanceUnitsPublishOpc30270FacetWithoutProfileUris()
        {
            using AasV2EnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasV2EnvironmentProvider([]),
                new RecordingValueProvider(),
                new DefaultAasOperationHandler(),
                new RecordingProjectionHost());

            string[] units = Names(nodeManager.ConformanceUnits);

            Assert.Multiple(() =>
            {
                Assert.That(units, Is.EquivalentTo(s_opc30270Units));
                Assert.That(nodeManager.ServerProfiles, Is.Empty);
            });
        }

        [Test]
        public void AddAasV2ServerRegistersResolvableServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<INodeManagerLifecycle>());

            IAasV2ServerBuilder builder = services.AddOpcUa().AddAasV2Server(
                options => options.ControlNamespaceUri = ControlNamespaceUri);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(builder.Services, Is.SameAs(services));
                Assert.That(provider.GetService<IAasV2EnvironmentProvider>(), Is.Not.Null);
                Assert.That(provider.GetService<IAasV2ValueProvider>(), Is.TypeOf<DocumentAasV2ValueProvider>());

                // Deliberately not registered under the shared contract: that
                // is the registration the V3 half claims, and sharing it would
                // let whichever generation was added first serve both.
                Assert.That(provider.GetService<IAasValueProvider>(), Is.Null);
                Assert.That(provider.GetService<IAasEnvironmentProjectionHost>(), Is.Not.Null);
                Assert.That(provider.GetService<AasV2EnvironmentNodeManagerFactory>(), Is.Not.Null);
                Assert.That(provider.GetService<OpcUaServerNodeManagerRegistration>(), Is.Not.Null);
            });
        }

        private const string ControlNamespaceUri = "http://opcfoundation.org/UA/I4AAS/V2/Server/";

        private static readonly string[] s_opc30270Units =
        [
            "I4AAS AAS",
            "I4AAS Asset",
            "I4AAS Submodel",
            "I4AAS ConceptDescription",
            "I4AAS View",
            "I4AAS RelationshipElement",
            "I4AAS Property",
            "I4AAS MultiLanguageProperty",
            "I4AAS Range",
            "I4AAS Blob",
            "I4AAS File",
            "I4AAS ReferenceElement",
            "I4AAS Capability",
            "I4AAS SubmodelElementCollection",
            "I4AAS Operation",
            "I4AAS Event",
            "I4AAS Entity"
        ];

        /// <summary>
        /// The runtime binds callbacks by NodeId against the NodeSet the
        /// materializer produced, and the real builder throws BadNodeIdUnknown
        /// for a NodeId that is not in it - which aborts the entire projection,
        /// not just the one node. The two therefore have to agree exactly, and
        /// this pins that rather than any single member, because the mock
        /// builder used by the other tests answers every NodeId and so can
        /// never notice the disagreement.
        /// </summary>
        [Test]
        public async Task EveryBoundNodeIdExistsInTheMaterializedNodeSetAsync()
        {
            AasEnvironment environment = CreateEnvironment("one");
            HashSet<string> materialized = MaterializedIdentifiers(environment);

            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                environment,
                Mock.Of<IAasValueProvider>(),
                new DefaultAasOperationHandler()).ConfigureAwait(false);

            List<NodeId> bound = [.. callbacks.Reads.Keys
                .Concat(callbacks.Writes.Keys)
                .Concat(callbacks.Calls.Keys)
                .Distinct()];
            var missing = bound
                .Where(nodeId => !materialized.Contains(Identifier(nodeId)))
                .Select(nodeId => nodeId.ToString())
                .ToList();

            Assert.That(bound, Is.Not.Empty);
            Assert.That(missing, Is.Empty);
        }

        /// <summary>
        /// A File read from a document carries no content - the bytes live in
        /// the AASX package, not the XML - so File is absent on every element
        /// either reader produces. The projection has to come up regardless.
        /// </summary>
        [Test]
        public async Task ProjectionBindsAFileThatCarriesNoContentAsync()
        {
            var element = new AasFile
            {
                IdShort = "Manual",
                Category = "FILE",
                ModelingKind = AASModelingKindDataType.Instance,
                MimeType = "text/plain",
                Value = "/aasx/manual.txt"
            };

            AasEnvironment environment = EnvironmentWith("one", element);
            HashSet<string> materialized = MaterializedIdentifiers(environment);

            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                environment,
                Mock.Of<IAasValueProvider>(),
                new DefaultAasOperationHandler()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(element.File.IsPresent, Is.False);
                Assert.That(callbacks.Reads.Keys.Select(Identifier), Is.SubsetOf(materialized));
            });
        }

        private static HashSet<string> MaterializedIdentifiers(AasEnvironment environment)
        {
            return [.. (AasEnvironmentMaterializer
                .Materialize(environment).NodeSet.Items ?? [])
                .Select(item => item.NodeId)
                .Where(nodeId => !string.IsNullOrEmpty(nodeId))
                .Select(nodeId => StripNamespace(nodeId!))];
        }

        private static string Identifier(NodeId nodeId)
        {
            return StripNamespace(nodeId.ToString() ?? string.Empty);
        }

        private static string StripNamespace(string nodeId)
        {
            int separator = nodeId.IndexOf(';', StringComparison.Ordinal);
            return separator < 0 ? nodeId : nodeId[(separator + 1)..];
        }

        private static Task<RuntimeCallbacks> CreateCallbacksAsync(
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            return CreateCallbacksAsync(CreateEnvironment("one"), valueProvider, operationHandler);
        }

        private static async Task<RuntimeCallbacks> CreateCallbacksAsync(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            var callbacks = new RuntimeCallbacks();
            var builder = new Mock<INodeManagerBuilder>(MockBehavior.Strict);
            builder.Setup(b => b.Node(It.IsAny<NodeId>()))
                .Returns((NodeId nodeId) => callbacks.CreateNodeBuilder(nodeId));
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(Opc.Ua.Aas.V2.Namespaces.AasV2);
            builder.Setup(b => b.Context)
                .Returns(new SystemContext(telemetry: null!) { NamespaceUris = namespaces });
            var runtime = new AasV2EnvironmentRuntime(environment, valueProvider, operationHandler);
            await runtime.ConfigureAsync(builder.Object, CancellationToken.None).AsTask().ConfigureAwait(false);
            return callbacks;
        }

        private static AasV2EnvironmentNodeManager CreateNodeManager(
            IAasV2EnvironmentProvider environmentProvider,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            IAasEnvironmentProjectionHost projectionHost)
        {
            Mock<IServerInternal> server = AasServerTestHarness.CreateServer(ControlNamespaceUri);
            return new AasV2EnvironmentNodeManager(
                server.Object,
                null!,
                new AasServerOptions { ControlNamespaceUri = ControlNamespaceUri },
                environmentProvider,
                valueProvider,
                operationHandler,
                projectionHost);
        }

        private static string[] Names(ArrayOf<QualifiedName> units)
        {
            var names = new string[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                names[i] = units[i].Name ?? string.Empty;
            }
            return names;
        }

        private static AasEnvironment EnvironmentWith(string id, params AasSubmodelElement[] elements)
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(new[]
                {
                    new AasSubmodel
                    {
                        IdShort = "submodel",
                        Category = "CONSTANT",
                        Identification = Identifier(id),
                        Administration = Administration(),
                        ModelingKind = AASModelingKindDataType.Instance,
                        SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                            new ArrayOf<AasSubmodelElement>(elements))
                    }
                }))
            };
        }

        private static AasEnvironment CreateEnvironment(string id)
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(new[]
                {
                    new AasSubmodel
                    {
                        IdShort = "submodel",
                        Category = "CONSTANT",
                        Identification = Identifier(id),
                        Administration = Administration(),
                        ModelingKind = AASModelingKindDataType.Instance,
                        SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                            new ArrayOf<AasSubmodelElement>(new AasSubmodelElement[]
                            {
                                new AasProperty
                                {
                                    IdShort = "Temperature",
                                    Category = "VARIABLE",
                                    ModelingKind = AASModelingKindDataType.Instance,
                                    ValueType = AASValueTypeDataType.String,
                                    Value = AasOptional<Variant>.Present(Variant.From("42"))
                                },
                                new AasOperation
                                {
                                    IdShort = "Calibrate",
                                    Category = "OPERATION",
                                    ModelingKind = AASModelingKindDataType.Instance
                                },
                                new AasFile
                                {
                                    IdShort = "Manual",
                                    Category = "FILE",
                                    ModelingKind = AASModelingKindDataType.Instance,
                                    MimeType = "text/plain",
                                    Value = "manual.txt",
                                    File = AasOptional<AasFileObject>.Present(new AasFileObject
                                    {
                                        Value = AasOptional<ByteString>.Present(
                                            ByteString.From(Encoding.UTF8.GetBytes("manual")))
                                    })
                                }
                            }))
                    }
                }))
            };
        }

        private static AasIdentifier Identifier(string id)
        {
            return new AasIdentifier { Id = id, IdType = AASIdentifierTypeDataType.IRI };
        }

        private static AasAdministrativeInformation Administration()
        {
            return new AasAdministrativeInformation { Revision = "0", Version = "1" };
        }

        private static NodeId ElementNodeId(string ownerId, string idShortPath)
        {
            return new NodeId(AasNodeIdEncoding.CreateElementId(ownerId, idShortPath), 1);
        }

        private static NodeId MemberNodeId(NodeId parent, string browseName)
        {
            Assert.That(parent.TryGetValue(out string? identifier), Is.True);
            return new NodeId(identifier + "." + AasNodeIdEncoding.Escape(browseName), parent.NamespaceIndex);
        }

        private sealed class RuntimeCallbacks
        {
            public Dictionary<NodeId, GenericMethodCalledEventHandler2Async> Calls { get; } = [];

            public Dictionary<NodeId, NodeValueEventHandlerAsync> Reads { get; } = [];

            public Dictionary<NodeId, NodeValueWriteEventHandlerAsync> Writes { get; } = [];

            public INodeBuilder CreateNodeBuilder(NodeId nodeId)
            {
                var node = new Mock<INodeBuilder>(MockBehavior.Strict);
                node.Setup(n => n.OnCall(It.IsAny<GenericMethodCalledEventHandler2Async>()))
                    .Callback<GenericMethodCalledEventHandler2Async>(h => Calls[nodeId] = h)
                    .Returns(node.Object);
                node.Setup(n => n.OnRead(It.IsAny<NodeValueEventHandlerAsync>()))
                    .Callback<NodeValueEventHandlerAsync>(h => Reads[nodeId] = h)
                    .Returns(node.Object);
                node.Setup(n => n.OnWrite(It.IsAny<NodeValueWriteEventHandlerAsync>()))
                    .Callback<NodeValueWriteEventHandlerAsync>(h => Writes[nodeId] = h)
                    .Returns(node.Object);
                return node.Object;
            }
        }

        private sealed class RecordingProjectionHost : IAasEnvironmentProjectionHost
        {
            public List<AasEnvironment> V2Environments { get; } = [];

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                Opc.Ua.Aas.V3.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                V2Environments.Add(environment);
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V3.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V3.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask RemoveAsync(
                AasEnvironmentProjectionHandle handle,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            private static AasEnvironmentProjectionHandle CreateHandle()
            {
                return new AasEnvironmentProjectionHandle(new TestProjectionRegistration());
            }

            private sealed class TestProjectionRegistration : IAasProjectionRegistration
            {
                public Guid Id { get; } = Guid.NewGuid();
            }
        }

        private sealed class RecordingValueProvider : IAasValueProvider
        {
            public ValueTask<AasValueReadResult> ReadValueAsync(
                NodeId valueNodeId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasValueReadResult>(new AasValueReadResult(
                    ServiceResult.Good,
                    Variant.Null,
                    StatusCodes.Good,
                    DateTime.UtcNow));
            }

            public ValueTask<ServiceResult> WriteValueAsync(
                NodeId valueNodeId,
                Variant value,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }
    }
}
