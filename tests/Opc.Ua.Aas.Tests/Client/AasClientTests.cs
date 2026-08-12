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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Client;
using Opc.Ua.Aas.Client.Hosting;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Tests.Client
{
    [TestFixture]
    [Category("Aas")]
    public class AasClientTests
    {
        [Test]
        public void NodeIdDerivationMatchesEncodingForAllMetamodelKinds()
        {
            AasClient client = CreateClient();

            Assert.Multiple(() =>
            {
                Assert.That(
                    client.CreateShellNodeId("shell").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, "shell")));
                Assert.That(
                    client.CreateSubmodelNodeId("submodel").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Submodel, "submodel")));
                Assert.That(
                    client.CreateConceptDescriptionNodeId("concept").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateIdentifiableId(
                        AasNodeKind.ConceptDescription,
                        "concept")));
                Assert.That(
                    client.CreateSubmodelElementNodeId("submodel", "items[0].name").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateElementId("submodel", "items[0].name")));
            });
        }

        [Test]
        public void OpenMethodsResolveByIdentifierAndIdShortPath()
        {
            AasClient client = CreateClient();

            AASTypeClient shell = client.OpenShell("shell");
            AASSubmodelTypeClient submodel = client.OpenSubmodel("submodel");
            AASConceptDescriptionTypeClient concept = client.OpenConceptDescription("concept");
            AASSubmodelElementTypeClient element = client.OpenSubmodelElement("submodel", "temperature");

            Assert.Multiple(() =>
            {
                Assert.That(shell.ObjectId, Is.EqualTo(client.CreateShellNodeId("shell")));
                Assert.That(submodel.ObjectId, Is.EqualTo(client.CreateSubmodelNodeId("submodel")));
                Assert.That(concept.ObjectId, Is.EqualTo(client.CreateConceptDescriptionNodeId("concept")));
                Assert.That(element.ObjectId, Is.EqualTo(client.CreateSubmodelElementNodeId("submodel", "temperature")));
            });
        }

        [Test]
        public async Task ReadAndWriteValueUseDeclaredXsdType()
        {
            NodeId elementNodeId = new("element", 2);
            NodeId valueNodeId = new("element.Value", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(session, elementNodeId, BrowseReference(valueNodeId, "Value", NodeClass.Variable));
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 2 && r[0].NodeId == valueNodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant(123)),
                        new DataValue(new Variant(new NodeId(Opc.Ua.DataTypes.Int32)))
                    ]
                });
            WriteValue? written = null;
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<WriteValue>, CancellationToken>((_, values, _) => written = values[0])
                .ReturnsAsync(new WriteResponse { Results = [StatusCodes.Good] });

            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            AasValueReadResult read = await client.ReadValueAsync(elementNodeId);
            StatusCode status = await client.WriteLexicalValueAsync(elementNodeId, "456");

            Assert.Multiple(() =>
            {
                Assert.That(read.ValueType, Is.EqualTo(AASDataTypeDefXsdDataType.Int));
                Assert.That(read.LexicalValue, Is.EqualTo("123"));
                Assert.That(StatusCode.IsGood(status), Is.True);
                Assert.That(written, Is.Not.Null);
                Assert.That(written!.Value.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(456));
            });
        }

        [Test]
        public async Task InvokeMarshalsArguments()
        {
            NodeId operationNodeId = new("operation", 2);
            ArrayOf<Variant> inputs = new[] { new Variant(1) }.ToArrayOf();
            ArrayOf<Variant> inoutputs = new[] { new Variant("a") }.ToArrayOf();
            CallMethodRequest? request = null;
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) => request = requests[0])
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant(ArrayOf<Variant>.Empty),
                                new Variant(ArrayOf<Variant>.Empty),
                                new Variant(true),
                                new Variant(string.Empty)
                            ]
                        }
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            AasOperationInvokeResult result = await client.InvokeAsync(operationNodeId, inputs, inoutputs, 12);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.CallStatusCode), Is.True);
                Assert.That(result.Success, Is.True);
                Assert.That(request, Is.Not.Null);
                Assert.That(request!.ObjectId, Is.EqualTo(operationNodeId));
                Assert.That(request.InputArguments, Has.Count.EqualTo(3));
                Assert.That(request.InputArguments[2].TryGetValue(out double timeout), Is.True);
                Assert.That(timeout, Is.EqualTo(12));
            });
        }

        [Test]
        public async Task InvokeKeepsSuccessFalseDistinctFromBadStatusCode()
        {
            NodeId operationNodeId = new("operation", 2);
            Mock<ISession> goodCallSession = CreateSessionMock();
            goodCallSession
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant(ArrayOf<Variant>.Empty),
                                new Variant(ArrayOf<Variant>.Empty),
                                new Variant(false),
                                new Variant("rejected")
                            ]
                        }
                    ]
                });
            Mock<ISession> badCallSession = CreateSessionMock();
            badCallSession
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = StatusCodes.BadInvalidArgument }]
                });

            AasOperationInvokeResult operationFailure = await new AasClient(
                goodCallSession.Object,
                2,
                Mock.Of<ITelemetryContext>()).InvokeAsync(
                    operationNodeId,
                    ArrayOf<Variant>.Empty,
                    ArrayOf<Variant>.Empty,
                    0);
            AasOperationInvokeResult callFailure = await new AasClient(
                badCallSession.Object,
                2,
                Mock.Of<ITelemetryContext>()).InvokeAsync(
                    operationNodeId,
                    ArrayOf<Variant>.Empty,
                    ArrayOf<Variant>.Empty,
                    0);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(operationFailure.CallStatusCode), Is.True);
                Assert.That(operationFailure.Success, Is.False);
                Assert.That(operationFailure.Diagnostic, Is.EqualTo("rejected"));
                Assert.That(StatusCode.IsBad(callFailure.CallStatusCode), Is.True);
                Assert.That(callFailure.Success, Is.False);
            });
        }

        [Test]
        public async Task ListMembersAreOrderedByIndexPropertyNotBrowseOrder()
        {
            NodeId listNodeId = new("list", 2);
            NodeId firstNodeId = new("first", 2);
            NodeId secondNodeId = new("second", 2);
            NodeId firstIndexNodeId = new("first.Index", 2);
            NodeId secondIndexNodeId = new("second.Index", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                listNodeId,
                BrowseReference(secondNodeId, "1", NodeClass.Object),
                BrowseReference(firstNodeId, "0", NodeClass.Object));
            SetupBrowse(session, firstNodeId, BrowseReference(firstIndexNodeId, "Index", NodeClass.Variable));
            SetupBrowse(session, secondNodeId, BrowseReference(secondIndexNodeId, "Index", NodeClass.Variable));
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> nodes, CancellationToken _) =>
                    new ReadResponse
                    {
                        Results =
                        [
                            new DataValue(new Variant(nodes[0].NodeId == firstIndexNodeId ? 0 : 1))
                        ]
                    });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ArrayOf<AasBrowseEntry> entries = await client.BrowseListElementsAsync(listNodeId);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries[0].NodeId, Is.EqualTo(firstNodeId));
                Assert.That(entries[1].NodeId, Is.EqualTo(secondNodeId));
            });
        }

        [Test]
        public void AddAasClientActionOverloadRegistersOptions()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestOpcUaBuilder(services);

            builder.AddAasV3Client(options => options.LazyConnect = false);

            using ServiceProvider provider = services.BuildServiceProvider();
            AasClientOptions options = provider.GetRequiredService<IOptions<AasClientOptions>>().Value;
            Assert.That(options.LazyConnect, Is.False);
        }

        [Test]
        public void AddAasClientConfigurationOverloadRegistersOptions()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Aas:Client:InstanceNamespaceUri"] = "urn:aas"
                })
                .Build();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestOpcUaBuilder(services);

            builder.AddAasV3Client(configuration);

            using ServiceProvider provider = services.BuildServiceProvider();
            AasClientOptions options = provider.GetRequiredService<IOptions<AasClientOptions>>().Value;
            Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:aas"));
        }

        [Test]
        public void AddAasClientSectionOverloadRegistersOptions()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aas:LazyConnect"] = "false"
                })
                .Build();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestOpcUaBuilder(services);

            builder.AddAasV3Client(configuration.GetSection("Aas"));

            using ServiceProvider provider = services.BuildServiceProvider();
            AasClientOptions options = provider.GetRequiredService<IOptions<AasClientOptions>>().Value;
            Assert.That(options.LazyConnect, Is.False);
        }

        private static AasClient CreateClient()
        {
            return new AasClient(CreateSessionMock().Object, 2, Mock.Of<ITelemetryContext>());
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            namespaceUris.GetIndexOrAppend("urn:instances");
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(Mock.Of<ITelemetryContext>());
            messageContext.NamespaceUris = namespaceUris;
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.Setup(s => s.Dispose());
            return session;
        }

        private static void SetupBrowse(
            Mock<ISession> session,
            NodeId nodeId,
            params ReferenceDescription[] references)
        {
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(b => b.Count == 1 && b[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = references.ToArrayOf()
                        }
                    ]
                });
        }

        private static ReferenceDescription BrowseReference(
            NodeId nodeId,
            string browseName,
            NodeClass nodeClass)
        {
            return new ReferenceDescription
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, 1),
                DisplayName = new LocalizedText(browseName),
                NodeClass = nodeClass
            };
        }

        private sealed class TestOpcUaBuilder : IOpcUaBuilder
        {
            public TestOpcUaBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
