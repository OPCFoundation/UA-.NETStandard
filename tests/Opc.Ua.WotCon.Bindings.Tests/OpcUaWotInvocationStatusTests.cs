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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Tests status propagation for OPC UA WoT action invocations.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotInvocationStatusTests
    {
        /// <summary>
        /// Verifies that explicit call receiver wins over unrelated local placement.
        /// </summary>
        [Test]
        public async Task ExplicitCallReceiverWinsOverUnrelatedLocalPlacement()
        {
            var namespaces = new NamespaceTable();
            ushort sourceNamespace = namespaces.GetIndexOrAppend("urn:source");
            namespaces.Append("urn:local");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", sourceNamespace)));
                    Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", sourceNamespace)));
                    Assert.That(requests[0].InputArguments.IsEmpty, Is.True);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            WotCompiledForm form = CreateForm(ImmutableDictionary<string, string>.Empty
                .Add("callObjectId", "nsu=urn:source;s=Owner")
                .Add("componentOf", "nsu=urn:local;s=Projection"));
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that component-of metadata alone cannot provide a Call receiver.
        /// </summary>
        [Test]
        public async Task ComponentOfAloneCannotProvideCallReceiver()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            WotCompiledForm form = CreateForm(
                ImmutableDictionary<string, string>.Empty.Add("componentOf", "nsu=urn:source;s=Owner"));
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Error, Does.Contain("uav:callObjectId"));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that a derived receiver whose Object does not own the Method is denied before Call.
        /// </summary>
        [Test]
        public async Task DerivedReceiverWithoutMethodOwnershipIsRejectedBeforeCall()
        {
            Mock<ISession> session = CreateSourceReceiverSession(out NamespaceTable namespaces);
            ushort sourceNamespace = (ushort)namespaces.GetIndex("urn:source");
            var browses = new List<BrowseDescription>();
            session.Setup(value => value.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.Is<ArrayOf<ReadValueId>>(nodes =>
                        nodes.Count == 2 &&
                        nodes[0].NodeId == new NodeId("Owner", 1) &&
                        nodes[0].AttributeId == Attributes.NodeClass &&
                        nodes[1].NodeId == new NodeId("Run", 1) &&
                        nodes[1].AttributeId == Attributes.NodeClass),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant((int)NodeClass.Object)),
                        new DataValue(new Variant((int)NodeClass.Method))
                    ]
                });
            session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        Assert.That(requests, Has.Count.EqualTo(1));
                        browses.Add(requests[0]);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { References = [] }]
                        });
                    });

            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadMethodInvalid));
                Assert.That(result.Outputs, Is.Empty);
                Assert.That(browses, Has.Count.EqualTo(2));
                Assert.That(browses[0].NodeId, Is.EqualTo(new NodeId("Owner", sourceNamespace)));
                Assert.That(browses[0].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                Assert.That(browses[0].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                Assert.That(browses[0].NodeClassMask, Is.EqualTo((uint)NodeClass.Method));
                Assert.That(browses[1].NodeId, Is.EqualTo(new NodeId("Owner", sourceNamespace)));
                Assert.That(browses[1].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasTypeDefinition));
                Assert.That(browses[1].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                Assert.That(browses[1].NodeClassMask, Is.EqualTo((uint)NodeClass.ObjectType));
            });
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that denied access to inspect the derived receiver is propagated before Call.
        /// </summary>
        [Test]
        public async Task DerivedReceiverAccessDeniedDuringPreflightPreventsCall()
        {
            Mock<ISession> session = CreateSourceReceiverSession(out _);
            session.Setup(value => value.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.Is<ArrayOf<ReadValueId>>(nodes =>
                        nodes.Count == 2 &&
                        nodes[0].NodeId == new NodeId("Owner", 1) &&
                        nodes[0].AttributeId == Attributes.NodeClass &&
                        nodes[1].NodeId == new NodeId("Run", 1) &&
                        nodes[1].AttributeId == Attributes.NodeClass),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied),
                        new DataValue(new Variant((int)NodeClass.Method))
                    ]
                });

            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(result.Outputs, Is.Empty);
                Assert.That(result.Error, Does.Contain("could not be inspected"));
            });
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that endpoint mismatch is rejected before inspecting or invoking a derived receiver.
        /// </summary>
        [Test]
        public async Task DerivedReceiverOnDifferentConfiguredEndpointIsRejectedBeforeCall()
        {
            Mock<ISession> session = CreateSourceReceiverSession(
                out _,
                configuredEndpoint: "opc.tcp://other-source:4840");
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(result.Outputs, Is.Empty);
                Assert.That(result.Error, Does.Contain("different endpoint"));
            });
            session.Verify(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that both the derived receiver and selected target must have their required NodeClass.
        /// </summary>
        [TestCase(NodeClass.Variable, NodeClass.Method)]
        [TestCase(NodeClass.Object, NodeClass.Object)]
        public async Task DerivedReceiverWithWrongNodeClassIsRejectedBeforeCall(
            NodeClass receiverClass,
            NodeClass methodClass)
        {
            Mock<ISession> session = CreateSourceReceiverSession(out _);
            session.Setup(value => value.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.Is<ArrayOf<ReadValueId>>(nodes =>
                        nodes.Count == 2 &&
                        nodes[0].NodeId == new NodeId("Owner", 1) &&
                        nodes[0].AttributeId == Attributes.NodeClass &&
                        nodes[1].NodeId == new NodeId("Run", 1) &&
                        nodes[1].AttributeId == Attributes.NodeClass),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant((int)receiverClass)),
                        new DataValue(new Variant((int)methodClass))
                    ]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeClassInvalid));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that direct and inherited ObjectType Method declarations authorize the source Object.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DerivedReceiverMayCallMethodOwnedByItsObjectTypeHierarchy(bool inherited)
        {
            Mock<ISession> session = CreateSourceReceiverSession(out NamespaceTable namespaces);
            ushort sourceNamespace = (ushort)namespaces.GetIndex("urn:source");
            var browses = new List<BrowseDescription>();
            session.Setup(value => value.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    It.Is<ArrayOf<ReadValueId>>(nodes =>
                        nodes.Count == 2 &&
                        nodes[0].NodeId == new NodeId("Owner", 1) &&
                        nodes[0].AttributeId == Attributes.NodeClass &&
                        nodes[1].NodeId == new NodeId("Run", 1) &&
                        nodes[1].AttributeId == Attributes.NodeClass),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant((int)NodeClass.Object)),
                        new DataValue(new Variant((int)NodeClass.Method))
                    ]
                });
            session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        Assert.That(requests, Has.Count.EqualTo(1));
                        BrowseDescription request = requests[0];
                        browses.Add(request);
                        NodeId typeOwner = new("OwnerType", sourceNamespace);
                        ArrayOf<ReferenceDescription> references =
                            request.NodeId == new NodeId("Owner", sourceNamespace) &&
                            request.ReferenceTypeId == Types.ReferenceTypeIds.HasTypeDefinition
                                ? [new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(
                                        "OwnerType", sourceNamespace)
                                }]
                                : request.NodeId == typeOwner &&
                                    request.ReferenceTypeId == Types.ReferenceTypeIds.HasComponent
                                    && !inherited
                                ? [new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(
                                        "Run", sourceNamespace)
                                }]
                                : request.NodeId == typeOwner &&
                                    request.ReferenceTypeId == Types.ReferenceTypeIds.HasSubtype &&
                                    request.BrowseDirection == BrowseDirection.Inverse &&
                                    inherited
                                ? [new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(
                                        "BaseType", sourceNamespace)
                                }]
                                : request.NodeId == new NodeId("BaseType", sourceNamespace) &&
                                    request.ReferenceTypeId == Types.ReferenceTypeIds.HasComponent &&
                                    inherited
                                ? [new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(
                                        "Run", sourceNamespace)
                                }]
                                : [];
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { References = references }]
                        });
                    });
            int callCount = 0;
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    callCount++;
                    Assert.That(requests, Has.Count.EqualTo(1));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", 1)));
                    Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", 1)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
                Assert.That(callCount, Is.EqualTo(1));
                Assert.That(browses, Has.Count.EqualTo(inherited ? 5 : 3));
                Assert.That(browses[0].NodeId, Is.EqualTo(new NodeId("Owner", 1)));
                Assert.That(browses[0].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                Assert.That(browses[1].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasTypeDefinition));
                Assert.That(browses[2].NodeId, Is.EqualTo(new NodeId("OwnerType", 1)));
                Assert.That(browses[2].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                Assert.That(browses[2].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                if (inherited)
                {
                    const int parentBrowse = 3;
                    Assert.That(browses[parentBrowse].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasSubtype));
                    Assert.That(browses[parentBrowse].BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
                    Assert.That(browses[parentBrowse].NodeClassMask, Is.EqualTo((uint)NodeClass.ObjectType));
                    Assert.That(browses[parentBrowse + 1].NodeId, Is.EqualTo(new NodeId("BaseType", 1)));
                    Assert.That(browses[parentBrowse + 1].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                }
            });
        }

        /// <summary>
        /// Verifies that source Object inference cannot invoke type-level Condition refresh.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InferredObjectCannotInvokeConditionRefreshBeforeCall(bool refresh2)
        {
            Mock<ISession> session = CreateSourceReceiverSession(out _);
            NodeId method = refresh2
                ? global::Opc.Ua.MethodIds.ConditionType_ConditionRefresh2
                : global::Opc.Ua.MethodIds.ConditionType_ConditionRefresh;
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Neither,
                    It.Is<ArrayOf<ReadValueId>>(nodes =>
                        nodes.Count == 2 &&
                        nodes[0].NodeId == new NodeId("Owner", 1) &&
                        nodes[1].NodeId == method),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant((int)NodeClass.Object)),
                        new DataValue(new Variant((int)NodeClass.Method))
                    ]
                });
            WotCompiledForm form = CreateForm(
                ImmutableDictionary<string, string>.Empty
                    .Add("callObjectId", "nsu=urn:source;s=Owner")
                    .Add("sourceCallReceiver", "object"),
                method.ToString());
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant((uint)1)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(result.Error, Does.Contain("ConditionType receiver"));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that successful ownership metadata from an obsolete session cannot admit Call.
        /// </summary>
        [Test]
        public async Task SessionIdentityChangedDuringOwnershipBrowsePreventsCall()
        {
            Mock<ISession> session = CreateSourceReceiverSession(out _);
            NodeId sessionId = new("source-session", 0);
            session.SetupGet(value => value.SessionId).Returns(() => sessionId);
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Neither,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(new Variant((int)NodeClass.Object)),
                        new DataValue(new Variant((int)NodeClass.Method))
                    ]
                });
            int browseCount = 0;
            session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        browseCount++;
                        Assert.That(requests[0].NodeId, Is.EqualTo(new NodeId("Owner", 1)));
                        Assert.That(requests[0].ReferenceTypeId, Is.EqualTo(Types.ReferenceTypeIds.HasComponent));
                        sessionId = new NodeId("replacement-session", 0);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new BrowseResult
                                {
                                    References = [new ReferenceDescription
                                    {
                                        NodeId = new ExpandedNodeId("Run", 1)
                                    }]
                                }
                            ]
                        });
                    });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, SourceCapturedForm(), new WotExecutorContext(),
                new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(browseCount, Is.EqualTo(1));
            Assert.That(sessionId, Is.EqualTo(new NodeId("replacement-session", 0)));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that an invalid Call receiver cannot fall back to component-of.
        /// </summary>
        [TestCase("")]
        [TestCase("not-a-node-id")]
        [TestCase("nsu=urn:absent;s=Owner")]
        [TestCase("ns=1;s=Owner")]
        [TestCase("svr=1;i=1")]
        [TestCase("i=0")]
        public async Task InvalidCallObjectIdCannotFallBackToComponentOf(string receiver)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                });
            WotCompiledForm form = CreateForm(ImmutableDictionary<string, string>.Empty
                .Add("callObjectId", receiver)
                .Add("componentOf", "nsu=urn:source;s=Owner"));
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, form, new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Error, Does.Contain("uav:callObjectId"));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that rejected invocation preserves every argument result and resolves source diagnostics.
        /// </summary>
        [Test]
        public async Task RejectedInvocationPreservesEveryArgumentResultAndResolvesSourceDiagnostics()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["urn:source:diagnostics", "SpeedRejected", "de", "Drehzahl zu hoch", "CallRejected"]
                    },
                    DiagnosticInfos = [new DiagnosticInfo { NamespaceUri = 0, SymbolicId = 4 }],
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                            InputArgumentDiagnosticInfos =
                            [
                                null!,
                                new DiagnosticInfo
                                {
                                    NamespaceUri = 0,
                                    SymbolicId = 1,
                                    Locale = 2,
                                    LocalizedText = 3,
                                    AdditionalInfo = "limit: 100",
                                    InnerStatusCode = StatusCodes.BadInvalidArgument,
                                    InnerDiagnosticInfo = new DiagnosticInfo { SymbolicId = 4 }
                                }
                            ]
                        }
                    ]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1), new Variant(200)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.InputArgumentResults.Count, Is.EqualTo(2));
            Assert.That(result.InputArgumentResults[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult rejected = result.InputArgumentResults[1];
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(rejected.NamespaceUri, Is.EqualTo("urn:source:diagnostics"));
            Assert.That(rejected.SymbolicId, Is.EqualTo("SpeedRejected"));
            Assert.That(rejected.LocalizedText, Is.EqualTo(new LocalizedText("de", "Drehzahl zu hoch")));
            Assert.That(rejected.AdditionalInfo, Is.EqualTo("limit: 100"));
            Assert.That(rejected.InnerResult?.SymbolicId, Is.EqualTo("CallRejected"));
            Assert.That(result.OperationResult.SymbolicId, Is.EqualTo("CallRejected"));
            Assert.That(result.OperationResult.NamespaceUri, Is.EqualTo("urn:source:diagnostics"));
            Assert.That(result.Outputs, Is.Empty);
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that malformed call details fail without retrying or returning partial results.
        /// </summary>
        [TestCase("short-inputs")]
        [TestCase("long-inputs")]
        [TestCase("short-diagnostics")]
        [TestCase("invalid-index")]
        [TestCase("negative-index")]
        [TestCase("invalid-operation-index")]
        [TestCase("successful-inputs")]
        public async Task MalformedCallDetailsFailWithoutRetryingOrReturningPartialResults(string malformed)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var method = new CallMethodResult
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange]
            };
            switch (malformed)
            {
                case "short-inputs":
                    method.InputArgumentResults = [StatusCodes.BadOutOfRange];
                    break;
                case "long-inputs":
                    method.InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange, StatusCodes.Good];
                    break;
                case "short-diagnostics":
                    method.InputArgumentDiagnosticInfos = [new DiagnosticInfo()];
                    break;
                case "invalid-index":
                    method.InputArgumentDiagnosticInfos = [null!, new DiagnosticInfo { SymbolicId = 2 }];
                    break;
                case "negative-index":
                    method.InputArgumentDiagnosticInfos = [null!, new DiagnosticInfo { LocalizedText = -2 }];
                    break;
                case "successful-inputs":
                    method.StatusCode = StatusCodes.Good;
                    break;
            }
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader { StringTable = ["available"] },
                    DiagnosticInfos = malformed == "invalid-operation-index"
                        ? [new DiagnosticInfo { NamespaceUri = 1 }] : [],
                    Results = [method]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1), new Variant(200)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.InputArgumentResults.IsEmpty, Is.True);
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that invocation diagnostic depth is bounded without discarding valid nested text.
        /// </summary>
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task InvocationDiagnosticDepthIsBoundedWithoutDiscardingValidNestedText(int depth)
        {
            var diagnostic = new DiagnosticInfo { SymbolicId = 0 };
            for (int i = 0; i < depth; i++)
            {
                diagnostic = new DiagnosticInfo { SymbolicId = 0, InnerDiagnosticInfo = diagnostic };
            }
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader { StringTable = ["ValidationContext"] },
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            InputArgumentResults = [StatusCodes.BadTypeMismatch],
                            InputArgumentDiagnosticInfos = [diagnostic]
                        }
                    ]
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(1)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(depth < 5
                ? StatusCodes.BadInvalidArgument : StatusCodes.BadDecodingError));
            Assert.That(result.Outputs, Is.Empty);
            if (depth < 5)
            {
                ServiceResult? nested = result.InputArgumentResults[0];
                for (int i = 0; i <= depth; i++)
                {
                    Assert.That(nested, Is.Not.Null);
                    Assert.That(nested!.SymbolicId, Is.EqualTo("ValidationContext"));
                    nested = nested.InnerResult;
                }
                Assert.That(nested, Is.Null);
            }
            else
            {
                Assert.That(result.InputArgumentResults.IsEmpty, Is.True);
            }
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that invocation retains the operation status and ordered arguments.
        /// </summary>
        /// <param name="status">The status code.</param>
        [TestCaseSource(nameof(s_statusCases))]
        public async Task InvocationRetainsTheOperationStatusAndOrderedArguments(StatusCode status)
        {
            var namespaces = new NamespaceTable();
            ushort sourceIndex = namespaces.GetIndexOrAppend("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", sourceIndex)));
                    Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", sourceIndex)));
                    Assert.That(requests[0].InputArguments.Count, Is.EqualTo(1));
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out int input) && input == 31, Is.True);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = status,
                                OutputArguments = [new Variant(32), new Variant("finished")]
                            }
                        ]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable channelOwner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([new Variant(31)]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(status));
            if (StatusCode.IsBad(status))
            {
                Assert.That(result.Outputs, Is.Empty);
            }
            else
            {
                Assert.That(result.Outputs, Has.Count.EqualTo(2));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out int number) && number == 32, Is.True);
                Assert.That(result.Outputs[1].WrappedValue.TryGetValue(out string text) && text == "finished",
                    Is.True);
            }
        }

        /// <summary>
        /// Verifies that contextual call forwards requested diagnostics without local permission.
        /// </summary>
        /// <param name="mask">The diagnostics masks.</param>
        [TestCase(DiagnosticsMasks.None)]
        [TestCase(DiagnosticsMasks.OperationAll)]
        [TestCase(DiagnosticsMasks.All | DiagnosticsMasks.UserPermissionAdditionalInfo)]
        public async Task ContextualCallForwardsRequestedDiagnosticsWithoutLocalPermission(DiagnosticsMasks mask)
        {
            var context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((header, _, _) =>
                {
                    Assert.That(header?.ReturnDiagnostics ?? 0, Is.EqualTo((uint)(mask & DiagnosticsMasks.All)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest([], context, mask))
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that contextual invocation translates node IDs and qualified names by URI.
        /// </summary>
        [Test]
        public async Task ContextualInvocationTranslatesNodeIdsAndQualifiedNamesByUri()
        {
            var caller = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            caller.NamespaceUris.Append("urn:local-only");
            ushort local = caller.NamespaceUris.GetIndexOrAppend("urn:source");
            var namespaces = new NamespaceTable();
            ushort remote = namespaces.GetIndexOrAppend("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.SetupGet(s => s.ServerUris).Returns(new StringTable());
            session.SetupGet(s => s.Factory).Returns(caller.Factory);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out NodeId node), Is.True);
                    Assert.That(node, Is.EqualTo(new NodeId("Input", remote)));
                    Assert.That(requests[0].InputArguments[1].TryGetValue(out QualifiedName name), Is.True);
                    Assert.That(name, Is.EqualTo(new QualifiedName("Mode", remote)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments =
                                [
                                    new Variant(new NodeId("Output", remote)),
                                    new Variant(new QualifiedName("Result", remote))
                                ]
                            }
                        ]
                    });
                });
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest(
                [new Variant(new NodeId("Input", local)), new Variant(new QualifiedName("Mode", local))], caller))
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Context, Is.Not.Null);
            Variant output = WotBindingValueMapper.Translate(result.Outputs[0].WrappedValue, result.Context!, caller);
            Variant qualified = WotBindingValueMapper.Translate(
                result.Outputs[1].WrappedValue, result.Context!, caller);
            Assert.That(output.TryGetValue(out NodeId outputId), Is.True);
            Assert.That(outputId, Is.EqualTo(new NodeId("Output", local)));
            Assert.That(qualified.TryGetValue(out QualifiedName outputName), Is.True);
            Assert.That(outputName, Is.EqualTo(new QualifiedName("Result", local)));
            Assert.That(namespaces.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that missing remote namespace fails before calling and does not invent a session index.
        /// </summary>
        [Test]
        public async Task MissingRemoteNamespaceFailsBeforeCallingAndDoesNotInventASessionIndex()
        {
            var caller = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort local = caller.NamespaceUris.GetIndexOrAppend("urn:absent-remotely");
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.SetupGet(s => s.ServerUris).Returns(new StringTable());
            session.SetupGet(s => s.Factory).Returns(caller.Factory);
            var channel = new OpcUaWotBindingChannel(
                session.Object, false, CreateForm(), new WotExecutorContext(), new OpcUaWotBindingOptions());
            await using ConfiguredAsyncDisposable owner = channel.ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync(new WotInvokeRequest(
                [new Variant(new NodeId("Unknown", local))], caller)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Outputs, Is.Empty);
            Assert.That(namespaces.Count, Is.EqualTo(2));
            session.Verify(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static WotCompiledForm CreateForm(
            ImmutableDictionary<string, string>? addressing = null,
            string methodId = "nsu=urn:source;s=Run")
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "1", "urn:test"), WotAffordanceKind.Action,
                "run", "/actions/run/forms/0", WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                new WotAddressingDescriptor(methodId,
                    addressing ??
                    ImmutableDictionary<string, string>.Empty.Add("callObjectId", "nsu=urn:source;s=Owner")),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/octet-stream", "binary"), [], true);
        }

        private static WotCompiledForm SourceCapturedForm()
        {
            return CreateForm(ImmutableDictionary<string, string>.Empty
                .Add("callObjectId", "nsu=urn:source;s=Owner")
                .Add("sourceCallReceiver", "object"));
        }

        private static Mock<ISession> CreateSourceReceiverSession(
            out NamespaceTable namespaces,
            string configuredEndpoint = "opc.tcp://source:4840")
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            namespaces = new NamespaceTable();
            namespaces.Append("urn:source");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
            session.SetupGet(value => value.ServerUris).Returns(new StringTable());
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.SetupGet(value => value.SessionId).Returns(new NodeId("source-session", 0));
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = configuredEndpoint,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                null));
            return session;
        }

        private static readonly TestCaseData[] s_statusCases =
        [
            new TestCaseData(StatusCodes.Good),
            new TestCaseData(StatusCodes.Uncertain),
            new TestCaseData(StatusCodes.BadUserAccessDenied)
        ];
    }
}
