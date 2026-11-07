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
using System.IO;
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
                () => bare.BindToTicketStore(new MemoryTicketStore(), AcceptTickets()));
        }

        [Test]
        public void BindRejectsNullArgs()
        {
            (_, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            Assert.Throws<ArgumentNullException>(
                () => registrar.BindToTicketStore(null!, AcceptTickets()));
            BaseObjectState? nullState = null;
            Assert.Throws<ArgumentNullException>(
                () => nullState!.BindToTicketStore(new MemoryTicketStore(), AcceptTickets()));
        }

        [Test]
        public async Task RegisterTicketsStoresTicketsAndReplacesSeededOutput()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store, AcceptTickets());
            ArrayOf<string> tickets =
            [
                "first-ticket",
                "second-ticket"
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
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store, AcceptTickets());
            ArrayOf<string> tickets =
            [
                "ticket"
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

        /// <summary>
        /// OPC 10000-21 §9.2.11: RegisterTickets shall be called from a Session
        /// that has access to the RegistrarAdmin Role.
        /// </summary>
        [Test]
        public async Task RegisterTicketsRequiresRegistrarAdminRole()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store, AcceptTickets());
            ArrayOf<string> tickets = ["ticket"];
            byte[] password = "password"u8.ToArray();

            var userContext = new SessionSystemContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                TypeTable = context.TypeTable,
                UserIdentity = new UserIdentity("user", password)
            };
            (ServiceResult denied, _, _) = await CallAsync(
                registrar.RegisterTickets!,
                userContext,
                registrar.NodeId,
                Variant.From(tickets)).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await store.ListAsync().CountAsync().ConfigureAwait(false), Is.Zero);

            var adminContext = new SessionSystemContext(context.Telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                TypeTable = context.TypeTable,
                UserIdentity = new Opc.Ua.Server.RoleBasedIdentity(
                    new UserIdentity("admin", password),
                    [new Opc.Ua.Server.Role(Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, "RegistrarAdmin")],
                    context.NamespaceUris)
            };
            (ServiceResult allowed, _, _) = await CallAsync(
                registrar.RegisterTickets!,
                adminContext,
                registrar.NodeId,
                Variant.From(tickets)).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(allowed), Is.True);
            Assert.That(await store.ListAsync().CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }

        [Test]
        public async Task RegisterTicketsReportsPerTicketStoreFailure()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            store
                .Setup(s => s.AddAsync(
                    It.IsAny<string>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<TicketMetadata>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadEntryExists));
            registrar.BindToTicketStore(store.Object, AcceptTickets());
            ArrayOf<string> tickets = ["ticket"];

            (_, _, List<Variant> outputs) = await CallAsync(
                registrar.RegisterTickets!,
                context,
                registrar.NodeId,
                Variant.From(tickets))
                .ConfigureAwait(false);

            Assert.That(
                outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses),
                Is.True);
            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.BadEntryExists }.ToArrayOf()));
        }

        /// <summary>
        /// A failed middle storage operation preserves earlier results and does not skip later tickets.
        /// </summary>
        [TestCase(false, "io")]
        [TestCase(true, "io")]
        [TestCase(false, "checkpoint")]
        [TestCase(true, "checkpoint")]
        [TestCase(false, "service")]
        [TestCase(true, "service")]
        [TestCase(false, "uncancelled")]
        [TestCase(true, "uncancelled")]
        public async Task TicketMethodsReportStorageFailureAndContinueBatch(bool remove, string failureKind)
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            Exception failure = failureKind switch
            {
                "io" => new IOException("Storage unavailable."),
                "checkpoint" => new InvalidDataException("Checkpoint limit exceeded."),
                "service" => new ServiceResultException(StatusCodes.BadEntryExists),
                _ => new OperationCanceledException("Provider cancelled without caller cancellation.")
            };
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            if (remove)
            {
                store.SetupSequence(value => value.RemoveAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true))
                    .Throws(failure)
                    .Returns(new ValueTask<bool>(true));
            }
            else
            {
                store.SetupSequence(value => value.AddAsync(
                    It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TicketMetadata>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask))
                    .Throws(failure)
                    .Returns(default(ValueTask));
            }
            registrar.BindToTicketStore(store.Object, AcceptTickets());
            MethodState method = remove ? registrar.UnregisterTickets! : registrar.RegisterTickets!;
            ArrayOf<string> tickets = ["before", "failed", "after"];

            (ServiceResult result, _, List<Variant> outputs) = await CallAsync(
                method, context, registrar.NodeId, Variant.From(tickets)).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            StatusCode expectedFailure = failureKind == "service"
                ? StatusCodes.BadEntryExists
                : StatusCodes.BadUnexpectedError;
            Assert.That(statuses, Is.EqualTo(
                new StatusCode[] { StatusCodes.Good, expectedFailure, StatusCodes.Good }.ToArrayOf()));
            if (remove)
            {
                store.Verify(value => value.RemoveAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            }
            else
            {
                store.Verify(value => value.AddAsync(
                    It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TicketMetadata>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(3));
            }
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Caller cancellation raised during storage stops the batch instead of becoming a ticket status.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TicketMethodsPropagateCallerCancellationDuringStorage(bool remove)
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            using var cancellation = new CancellationTokenSource();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            int calls = 0;
            if (remove)
            {
                store.Setup(value => value.RemoveAsync(It.IsAny<string>(), cancellation.Token))
                    .Returns<string, CancellationToken>((_, ct) =>
                    {
                        if (++calls == 2)
                        {
                            cancellation.Cancel();
                            ct.ThrowIfCancellationRequested();
                        }
                        return new ValueTask<bool>(true);
                    });
            }
            else
            {
                store.Setup(value => value.AddAsync(
                    It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TicketMetadata>(), cancellation.Token))
                    .Returns<string, byte[], TicketMetadata, CancellationToken>((_, _, _, ct) =>
                    {
                        if (++calls == 2)
                        {
                            cancellation.Cancel();
                            ct.ThrowIfCancellationRequested();
                        }
                        return default;
                    });
            }
            registrar.BindToTicketStore(store.Object, AcceptTickets());
            MethodState method = remove ? registrar.UnregisterTickets! : registrar.RegisterTickets!;
            List<Variant> outputs = [Variant.Null];
            ArrayOf<string> tickets = ["before", "cancelled", "after"];

            OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await method.OnCallMethod2Async!(
                    context, method, registrar.NodeId, [Variant.From(tickets)], outputs, cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(calls, Is.EqualTo(2), "The remaining ticket must not reach storage after cancellation.");
            Assert.That(outputs[0].IsNull, Is.True);
        }

        [Test]
        public async Task RegisterTicketsRejectsNonStringWireType()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            registrar.BindToTicketStore(new MemoryTicketStore(), AcceptTickets());
            ArrayOf<ByteString> invalidTickets = [ByteString.From("not-a-ticket"u8)];

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

        [TestCase(false)]
        [TestCase(true)]
        public async Task TicketMethodsRejectExtraArgumentsBeforeCallingProviders(bool remove)
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            registrar.BindToTicketStore(store.Object, validator.Object);
            MethodState method = remove ? registrar.UnregisterTickets! : registrar.RegisterTickets!;
            List<Variant> outputs = [Variant.Null];

            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, registrar.NodeId,
                [Variant.From(ArrayOf<string>.Empty), Variant.Null], outputs, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTooManyArguments));
            Assert.That(outputs[0].IsNull, Is.True);
            store.VerifyNoOtherCalls();
            validator.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegisterTicketsPropagatesCancellationToTicketStore()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
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
            registrar.BindToTicketStore(store.Object, AcceptTickets());
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            List<Variant> outputs = [Variant.Null];
            ArrayOf<string> tickets = ["ticket"];

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await registrar.RegisterTickets!.OnCallMethod2Async!(
                    context,
                    registrar.RegisterTickets,
                    registrar.NodeId,
                    [Variant.From(tickets)],
                    outputs,
                    cts.Token).ConfigureAwait(false));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TicketMethodsRejectMissingSessionIdentity(bool remove)
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            registrar.BindToTicketStore(store.Object, AcceptTickets());
            context.UserIdentity = null;
            MethodState method = remove ? registrar.UnregisterTickets! : registrar.RegisterTickets!;
            ArrayOf<string> tickets = ["ticket"];

            (ServiceResult denied, _, _) = await CallAsync(
                method, context, registrar.NodeId, Variant.From(tickets))
                .ConfigureAwait(false);

            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Legacy callers cannot register unverified tickets.
        /// </summary>
        [Test]
        public async Task LegacyBindingRejectsRegistrationWithoutValidator()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new Mock<ITicketStore>(MockBehavior.Strict);
            var method = typeof(DeviceRegistrarAdminExtensions).GetMethod(
                nameof(DeviceRegistrarAdminExtensions.BindToTicketStore), s_legacyBindingSignature) ??
                throw new AssertionException("The legacy unvalidated binding overload is missing.");
