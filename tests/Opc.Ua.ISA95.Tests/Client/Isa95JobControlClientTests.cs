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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.ISA95.Client;
using Opc.Ua.ISA95.Server.Providers;
using MonitoringOptions =
    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Client
{
    [TestFixture]
    public sealed class Isa95JobControlClientTests
    {
        [Test]
        public void ConstructorsValidateRequiredArguments()
        {
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            ISession session = new Mock<ISession>().Object;

            Assert.That(
                () => new Isa95JobControlV1Client(null!, new NodeId(1), new NodeId(2), new NodeId(3), telemetry),
                Throws.ArgumentNullException);
            Assert.That(
                () => new Isa95JobControlV1Client(session, NodeId.Null, new NodeId(2), new NodeId(3), telemetry),
                Throws.ArgumentException);
            Assert.That(
                () => new Isa95JobControlV2Client(session, new NodeId(1), new NodeId(2), NodeId.Null, telemetry),
                Throws.ArgumentException);
            Assert.That(() => new Isa95Client(session, null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task V1AndV2MethodsInvokeGeneratedProxyMethodsAsync()
        {
            var calls = new List<uint>();
            ISession session = CreateCallSession(calls);
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            var v1 = new Isa95JobControlV1Client(session, new NodeId(1), new NodeId(2), new NodeId(3), telemetry);
            var v2 = new Isa95JobControlV2Client(session, new NodeId(4), new NodeId(5), new NodeId(6), telemetry);

            await v1.ReceiveJobOrderAsync(
                V1.ISA95JobOrderCommandEnum.Store,
                new V1.ISA95JobOrderDataType { ID = "job1" }).ConfigureAwait(false);
            await v1.RequestJobResponseAsync(
                "job1",
                V1.ISA95JobOrderStateEnum.Undefined).ConfigureAwait(false);
            await v1.ReceiveJobResponseAsync(
                new V1.ISA95JobResponseDataType
                {
                    ID = "response1",
                    JobOrderID = "job1"
                }).ConfigureAwait(false);
            await v2.AbortAsync("job1").ConfigureAwait(false);
            await v2.RequestJobResponseByJobOrderIdAsync("job1").ConfigureAwait(false);
            await v2.ReceiveJobResponseAsync(
                new V2.ISA95JobResponseDataType
                {
                    JobResponseID = "response2",
                    JobOrderID = "job1"
                }).ConfigureAwait(false);

            Assert.That(
                calls,
                Is.EquivalentTo(
                [
                    V1.Methods.ISA95JobOrderReceiverObjectType_ReceiveJobOrder,
                    V1.Methods.ISA95JobResponseProviderObjectType_RequestJobResponse,
                    V1.Methods.ISA95JobResponseReceiverObjectType_ReceiveJobResponse,
                    V2.Methods.ISA95JobOrderReceiverObjectType_Abort,
                    V2.Methods.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID,
                    V2.Methods.ISA95JobResponseReceiverObjectType_ReceiveJobResponse
                ]));
        }

        [Test]
        public void DirectJobControlClientRegistersAllIsa95Encodeables()
        {
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            var messageContext =
                ServiceMessageContext.Create(telemetry);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext).Returns(messageContext);

            _ = new Isa95JobControlV1Client(
                session.Object,
                new NodeId(1),
                new NodeId(2),
                new NodeId(3),
                telemetry);

            Assert.That(
                messageContext.Factory.ContainsEncodeableType(
                    DataTypeIds.ISA95TestResultDataType),
                Is.True);
            Assert.That(
                messageContext.Factory.ContainsEncodeableType(
                    V1.DataTypeIds.ISA95JobOrderDataType),
                Is.True);
            Assert.That(
                messageContext.Factory.ContainsEncodeableType(
                    V2.DataTypeIds.ISA95JobOrderDataType),
                Is.True);
        }

        [Test]
        public void AddIsa95ClientRegistersFactory()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddIsa95Client(options => options.LazyConnect = false);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IIsa95ClientFactory factory =
                serviceProvider.GetRequiredService<IIsa95ClientFactory>();
            ITelemetryContext telemetry =
                serviceProvider.GetRequiredService<ITelemetryContext>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext).Returns(
                ServiceMessageContext.Create(telemetry));

            Isa95Client client = factory.Create(session.Object);

            Assert.That(client.Session, Is.SameAs(session.Object));
        }

        [Test]
        public async Task LazyFactoryHonorsDisabledConnectionAsync()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa().AddIsa95Client(options => options.LazyConnect = false);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IIsa95ClientFactory factory =
                serviceProvider.GetRequiredService<IIsa95ClientFactory>();

            bool threw = false;
            try
            {
                await factory.ConnectAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.That(threw, Is.True);
        }

        [Test]
        public void StatusEventSubscriptionValidatesArguments()
        {
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            var sessionMock = new Mock<ISession>();
            sessionMock.SetupGet(s => s.MessageContext).Returns(
                ServiceMessageContext.Create(telemetry));
            ISession session = sessionMock.Object;
            var client = new Isa95JobControlV2Client(
                session,
                new NodeId(1),
                new NodeId(2),
                new NodeId(3),
                telemetry);
            IStreamingSubscription streaming = new EmptyStreamingSubscription();

            Assert.That(
                () => client.SubscribeJobOrderStatusEventsAsync(
                    null!,
                    Ua.ObjectIds.Server),
                Throws.ArgumentNullException);
            Assert.That(
                () => client.SubscribeJobOrderStatusEventsAsync(streaming, NodeId.Null),
                Throws.ArgumentException);
        }

        [Test]
        public void GeneratedStatusEventDecoderReturnsTypedPayload()
        {
            QualifiedName[][] standardFields =
                V2.ISA95JobOrderStatusEventTypeRecord.Decoder.StandardFields;
            var fields = new Variant[standardFields.Length];
            fields[FindFieldIndex(standardFields, V2.BrowseNames.JobOrder)] =
                Variant.FromStructure(
                new V2.ISA95JobOrderDataType { JobOrderID = "job1" });
            fields[FindFieldIndex(standardFields, V2.BrowseNames.JobResponse)] =
                Variant.FromStructure(
                new V2.ISA95JobResponseDataType
                {
                    JobResponseID = "response1",
                    JobOrderID = "job1"
                });
            fields[FindFieldIndex(standardFields, V2.BrowseNames.JobState)] =
                Variant.FromStructure(
                new[]
                {
                    new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateNumber = 3,
                        StateText = new LocalizedText("Running")
                    }
                }.ToArrayOf());

            V2.ISA95JobOrderStatusEventTypeRecord? record =
                V2.ISA95JobOrderStatusEventTypeRecord.Decoder.Decode(fields);

            Assert.That(record, Is.Not.Null);
            Assert.That(record!.JobOrder.JobOrderID, Is.EqualTo("job1"));
            Assert.That(record.JobResponse.JobResponseID, Is.EqualTo("response1"));
            Assert.That(record.JobState, Has.Length.EqualTo(1));
            Assert.That(record.JobState[0].StateNumber, Is.EqualTo(3));
        }

        [Test]
        public void GeneratedMethodFactoriesIncludeArgumentMetadata()
        {
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            context.NamespaceUris.GetIndexOrAppend(V2.Namespaces.ISA95JobControlV2);

            V2.StoreMethodState store =
                V2.OpcUaISA95JobControlV2Extensions.CreateInstanceOfStoreMethodType(
                    context);

            Assert.That(store.InputArguments, Is.Not.Null);
            Assert.That(store.InputArguments!.Value, Has.Count.EqualTo(2));
            Assert.That(store.InputArguments.Value[0].Name, Is.EqualTo("JobOrder"));
            Assert.That(store.InputArguments.Value[1].Name, Is.EqualTo("Comment"));
            Assert.That(store.OutputArguments, Is.Not.Null);
            Assert.That(store.OutputArguments!.Value, Has.Count.EqualTo(1));
            Assert.That(
                store.OutputArguments.Value[0].Name,
                Is.EqualTo("ReturnStatus"));
        }

        private static int FindFieldIndex(QualifiedName[][] fields, string browseName)
        {
            int index = Array.FindIndex(
                fields,
                path => path.Length > 0 &&
                    string.Equals(path[^1].Name, browseName, StringComparison.Ordinal));
            Assert.That(index, Is.GreaterThanOrEqualTo(0), browseName);
            return index;
        }

        private static ISession CreateCallSession(List<uint> calls)
        {
            var telemetry = new Mock<ITelemetryContext>();
            var messageContext = ServiceMessageContext.Create(telemetry.Object);
            messageContext.NamespaceUris.GetIndexOrAppend(V1.Namespaces.ISA95JobControlV1);
            messageContext.NamespaceUris.GetIndexOrAppend(V2.Namespaces.ISA95JobControlV2);

            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                {
                    Assert.That(requests, Has.Count.EqualTo(1));
                    Assert.That(requests[0].MethodId.TryGetValue(out uint methodId), Is.True);
                    Assert.That(requests[0].ObjectId.TryGetValue(out uint objectId), Is.True);
                    calls.Add(methodId);
                    ArrayOf<Variant> outputs = (objectId, methodId) switch
                    {
                        (2, V1.Methods.ISA95JobResponseProviderObjectType_RequestJobResponse) =>
                        [
                            Variant.FromStructure(ArrayOf.Empty<V1.ISA95JobResponseDataType>()),
                            Variant.From(Isa95JobReturnStatus.Success)
                        ],
                        (5, V2.Methods.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID) =>
                        [
                            Variant.FromStructure(new V2.ISA95JobResponseDataType()),
                            Variant.From(Isa95JobReturnStatus.Success)
                        ],
                        _ => [Variant.From(Isa95JobReturnStatus.Success)]
                    };
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = outputs
                            }
                        ],
                        DiagnosticInfos = default
                    });
                });
            return session.Object;
        }

        private sealed class EmptyStreamingSubscription : IStreamingSubscription
        {
            public ValueTask DisposeAsync()
            {
                return default;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId,
                EventFilter filter,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }
        }
    }
}
