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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotPathTargetTests
    {
        [Test]
        public async Task AnOpenChannelResolvesAgainAfterItsSessionNamespaceTableChanges()
        {
            ServiceMessageContext context = Context();
            NamespaceTable namespaces = context.NamespaceUris;
            Mock<ISession> session = Session(context);
            session.SetupGet(value => value.NamespaceUris).Returns(() => namespaces);
            int translations = 0;
            int reads = 0;
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> requests, CancellationToken _) =>
                {
                    translations++;
                    ushort index = (ushort)namespaces.GetIndex("urn:path:source");
                    Assert.That(requests[0].RelativePath.Elements[0].TargetName,
                        Is.EqualTo(new QualifiedName("Value", index)));
                    Assert.That(requests[0].StartingNode, Is.EqualTo(new NodeId("Source", index)));
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        Translation(new NodeId("Value", index)));
                });
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    ushort index = (ushort)namespaces.GetIndex("urn:path:source");
                    Assert.That(requests[0].NodeId, Is.EqualTo(new NodeId("Value", index)));
                    if (requests[0].AttributeId == Attributes.NodeClass)
                    {
                        return new ValueTask<ReadResponse>(Read(new Variant((int)NodeClass.Variable)));
                    }
                    reads++;
                    return new ValueTask<ReadResponse>(Read(new Variant((int)index)));
                });
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "properties", "readproperty", "t:Value");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult first = await channel.ReadAsync().ConfigureAwait(false);
            namespaces = new NamespaceTable([Namespaces.OpcUa, "urn:path:source", "urn:different"]);
            WotReadResult second = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(first.Status, Is.EqualTo(StatusCodes.Good), first.Error);
            Assert.That(second.Status, Is.EqualTo(StatusCodes.Good), second.Error);
            Assert.That(first.Value.WrappedValue.TryGetValue(out int before), Is.True);
            Assert.That(second.Value.WrappedValue.TryGetValue(out int after), Is.True);
            Assert.That(before, Is.EqualTo(2));
            Assert.That(after, Is.EqualTo(1));
            Assert.That(translations, Is.EqualTo(2));
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(context.NamespaceUris.Count, Is.EqualTo(3));
            Assert.That(namespaces.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task PathWriteUsesTheValidatedTargetOnce()
        {
            Mock<ISession> session = Session(Context());
            NodeId target = new("ResolvedValue", 2);
            SetupTarget(session, target, NodeClass.Variable);
            int writes = 0;
            session.Setup(value => value.WriteAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<WriteValue> requests, CancellationToken _) =>
                {
                    writes++;
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].NodeId, Is.EqualTo(target));
                    Assert.That(requests[0].AttributeId, Is.EqualTo(Attributes.Value));
                    Assert.That(requests[0].Value.WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(42));
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.GoodClamped]
                    });
                });
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "properties", "writeproperty", "/Objects/t:Value");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(42))).ConfigureAwait(false);

            Assert.That(result.Status.Code, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(writes, Is.EqualTo(1));
        }

        [Test]
        public async Task PathCallKeepsItsReceiverSeparateFromTheResolvedMethod()
        {
            Mock<ISession> session = Session(Context());
            NodeId target = new("ResolvedMethod", 2);
            SetupTarget(session, target, NodeClass.Method);
            int calls = 0;
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    calls++;
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(requests[0].MethodId, Is.EqualTo(target));
                    Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Source", 2)));
                    Assert.That(requests[0].InputArguments.Count, Is.Zero);
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "actions", "invokeaction", "/Objects/t:Method");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good), result.Error);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellationStopsPathResolutionBeforeAnySourceRequest()
        {
            Mock<ISession> session = Session(Context());
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "properties", "readproperty", "/Objects/t:Value");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await channel.ReadAsync(new CancellationToken(canceled: true)).ConfigureAwait(false));

            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(value => value.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task NamespaceMutationDuringResolutionCannotReachTheBusinessRead()
        {
            ServiceMessageContext context = Context();
            Mock<ISession> session = Session(context);
            int valueReads = 0;
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> _, CancellationToken _) =>
                {
                    context.NamespaceUris.Append("urn:changed-during-translation");
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(Translation(new NodeId("Value", 2)));
                });
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    if (requests[0].AttributeId == Attributes.Value)
                    {
                        valueReads++;
                    }
                    return new ValueTask<ReadResponse>(Read(new Variant((int)NodeClass.Variable)));
                });
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, "properties", "readproperty", "/Objects/t:Value");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidState), result.Error);
            Assert.That(valueReads, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WrongObservationTargetClassFailsBeforeCreatingASubscription(bool events)
        {
            Mock<ISession> session = Session(Context());
            SetupTarget(session, new NodeId("WrongClass", 2), NodeClass.Method);
            WotProtocolBinderRegistry registry = Registry(session);
            WotBindingPlan plan = Plan(registry, events ? "events" : "properties",
                events ? "subscribeevent" : "observeproperty", "/Objects/t:Value");
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(plan.CompiledForms[0])
                .ConfigureAwait(false);

            ServiceResultException? error = events
                ? Assert.ThrowsAsync<ServiceResultException>(
                    async () => await channel.SubscribeEventAsync(_ => { }).ConfigureAwait(false))
                : Assert.ThrowsAsync<ServiceResultException>(
                    async () => await channel.ObserveAsync(_ => { }).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeClassInvalid));
            session.Verify(value => value.AddSubscription(It.IsAny<Subscription>()), Times.Never);
        }

        private static void SetupTarget(Mock<ISession> session, NodeId target, NodeClass nodeClass)
        {
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Translation(target));
            session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    Assert.That(requests[0].NodeId, Is.EqualTo(target));
                    Assert.That(requests[0].AttributeId, Is.EqualTo(Attributes.NodeClass));
                    return new ValueTask<ReadResponse>(Read(new Variant((int)nodeClass)));
                });
        }
    }
}
