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
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingTicketBuilderTests
    {
        [Test]
        public void AddOnboardingTicketsRejectsNullBuildersAndMissingTrustedPath()
        {
            Assert.That(
                () => OnboardingTicketBuilderExtensions.AddOnboardingTickets(
                    (IGdsServerBuilder)null!, "trusted"),
                Throws.ArgumentNullException);
            Assert.That(
                () => OnboardingTicketBuilderExtensions.AddOnboardingTickets(
                    (IOpcUaServerBuilder)null!, "trusted"),
                Throws.ArgumentNullException);

            var gdsServices = new ServiceCollection();
            IGdsServerBuilder gdsBuilder = gdsServices.AddOpcUa().AddGdsServer(_ => { });
            Assert.That(
                () => gdsBuilder.AddOnboardingTickets(null!),
                Throws.ArgumentException);
            Assert.That(
                () => gdsBuilder.AddOnboardingTickets(string.Empty),
                Throws.ArgumentException);
            Assert.That(
                () => gdsBuilder.AddOnboardingTickets("  "),
                Throws.ArgumentException);

            var serverServices = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = serverServices.AddOpcUa().AddServer(_ => { });
            Assert.That(
                () => serverBuilder.AddOnboardingTickets(null!),
                Throws.ArgumentException);
            Assert.That(
                () => serverBuilder.AddOnboardingTickets("trusted", issuerStorePath: string.Empty),
                Throws.ArgumentException);
            Assert.That(
                () => serverBuilder.AddOnboardingTickets("trusted", issuerStorePath: " "),
                Throws.ArgumentException);
        }

        [Test]
        public void AddOnboardingTicketsRegistersDefaultsForGdsAndRegularServerBuilders()
        {
            AssertDefaultsResolve(
                services => services.AddOpcUa().AddGdsServer(_ => { }).AddOnboardingTickets(
                    "ticket-trusted", "ticket-issuers"),
                "ticket-issuers");
            AssertDefaultsResolve(
                services => services.AddOpcUa().AddServer(_ => { }).AddOnboardingTickets("ticket-trusted"),
                null);
        }

        [Test]
        public void AddOnboardingIdentitiesRejectsMissingBuildersAndStorePaths()
        {
            Assert.That(() => OnboardingTicketBuilderExtensions.AddOnboardingIdentities(
                (IGdsServerBuilder)null!, "trusted"), Throws.ArgumentNullException);
            Assert.That(() => OnboardingTicketBuilderExtensions.AddOnboardingIdentities(
                (IOpcUaServerBuilder)null!, "trusted"), Throws.ArgumentNullException);
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            Assert.That(() => builder.AddOnboardingIdentities(string.Empty), Throws.ArgumentException);
            Assert.That(() => builder.AddOnboardingIdentities("trusted", " "), Throws.ArgumentException);
        }

        /// <summary>
        /// Sharing a container cannot silently replace a later registrar's explicitly selected trust context.
        /// </summary>
        [TestCase(false, false, "trusted")]
        [TestCase(false, false, "issuer")]
        [TestCase(false, false, "missing-issuer")]
        [TestCase(false, true, "trusted")]
        [TestCase(false, true, "issuer")]
        [TestCase(false, true, "missing-issuer")]
        [TestCase(true, false, "trusted")]
        [TestCase(true, false, "issuer")]
        [TestCase(true, false, "missing-issuer")]
        [TestCase(true, true, "trusted")]
        [TestCase(true, true, "issuer")]
        [TestCase(true, true, "missing-issuer")]
        public void ConflictingAuthorityStoresRejectWithoutChangingRegistrations(
            bool identities, bool gdsFirst, string conflict)
        {
            var services = new ServiceCollection();
            IOpcUaBuilder root = services.AddOpcUa();
            IOpcUaServerBuilder server = root.AddServer(_ => { });
            IGdsServerBuilder gds = root.AddGdsServer(_ => { });

            void Register(bool useGds, string trusted, string? issuer)
            {
                if (useGds)
                {
                    if (identities)
                    {
                        gds.AddOnboardingIdentities(trusted, issuer);
                    }
                    else
                    {
                        gds.AddOnboardingTickets(trusted, issuer);
                    }
                }
                else if (identities)
                {
                    server.AddOnboardingIdentities(trusted, issuer);
                }
                else
                {
                    server.AddOnboardingTickets(trusted, issuer);
                }
            }

            Register(gdsFirst, "trusted", "issuers");
            Register(!gdsFirst, "trusted", "issuers");
            ServiceDescriptor[] before = services.ToArray();
            string trusted = conflict == "trusted" ? "other-trusted" : "trusted";
            string? issuer = conflict == "issuer" ? "other-issuers"
                : conflict == "missing-issuer" ? null : "issuers";

            Assert.That(() => Register(!gdsFirst, trusted, issuer), Throws.InvalidOperationException);

            Assert.That(services.ToArray(), Is.EqualTo(before));
            Assert.That(services.Count(descriptor => descriptor.ServiceType ==
                (identities ? typeof(IOnboardingIdentityTrustProvider) : typeof(IOnboardingTicketTrustProvider))),
                Is.EqualTo(1));
        }

        [Test]
        public void AddOnboardingIdentitiesResolvesTicketScopedDefaultsAndPreservesOverrides()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .AddOnboardingIdentities("identity-trusted", "identity-issuers");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(value => value.RegisterTrustList(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities, "identity-trusted", "identity-issuers"));
            services.AddSingleton(manager.Object);
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton<ITicketStore>(new MemoryTicketStore());
            services.AddSingleton(new Mock<IOnboardingTicketValidator>(MockBehavior.Strict).Object);

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                Assert.That(provider.GetRequiredService<IOnboardingIdentityTrustProvider>(),
                    Is.TypeOf<OnboardingIdentityTrustProvider>());
                Assert.That(provider.GetRequiredService<IOnboardingIdentitySelector>(),
                    Is.TypeOf<OnboardingIdentitySelector>());
            }
            manager.Verify(value => value.RegisterTrustList(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                "identity-trusted", "identity-issuers"), Times.Once);
            manager.VerifyNoOtherCalls();

            var customServices = new ServiceCollection();
            var customTrust = new Mock<IOnboardingIdentityTrustProvider>().Object;
            var customSelector = new Mock<IOnboardingIdentitySelector>().Object;
            customServices.AddSingleton(customTrust);
            customServices.AddSingleton(customSelector);
            customServices.AddOpcUa().AddGdsServer(_ => { }).AddOnboardingIdentities("identity-trusted");
            using ServiceProvider customProvider = customServices.BuildServiceProvider();
            Assert.That(customProvider.GetRequiredService<IOnboardingIdentityTrustProvider>(), Is.SameAs(customTrust));
            Assert.That(customProvider.GetRequiredService<IOnboardingIdentitySelector>(), Is.SameAs(customSelector));
        }

        [Test]
        public void AddOnboardingTicketsPreservesCustomTrustProviderAndValidator()
        {
            var services = new ServiceCollection();
            var customTrustProvider = new Mock<IOnboardingTicketTrustProvider>().Object;
            var customValidator = new Mock<IOnboardingTicketValidator>().Object;
            services.AddSingleton(customTrustProvider);
            services.AddSingleton(customValidator);
            services.AddOpcUa().AddGdsServer(_ => { }).AddOnboardingTickets("ticket-trusted");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            var certificateProvider = new Mock<ICertificateProvider>(MockBehavior.Strict).Object;
            manager.SetupGet(m => m.CertificateProvider).Returns(certificateProvider);
            services.AddSingleton(manager.Object);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            services.AddSingleton<ITelemetryContext>(telemetry);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IOnboardingTicketTrustProvider>(),
                Is.SameAs(customTrustProvider));
            Assert.That(
                provider.GetRequiredService<IOnboardingTicketValidator>(),
                Is.SameAs(customValidator));
            Assert.That(provider.GetRequiredService<OnboardingTicketWriter>(), Is.Not.Null);
            manager.Verify(m => m.RegisterTrustList(
                It.IsAny<TrustListIdentifier>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
            manager.VerifyGet(m => m.CertificateProvider, Times.Once);
            manager.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Managed authority locations exist before node managers attempt to bind their file services.
        /// </summary>
        [Test]
        public async Task AuthorityStartupRegistersBothListsBeforeModelBindingsWithoutConstructingWriter()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .AddOnboardingTickets("ticket-trusted", "ticket-issuers")
                .AddOnboardingIdentities("identity-trusted", "identity-issuers");
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(m => m.RegisterTrustList(
                OnboardingTicketTrustProvider.TicketAuthorities, "ticket-trusted", "ticket-issuers"));
            manager.Setup(m => m.RegisterTrustList(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities, "identity-trusted", "identity-issuers"));
            services.AddSingleton(manager.Object);
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            using ServiceProvider provider = services.BuildServiceProvider();
            IServerContext server = new Mock<IServerContext>().Object;
            var ticketStartup = provider.GetServices<IServerPreStartupTask>()
                .OfType<OnboardingTicketBuilderExtensions.AuthorityStartupTask<IOnboardingTicketTrustProvider>>()
                .Single();
            var identityStartup = provider.GetServices<IServerPreStartupTask>()
                .OfType<OnboardingTicketBuilderExtensions.AuthorityStartupTask<IOnboardingIdentityTrustProvider>>()
                .Single();
            manager.VerifyNoOtherCalls();

            await ticketStartup.OnServerStartingAsync(server).ConfigureAwait(false);
            await identityStartup.OnServerStartingAsync(server).ConfigureAwait(false);
            await ticketStartup.OnServerStartingAsync(server).ConfigureAwait(false);
            await identityStartup.OnServerStartingAsync(server).ConfigureAwait(false);

            manager.Verify(m => m.RegisterTrustList(
                OnboardingTicketTrustProvider.TicketAuthorities, "ticket-trusted", "ticket-issuers"), Times.Once);
            manager.Verify(m => m.RegisterTrustList(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities,
                "identity-trusted", "identity-issuers"), Times.Once);
            manager.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A canceled startup does not resolve an authority provider or mutate its managed stores.
        /// </summary>
        [Test]
        public async Task AuthorityStartupHonorsCancellationBeforeProviderResolution()
        {
            var initialize = new Mock<Func<IOnboardingTicketTrustProvider>>(MockBehavior.Strict);
            var task = new OnboardingTicketBuilderExtensions.AuthorityStartupTask<IOnboardingTicketTrustProvider>(
                initialize.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await task.OnServerStartingAsync(new Mock<IServerContext>().Object, cancellation.Token)
                    .ConfigureAwait(false));

            initialize.VerifyNoOtherCalls();
        }

        /// <summary>
        /// GDS-specific authorities initialize on the GDS even when a regular server shares its container.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AuthorityStartupRemainsOnItsSelectedHostWhenServersShareContainer(bool useGds)
        {
            var services = new ServiceCollection();
            IOpcUaBuilder root = services.AddOpcUa();
            IOpcUaServerBuilder serverBuilder = root.AddServer(_ => { });
            IGdsServerBuilder gdsBuilder = root.AddGdsServer(_ => { });
            if (useGds)
            {
                gdsBuilder.AddOnboardingTickets("ticket-trusted").AddOnboardingIdentities("identity-trusted");
            }
            else
            {
                serverBuilder.AddOnboardingTickets("ticket-trusted").AddOnboardingIdentities("identity-trusted");
            }
            int ticketResolutions = 0;
            int identityResolutions = 0;
            services.AddSingleton<IOnboardingTicketTrustProvider>(_ =>
            {
                ticketResolutions++;
                return new Mock<IOnboardingTicketTrustProvider>(MockBehavior.Strict).Object;
            });
            services.AddSingleton<IOnboardingIdentityTrustProvider>(_ =>
            {
                identityResolutions++;
                return new Mock<IOnboardingIdentityTrustProvider>(MockBehavior.Strict).Object;
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            IServerPreStartupTask[] sharedTasks = provider.GetServices<IServerPreStartupTask>()
                .Where(task => task is
                    OnboardingTicketBuilderExtensions.AuthorityStartupTask<IOnboardingTicketTrustProvider> or
                    OnboardingTicketBuilderExtensions.AuthorityStartupTask<IOnboardingIdentityTrustProvider>)
                .ToArray();
            GdsServerPreStartupTaskRegistration[] gdsTasks =
                provider.GetServices<GdsServerPreStartupTaskRegistration>().ToArray();
            Assert.That(sharedTasks, Has.Length.EqualTo(useGds ? 0 : 2));
            Assert.That(gdsTasks, Has.Length.EqualTo(useGds ? 2 : 0));
            Assert.That(ticketResolutions, Is.Zero);
            Assert.That(identityResolutions, Is.Zero);

            IServerContext server = new Mock<IServerContext>().Object;
            foreach (IServerPreStartupTask task in sharedTasks)
            {
                await task.OnServerStartingAsync(server).ConfigureAwait(false);
            }
            foreach (GdsServerPreStartupTaskRegistration registration in gdsTasks)
            {
                await registration.Factory(provider).OnServerStartingAsync(server).ConfigureAwait(false);
            }
            Assert.That(ticketResolutions, Is.EqualTo(1));
            Assert.That(identityResolutions, Is.EqualTo(1));
        }

        private static void AssertDefaultsResolve(
            Func<IServiceCollection, object> addBuilderAndOnboarding,
            string? expectedIssuerStorePath)
        {
            var services = new ServiceCollection();
            _ = addBuilderAndOnboarding(services);
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            manager.Setup(m => m.RegisterTrustList(
                OnboardingTicketTrustProvider.TicketAuthorities, "ticket-trusted", expectedIssuerStorePath));
            var certificateProvider = new Mock<ICertificateProvider>(MockBehavior.Strict).Object;
            manager.SetupGet(m => m.CertificateProvider).Returns(certificateProvider);
            services.AddSingleton(manager.Object);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            services.AddSingleton<ITelemetryContext>(telemetry);

            Assert.That(services.Count(descriptor =>
                descriptor.ServiceType == typeof(IOnboardingTicketTrustProvider)), Is.EqualTo(1));
            Assert.That(services.Count(descriptor =>
                descriptor.ServiceType == typeof(IOnboardingTicketValidator)), Is.EqualTo(1));
            Assert.That(services.Count(descriptor =>
                descriptor.ServiceType == typeof(OnboardingTicketWriter)), Is.EqualTo(1));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IOnboardingTicketTrustProvider>(),
                Is.TypeOf<OnboardingTicketTrustProvider>());
            Assert.That(provider.GetRequiredService<IOnboardingTicketValidator>(),
                Is.TypeOf<OnboardingTicketValidator>());
            Assert.That(provider.GetRequiredService<OnboardingTicketWriter>(), Is.Not.Null);

            manager.Verify(m => m.RegisterTrustList(
                OnboardingTicketTrustProvider.TicketAuthorities,
                "ticket-trusted",
                expectedIssuerStorePath), Times.Once);
            manager.VerifyGet(m => m.CertificateProvider, Times.Once);
        }
    }
}
