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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Client;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Tests.Client
{
    /// <summary>
    /// Exercises the raw value write path and the browse plumbing every AAS client call depends on.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasClientValueAccessTests
    {
        /// <summary>
        /// The raw write overload has to resolve the element's Value Variable itself and write to
        /// that, not to the element: writing the element NodeId would target the wrong Node.
        /// </summary>
        [Test]
        public async Task WriteValueResolvesTheValueVariableBeforeWritingAsync()
        {
            NodeId elementNodeId = new("element", 2);
            NodeId valueNodeId = new("element.Value", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Index", 2), "Index", NodeClass.Variable),
                BrowseReference(valueNodeId, "Value", NodeClass.Variable));
            WriteValue? written = null;
            SetupWrite(session, values => written = values[0], StatusCodes.Good);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            StatusCode status = await client.WriteValueAsync(elementNodeId, new Variant(7)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(status), Is.True);
                Assert.That(written, Is.Not.Null);
                Assert.That(written!.NodeId, Is.EqualTo(valueNodeId));
                Assert.That(written.AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(written.Value.WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(7));
            });
        }

        /// <summary>
        /// A write that the Server rejects has to surface that Server status verbatim, so a caller
        /// can distinguish an access denial from a type mismatch.
        /// </summary>
        [Test]
        public async Task WriteValueSurfacesTheServerStatusAsync()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Value", 2), "Value", NodeClass.Variable));
            SetupWrite(session, _ => { }, StatusCodes.BadUserAccessDenied);
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            StatusCode status = await client.WriteValueAsync(elementNodeId, new Variant(7)).ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A Server that answers a single write with no result at all is protocol-violating, and
        /// silently reporting success would tell the caller the value was stored when it was not.
        /// </summary>
        [Test]
        public async Task WriteValueReportsAnUnexpectedErrorWhenTheServerReturnsNoResultAsync()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Value", 2), "Value", NodeClass.Variable));
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse { Results = ArrayOf<StatusCode>.Empty });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            StatusCode status = await client.WriteValueAsync(elementNodeId, new Variant(7)).ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// An element without a Value Variable is not writable; reporting BadNoMatch names the
        /// missing child rather than failing later with an unrelated Node error.
        /// </summary>
        [Test]
        public void WriteValueReportsBadNoMatchWhenTheElementHasNoValueChild()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Index", 2), "Index", NodeClass.Variable));
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.WriteValueAsync(elementNodeId, new Variant(7))
                    .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
                Assert.That(error.Message, Does.Contain("Value"));
            });
        }

        /// <summary>
        /// A null NodeId can never be browsed, so it is rejected as an argument rather than sent to
        /// the Server as a malformed Browse.
        /// </summary>
        [Test]
        public void BrowseRejectsANullNodeId()
        {
            AasClient client = new(CreateSessionMock().Object, 2, Mock.Of<ITelemetryContext>());

            Assert.That(
                () => client.BrowseSubmodelElementsAsync(NodeId.Null).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("nodeId"));
        }

        /// <summary>
        /// A Server that truncates the Browse must be followed with BrowseNext until the
        /// continuation point clears, otherwise the client would silently see a partial submodel.
        /// </summary>
        [Test]
        public async Task BrowseFollowsContinuationPointsUntilTheServerClearsThemAsync()
        {
            NodeId submodelNodeId = new("submodel", 2);
            NodeId firstNodeId = new("first", 2);
            NodeId secondNodeId = new("second", 2);
            ByteString continuationPoint = ByteString.From([1, 2, 3]);
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(b => b.Count == 1 && b[0].NodeId == submodelNodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = continuationPoint,
                            References = new[]
                            {
                                BrowseReference(firstNodeId, "first", NodeClass.Object)
                            }.ToArrayOf()
                        }
                    ]
                });
            bool released = true;
            session
                .Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.Is<ArrayOf<ByteString>>(c => c.Count == 1 && c[0].Equals(continuationPoint)),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, bool, ArrayOf<ByteString>, CancellationToken>(
                    (_, release, _, _) => released = release)
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = new[]
                            {
                                BrowseReference(secondNodeId, "second", NodeClass.Object)
                            }.ToArrayOf()
                        }
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ArrayOf<AasBrowseEntry> entries = await client.BrowseSubmodelElementsAsync(submodelNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries[0].NodeId, Is.EqualTo(firstNodeId));
                Assert.That(entries[1].NodeId, Is.EqualTo(secondNodeId));
                Assert.That(released, Is.False,
                    "Releasing the continuation point would discard the rest of the result set.");
            });
        }

        /// <summary>
        /// An element whose Index cannot be read must not disappear from the list; it sorts last so
        /// the remaining well-formed members keep their declared order.
        /// </summary>
        [Test]
        public async Task ListMembersWithoutAReadableIndexSortLastAsync()
        {
            NodeId listNodeId = new("list", 2);
            NodeId indexedNodeId = new("indexed", 2);
            NodeId unreadableNodeId = new("unreadable", 2);
            NodeId indexNodeId = new("indexed.Index", 2);
            NodeId badIndexNodeId = new("unreadable.Index", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                listNodeId,
                BrowseReference(unreadableNodeId, "0", NodeClass.Object),
                BrowseReference(indexedNodeId, "1", NodeClass.Object));
            SetupBrowse(session, indexedNodeId, BrowseReference(indexNodeId, "Index", NodeClass.Variable));
            SetupBrowse(session, unreadableNodeId, BrowseReference(badIndexNodeId, "Index", NodeClass.Variable));
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    RequestHeader _,
                    double _,
                    TimestampsToReturn _,
                    ArrayOf<ReadValueId> nodes,
                    CancellationToken _) => new ReadResponse
                    {
                        Results = nodes[0].NodeId == indexNodeId
                            ? [new DataValue(new Variant((uint)3))]
                            : [DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown)]
                    });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ArrayOf<AasBrowseEntry> entries = await client.BrowseListElementsAsync(listNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries[0].NodeId, Is.EqualTo(indexedNodeId));
                Assert.That(entries[1].NodeId, Is.EqualTo(unreadableNodeId));
            });
        }

        /// <summary>
        /// An element whose Index child is missing entirely takes the same last-place path, so a
        /// list is never silently truncated by a materialization gap.
        /// </summary>
        [Test]
        public async Task ListMembersWithoutAnIndexChildSortLastAsync()
        {
            NodeId listNodeId = new("list", 2);
            NodeId indexedNodeId = new("indexed", 2);
            NodeId noIndexNodeId = new("no-index", 2);
            NodeId indexNodeId = new("indexed.Index", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                listNodeId,
                BrowseReference(noIndexNodeId, "0", NodeClass.Object),
                BrowseReference(indexedNodeId, "1", NodeClass.Object));
            SetupBrowse(session, indexedNodeId, BrowseReference(indexNodeId, "Index", NodeClass.Variable));
            SetupBrowse(session, noIndexNodeId);
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { Results = [new DataValue(new Variant(2))] });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ArrayOf<AasBrowseEntry> entries = await client.BrowseListElementsAsync(listNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries[0].NodeId, Is.EqualTo(indexedNodeId));
                Assert.That(entries[1].NodeId, Is.EqualTo(noIndexNodeId));
            });
        }

        /// <summary>
        /// A bad Read on either the Value or its DataType has to fault: returning a default value
        /// would hand the caller a fabricated reading.
        /// </summary>
        [Test]
        public void ReadValueFaultsWhenTheServerRejectsTheRead()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Value", 2), "Value", NodeClass.Variable));
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
                        DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied),
                        new DataValue(new Variant(new NodeId(Opc.Ua.DataTypes.Int32)))
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValueAsync(elementNodeId).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A Value Variable whose DataType is outside the AAS xsd mapping cannot be canonicalized,
        /// and clause 6.3.1 fidelity means guessing a lexical form is not acceptable.
        /// </summary>
        [Test]
        public void ReadValueFaultsWhenTheDataTypeIsNotAnAasXsdType()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Value", 2), "Value", NodeClass.Variable));
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
                        new DataValue(new Variant(new NodeId(Opc.Ua.DataTypes.Argument)))
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadValueAsync(elementNodeId).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(error.Message, Does.Contain("xsd type mapping"));
            });
        }

        /// <summary>
        /// A lexical form that does not parse against the declared xsd type has to be rejected
        /// before any write reaches the Server.
        /// </summary>
        [Test]
        public void WriteLexicalValueRejectsALexicalFormThatDoesNotParse()
        {
            NodeId elementNodeId = new("element", 2);
            Mock<ISession> session = CreateSessionMock();
            SetupBrowse(
                session,
                elementNodeId,
                BrowseReference(new NodeId("element.Value", 2), "Value", NodeClass.Variable));
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
                        new DataValue(new Variant(new NodeId(Opc.Ua.DataTypes.Int32)))
                    ]
                });
            AasClient client = new(session.Object, 2, Mock.Of<ITelemetryContext>());

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.WriteLexicalValueAsync(elementNodeId, "not-an-int")
                    .ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            session.Verify(
                s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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

        private static void SetupWrite(
            Mock<ISession> session,
            Action<ArrayOf<WriteValue>> observe,
            StatusCode result)
        {
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<WriteValue>, CancellationToken>(
                    (_, values, _) => observe(values))
                .ReturnsAsync(new WriteResponse { Results = new[] { result }.ToArrayOf() });
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
    }
}
