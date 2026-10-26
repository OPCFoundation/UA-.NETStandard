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

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.DataChannels;
using Opc.Ua.Di;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;
using Opc.Ua.Fx.Data;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes the experimental server-side FX bindings after native compilation and trimming.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class FxPubSubServerAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Resolves the injectable binder and executes generated Method dispatch, typed results and Session boundaries.
        /// </summary>
        [Test]
        public async Task FxReservationHandlerAndDependencyInjectionAreAotSafeAsync()
        {
            using var reservations = new InMemoryPubSubCommunicationIdReservations(
                new PubSubDefaultPublisherIdPolicy(
                [
                    new PubSubDefaultPublisherId(DataChannelProfiles.PubSubUasc, new Variant((uint)23))
                ]));
            await using IPubSubApplication application = new PubSubApplicationBuilder(fixture.Telemetry)
                .WithCommunicationIdReservations(reservations)
                .UseConfiguration(new PubSubConfigurationDataType())
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton(application);
            services.AddSingleton(fixture.Telemetry);
            services.AddOpcUa().AddServer(_ => { })
                .AddPubSub(options => options.ExposeConfigurationMethods = true)
                .WithFxCommunicationIdReservations();
            await using ServiceProvider provider = services.BuildServiceProvider();
            PubSubFxReservationMethods binder = provider.GetRequiredService<PubSubFxReservationMethods>();
            await Assert.That(provider.GetRequiredService<PubSubFxReservationMethods>()).IsSameReferenceAs(binder);
            SessionSystemContext context = CreateFxContext(fixture.Telemetry);
            var component = new AutomationComponentState(null) { NodeId = new NodeId("fx-aot-component", 1) };
            var method = new EstablishConnectionsMethodState(component)
            {
                NodeId = new NodeId("fx-aot-establish", 1)
            };
            component.EstablishConnections = method;
            method.Create(context, method.NodeId, new QualifiedName("EstablishConnections"),
                new LocalizedText("EstablishConnections"), true);
            binder.Bind(component, _ => new ValueTask<string?>("opc.tcp://localhost:4840/fx-aot"));
            EstablishConnectionsMethodStateMethodAsyncCallHandler handler = method.OnCallAsync
                ?? throw new InvalidOperationException("The generated asynchronous handler was not bound.");
            ArrayOf<ReserveCommunicationIdsDataType> requests =
            [
                new PubSubReserveCommunicationIds2DataType
                {
                    TransportProfileUri = DataChannelProfiles.PubSubUasc,
                    NumReqWriterGroupIds = 1,
                    NumReqDataSetWriterIds = 2,
                    RequestTransportSpecificInfo = true
                }
            ];
            EstablishConnectionsMethodStateResult result = await handler(
                context, method, component.NodeId, (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                [], [], requests, [], CancellationToken.None).ConfigureAwait(false);
            await Assert.That(result.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(result.ReserveCommunicationIdsResults.Count).IsEqualTo(1);
            var reserved = result.ReserveCommunicationIdsResults[0] as PubSubReserveCommunicationIdsResult2DataType
                ?? throw new InvalidOperationException("The derived reservation result was not retained.");
            await Assert.That(reserved.Result).IsEqualTo(StatusCodes.Good);
            await Assert.That(reserved.DefaultPublisherId).IsEqualTo(new Variant((uint)23));
            await Assert.That(reserved.WriterGroupIds.Count).IsEqualTo(1);
            await Assert.That(reserved.DataSetWriterIds.Count).IsEqualTo(2);
            await Assert.That(reserved.WriterGroupIds[0] > 0).IsTrue();
            await Assert.That(reserved.DataSetWriterIds[0] != reserved.DataSetWriterIds[1]).IsTrue();
            await Assert.That(reserved.TransportSpecificInfo)
                .IsEqualTo(new Variant("opc.tcp://localhost:4840/fx-aot"));
            await Assert.That(result.AssetVerificationResults.IsEmpty).IsTrue();
            await Assert.That(result.ConnectionEndpointConfigurationResults.IsEmpty).IsTrue();
            await Assert.That(result.CommunicationConfigurationResults.IsEmpty).IsTrue();

            var outputs = new List<Variant>();
            var errors = new List<ServiceResult>();
            ServiceResult dispatched = await method.CallAsync(context, component.NodeId,
            [
                Variant.From((uint)FxCommandMask.ReserveCommunicationIdsCmd),
                Variant.FromStructure(ArrayOf.Empty<AssetVerificationDataType>()),
                Variant.FromStructure(ArrayOf.Empty<ConnectionEndpointConfigurationDataType>()),
                Variant.FromStructure(requests),
                Variant.FromStructure(ArrayOf.Empty<CommunicationConfigurationDataType>())
            ], errors, outputs, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(dispatched.StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(errors.Count).IsEqualTo(5);
            await Assert.That(errors.All(ServiceResult.IsGood)).IsTrue();
            await Assert.That(outputs.Count).IsEqualTo(4);
            await Assert.That(outputs[0].TryGetValue(
                out ArrayOf<AssetVerificationResultDataType> assets, context.AsMessageContext())).IsTrue();
            await Assert.That(assets.IsEmpty).IsTrue();
            await Assert.That(outputs[1].TryGetValue(
                out ArrayOf<ConnectionEndpointConfigurationResultDataType> endpoints,
                context.AsMessageContext())).IsTrue();
            await Assert.That(endpoints.IsEmpty).IsTrue();
            await Assert.That(outputs[3].TryGetValue(
                out ArrayOf<CommunicationConfigurationResultDataType> configurations,
                context.AsMessageContext())).IsTrue();
            await Assert.That(configurations.IsEmpty).IsTrue();
            bool decoded = outputs[2].TryGetValue(
                out ArrayOf<ReserveCommunicationIdsResultDataType> dispatchedResults, context.AsMessageContext());
            await Assert.That(decoded).IsTrue();
            await Assert.That(dispatchedResults.Count).IsEqualTo(1);
            var dispatchedReservation = dispatchedResults[0] as PubSubReserveCommunicationIdsResult2DataType
                ?? throw new InvalidOperationException("Method dispatch lost the derived reservation result.");
            await Assert.That(dispatchedReservation.Result).IsEqualTo(StatusCodes.Good);
            await Assert.That(dispatchedReservation.DefaultPublisherId).IsEqualTo(new Variant((uint)23));
            await Assert.That(dispatchedReservation.WriterGroupIds.Count).IsEqualTo(1);
            await Assert.That(dispatchedReservation.WriterGroupIds[0] != reserved.WriterGroupIds[0]).IsTrue();
            await Assert.That(dispatchedReservation.DataSetWriterIds.Count).IsEqualTo(2);
            await Assert.That(dispatchedReservation.DataSetWriterIds[0] != reserved.DataSetWriterIds[0]).IsTrue();
            await Assert.That(dispatchedReservation.TransportSpecificInfo)
                .IsEqualTo(new Variant("opc.tcp://localhost:4840/fx-aot"));

            reservations.CloseSession(context.SessionId ?? throw new InvalidOperationException("Session missing."));
            result = await handler(context, method, component.NodeId, (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                [], [], requests, [], CancellationToken.None).ConfigureAwait(false);
            await Assert.That(result.ServiceResult.StatusCode).IsEqualTo(StatusCodes.Uncertain);
            var closed = result.ReserveCommunicationIdsResults[0] as PubSubReserveCommunicationIdsResult2DataType
                ?? throw new InvalidOperationException("The closed Session result lost its declared type.");
            await Assert.That(closed.Result).IsEqualTo(StatusCodes.BadUserAccessDenied);
            context.UserIdentity = new UserIdentity();
            result = await handler(context, method, component.NodeId, (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                [], [], requests, [], CancellationToken.None).ConfigureAwait(false);
            await Assert.That(result.ServiceResult.StatusCode)
                .IsEqualTo(StatusCodes.BadUserAccessDenied);
            await Assert.That(result.ReserveCommunicationIdsResults.IsEmpty).IsTrue();
        }

        /// <summary>
        /// Reads generated endpoint Status and follows real DataSetReader state changes without reflection.
        /// </summary>
        [Test]
        public async Task FxEndpointStatusReadNotificationsAndDisposalAreAotSafeAsync()
        {
            await using IPubSubApplication application = new PubSubApplicationBuilder(fixture.Telemetry)
                .UseConfiguration(new PubSubConfigurationDataType()).Build();
            using var manager = new PubSubNodeManager(
                fixture.ServerFixture.Server.CurrentInstance, fixture.ServerFixture.Config,
                application, null, new PubSubServerOptions(), fixture.Telemetry);
            var context = new SystemContext(fixture.Telemetry);
            var endpoint = new PubSubConnectionEndpointState(null) { NodeId = new NodeId("fx-aot-endpoint", 1) };
            endpoint.Mode = BaseDataVariableState<PubSubConnectionEndpointModeEnum>
                .With<EnumerationBuilder<PubSubConnectionEndpointModeEnum>>(endpoint);
            endpoint.Mode.Value = PubSubConnectionEndpointModeEnum.Subscriber;
            endpoint.Status = BaseDataVariableState<ConnectionEndpointStatusEnum>
                .With<EnumerationBuilder<ConnectionEndpointStatusEnum>>(endpoint);
            endpoint.Status.AccessLevel = AccessLevels.CurrentRead;
            endpoint.Status.UserAccessLevel = AccessLevels.CurrentRead;
            endpoint.Status.DataType = DataTypeIds.Int32;
            endpoint.Status.ValueRank = ValueRanks.Scalar;
            var sink = new CountingSink();
            var reader = new DataSetReader(new DataSetReaderDataType { Name = "fx-aot-reader", DataSetWriterId = 1 },
                sink, fixture.Telemetry, TimeProvider.System);
            using IDisposable binding = manager.BindFxEndpointStatus(context, endpoint, reader);
            await Assert.That(ReadStatus(context, endpoint)).IsEqualTo(ConnectionEndpointStatusEnum.Ready);
            int notifications = 0;
            endpoint.Status.ClearChangeMasks(context, false);
            endpoint.Status.StateChanged += (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Value) != 0)
                {
                    notifications++;
                }
            };
            await Assert.That(reader.State.TryEnable()).IsTrue();
            await Assert.That(ReadStatus(context, endpoint)).IsEqualTo(ConnectionEndpointStatusEnum.PreOperational);
            await reader.DispatchAsync(new UadpDataSetMessage { DataSetWriterId = 1 }).ConfigureAwait(false);
            await Assert.That(ReadStatus(context, endpoint)).IsEqualTo(ConnectionEndpointStatusEnum.Operational);
            await Assert.That(reader.State.TryFault(StatusCodes.BadTimeout)).IsTrue();
            await Assert.That(ReadStatus(context, endpoint)).IsEqualTo(ConnectionEndpointStatusEnum.Error);
            await Assert.That(reader.State.StatusCode).IsEqualTo(StatusCodes.BadTimeout);
            await Assert.That(notifications).IsEqualTo(3);
            await Assert.That(sink.Writes).IsEqualTo(1);
            binding.Dispose();
            await Assert.That(endpoint.Status.OnSimpleReadValue is null).IsTrue();
            await Assert.That(reader.State.TryDisable()).IsTrue();
            await Assert.That(endpoint.Status.Value).IsEqualTo(ConnectionEndpointStatusEnum.Error);
        }

        /// <summary>
        /// Builds an isolated generated-model context so argument validation cannot depend on another test's model load.
        /// </summary>
        private static SessionSystemContext CreateFxContext(ITelemetryContext telemetry)
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:fx-server-aot:application");
            namespaces.GetIndexOrAppend(Fx.Cm.Namespaces.FxCm);
            namespaces.GetIndexOrAppend(Fx.Ac.Namespaces.FxAc);
            namespaces.GetIndexOrAppend(Fx.Data.Namespaces.FxData);
            namespaces.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            var types = new TypeTable(namespaces);
            var context = new SessionSystemContext(telemetry)
            {
                NamespaceUris = namespaces,
                TypeTable = types,
                EncodeableFactory = EncodeableFactory.Create(),
                SessionId = new NodeId("fx-aot-session", 1),
                UserIdentity = new RoleBasedIdentity(new UserIdentity(), [Role.ConfigureAdmin], namespaces)
            };
            context.EncodeableFactory.Builder.AddOpcUaDi().AddOpcUaFxData().AddOpcUaFxAc().AddOpcUaFxCm().Commit();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context).AddOpcUaFxData(context).AddOpcUaFxAc(context).AddOpcUaFxCm(context);
            BaseTypeState[] modelTypes = nodes.OfType<BaseTypeState>().ToArray();
            foreach (BaseTypeState type in modelTypes)
            {
                types.AddSubtype(type.NodeId, NodeId.Null);
            }
            foreach (NodeId supertype in modelTypes.Select(type => type.SuperTypeId).Distinct())
            {
                if (!supertype.IsNull && !types.IsKnown(supertype))
                {
                    types.AddSubtype(supertype, NodeId.Null);
                }
            }
            foreach (BaseTypeState type in modelTypes)
            {
                types.AddSubtype(type.NodeId, type.SuperTypeId);
            }
            return context;
        }

        /// <summary>
        /// Performs an ordinary Value-attribute read and validates its status and typed enumeration.
        /// </summary>
        private static ConnectionEndpointStatusEnum ReadStatus(
            ISystemContext context, PubSubConnectionEndpointState endpoint)
        {
            var value = new DataValue();
            ServiceResult result = endpoint.Status!.ReadAttribute(
                context, Attributes.Value, default, default, ref value);
            if (ServiceResult.IsBad(result) || StatusCode.IsBad(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out ConnectionEndpointStatusEnum status))
            {
                throw new InvalidOperationException("The endpoint Status read failed.");
            }
            return status;
        }

        /// <summary>
        /// Records actual receive dispatch without requiring a transport or reflection-based mock.
        /// </summary>
        private sealed class CountingSink : ISubscribedDataSetSink
        {
            /// <summary>
            /// Gets the number of applied DataSetMessages.
            /// </summary>
            public int Writes { get; private set; }

            /// <inheritdoc/>
            public ValueTask WriteAsync(IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Writes++;
                return default;
            }
        }
    }
}
