/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;
using Opc.Ua.PackML;
using Opc.Ua.PackML.Client;
using Opc.Ua.PackML.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using ServerUnit = Opc.Ua.PackML.Server.PackMLUnit;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT regressions for PackML generated codecs, direct runtime commands,
    /// fluent hosting and typed-client factories over a borrowed session.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public sealed class PackMLAotTests(AotTestFixture fixture)
    {
        /// <summary>
        /// Decodes all six generated structures through the registered binary codecs,
        /// including nested product and remote-interface descriptor arrays.
        /// </summary>
        [Test]
        public async Task PackMLGeneratedStructuresRoundTripThroughRegisteredCodecsAsync()
        {
            var context = ServiceMessageContext.Create(fixture.Telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:packml:aot:padding");
            context.NamespaceUris.GetIndexOrAppend(PackML.Namespaces.PackML);
            context.Factory.Builder.AddOpcUaPackML().Commit();

            var descriptor = new PackMLDescriptorDataType { ID = 9, Name = "Fill", Value = 250.5f };
            PackMLDescriptorDataType decodedDescriptor = await RoundTripAsync(context, descriptor)
                .ConfigureAwait(false);
            await Assert.That(decodedDescriptor.Value).IsEqualTo(250.5f);
            var ingredient = new PackMLIngredientsDataType
            {
                IngredientID = 17,
                Parameter = [descriptor]
            };
            PackMLIngredientsDataType decodedIngredient = await RoundTripAsync(context, ingredient)
                .ConfigureAwait(false);
            await Assert.That(decodedIngredient.IngredientID).IsEqualTo(17);
            var product = new PackMLProductDataType
            {
                ProductID = 42,
                ProcessVariables = [descriptor],
                Ingredients = [ingredient]
            };
            PackMLProductDataType decodedProduct = await RoundTripAsync(context, product).ConfigureAwait(false);
            await Assert.That(decodedProduct.ProductID).IsEqualTo(42);
            await Assert.That(decodedProduct.ProcessVariables.Count).IsEqualTo(1);
            await Assert.That(decodedProduct.ProcessVariables[0].Name).IsEqualTo("Fill");
            await Assert.That(decodedProduct.Ingredients.Count).IsEqualTo(1);
            await Assert.That(decodedProduct.Ingredients[0].Parameter[0].Value).IsEqualTo(250.5f);
            PackMLRemoteInterfaceDataType decodedRemote = await RoundTripAsync(context,
                new PackMLRemoteInterfaceDataType
                {
                    Number = 4,
                    ControlCmdNumber = 7,
                    CmdValue = 2,
                    Parameter = [descriptor]
                }).ConfigureAwait(false);
            await Assert.That(decodedRemote.ControlCmdNumber).IsEqualTo(7);
            await Assert.That(decodedRemote.Parameter.Count).IsEqualTo(1);
            await Assert.That(decodedRemote.Parameter[0].ID).IsEqualTo(9);
            PackMLAlarmDataType decodedAlarm = await RoundTripAsync(context,
                new PackMLAlarmDataType { ID = 10, Message = "First out", Trigger = true }).ConfigureAwait(false);
            await Assert.That(decodedAlarm.Trigger).IsTrue();
            await Assert.That(decodedAlarm.Message).IsEqualTo("First out");
            PackMLCountDataType decodedCount = await RoundTripAsync(context,
                new PackMLCountDataType { ID = 20, Count = 5, AccCount = 100 }).ConfigureAwait(false);
            await Assert.That(decodedCount.Count).IsEqualTo(5);
            await Assert.That(decodedCount.AccCount).IsEqualTo(100);
        }

        /// <summary>
        /// Materializes a standalone unit and requires correlated equipment completion
        /// before leaving its acting state.
        /// </summary>
        [Test]
        public async Task PackMLDirectRuntimeRequiresCorrelatedEquipmentCompletionAsync()
        {
            var message = ServiceMessageContext.Create(fixture.Telemetry);
            ushort instanceNamespace = message.NamespaceUris.GetIndexOrAppend("urn:packml:aot:units");
            message.NamespaceUris.GetIndexOrAppend(PackML.Namespaces.PackML);
            message.Factory.Builder.AddOpcUaPackML().Commit();
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Numeric, instanceNamespace)
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaPackML(context);
            var equipment = new AotEquipmentProvider();
            await using ServerUnit unit = await ServerUnit.CreateAsync(
                context, null, new QualifiedName("AotUnit", instanceNamespace), equipment).ConfigureAwait(false);
            await Assert.That(unit.Node.Status).IsNotNull();
            await Assert.That(unit.Node.Admin).IsNotNull();
            await Assert.That(unit.CurrentState).IsEqualTo(PackMLStateNumbers.Stopped);
            ServiceResult accepted = await unit.RequestAsync(context, new PackMLRequest
            {
                Operation = PackMLOperation.Command,
                Command = PackMLCommand.Reset
            }).ConfigureAwait(false);
            await Assert.That(ServiceResult.IsGood(accepted)).IsTrue();
            long requestId = unit.PendingRequestId;
            PackMLRequest request = equipment.LastAccepted
                ?? throw new InvalidOperationException("The equipment provider did not accept Reset.");
            await Assert.That(requestId).IsGreaterThan(0);
            await Assert.That(request.RequestId).IsEqualTo(requestId);
            await Assert.That(request.Command).IsEqualTo(PackMLCommand.Reset);
            await Assert.That(unit.CurrentState).IsEqualTo(PackMLStateNumbers.Resetting);
            ServiceResult stale = await unit.CompleteActingStateAsync(
                requestId + 1, PackMLStateNumbers.Resetting).ConfigureAwait(false);
            await Assert.That(stale.StatusCode).IsEqualTo((StatusCode)StatusCodes.BadInvalidState);
            await Assert.That(unit.CurrentState).IsEqualTo(PackMLStateNumbers.Resetting);
            ServiceResult completed = await unit.CompleteActingStateAsync(
                requestId, PackMLStateNumbers.Resetting).ConfigureAwait(false);
            await Assert.That(ServiceResult.IsGood(completed)).IsTrue();
            await Assert.That(unit.CurrentState).IsEqualTo(PackMLStateNumbers.Idle);
            await Assert.That(unit.PendingRequestId).IsEqualTo(0L);
            await Assert.That(equipment.AcceptedCount).IsEqualTo(1);
            await Assert.That(unit.ServerProfiles.Count).IsEqualTo(0);
        }

        /// <summary>
        /// Resolves the public hosting registration and invokes the typed-client factory
        /// repeatedly without taking ownership of its existing managed session.
        /// </summary>
        [Test]
        public async Task PackMLFluentHostingAndClientFactoryBorrowTheManagedSessionAsync()
        {
            ManagedSession session = await fixture.CreateManagedSessionAsync("PackMLAotFactory")
                .ConfigureAwait(false);
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IPackMLEquipmentProvider>(new AotEquipmentProvider());
                services.AddOpcUa()
                    .AddServer(options => options.ApplicationName = "PackMLAotServer")
                    .AddPackML((provider, options) =>
                    {
                        options.NamespaceUri = "urn:packml:aot:hosting";
                        options.AddUnit("AotUnit", provider.GetRequiredService<IPackMLEquipmentProvider>());
                    });
                services.AddOpcUa().AddClient(options => options.ApplicationName = "PackMLAotClient")
                    .AddPackMLClient();
                int borrowed = 0;
                services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(_ => ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    borrowed++;
                    return Task.FromResult(session);
                });
                await using (ServiceProvider provider = services.BuildServiceProvider())
                {
                    OpcUaServerNodeManagerRegistration registration =
                        provider.GetRequiredService<OpcUaServerNodeManagerRegistration>();
                    var factory = registration.AsyncFactory
                        ?? throw new InvalidOperationException("PackML did not register an asynchronous factory.");
                    await Assert.That(factory.NamespacesUris.Count).IsEqualTo(2);
                    await Assert.That(factory.NamespacesUris[0]).IsEqualTo("urn:packml:aot:hosting");
                    await Assert.That(factory.NamespacesUris[1]).IsEqualTo(PackML.Namespaces.PackML);
                    Func<CancellationToken, Task<PackMLClient>> create =
                        provider.GetRequiredService<Func<CancellationToken, Task<PackMLClient>>>();
                    PackMLClient first = await create(CancellationToken.None).ConfigureAwait(false);
                    PackMLClient second = await create(CancellationToken.None).ConfigureAwait(false);
                    await Assert.That(first.Session).IsSameReferenceAs(session);
                    await Assert.That(second.Session).IsSameReferenceAs(session);
                    await Assert.That(borrowed).IsEqualTo(2);
                    await Assert.That(session.MessageContext.Factory.ContainsEncodeableType(
                        PackML.DataTypeIds.PackMLAlarmDataType)).IsTrue();
                }
                await Assert.That(session.Connected).IsTrue();
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                await Assert.That(StatusCode.IsGood(state.StatusCode)).IsTrue();
            }
            finally
            {
                try
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Encodes and decodes a generated structure through its registered type identity.
        /// </summary>
        private static async Task<T> RoundTripAsync<T>(ServiceMessageContext context, T value)
            where T : class, IEncodeable, new()
        {
            byte[] encoded = BinaryEncoder.EncodeMessage(value, context);
            T decoded = BinaryDecoder.DecodeMessage<T>(encoded, context);
            await Assert.That(encoded.Length).IsGreaterThan(0);
            await Assert.That(value.IsEqual(decoded)).IsTrue();
            return decoded;
        }

        /// <summary>
        /// Accepts commands without completing the simulated equipment operation.
        /// </summary>
        private sealed class AotEquipmentProvider : IPackMLEquipmentProvider
        {
            /// <summary>
            /// Gets the last accepted request, including its runtime-assigned identity.
            /// </summary>
            public PackMLRequest? LastAccepted { get; private set; }

            /// <summary>
            /// Gets the number of accepted equipment requests.
            /// </summary>
            public int AcceptedCount { get; private set; }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> AuthorizeAsync(
                ISystemContext context, PackMLRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> ExecuteAsync(
                PackMLRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastAccepted = request;
                AcceptedCount++;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<PackMLEquipmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<PackMLEquipmentSnapshot>(new PackMLEquipmentSnapshot
                {
                    CurrentMode = 1,
                    CurrentMachineSpeed = 12.5f,
                    SourceTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    StatusCode = StatusCodes.Good
                });
            }
        }
    }
}