#if NETFRAMEWORK
            var bind = (Action<BaseObjectState, ITicketStore>)method.CreateDelegate(
                typeof(Action<BaseObjectState, ITicketStore>));
#else
            Action<BaseObjectState, ITicketStore> bind = method.CreateDelegate<Action<BaseObjectState, ITicketStore>>();
#endif
            bind(registrar, store.Object);
            ArrayOf<string> tickets = ["ticket"];

            (ServiceResult result, _, _) = await CallAsync(
                registrar.RegisterTickets!, context, registrar.NodeId,
                Variant.From(tickets)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid tickets do not prevent independently valid inputs from being stored.
        /// </summary>
        [Test]
        public async Task RegisterTicketsRejectsInvalidTicketAndStoresValidatedMetadata()
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            var validator = Mock.Get(AcceptTickets());
            validator.Setup(value => value.ValidateAsync(
                "invalid", OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadTicketInvalid));
            registrar.BindToTicketStore(store, validator.Object);
            ArrayOf<string> tickets = ["before", "invalid", "after"];

            (ServiceResult result, _, List<Variant> outputs) = await CallAsync(
                registrar.RegisterTickets!, context, registrar.NodeId,
                Variant.From(tickets)).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            Assert.That(statuses, Is.EqualTo(
                new StatusCode[] { StatusCodes.Good, StatusCodes.BadTicketInvalid, StatusCodes.Good }.ToArrayOf()));
            var serials = new List<string>();
            await foreach (TicketRecord record in store.ListAsync().ConfigureAwait(false))
            {
                Assert.That(record.Metadata.Kind, Is.EqualTo(TicketKind.DeviceIdentity));
                Assert.That(record.Metadata.ManufacturerName, Is.EqualTo("Test manufacturer"));
                serials.Add(record.Metadata.SerialNumber);
            }
            string[] expectedSerials = ["before", "after"];
            Assert.That(serials, Is.EquivalentTo(expectedSerials));
        }

        /// <summary>
        /// A null input is reported independently without blocking the remaining tickets.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TicketMethodsRejectNullElementsAndContinueProcessing(bool remove)
        {
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) = CreateRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store, AcceptTickets());
            if (remove)
            {
                ArrayOf<string> registeredTickets = ["before", "after"];
                _ = await CallAsync(
                    registrar.RegisterTickets!, context, registrar.NodeId,
                    Variant.From(registeredTickets)).ConfigureAwait(false);
            }
            MethodState method = remove ? registrar.UnregisterTickets! : registrar.RegisterTickets!;
            ArrayOf<string> tickets = ["before", null!, "after"];

            (ServiceResult result, _, List<Variant> outputs) = await CallAsync(
                method, context, registrar.NodeId, Variant.From(tickets)).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            Assert.That(statuses, Is.EqualTo(
                new StatusCode[] { StatusCodes.Good, StatusCodes.BadTicketInvalid, StatusCodes.Good }.ToArrayOf()));
            Assert.That(
                await store.ListAsync().CountAsync().ConfigureAwait(false),
                Is.EqualTo(remove ? 0 : 2));
        }

        /// <summary>
        /// Supplies a test verifier; envelope and trust behavior have separate tests.
        /// </summary>
        private static IOnboardingTicketValidator AcceptTickets()
        {
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            validator.Setup(value => value.ValidateAsync(
                It.IsAny<string>(), OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                .Returns<string, OnboardingTicketSource, CancellationToken>((ticket, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<ValidatedOnboardingTicket>(new ValidatedOnboardingTicket(
                        new DeviceIdentityTicketType
                        {
                            ManufacturerName = "Test manufacturer",
                            ModelName = "Test model",
                            SerialNumber = ticket,
                            ProductInstanceUri = "urn:test:" + ticket
                        }, string.Empty));
                });
            return validator.Object;
        }

        private static (SessionSystemContext Context, DeviceRegistrarAdminState Registrar)
            CreateRegistrar()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var typeTable = new TypeTable(messageContext.NamespaceUris);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.String, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.DataTypeIds.EncodedTicket,
                Opc.Ua.DataTypeIds.String);
            var context = new SessionSystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = typeTable,
                UserIdentity = new Opc.Ua.Server.RoleBasedIdentity(
                    new UserIdentity(),
                    [new Opc.Ua.Server.Role(
                        Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, "RegistrarAdmin")],
                    messageContext.NamespaceUris)
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

        /// <summary>The legacy binding intentionally omits the required ticket validator.</summary>
        private static readonly Type[] s_legacyBindingSignature = [typeof(BaseObjectState), typeof(ITicketStore)];
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
