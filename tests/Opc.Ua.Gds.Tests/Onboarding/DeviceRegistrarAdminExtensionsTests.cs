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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests for <see cref="DeviceRegistrarAdminExtensions.BindToTicketStore"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarAdminExtensionsTests
    {
        [Test]
        public void BindRejectsNodeMissingMethods()
        {
            var bare = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Bare", 2),
                BrowseName = new QualifiedName("Bare", 2)
            };

            Assert.Throws<ServiceResultException>(
                () => bare.BindToTicketStore(new MemoryTicketStore()));
        }

        [Test]
        public void BindRejectsNullArgs()
        {
            (_, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            Assert.Throws<ArgumentNullException>(
                () => registrar.BindToTicketStore(null!));
            BaseObjectState? nullState = null;
            Assert.Throws<ArgumentNullException>(
                () => nullState!.BindToTicketStore(new MemoryTicketStore()));
        }

        [Test]
        public async Task RegisterTicketsStoresTicketsAndReplacesSeededOutput()
        {
            (SystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store);
            ArrayOf<ByteString> tickets =
            [
                new ByteString(new byte[] { 1, 2, 3 }),
                new ByteString(new byte[] { 4, 5, 6 })
            ];

            (ServiceResult result, List<ServiceResult> argumentErrors, List<Variant> outputs) =
                await CallAsync(
                    registrar.RegisterTickets!,
                    context,
                    registrar.NodeId,
                    Variant.From(tickets))
                    .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(argumentErrors, Has.All.Matches<ServiceResult>(ServiceResult.IsGood));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(
                outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses),
                Is.True);
            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[]
                {
                    StatusCodes.Good,
                    StatusCodes.Good
                }.ToArrayOf()));

            var stored = new List<TicketRecord>();
            await foreach (TicketRecord ticket in store.ListAsync().ConfigureAwait(false))
            {
                stored.Add(ticket);
            }
            Assert.That(stored, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task UnregisterTicketsRemovesPreviouslyStored()
        {
            (SystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store);
            ArrayOf<ByteString> tickets =
            [
                new ByteString(new byte[] { 0xAA })
            ];

            _ = await CallAsync(
                registrar.RegisterTickets!,
                context,
                registrar.NodeId,
                Variant.From(tickets))
                .ConfigureAwait(false);
            (_, _, List<Variant> outputs) = await CallAsync(
                registrar.UnregisterTickets!,
                context,
                registrar.NodeId,
                Variant.From(tickets))
                .ConfigureAwait(false);

            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(
                outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses),
                Is.True);
            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
            Assert.That(
                await store.ListAsync().CountAsync().ConfigureAwait(false),
                Is.Zero);
        }

        [Test]
        public async Task RegisterTicketsReportsPerTicketStoreFailure()
        {
            (SystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            store
                .Setup(s => s.AddAsync(
                    It.IsAny<string>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<TicketMetadata>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadEntryExists));
            registrar.BindToTicketStore(store.Object);

            (_, _, List<Variant> outputs) = await CallAsync(
                registrar.RegisterTickets!,
                context,
                registrar.NodeId,
                Variant.From(
                    new ByteString[]
                    {
                        new(new byte[] { 1 })
                    }.ToArrayOf()))
                .ConfigureAwait(false);

            Assert.That(
                outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses),
                Is.True);
            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.BadEntryExists }.ToArrayOf()));
        }

        [Test]
        public async Task RegisterTicketsRejectsNonByteStringWireType()
        {
            (SystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            registrar.BindToTicketStore(new MemoryTicketStore());
            ArrayOf<string> invalidTickets = ["not-a-ticket"];

            (ServiceResult result, List<ServiceResult> argumentErrors, List<Variant> outputs) =
                await CallAsync(
                    registrar.RegisterTickets!,
                    context,
                    registrar.NodeId,
                    Variant.From(invalidTickets))
                    .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(argumentErrors, Has.Count.EqualTo(1));
            Assert.That(
                argumentErrors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public void RegisterTicketsPropagatesCancellationToTicketStore()
        {
            (_, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            store
                .Setup(s => s.AddAsync(
                    It.IsAny<string>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<TicketMetadata>(),
                    It.IsAny<CancellationToken>()))
                .Returns<string, byte[], TicketMetadata, CancellationToken>(
                    (_, _, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return default;
                    });
            registrar.BindToTicketStore(store.Object);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            List<Variant> outputs = [Variant.Null];

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await registrar.RegisterTickets!.OnCallMethod2Async!(
                    new SystemContext(NUnitTelemetryContext.Create()),
                    registrar.RegisterTickets,
                    registrar.NodeId,
                    [Variant.From(
                        new ByteString[]
                        {
                            new(new byte[] { 1 })
                        }.ToArrayOf())],
                    outputs,
                    cts.Token).ConfigureAwait(false));
        }

        private static (SystemContext Context, DeviceRegistrarAdminState Registrar)
            CreateRegistrar()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var typeTable = new TypeTable(messageContext.NamespaceUris);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.ByteString, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.DataTypeIds.EncodedTicket,
                Opc.Ua.DataTypeIds.ByteString);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = typeTable
            };
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaOnboarding(context);
            DeviceRegistrarState root = nodes.OfType<DeviceRegistrarState>().Single();
            DeviceRegistrarAdminState registrar = root.Administration ??
                throw new InvalidOperationException(
                    "The generated DeviceRegistrar has no Administration child.");
            return (context, registrar);
        }

        private static async ValueTask<(
            ServiceResult Result,
            List<ServiceResult> ArgumentErrors,
            List<Variant> Outputs)> CallAsync(
                MethodState method,
                ISystemContext context,
                NodeId objectId,
                Variant input)
        {
            var argumentErrors = new List<ServiceResult>();
            var outputs = new List<Variant>();
            ServiceResult result = await method.CallAsync(
                    context,
                    objectId,
                    [input],
                    argumentErrors,
                    outputs)
                .ConfigureAwait(false);
            return (result, argumentErrors, outputs);
        }
    }

    internal static class AsyncEnumerableTestExtensions
    {
        public static async ValueTask<int> CountAsync<T>(
            this IAsyncEnumerable<T> values)
        {
            int count = 0;
            await foreach (T _ in values.ConfigureAwait(false))
            {
                count++;
            }
            return count;
        }
    }
}
