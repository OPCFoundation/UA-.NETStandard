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
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Tests explicit registrar dependency registration without starting server hosting.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarBuilderTests
    {
        /// <summary>
        /// The receipt startup binding belongs only to the GDS even when a regular server shares its container.
        /// </summary>
        [Test]
        public void HostedReceiptsRegisterOnlyGdsStartupBindingAndOneAdapter()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { });
            IGdsServerBuilder gds = services.AddOpcUa().AddGdsServer(_ => { });
            var secrets = new InMemorySecretStore();

            Assert.That(gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, "gds"), Is.SameAs(gds));

            using ServiceProvider provider = services.BuildServiceProvider();
            SecretStoreCertificateRequestIssuanceStore store =
                provider.GetRequiredService<SecretStoreCertificateRequestIssuanceStore>();
            Assert.That(provider.GetRequiredService<ICertificateRequestIssuanceStore>(), Is.SameAs(store));
            Assert.That(provider.GetServices<IServerPreStartupTask>()
                .OfType<OpcUaDeviceRegistrarBuilderExtensions.IssuanceNamespaceStartupTask>(), Is.Empty);
            GdsServerPreStartupTaskRegistration task =
                provider.GetServices<GdsServerPreStartupTaskRegistration>().Single();
            Assert.That(task.Factory(provider),
                Is.TypeOf<OpcUaDeviceRegistrarBuilderExtensions.IssuanceNamespaceStartupTask>());
        }

        /// <summary>
        /// The GDS task uses its actual table and cancellation cannot accidentally enable unbound receipt access.
        /// </summary>
        [Test]
        public async Task HostedReceiptStartupBindsTheActualServerNamespaceTable()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder gds = services.AddOpcUa().AddGdsServer(_ => { });
            var secrets = new InMemorySecretStore();
            gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, "gds");
            using ServiceProvider provider = services.BuildServiceProvider();
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:test:unrelated");
            namespaces.GetIndexOrAppend("urn:test:actual-gds");
            var native = new Mock<IServerInternal>();
            native.SetupGet(value => value.Telemetry).Returns(provider.GetRequiredService<ITelemetryContext>());
            native.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            var server = new Mock<IServerContext>();
            server.SetupGet(context => context.DefaultSystemContext).Returns(new ServerSystemContext(native.Object));
            IServerPreStartupTask task = provider.GetServices<GdsServerPreStartupTaskRegistration>().Single()
                .Factory(provider);
            ICertificateRequestIssuanceStore store = provider.GetRequiredService<ICertificateRequestIssuanceStore>();
            NodeId application = new("application", 2);
            NodeId request = new("request", 2);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await task.OnServerStartingAsync(server.Object, cancelled.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.RemoveAsync(application, request).ConfigureAwait(false));
            await task.OnServerStartingAsync(server.Object).ConfigureAwait(false);
            var receipt = new CertificateRequestIssuance(
                "group", "type", ByteString.From(new byte[] { 2, 4, 6 }), default, []);
            await store.GetOrCreateAsync(application, request,
                _ => new ValueTask<CertificateRequestIssuance>(receipt)).ConfigureAwait(false);
            var reordered = new NamespaceTable();
            reordered.GetIndexOrAppend("urn:test:actual-gds");
            using var restarted = new SecretStoreCertificateRequestIssuanceStore(secrets, reordered, "gds");

            CertificateRequestIssuance restored = await restarted.GetOrCreateAsync(
                new NodeId("application", 1), new NodeId("request", 1),
                _ => throw new AssertionException("Startup must bind the actual GDS namespace URI."))
                .ConfigureAwait(false);

            Assert.That(restored.Certificate, Is.EqualTo(receipt.Certificate));
        }

        /// <summary>
        /// Invalid hosted receipt configuration and competing stores leave the registrations unchanged.
        /// </summary>
        [Test]
        public void HostedReceiptsRejectMissingInputsAndCompetingAdapters()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder gds = services.AddOpcUa().AddGdsServer(_ => { });
            var secrets = new InMemorySecretStore();
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddSecretStoreCertificateRequestIssuanceStore(
                    (IGdsServerBuilder)null!, secrets, "gds"));
            Assert.Throws<ArgumentNullException>(() => gds.AddSecretStoreCertificateRequestIssuanceStore(null!, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, null!));
            Assert.Throws<ArgumentException>(() =>
                gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, string.Empty));
            gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, "gds");
            ServiceDescriptor[] before = services.ToArray();

            Assert.Throws<InvalidOperationException>(() =>
                gds.AddSecretStoreCertificateRequestIssuanceStore(secrets, "other"));
            Assert.Throws<InvalidOperationException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(secrets, new NamespaceTable(), "other"));
            Assert.That(services, Is.EqualTo(before));
        }

        /// <summary>
        /// All lifecycle contracts share one container-owned provider without inventing a namespace table.
        /// </summary>
        [Test]
        public async Task HostedFileSystemLifecycleUsesOneProviderAndRequiresModelBinding()
        {
            var services = new ServiceCollection();
            var files = new Mock<IFileSystem>(MockBehavior.Strict);
            Assert.That(services.AddHostedFileSystemDeviceRegistrarLifecycle("registrar-state.json", files.Object),
                Is.SameAs(services));
            using ServiceProvider provider = services.BuildServiceProvider();
            FileSystemDeviceRegistrarLifecycle lifecycle =
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarLifecycle>(), Is.SameAs(lifecycle));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarIdentityLifecycle>(), Is.SameAs(lifecycle));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarCertificateLifecycle>(), Is.SameAs(lifecycle));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.FindDeviceAsync("urn:test:device").ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            files.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid paths and competing lifecycle contracts are rejected before registrations change.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void HostedFileSystemLifecycleRejectsConflictingProviders(int contract)
        {
            var services = new ServiceCollection();
            switch (contract)
            {
                case 0:
                    services.AddSingleton(new Mock<IDeviceRegistrarLifecycle>().Object);
                    break;
                case 1:
                    services.AddSingleton(new Mock<IDeviceRegistrarIdentityLifecycle>().Object);
                    break;
                case 2:
                    services.AddSingleton(new Mock<IDeviceRegistrarCertificateLifecycle>().Object);
                    break;
                default:
                    services.AddHostedFileSystemDeviceRegistrarLifecycle("existing.json");
                    break;
            }
            ServiceDescriptor[] before = services.ToArray();

            Assert.Throws<InvalidOperationException>(() =>
                services.AddHostedFileSystemDeviceRegistrarLifecycle("new.json"));
            Assert.That(services, Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() =>
                new ServiceCollection().AddHostedFileSystemDeviceRegistrarLifecycle(" "));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddHostedFileSystemDeviceRegistrarLifecycle(null!, "state.json"));
        }

        [Test]
        public void RegistrationRejectsNullServicesBuildersAndConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddDeviceRegistrarRuntime(null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddDeviceRegistrar((IGdsServerBuilder)null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddDeviceRegistrar((IOpcUaServerBuilder)null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() =>
                new ServiceCollection().AddDeviceRegistrarRuntime(null!));
            IGdsServerBuilder gds = new ServiceCollection().AddOpcUa().AddGdsServer(_ => { });
            IOpcUaServerBuilder server = new ServiceCollection().AddOpcUa().AddServer(_ => { });
            Assert.Throws<ArgumentNullException>(() => gds.AddDeviceRegistrar(null!));
            Assert.Throws<ArgumentNullException>(() => server.AddDeviceRegistrar(null!));
        }

        [Test]
        public async Task ReceiptRegistrationKeepsOneAdapterAndRetainsTheHostOwnedSecretStore()
        {
            var services = new ServiceCollection();
            var secrets = new InMemorySecretStore();
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:test:receipt");
            IServiceCollection returned = services.AddSecretStoreCertificateRequestIssuanceStore(
                secrets, namespaces, "gds");
            NodeId application = new("application", 1);
            NodeId request = new("request", 1);
            var issuance = new CertificateRequestIssuance(
                "group", "type", ByteString.From(new byte[] { 1 }), default, []);
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                ICertificateRequestIssuanceStore store =
                    provider.GetRequiredService<ICertificateRequestIssuanceStore>();
                Assert.That(returned, Is.SameAs(services));
                Assert.That(provider.GetRequiredService<ICertificateRequestIssuanceStore>(), Is.SameAs(store));
                await store.GetOrCreateAsync(application, request,
                    _ => new ValueTask<CertificateRequestIssuance>(issuance)).ConfigureAwait(false);
            }
            using var recreated = new SecretStoreCertificateRequestIssuanceStore(secrets, namespaces, "gds");
            CertificateRequestIssuance restored = await recreated.GetOrCreateAsync(application, request,
                _ => throw new AssertionException("Container disposal must not remove host-owned receipts."))
                .ConfigureAwait(false);
            Assert.That(restored.Certificate, Is.EqualTo(issuance.Certificate));
        }

        [Test]
        public void ReceiptRegistrationRejectsMissingInputsAndConflictingProviders()
        {
            var services = new ServiceCollection();
            var secrets = new InMemorySecretStore();
            var namespaces = new NamespaceTable();
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddSecretStoreCertificateRequestIssuanceStore(
                    null!, secrets, namespaces, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(null!, namespaces, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(secrets, null!, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(secrets, namespaces, null!));
            Assert.Throws<ArgumentException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(secrets, namespaces, string.Empty));
            services.AddSingleton(new Mock<ICertificateRequestIssuanceStore>().Object);
            Assert.Throws<InvalidOperationException>(() =>
                services.AddSecretStoreCertificateRequestIssuanceStore(secrets, namespaces, "gds"));
            Assert.That(services.Count(s => s.ServiceType == typeof(ICertificateRequestIssuanceStore)), Is.EqualTo(1));
        }

        [TestCase(0, nameof(IApplicationsDatabase))]
        [TestCase(1, nameof(IDeviceRegistrarLifecycle))]
        [TestCase(2, nameof(IDeviceRegistrarApplicationAuthorization))]
        [TestCase(3, nameof(IDeviceRegistrarPushScheduler))]
        [TestCase(4, nameof(IDeviceRegistrarManagerProvider))]
        [TestCase(5, nameof(IDeviceRegistrarAuditReporter))]
        [TestCase(6, nameof(IOnboardingIdentitySelector))]
        [TestCase(7, nameof(IDeviceRegistrarIdentityLifecycle))]
        [TestCase(8, nameof(IDeviceRegistrarOnboardingPolicy))]
        public void EveryProviderMustBeExplicitlyConfigured(int missing, string expectedName)
        {
            var services = new ServiceCollection();
            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                services.AddDeviceRegistrarRuntime(s => ConfigureProviders(s, missing)));
            Assert.That(error!.Message, Does.Contain(expectedName));
            Assert.That(services.Any(s => s.ServiceType == typeof(IDeviceRegistrarRuntime)), Is.False);
            Assert.That(services.Any(s => s.ServiceType == typeof(MemoryDeviceRegistrarLifecycle)), Is.False);
        }

        [Test]
        public void KeyedProviderDoesNotSatisfyUnkeyedRuntimeRequirement()
        {
            var services = new ServiceCollection();
            ConfigureProviders(services, missing: 0);
            var keyed = new Mock<IApplicationsDatabase>();
            services.AddKeyedSingleton("onboarding-only", keyed.Object);

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                services.AddDeviceRegistrarRuntime(_ => { }));

            Assert.That(error!.Message, Does.Contain(nameof(IApplicationsDatabase)));
            Assert.That(services.Any(s => s.ServiceType == typeof(IDeviceRegistrarRuntime)), Is.False);
            var unkeyed = new Mock<IApplicationsDatabase>();
            services.AddSingleton(unkeyed.Object);
            services.AddDeviceRegistrarRuntime(_ => { });
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IApplicationsDatabase>(), Is.SameAs(unkeyed.Object));
            Assert.That(provider.GetRequiredKeyedService<IApplicationsDatabase>("onboarding-only"),
                Is.SameAs(keyed.Object));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(), Is.TypeOf<DeviceRegistrarRuntime>());
        }

        [Test]
        public void GdsBuilderSupportsFluentRegistrationAndSharedSingletonAliases()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IGdsServerBuilder builder = services.AddOpcUa().AddGdsServer(_ => { });
            IGdsServerBuilder returned = builder.AddDeviceRegistrar(s => ConfigureProviders(s));
            Assert.That(returned, Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(),
                Is.SameAs(provider.GetRequiredService<DeviceRegistrarRuntime>()));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarAuthorization>(),
                Is.SameAs(provider.GetRequiredService<DeviceRegistrarAuthorization>()));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarCertificateAuthorization>(),
                Is.SameAs(provider.GetRequiredService<DeviceRegistrarAuthorization>()));
            IDeviceRegistrarRuntime runtime = provider.GetRequiredService<IDeviceRegistrarRuntime>();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(), Is.SameAs(runtime));
            OnboardingPushIdentityValidator pushIdentity =
                provider.GetRequiredService<OnboardingPushIdentityValidator>();
            Assert.That(provider.GetRequiredService<OnboardingPushIdentityValidator>(), Is.SameAs(pushIdentity));
        }

        [Test]
        public void RegularServerBuilderSupportsFluentRegistrarRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            IOpcUaServerBuilder returned = builder.AddDeviceRegistrar(s => ConfigureProviders(s));
            Assert.That(returned, Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(), Is.TypeOf<DeviceRegistrarRuntime>());
        }

        /// <summary>
        /// Hosts can select restricted model-bound auditing without constructing nodes during DI resolution.
        /// </summary>
        [Test]
        public void HostedAuditingSuppliesRequiredRuntimeProviderAndPreservesCustomOverrides()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            ConfigureProviders(services, missing: 5);
            IServiceCollection returned = services.AddHostedDeviceRegistrarAuditing();
            services.AddHostedDeviceRegistrarAuditing();
            services.AddDeviceRegistrarRuntime(_ => { });
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(services));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarAuditReporter>(),
                Is.SameAs(provider.GetRequiredService<HostedDeviceRegistrarAuditReporter>()));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(), Is.TypeOf<DeviceRegistrarRuntime>());
            Assert.That(services.Count(descriptor => descriptor.ServiceType ==
                typeof(HostedDeviceRegistrarAuditReporter)), Is.EqualTo(1));

            var customServices = new ServiceCollection();
            var custom = new Mock<IDeviceRegistrarAuditReporter>();
            customServices.AddSingleton(custom.Object);
            customServices.AddHostedDeviceRegistrarAuditing();
            using ServiceProvider customProvider = customServices.BuildServiceProvider();
            Assert.That(customProvider.GetRequiredService<IDeviceRegistrarAuditReporter>(), Is.SameAs(custom.Object));
            Assert.That(() => OpcUaDeviceRegistrarBuilderExtensions.AddHostedDeviceRegistrarAuditing(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// The fluent role selection reaches the runtime without inventing a standard onboarding NodeId.
        /// </summary>
        [Test]
        public void SoftwareUpdateRoleSelectionIsExplicitIdempotentAndRejectsConflicts()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            var role = new ExpandedNodeId("SoftwareUpdateAdmin", "urn:registrar:test:roles");
            Assert.That(services.AddDeviceRegistrarSoftwareUpdateRole(role), Is.SameAs(services));
            services.AddDeviceRegistrarSoftwareUpdateRole(role);
            int count = services.Count;
            Assert.That(() => services.AddDeviceRegistrarSoftwareUpdateRole(
                new ExpandedNodeId("DifferentRole", "urn:registrar:test:roles")), Throws.InvalidOperationException);
            Assert.That(services, Has.Count.EqualTo(count));
            services.AddDeviceRegistrarRuntime(value => ConfigureProviders(value));
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<DeviceRegistrarRuntime>().SoftwareUpdateAdminRoleId,
                Is.EqualTo(role));
            Assert.That(() => new ServiceCollection().AddDeviceRegistrarSoftwareUpdateRole(ExpandedNodeId.Null),
                Throws.ArgumentException);
            Assert.That(() => new ServiceCollection().AddDeviceRegistrarSoftwareUpdateRole(new ExpandedNodeId(1234u)),
                Throws.ArgumentException);
            Assert.That(() => OpcUaDeviceRegistrarBuilderExtensions.AddDeviceRegistrarSoftwareUpdateRole(null!, role),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Repeated selection registers the model only for the chosen server kind.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FluentRegistrationIncludesStandardRegistrarModelFactoryWithoutDuplicatingGds(bool gds)
        {
            var services = new ServiceCollection();
            ConfigureProviders(services);
            services.AddSingleton(new Mock<ITicketStore>().Object);
            services.AddSingleton(new Mock<IOnboardingTicketValidator>().Object);
            services.AddSingleton(new Mock<ICertificateManager>().Object);
            if (gds)
            {
                IGdsServerBuilder builder = services.AddOpcUa().AddGdsServer(_ => { });
                builder.AddDeviceRegistrar(_ => { });
                builder.AddDeviceRegistrar(_ => { });
            }
            else
            {
                IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
                builder.AddDeviceRegistrar(_ => { });
                builder.AddDeviceRegistrar(_ => { });
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            DeviceRegistrarNodeManagerFactory factory =
                provider.GetRequiredService<DeviceRegistrarNodeManagerFactory>();
            Assert.That(factory.NamespacesUris.ToArray(),
                Is.EqualTo(new[] { Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding }));
            Assert.That(provider.GetServices<IAsyncNodeManagerFactory>()
                .Count(f => f is DeviceRegistrarNodeManagerFactory), Is.EqualTo(gds ? 0 : 1));
            GdsDeviceRegistrarRegistration? registration = provider.GetService<GdsDeviceRegistrarRegistration>();
            if (gds)
            {
                Assert.That(registration, Is.Not.Null);
                Assert.That(registration!.Factory, Is.SameAs(factory));
                Assert.That(registration.CertificateAuthorization,
                    Is.SameAs(provider.GetRequiredService<IDeviceRegistrarCertificateAuthorization>()));
            }
            else
            {
                Assert.That(registration, Is.Null);
            }
        }

        /// <summary>
        /// A registrar added to one builder does not publish its namespace through the other builder.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RegistrarModelAndIssuanceGateRemainOnSelectedHost(bool gds)
        {
            var services = new ServiceCollection();
            ConfigureProviders(services);
            services.AddSingleton(new Mock<ITicketStore>().Object);
            services.AddSingleton(new Mock<IOnboardingTicketValidator>().Object);
            services.AddSingleton(new Mock<ICertificateManager>().Object);
            IOpcUaBuilder root = services.AddOpcUa();
            IOpcUaServerBuilder server = root.AddServer(_ => { });
            IGdsServerBuilder directory = root.AddGdsServer(_ => { });
            if (gds)
            {
                directory.AddDeviceRegistrar(_ => { });
            }
            else
            {
                server.AddDeviceRegistrar(_ => { });
            }
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<IAsyncNodeManagerFactory>()
                .Count(factory => factory is DeviceRegistrarNodeManagerFactory), Is.EqualTo(gds ? 0 : 1));
            Assert.That(provider.GetServices<GdsDeviceRegistrarRegistration>().Count(), Is.EqualTo(gds ? 1 : 0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RegistrarFactoryRejectsMissingDependencies(int missing)
        {
            Assert.Throws<ArgumentNullException>(() => new DeviceRegistrarNodeManagerFactory(
                missing == 0 ? null! : new Mock<IDeviceRegistrarRuntime>().Object,
                missing == 1 ? null! : new Mock<ITicketStore>().Object,
                missing == 2 ? null! : new Mock<IOnboardingTicketValidator>().Object,
                missing == 3 ? null! : new Mock<ICertificateManager>().Object));
        }

        [Test]
        public void RepeatedRegistrationRetainsExistingRuntimeAndProviderRegistrations()
        {
            var services = new ServiceCollection();
            ConfigureProviders(services);
            IServiceCollection returned = services.AddDeviceRegistrarRuntime(_ => { });
            services.AddDeviceRegistrarRuntime(_ => { });
            Assert.That(returned, Is.SameAs(services));
            Assert.That(services.Count(s => s.ServiceType == typeof(IDeviceRegistrarRuntime)), Is.EqualTo(1));
            Assert.That(services.Count(s => s.ServiceType == typeof(IDeviceRegistrarAuthorization)), Is.EqualTo(1));
            Assert.That(services.Count(s => s.ServiceType == typeof(IApplicationsDatabase)), Is.EqualTo(1));
            Assert.That(services.Count(s => s.ServiceType == typeof(IDeviceRegistrarLifecycle)), Is.EqualTo(1));
        }

        [Test]
        public void ExplicitRuntimeAndAuthorizationOverridesAreNotReplaced()
        {
            var services = new ServiceCollection();
            var runtime = new Mock<IDeviceRegistrarRuntime>();
            var authorization = new Mock<IDeviceRegistrarAuthorization>();
            services.AddSingleton(runtime.Object);
            services.AddSingleton(authorization.Object);
            services.AddDeviceRegistrarRuntime(s => ConfigureProviders(s));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarRuntime>(), Is.SameAs(runtime.Object));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarAuthorization>(), Is.SameAs(authorization.Object));
        }

        [Test]
        public async Task ResolvedRuntimeUsesExplicitAuthorizationAndSharedDatabase()
        {
            var services = new ServiceCollection();
            ConfigureProviders(services);
            var authorization = new Mock<IDeviceRegistrarAuthorization>();
            authorization.Setup(a => a.RequireDcaAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>())).ReturnsAsync("urn:device:test");
            var database = new Mock<IApplicationsDatabase>();
            var applicationId = new NodeId("shared", 2);
            database.Setup(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>())).Returns(applicationId);
            var lifecycle = new Mock<IDeviceRegistrarLifecycle>();
            lifecycle.As<IDeviceRegistrarApplicationScopeLifecycle>()
                .Setup(l => l.RequireManagedApplicationAllowedAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            services.AddSingleton(authorization.Object);
            services.AddSingleton(database.Object);
            services.AddSingleton(lifecycle.Object);
            services.AddDeviceRegistrarRuntime(_ => { });
            using ServiceProvider provider = services.BuildServiceProvider();
            var application = new ApplicationRecordDataType
            {
                ApplicationUri = "urn:application:test",
                ApplicationNames = [new LocalizedText("Application")],
                ProductUri = "urn:product:test",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840"]
            };
            var context = new SystemContext(NUnitTelemetryContext.Create());
            NodeId actual = await provider.GetRequiredService<IDeviceRegistrarRuntime>()
                .RegisterManagedApplicationAsync(context, application, string.Empty).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(applicationId));
            authorization.Verify(a => a.RequireApplicationAsync(
                "urn:device:test", application, string.Empty, It.IsAny<CancellationToken>()), Times.Once);
            database.Verify(d => d.RegisterApplication(application), Times.Once);
            lifecycle.Verify(l => l.AssociateApplicationAsync(
                "urn:device:test", applicationId, string.Empty, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void ProviderConfigurationFailurePropagatesWithoutRuntimeRegistration()
        {
            var services = new ServiceCollection();
            var failure = new InvalidOperationException("Configuration failed.");
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
                services.AddDeviceRegistrarRuntime(_ => throw failure));
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(services.Any(s => s.ServiceType == typeof(IDeviceRegistrarRuntime)), Is.False);
        }

        [Test]
        public void ExplicitCertificateIssuanceGateOverrideIsPreserved()
        {
            var services = new ServiceCollection();
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>();
            services.AddSingleton(gate.Object);
            services.AddDeviceRegistrarRuntime(s => ConfigureProviders(s));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<IDeviceRegistrarCertificateAuthorization>(), Is.SameAs(gate.Object));
        }

        [Test]
        public void CustomAuthorizationCannotSilentlyBypassCertificateIssuanceGate()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new Mock<IDeviceRegistrarAuthorization>().Object);
            services.AddDeviceRegistrarRuntime(s => ConfigureProviders(s));
            using ServiceProvider provider = services.BuildServiceProvider();
            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IDeviceRegistrarCertificateAuthorization>());
            Assert.That(error!.Message, Does.Contain("explicit certificate-issuance gate"));
        }

        private static void ConfigureProviders(IServiceCollection services, int missing = -1)
        {
            if (missing != 0)
            {
                services.AddSingleton(new Mock<IApplicationsDatabase>().Object);
            }
            if (missing != 1)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarLifecycle>().Object);
            }
            if (missing != 2)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarApplicationAuthorization>().Object);
            }
            if (missing != 3)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarPushScheduler>().Object);
            }
            if (missing != 4)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarManagerProvider>().Object);
            }
            if (missing != 5)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarAuditReporter>().Object);
            }
            if (missing != 6)
            {
                services.AddSingleton(new Mock<IOnboardingIdentitySelector>().Object);
            }
            if (missing != 7)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarIdentityLifecycle>().Object);
            }
            if (missing != 8)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarOnboardingPolicy>().Object);
            }
        }
    }
}
