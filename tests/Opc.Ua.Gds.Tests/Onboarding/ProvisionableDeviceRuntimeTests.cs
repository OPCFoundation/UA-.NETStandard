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
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 ========================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises device-specific tickets, authorized registrar persistence, and generated method binding.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvisionableDeviceRuntimeTests
    {
        /// <summary>
        /// Direct construction requires all behavior providers.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ConstructorRejectsEachMissingProvider(int missing)
        {
            Assert.Throws<ArgumentNullException>(() => new ProvisionableDeviceRuntime(
                false, [],
                missing == 0 ? null! : Mock.Of<ITicketStore>(),
                missing == 1 ? null! : Mock.Of<IOnboardingTicketValidator>(),
                missing == 2 ? null! : Mock.Of<IProvisionableDeviceRegistrarStore>()));
        }

        /// <summary>
        /// Invalid configured identifiers cannot widen selection or cause repeated ticket disclosure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRejectsEmptyOrRepeatedTicketIdentifiers(bool duplicate)
        {
            ArrayOf<string> ids = duplicate ? ["device", "device"] : [" "];
            Assert.Throws<ArgumentException>(() => new ProvisionableDeviceRuntime(
                false, ids, Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(),
                Mock.Of<IProvisionableDeviceRegistrarStore>()));
        }

        /// <summary>
        /// RequestTickets does not expose other devices' entries or rewrite the selected envelopes.
        /// </summary>
        [Test]
        public async Task RequestTicketsUsesOnlyDetachedExplicitIdentifiersAndPreservesSignedStrings()
        {
            string[] identifiers = ["first", "second"];
            var tickets = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            for (int i = 0; i < identifiers.Length; i++)
            {
                string id = identifiers[i];
                string envelope = "signed-envelope-" + id;
                tickets.Setup(t => t.GetAsync(id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Record(id, Encoding.UTF8.GetBytes(envelope)));
                validator.Setup(v => v.ValidateAsync(
                    envelope, OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ValidatedOnboardingTicket(new DeviceIdentityTicketType(), string.Empty));
            }
            var runtime = new ProvisionableDeviceRuntime(
                true, identifiers, tickets.Object, validator.Object,
                Mock.Of<IProvisionableDeviceRegistrarStore>());
            identifiers[0] = "another-devices-ticket";

            ArrayOf<string> result = await runtime.RequestTicketsAsync().ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(s_expectedSignedEnvelopeIds));
            Assert.That(runtime.IsSingleton, Is.True);
            tickets.VerifyAll();
            tickets.Verify(t => t.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
            validator.VerifyAll();
        }

        /// <summary>
        /// Each configured ticket must exist, decode without replacement, and pass the supplied validation policy.
        /// </summary>
        [TestCase("missing")]
        [TestCase("utf8")]
        [TestCase("signature")]
        public async Task RequestTicketsRejectsMissingCorruptOrUntrustedConfiguredTicket(string failure)
        {
            StatusCode status = failure switch
            {
                "missing" => StatusCodes.BadConfigurationError,
                "utf8" or "signature" => StatusCodes.BadTicketInvalid,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            var tickets = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            tickets.Setup(t => t.GetAsync("device", It.IsAny<CancellationToken>()))
                .ReturnsAsync(failure == "missing" ? null : Record(
                    "device", failure == "utf8" ? [0xc3, 0x28] : Encoding.UTF8.GetBytes("invalid-signature")));
            if (failure == "signature")
            {
                validator.Setup(v => v.ValidateAsync(
                    "invalid-signature", OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ServiceResultException(StatusCodes.BadTicketInvalid));
            }
            var runtime = new ProvisionableDeviceRuntime(
                false, ["device"], tickets.Object, validator.Object,
                Mock.Of<IProvisionableDeviceRegistrarStore>());

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RequestTicketsAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(status));
            if (failure != "signature")
            {
                validator.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// An empty embedded-ticket configuration does not disclose the shared store.
        /// </summary>
        [Test]
        public async Task RequestTicketsWithNoEmbeddedTicketsDoesNotEnumerateSharedStore()
        {
            var tickets = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            var runtime = new ProvisionableDeviceRuntime(
                false, [], tickets.Object, validator.Object, Mock.Of<IProvisionableDeviceRegistrarStore>());
            Assert.That((await runtime.RequestTicketsAsync().ConfigureAwait(false)).Count, Is.Zero);
            tickets.VerifyNoOtherCalls();
            validator.VerifyNoOtherCalls();
        }

        /// <summary>
        /// No persistence call occurs when any required session/channel authorization evidence is absent.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task SetRegistrarEndpointsRequiresRoleEncryptedChannelPeerAndSession(int failure)
        {
            StatusCode status = failure switch
            {
                0 or 3 => StatusCodes.BadUserAccessDenied,
                1 => StatusCodes.BadSecurityModeInsufficient,
                2 => StatusCodes.BadSecurityChecksFailed,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(), store.Object);
            SessionSystemContext context = Context(
                administrator: failure != 0,
                encrypted: failure != 1,
                peer: failure != 2,
                sessionId: failure != 3);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.SetRegistrarEndpointsAsync(context, [Registrar()]).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(status));
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Security-provider callbacks cannot change the validated candidate snapshot before publication.
        /// </summary>
        [Test]
        public async Task SecurityGateCannotMutateProposedRegistrarCandidates()
        {
            ApplicationDescription candidate = Registrar();
            string? originalUri = candidate.ApplicationUri;
            ArrayOf<string> originalUrls = candidate.DiscoveryUrls;
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(s => s.RequireRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    candidate.ApplicationUri = "urn:changed:registrar";
                    candidate.DiscoveryUrls = ["opc.tcp://changed:4840"];
                })
                .Returns(default(ValueTask));
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> persisted = default;
            store.Setup(s => s.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((value, _) => persisted = value)
                .Returns(default(ValueTask));
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(),
                store.Object, security.Object);

            await runtime.SetRegistrarEndpointsAsync(Context(), [candidate]).ConfigureAwait(false);

            Assert.That(persisted.Count, Is.EqualTo(1));
            Assert.That(persisted[0], Is.Not.SameAs(candidate));
            Assert.That(persisted[0].ApplicationUri, Is.EqualTo(originalUri));
            Assert.That(persisted[0].DiscoveryUrls, Is.EqualTo(originalUrls));
        }

        /// <summary>
        /// Published candidates are independent of caller-owned model objects, including explicit clearing.
        /// </summary>
        [Test]
        public async Task SetRegistrarEndpointsPublishesDetachedDescriptionsAndAllowsExplicitClear()
        {
            ArrayOf<ApplicationDescription> captured = default;
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            store.Setup(s => s.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => captured = values)
                .Returns(default(ValueTask));
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(), store.Object);
            ApplicationDescription registrar = Registrar();

            await runtime.SetRegistrarEndpointsAsync(Context(), [registrar]).ConfigureAwait(false);
            registrar.ApplicationUri = "urn:another:registrar";
            registrar.DiscoveryUrls = ["opc.tcp://another:4840"];

            Assert.That(captured[0], Is.Not.SameAs(registrar));
            Assert.That(captured[0].ApplicationUri, Is.EqualTo("urn:registrar:one"));
            Assert.That(captured[0].DiscoveryUrls.ToArray(), Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            await runtime.SetRegistrarEndpointsAsync(Context(), []).ConfigureAwait(false);
            Assert.That(captured.Count, Is.Zero);
            store.Verify(s => s.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Invalid application and discovery metadata cannot reach persistent configuration.
        /// </summary>
        [TestCase("uri")]
        [TestCase("client")]
        [TestCase("empty")]
        [TestCase("endpoint")]
        public async Task SetRegistrarEndpointsRejectsInvalidDescriptionWithoutPublishing(string failure)
        {
            ApplicationDescription registrar = Registrar();
            switch (failure)
            {
                case "uri":
                    registrar.ApplicationUri = "relative";
                    break;
                case "client":
                    registrar.ApplicationType = ApplicationType.Client;
                    break;
                case "empty":
                    registrar.DiscoveryUrls = [];
                    break;
                case "endpoint":
                    registrar.DiscoveryUrls = ["relative"];
                    break;
            }
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(), store.Object);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.SetRegistrarEndpointsAsync(Context(), [registrar]).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A failed configuration publication remains visible to the caller.
        /// </summary>
        [Test]
        public async Task SetRegistrarEndpointsPropagatesPublicationFailure()
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var failure = new InvalidOperationException("Checkpoint publication failed.");
            store.Setup(s => s.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure);
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(), store.Object);
            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.SetRegistrarEndpointsAsync(Context(), [Registrar()]).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
        }

        /// <summary>
        /// The optional configuration interface owns publication instead of claiming the engineering client.
        /// </summary>
        [Test]
        public async Task SetRegistrarEndpointsDispatchesToRegistrarConfigurationWithoutLegacyClaim()
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            Mock<IProvisionableDeviceRegistrarConfiguration> configuration =
                security.As<IProvisionableDeviceRegistrarConfiguration>();
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            SessionSystemContext context = Context();
            using var cancellation = new CancellationTokenSource();
            ApplicationDescription candidate = Registrar();
            ArrayOf<ApplicationDescription> captured = default;
            configuration.Setup(value => value.ReplaceRegistrarEndpointsAsync(
                    context, store.Object, It.IsAny<ArrayOf<ApplicationDescription>>(), cancellation.Token))
                .Callback<ISystemContext, IProvisionableDeviceRegistrarStore, ArrayOf<ApplicationDescription>,
                    CancellationToken>((_, _, values, _) =>
                {
                    captured = values;
                    candidate.ApplicationUri = "urn:mutated:registrar";
                    candidate.DiscoveryUrls = ["opc.tcp://mutated:4840"];
                })
                .Returns(default(ValueTask));
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(),
                store.Object, security.Object);

            await runtime.SetRegistrarEndpointsAsync(context, [candidate], cancellation.Token).ConfigureAwait(false);

            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0], Is.Not.SameAs(candidate));
            Assert.That(captured[0].ApplicationUri, Is.EqualTo("urn:registrar:one"));
            Assert.That(captured[0].DiscoveryUrls.ToArray(), Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            configuration.Verify(value => value.ReplaceRegistrarEndpointsAsync(
                context, store.Object, It.IsAny<ArrayOf<ApplicationDescription>>(), cancellation.Token), Times.Once);
            security.Verify(value => value.RequireRegistrarAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Never);
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Custom security providers without the optional interface retain the previous ordered fallback.
        /// </summary>
        [Test]
        public async Task SetRegistrarEndpointsFallsBackToLegacyProviderBeforePublishing()
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var calls = new List<string>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var authorized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SessionSystemContext context = Context();
            using var cancellation = new CancellationTokenSource();
            ArrayOf<ApplicationDescription> captured = default;
            security.Setup(value => value.RequireRegistrarAsync(context, cancellation.Token))
                .Returns(async (ISystemContext _, CancellationToken token) =>
                {
                    calls.Add("authorize");
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    calls.Add("authorized");
                    authorized.TrySetResult(true);
                });
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), cancellation.Token))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) =>
                {
                    calls.Add("replace");
                    captured = values;
                })
                .Returns(default(ValueTask));
            var runtime = new ProvisionableDeviceRuntime(
                false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(),
                store.Object, security.Object);
            ApplicationDescription candidate = Registrar();

            Task operation = runtime.SetRegistrarEndpointsAsync(
                context, [candidate], cancellation.Token).AsTask();
            Task completed = await Task.WhenAny(entered.Task, operation).ConfigureAwait(false);
            if (completed == operation && !entered.Task.IsCompleted)
            {
                await operation.ConfigureAwait(false);
                throw new AssertionException("Legacy authorization was not invoked.");
            }
            bool operationWaited;
            bool storeCalledBeforeAuthorization;
            try
            {
                operationWaited = !operation.IsCompleted;
                storeCalledBeforeAuthorization = calls.Contains("replace");
            }
            finally
            {
                release.TrySetResult(true);
            }
            await authorized.Task.ConfigureAwait(false);
            await operation.ConfigureAwait(false);

            Assert.That(security.Object, Is.Not.InstanceOf<IProvisionableDeviceRegistrarConfiguration>());
            Assert.That(operationWaited, Is.True);
            Assert.That(storeCalledBeforeAuthorization, Is.False);
            Assert.That(calls, Is.EqualTo(s_expectedLegacyPublicationCalls));
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0], Is.Not.SameAs(candidate));
            Assert.That(captured[0].ApplicationUri, Is.EqualTo("urn:registrar:one"));
            Assert.That(captured[0].DiscoveryUrls.ToArray(), Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            security.Verify(value => value.RequireRegistrarAsync(context, cancellation.Token), Times.Once);
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), cancellation.Token), Times.Once);
        }

        /// <summary>
        /// Direct and fluent runtimes consult the selected device security provider before changing candidates.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SetRegistrarEndpointsHonorsOptionalDeviceSecurityGate(bool useDependencyInjection)
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            SessionSystemContext context = Context();
            var denied = new ServiceResultException(StatusCodes.BadUserAccessDenied);
            security.Setup(s => s.RequireRegistrarAsync(context, It.IsAny<CancellationToken>())).Throws(denied);
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<ITicketStore>());
            services.AddSingleton(Mock.Of<IOnboardingTicketValidator>());
            services.AddSingleton(store.Object);
            services.AddSingleton(security.Object);
            services.AddProvisionableDeviceRuntime(false, []);
            using ServiceProvider provider = services.BuildServiceProvider();
            IProvisionableDeviceRuntime runtime = useDependencyInjection
                ? provider.GetRequiredService<IProvisionableDeviceRuntime>()
                : new ProvisionableDeviceRuntime(
                    false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(),
                    store.Object, security.Object);

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.SetRegistrarEndpointsAsync(context, [Registrar()]).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(denied));
            security.Verify(s => s.RequireRegistrarAsync(context, It.IsAny<CancellationToken>()), Times.Once);
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation rejects the operation before invoking any external ticket or configuration provider.
        /// </summary>
        [Test]
        public async Task CancelledDeviceOperationsDoNotReadTicketsOrPublishConfiguration()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            var tickets = new Mock<ITicketStore>(MockBehavior.Strict);
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var runtime = new ProvisionableDeviceRuntime(
                false, ["device"], tickets.Object, validator.Object, store.Object);
            await Assert.ThatAsync(async () =>
                await runtime.RequestTicketsAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(async () =>
                await runtime.SetRegistrarEndpointsAsync(Context(), [Registrar()], cancellation.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            tickets.VerifyNoOtherCalls();
            validator.VerifyNoOtherCalls();
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Fluent registration publishes one device manager whose generated resource is organized by Resources.
        /// </summary>
        [Test]
        public void DeviceRuntimeIsAvailableThroughFluentDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<ITicketStore>());
            services.AddSingleton(Mock.Of<IOnboardingTicketValidator>());
            services.AddSingleton(Mock.Of<IProvisionableDeviceRegistrarStore>());
            Opc.Ua.Server.Hosting.IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            int factories = services.Count(
                s => s.ServiceType == typeof(ProvisionableDeviceNodeManagerFactory));
            Assert.That(builder.AddProvisionableDeviceRuntime(true, []), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();
            IProvisionableDeviceRuntime runtime = provider.GetRequiredService<IProvisionableDeviceRuntime>();
            Assert.That(runtime, Is.TypeOf<ProvisionableDeviceRuntime>());
            Assert.That(runtime.IsSingleton, Is.True);
            Assert.That(provider.GetRequiredService<IProvisionableDeviceRuntime>(), Is.SameAs(runtime));
            Assert.That(services.Count(
                s => s.ServiceType == typeof(ProvisionableDeviceNodeManagerFactory)), Is.EqualTo(factories + 1));

            var context = Context();
            ProvisionableDeviceState device =
                ProvisionableDeviceNodeManagerFactory.CreateProvisionableDevice(context, runtime);
            Assert.That(device.BrowseName, Is.EqualTo(new QualifiedName("ProvisionableDevice", 0)));
            ushort namespaceIndex = checked((ushort)context.NamespaceUris.GetIndex(
                ProvisionableDeviceNodeManagerFactory.NamespaceUri));
            Assert.That(device.NodeId, Is.EqualTo(new NodeId("ProvisionableDevice", namespaceIndex)));
            Assert.That(device.IsSingleton!.Value, Is.True);
            var references = new List<IReference>();
            device.GetReferences(context, references);
            Assert.That(references.Any(reference =>
                reference.ReferenceTypeId == ReferenceTypeIds.Organizes &&
                reference.IsInverse &&
                reference.TargetId == global::Opc.Ua.ObjectIds.Resources), Is.True);
        }

        /// <summary>
        /// The application-provider service collection extension resolves its selected provider as a singleton.
        /// </summary>
        [Test]
        public void ProvisionableDeviceApplicationsProviderIsRegisteredOnceAsSingleton()
        {
            var services = new ServiceCollection();
            var expected = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict).Object;
            int factoryCalls = 0;

            IServiceCollection registered = services.AddProvisionableDeviceApplications(_ =>
            {
                factoryCalls++;
                return expected;
            });

            Assert.That(registered, Is.SameAs(services));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IProvisionableDeviceApplicationsProvider>(),
                Is.SameAs(expected));
            Assert.That(provider.GetRequiredService<IProvisionableDeviceApplicationsProvider>(),
                Is.SameAs(expected));
            Assert.That(factoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Fluent and service-collection registration share one provider-selection rule without partial mutation.
        /// </summary>
        [Test]
        public void FluentProvisionableDeviceApplicationsRejectsDuplicateProviderSelection()
        {
            var services = new ServiceCollection();
            Opc.Ua.Server.Hosting.IOpcUaServerBuilder builder =
                services.AddOpcUa().AddServer(_ => { });
            var expected = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict).Object;

            Assert.That(builder.AddProvisionableDeviceApplications(_ => expected), Is.SameAs(builder));
            ServiceDescriptor[] beforeDuplicate = [.. services];

            Assert.Throws<InvalidOperationException>(() =>
                builder.AddProvisionableDeviceApplications(_ => expected));
            Assert.Throws<InvalidOperationException>(() =>
                services.AddProvisionableDeviceApplications(_ => expected));

            Assert.That(services, Is.EqualTo(beforeDuplicate));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IProvisionableDeviceApplicationsProvider>(),
                Is.SameAs(expected));
        }

        /// <summary>
        /// The generated binding returns the String array and rejects separate applications on singleton devices.
        /// </summary>
        [Test]
        public async Task GeneratedRequestTicketsReturnsStringArrayAndSingletonLayoutIsEnforced()
        {
            var runtime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime.SetupGet(r => r.IsSingleton).Returns(true);
            runtime.Setup(r => r.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<string>)["signed-ticket"]);
            var device = new ProvisionableDeviceState(null);
            device.IsSingleton = PropertyState<bool>.With<VariantBuilder>(device);
            device.RequestTickets = new RequestTicketsMethodState(device);
            device.BindToRuntime(Context(), runtime.Object);
            var outputs = new List<Variant> { default };
            MethodState method = device.RequestTickets;

            ServiceResult result = await method.OnCallMethod2Async!(
                Context(), method, device.NodeId, [], outputs, default).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(device.IsSingleton.Value, Is.True);
            Assert.That(outputs[0].TryGetValue(out ArrayOf<string> tickets), Is.True);
            Assert.That(tickets.ToArray(), Is.EqualTo(s_expectedSingleTicket));
            device.AddChild(new ApplicationConfigurationState(device));
            ServiceResultException? invalid = Assert.Throws<ServiceResultException>(() =>
                device.BindToRuntime(Context(), runtime.Object));
            Assert.That(invalid!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// The registrar method passes native descriptions and retains SecurityAdmin-only node admission.
        /// </summary>
        [Test]
        public async Task GeneratedSetRegistrarEndpointsPassesNativeDescriptionsAndRestrictsMethodRole()
        {
            SessionSystemContext context = Context();
            ArrayOf<ApplicationDescription> registrars = [Registrar()];
            var runtime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime.SetupGet(r => r.IsSingleton).Returns(false);
            runtime.Setup(r => r.SetRegistrarEndpointsAsync(
                context, It.Is<ArrayOf<ApplicationDescription>>(v =>
                    v.Count == 1 && v[0].ApplicationUri == "urn:registrar:one"), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var device = new ProvisionableDeviceState(null);
            device.IsSingleton = PropertyState<bool>.With<VariantBuilder>(device);
            device.RequestTickets = new RequestTicketsMethodState(device);
            device.SetRegistrarEndpoints = new SetRegistrarEndpointsMethodState(device);
            device.BindToRuntime(context, runtime.Object);
            MethodState method = device.SetRegistrarEndpoints;

            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, device.NodeId, [Variant.FromStructure(registrars)], [], default).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(method.RolePermissions.Count, Is.EqualTo(1));
            Assert.That(method.RolePermissions[0].RoleId,
                Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin));
            Assert.That(method.RolePermissions[0].Permissions,
                Is.EqualTo((uint)(PermissionType.Browse | PermissionType.Call)));
            runtime.Verify(r => r.SetRegistrarEndpointsAsync(
                context, It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Invalid generated method inputs never reach the device's persistent configuration runtime.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task GeneratedSetRegistrarEndpointsRejectsMalformedInputs(int count)
        {
            StatusCode status = count switch
            {
                0 => StatusCodes.BadArgumentsMissing,
                1 => StatusCodes.BadTypeMismatch,
                2 => StatusCodes.BadTooManyArguments,
                _ => throw new ArgumentOutOfRangeException(nameof(count))
            };
            var runtime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime.SetupGet(r => r.IsSingleton).Returns(false);
            var device = new ProvisionableDeviceState(null);
            device.IsSingleton = PropertyState<bool>.With<VariantBuilder>(device);
            device.RequestTickets = new RequestTicketsMethodState(device);
            device.SetRegistrarEndpoints = new SetRegistrarEndpointsMethodState(device);
            device.BindToRuntime(Context(), runtime.Object);
            MethodState method = device.SetRegistrarEndpoints;
            ArrayOf<Variant> inputs = Enumerable.Repeat(Variant.From(42), count).ToArrayOf();

            ServiceResult result = await method.OnCallMethod2Async!(
                Context(), method, device.NodeId, inputs, [], default).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            runtime.Verify(r => r.SetRegistrarEndpointsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<ArrayOf<ApplicationDescription>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Creates deterministic ticket storage evidence without interpreting a fabricated signature.
        /// </summary>
        private static TicketRecord Record(string id, byte[] bytes)
        {
            return new TicketRecord(id, bytes,
                new TicketMetadata(
                    TicketKind.DeviceIdentity,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "urn:device:one"),
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        /// <summary>
        /// Creates one well-formed registrar discovery candidate.
        /// </summary>
        private static ApplicationDescription Registrar()
        {
            return new ApplicationDescription
            {
                ApplicationUri = "urn:registrar:one",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://registrar:4840"]
            };
        }

        /// <summary>
        /// Models the server-owned session/channel evidence checked before configuration changes.
        /// </summary>
        private static SessionSystemContext Context(
            bool administrator = true,
            bool encrypted = true,
            bool peer = true,
            bool sessionId = true)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(i => i.GrantedRoleIds).Returns(administrator
                ? new NodeId[] { Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin }.ToArrayOf() : []);
            var session = new Mock<Opc.Ua.Server.ISession>();
            session.SetupGet(s => s.Id).Returns(sessionId ? new NodeId("device-session", 0) : NodeId.Null);
            session.SetupGet(s => s.EffectiveIdentity).Returns(identity.Object);
            var channel = new SecureChannelContext(
                "device-channel", new EndpointDescription
                {
                    SecurityMode = encrypted ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.Sign,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, RequestEncoding.Binary, peer ? new byte[] { 1, 2, 3 } : null);
            return new SessionSystemContext(new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object),
                NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
        }

        /// <summary>
        /// The signed envelope identifiers returned from the device ticket store.
        /// </summary>
        private static readonly string[] s_expectedSignedEnvelopeIds =
            ["signed-envelope-first", "signed-envelope-second"];
        /// <summary>
        /// The discovery URL expected on the copied registrar description.
        /// </summary>
        private static readonly string[] s_expectedRegistrarDiscoveryUrls = ["opc.tcp://registrar:4840"];

        /// <summary>The authorization sequence preceding legacy candidate publication.</summary>
        private static readonly string[] s_expectedLegacyPublicationCalls = ["authorize", "authorized", "replace"];
        /// <summary>
        /// The ticket identifier returned by the generated request-tickets method.
        /// </summary>
        private static readonly string[] s_expectedSingleTicket = ["signed-ticket"];
    }
}
