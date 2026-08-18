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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Client.V2;
using Opc.Ua.Aas.V2;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Tests.V2
{
    /// <summary>
    /// Tests the OPC 30270 AAS V2 client against a faked OPC UA session.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasClientTests
    {
        [Test]
        public void OpenMethodsResolveByIdentifierAndIdShortPath()
        {
            AasClient client = CreateClient();

            AASAssetAdministrationShellTypeClient shell = client.OpenShell("shell");
            AASAssetTypeClient asset = client.OpenAsset("asset");
            AASSubmodelTypeClient submodel = client.OpenSubmodel("submodel");
            AASSubmodelElementTypeClient element = client.OpenSubmodelElement("submodel", "items[0].name");

            Assert.Multiple(() =>
            {
                Assert.That(shell.ObjectId, Is.EqualTo(client.CreateShellNodeId("shell")));
                Assert.That(asset.ObjectId, Is.EqualTo(client.CreateAssetNodeId("asset")));
                Assert.That(submodel.ObjectId, Is.EqualTo(client.CreateSubmodelNodeId("submodel")));
                Assert.That(element.ObjectId, Is.EqualTo(client.CreateSubmodelElementNodeId("submodel", "items[0].name")));
                Assert.That(
                    client.CreateShellNodeId("shell").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateIdentifiableId(AasNodeKind.Shell, "shell")));
                Assert.That(
                    client.CreateSubmodelElementNodeId("submodel", "items[0].name").ToString(),
                    Is.EqualTo("ns=2;s=" + AasNodeIdEncoding.CreateElementId("submodel", "items[0].name")));
            });
        }

        [Test]
        public async Task ReadAndWriteValueUseDeclaredAasValueTypeAsync()
        {
            NodeId elementNodeId = new("element", 2);
            NodeId valueNodeId = new("element.Value", 2);
            NodeId valueTypeNodeId = new("element.ValueType", 2);
            NodeId browsedReferenceType = NodeId.Null;
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                descriptions => browsedReferenceType = descriptions[0].ReferenceTypeId,
                BrowseReference(valueNodeId, "Value", NodeClass.Variable),
                BrowseReference(valueTypeNodeId, "ValueType", NodeClass.Variable));
            SetupReadValue(session, valueNodeId, new Variant(123), valueTypeNodeId, AASValueTypeDataType.Int32);
            WriteValue? written = null;
            SetupWrite(session, values => written = values[0], StatusCodes.Good);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            AasValueReadResult read = await client.ReadValueAsync(elementNodeId).ConfigureAwait(false);
            StatusCode status = await client.WriteValueAsync(
                elementNodeId,
                AASValueTypeDataType.Int32,
                new Variant(456)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(read.ValueType, Is.EqualTo(AASValueTypeDataType.Int32));
                Assert.That(read.RawValue.TryGetValue(out int raw), Is.True);
                Assert.That(raw, Is.EqualTo(123));
                Assert.That(StatusCode.IsGood(status), Is.True);
                Assert.That(written, Is.Not.Null);
                Assert.That(written!.NodeId, Is.EqualTo(valueNodeId));
                Assert.That(written.Value.WrappedValue.TryGetValue(out int stored), Is.True);
                Assert.That(stored, Is.EqualTo(456));
                Assert.That(browsedReferenceType, Is.EqualTo(ReferenceTypeIds.Aggregates));
            });
        }

        [Test]
        public void ValueTypeMapAcceptsMatchingOpcUaValuesAndRejectsMismatches()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AasV2ValueTypeMap.IsCompatible(new Variant(1), AASValueTypeDataType.Int32), Is.True);
                Assert.That(AasV2ValueTypeMap.IsCompatible(new Variant("value"), AASValueTypeDataType.String), Is.True);
                Assert.That(AasV2ValueTypeMap.IsCompatible(new Variant(ByteString.From([1, 2])), AASValueTypeDataType.ByteString), Is.True);
                Assert.That(AasV2ValueTypeMap.IsCompatible(new Variant("not-an-int"), AASValueTypeDataType.Int32), Is.False);
            });
        }

        [Test]
        public async Task InvokeCallsTheEmbeddedOperationMethodAsync()
        {
            NodeId operationNodeId = new("operation", 2);
            NodeId methodNodeId = new("operation.Operation", 2);
            CallMethodRequest? request = null;
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(session, operationNodeId, null, BrowseReference(methodNodeId, "Operation", NodeClass.Method));
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
                            OutputArguments = ArrayOf<Variant>.Empty
                        }
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            AasOperationInvokeResult result = await client.InvokeAsync(operationNodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(StatusCode.IsGood(result.CallStatusCode), Is.True);
                Assert.That(request, Is.Not.Null);
                Assert.That(request!.ObjectId, Is.EqualTo(operationNodeId));
                Assert.That(request.MethodId, Is.EqualTo(methodNodeId));
                Assert.That(request.InputArguments, Is.Empty);
            });
        }

        [Test]
        public async Task FileAndBlobContentAreReadThroughEmbeddedFileTypeAsync()
        {
            NodeId fileElementNodeId = new("file", 2);
            NodeId blobElementNodeId = new("blob", 2);
            NodeId fileNodeId = new("file.File", 2);
            NodeId blobFileNodeId = new("blob.File", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(session, fileElementNodeId, null, BrowseReference(fileNodeId, "File", NodeClass.Object));
            SetupBrowse(session, blobElementNodeId, null, BrowseReference(blobFileNodeId, "File", NodeClass.Object));
            SetupBrowse(session, fileNodeId, null,
                BrowseReference(new NodeId("file.File.Open", 2), "Open", NodeClass.Method),
                BrowseReference(new NodeId("file.File.Read", 2), "Read", NodeClass.Method),
                BrowseReference(new NodeId("file.File.Close", 2), "Close", NodeClass.Method));
            SetupBrowse(session, blobFileNodeId, null,
                BrowseReference(new NodeId("blob.File.Open", 2), "Open", NodeClass.Method),
                BrowseReference(new NodeId("blob.File.Read", 2), "Read", NodeClass.Method),
                BrowseReference(new NodeId("blob.File.Close", 2), "Close", NodeClass.Method));
            SetupFileCalls(session);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ByteString file = await client.ReadFileContentAsync(fileElementNodeId, chunkSize: 2).ConfigureAwait(false);
            ByteString blob = await client.ReadFileContentAsync(blobElementNodeId, chunkSize: 2).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(file.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(blob.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
        }

        [Test]
        public void UnknownIdentifierSurfacesBrowseStatus()
        {
            NodeId elementNodeId = new("unknown", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowseStatus(session, elementNodeId, StatusCodes.BadNodeIdUnknown);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValueAsync(elementNodeId).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void BadIdShortPathReportsBadNoMatch()
        {
            NodeId elementNodeId = new("submodel.bad", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(session, elementNodeId, null, BrowseReference(new NodeId("submodel.bad.Value", 2), "Value", NodeClass.Variable));
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValueAsync(elementNodeId).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(error.Message, Does.Contain("ValueType"));
            });
        }

        [Test]
        public void ValueTypeMismatchIsRejectedBeforeWriting()
        {
            NodeId elementNodeId = new("element", 2);
            NodeId valueNodeId = new("element.Value", 2);
            NodeId valueTypeNodeId = new("element.ValueType", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                null,
                BrowseReference(valueNodeId, "Value", NodeClass.Variable),
                BrowseReference(valueTypeNodeId, "ValueType", NodeClass.Variable));
            SetupReadValue(session, valueNodeId, new Variant(1), valueTypeNodeId, AASValueTypeDataType.Int32);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.WriteValueAsync(elementNodeId, AASValueTypeDataType.String, new Variant("x"))
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                session.Verify(
                    s => s.WriteAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<WriteValue>>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            });
        }

        [Test]
        public void InvalidValueTypePropertyIsRejected()
        {
            NodeId elementNodeId = new("element", 2);
            NodeId valueNodeId = new("element.Value", 2);
            NodeId valueTypeNodeId = new("element.ValueType", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                null,
                BrowseReference(valueNodeId, "Value", NodeClass.Variable),
                BrowseReference(valueTypeNodeId, "ValueType", NodeClass.Variable));
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant(1)),
                        new DataValue(new Variant(999))
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValueAsync(elementNodeId).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void AddAasV2ClientRegistersOptionsAndFactories()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Aas:InstanceNamespaceUri"] = "urn:aas-v2-instances"
                })
                .Build();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = new TestOpcUaBuilder(services);

            builder.AddAasV2Client(configuration.GetSection("Aas"));

            using ServiceProvider provider = services.BuildServiceProvider();
            AasClientOptions options = provider.GetRequiredService<IOptions<AasClientOptions>>().Value;
            Func<ManagedSession, CancellationToken, Task<AasClient>> factory = provider
                .GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasClient>>>();
            Assert.Multiple(() =>
            {
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:aas-v2-instances"));
                Assert.That(factory, Is.Not.Null);
            });
        }

        private static AasClient CreateClient()
        {
            return new AasClient(CreateSessionMock().Object, 2, Mock.Of<ITelemetryContext>());
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V2.Namespaces.AasV2);
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
            Action<ArrayOf<BrowseDescription>>? capture,
            params ReferenceDescription[] references)
        {
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(b => b.Count == 1 && b[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, descriptions, _) => capture?.Invoke(descriptions))
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

        private static void SetupBrowseStatus(Mock<ISession> session, NodeId nodeId, StatusCode status)
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
                            StatusCode = status,
                            ContinuationPoint = default,
                            References = ArrayOf<ReferenceDescription>.Empty
                        }
                    ]
                });
        }

        private static void SetupReadValue(
            Mock<ISession> session,
            NodeId valueNodeId,
            Variant value,
            NodeId valueTypeNodeId,
            AASValueTypeDataType valueType)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r =>
                        r.Count == 2 && r[0].NodeId == valueNodeId && r[1].NodeId == valueTypeNodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(value),
                        new DataValue(new Variant((int)valueType))
                    ]
                });
        }

        private static void SetupWrite(
            Mock<ISession> session,
            Action<ArrayOf<WriteValue>> capture,
            StatusCode status)
        {
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<WriteValue>, CancellationToken>((_, values, _) => capture(values))
                .ReturnsAsync(new WriteResponse { Results = [status] });
        }

        private static void SetupFileCalls(Mock<ISession> session)
        {
            int nextHandle = 0;
            int readCalls = 0;
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    ArrayOf<Variant> arguments = requests[0].InputArguments;
                    if (arguments.Count == 1 && arguments[0].TryGetValue(out byte _))
                    {
                        return new CallResponse
                        {
                            Results =
                            [
                                new CallMethodResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    OutputArguments = [new Variant((uint)++nextHandle)]
                                }
                            ]
                        };
                    }

                    if (arguments.Count == 2)
                    {
                        ByteString chunk = readCalls % 2 == 0 ? ByteString.From([1, 2]) : ByteString.From([3]);
                        readCalls++;
                        return new CallResponse
                        {
                            Results =
                            [
                                new CallMethodResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    OutputArguments = [new Variant(chunk)]
                                }
                            ]
                        };
                    }

                    return new CallResponse
                    {
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    };
                });
        }

        private static ReferenceDescription BrowseReference(NodeId nodeId, string browseName, NodeClass nodeClass)
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
