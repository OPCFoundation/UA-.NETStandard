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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the registrar runtime with explicit host-owned authorization and orchestration providers.
    /// </summary>
    public static class OpcUaDeviceRegistrarBuilderExtensions
    {
        /// <summary>
        /// Adds registrar services to a GDS host and returns the same builder.
        /// The callback must register the shared application database, lifecycle, application policy,
        /// push scheduler, manager provider, restricted audit reporter, identity selector, candidate storage,
        /// and onboarding policy unless already registered.
        /// The host loads and binds the standard registrar namespace through its registered node manager factory.
        /// </summary>
        public static IGdsServerBuilder AddDeviceRegistrar(
            this IGdsServerBuilder builder,
            Action<IServiceCollection> configureProviders)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddDeviceRegistrarRuntime(configureProviders);
            AddNodeManagerFactory(builder.Services, isGds: true);
            return builder;
        }

        /// <summary>
        /// Adds registrar services to a regular OPC UA server host and returns the same builder.
        /// Providers have the same requirements as the GDS overload; the host also supplies a GDS directory.
        /// </summary>
        public static IOpcUaServerBuilder AddDeviceRegistrar(
            this IOpcUaServerBuilder builder,
            Action<IServiceCollection> configureProviders)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddDeviceRegistrarRuntime(configureProviders);
            AddNodeManagerFactory(builder.Services, isGds: false);
            return builder;
        }

        /// <summary>
        /// Registers device ticket/configuration services without creating Resources or claiming its namespace.
        /// The host binds the runtime to its generated ProvisionableDevice before publishing that resource.
        /// </summary>
        public static IOpcUaServerBuilder AddProvisionableDeviceRuntime(
            this IOpcUaServerBuilder builder,
            bool isSingleton,
            ArrayOf<string> ticketIds)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddProvisionableDeviceRuntime(isSingleton, ticketIds);
            return builder;
        }

        /// <summary>
        /// Registers the device runtime with explicit ticket-validation and persistent registrar-store providers.
        /// An existing custom runtime is preserved; no trust or storage provider is supplied implicitly.
        /// </summary>
        public static IServiceCollection AddProvisionableDeviceRuntime(
            this IServiceCollection services,
            bool isSingleton,
            ArrayOf<string> ticketIds)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            RequireProvider<ITicketStore>(services);
            RequireProvider<IOnboardingTicketValidator>(services);
            RequireProvider<IProvisionableDeviceRegistrarStore>(services);
            ArrayOf<string> identifiers = ticketIds.ToArray().ToArrayOf();
            services.TryAddSingleton<IProvisionableDeviceRuntime>(sp => new ProvisionableDeviceRuntime(
                isSingleton, identifiers, sp.GetRequiredService<ITicketStore>(),
                sp.GetRequiredService<IOnboardingTicketValidator>(),
                sp.GetRequiredService<IProvisionableDeviceRegistrarStore>(),
                sp.GetService<IProvisionableDeviceSecurity>()));
            return services;
        }

        /// <summary>
        /// Registers one filesystem lifecycle for candidate identity, software approval and DCA issuance.
        /// AddDeviceRegistrar binds it to the actual model namespace table before requests are accepted.
        /// The checkpoint path requires one writer; this does not register discovery candidates on the device.
        /// </summary>
        public static IServiceCollection AddHostedFileSystemDeviceRegistrarLifecycle(
            this IServiceCollection services,
            string path,
            IFileSystem? fileSystem = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A registrar lifecycle checkpoint path is required.", nameof(path));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(FileSystemDeviceRegistrarLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarIdentityLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarCertificateLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarApplicationScopeLifecycle)))
                {
                    throw new InvalidOperationException("Select only one registrar lifecycle provider.");
                }
            }
            services.AddSingleton(provider => new FileSystemDeviceRegistrarLifecycle(
                fileSystem ?? provider.GetService<IFileSystem>(), path));
            services.AddSingleton<IDeviceRegistrarLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarIdentityLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarCertificateLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarApplicationScopeLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            return services;
        }

        /// <summary>
        /// Selects filesystem persistence for device-side registrar discovery candidates.
        /// The path must have one writer instance. This does not pin a registrar, grant trust, or recover credentials.
        /// Register before AddProvisionableDeviceRuntime; custom stores must be selected instead, not alongside it.
        /// </summary>
        public static IServiceCollection AddFileSystemProvisionableDeviceRegistrarStore(
            this IServiceCollection services,
            string path,
            IFileSystem? fileSystem = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A device registrar checkpoint path is required.", nameof(path));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(FileSystemProvisionableDeviceRegistrarStore) ||
                    descriptor.ServiceType == typeof(IProvisionableDeviceRegistrarStore)))
                {
                    throw new InvalidOperationException("Select only one device registrar candidate store.");
                }
            }
            services.AddSingleton(provider => new FileSystemProvisionableDeviceRegistrarStore(
                path, provider.GetRequiredService<ITelemetryContext>(),
                fileSystem ?? provider.GetService<IFileSystem>()));
            services.AddSingleton<IProvisionableDeviceRegistrarStore>(provider =>
                provider.GetRequiredService<FileSystemProvisionableDeviceRegistrarStore>());
            return services;
        }

        /// <summary>
        /// Adds device security installation/recovery and the existing server-configuration reset provider.
        /// The host registers an exclusive durable secret store, certificate manager, and idempotent installer.
        /// A pre-startup task recovers before the standard server opens listeners. Other hosts call RecoverAsync.
        /// Registrar operations use RequireRegistrarAsync; the installer resets security without deleting software.
        /// </summary>
        public static IOpcUaServerBuilder AddProvisionableDeviceSecurity(
            this IOpcUaServerBuilder builder,
            string scope,
            string applicationUri)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddProvisionableDeviceSecurity(scope, applicationUri);
            return builder;
        }

        /// <summary>
        /// Adds the certificate-store-backed device installer and the provisionable-device security state machine.
        /// </summary>
        /// <param name="builder">The OPC UA server builder.</param>
        /// <param name="scope">The exclusive durable-state scope for this device.</param>
        /// <param name="applicationUri">The device application URI.</param>
        /// <param name="installerOptions">
        /// The application certificate slot, certificate group, and separate immutable identity store.
        /// </param>
        /// <returns>The same builder.</returns>
        public static IOpcUaServerBuilder AddProvisionableDeviceSecurity(
            this IOpcUaServerBuilder builder,
            string scope,
            string applicationUri,
            ProvisionableDeviceSecurityInstallerOptions installerOptions)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddProvisionableDeviceSecurity(scope, applicationUri, installerOptions);
            return builder;
        }

        /// <summary>
        /// Registers one security state machine and exposes that same instance to the server's standard reset path.
        /// Custom reset providers must be incorporated by the installer rather than registered alongside this adapter.
        /// </summary>
        public static IServiceCollection AddProvisionableDeviceSecurity(
            this IServiceCollection services,
            string scope,
            string applicationUri)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive device security scope is required.", nameof(scope));
            }
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute DCA application URI is required.", nameof(applicationUri));
            }
            RequireProvider<ISecretStore>(services);
            RequireProvider<ICertificateManager>(services);
            RequireProvider<IProvisionableDeviceSecurityInstaller>(services);
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(ProvisionableDeviceSecurity) ||
                    descriptor.ServiceType == typeof(IProvisionableDeviceSecurity) ||
                    descriptor.ServiceType == typeof(IServerConfigurationResetProvider)))
                {
                    throw new InvalidOperationException("Select only one device security/reset provider.");
                }
            }
            services.AddSingleton(provider => new ProvisionableDeviceSecurity(
                provider.GetRequiredService<ISecretStore>(), scope, applicationUri,
                provider.GetRequiredService<ICertificateManager>(),
                provider.GetRequiredService<IProvisionableDeviceSecurityInstaller>(),
                provider.GetRequiredService<ITelemetryContext>()));
            services.AddSingleton<IServerConfigurationResetProvider>(provider =>
                provider.GetRequiredService<ProvisionableDeviceSecurity>());
            services.AddSingleton<IProvisionableDeviceSecurity>(provider =>
                provider.GetRequiredService<ProvisionableDeviceSecurity>());
            services.AddSingleton<IServerPreStartupTask>(provider => new DeviceSecurityStartupTask(
                () => provider.GetRequiredService<IProvisionableDeviceSecurity>()));
            return services;
        }

        /// <summary>
        /// Registers the concrete certificate-store-backed installer and device security state machine.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="scope">The exclusive durable-state scope for this device.</param>
        /// <param name="applicationUri">The device application URI.</param>
        /// <param name="installerOptions">
        /// The application certificate slot, certificate group, and separate immutable identity store.
        /// </param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddProvisionableDeviceSecurity(
            this IServiceCollection services,
            string scope,
            string applicationUri,
            ProvisionableDeviceSecurityInstallerOptions installerOptions)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (installerOptions == null)
            {
                throw new ArgumentNullException(nameof(installerOptions));
            }
            services.AddSingleton(installerOptions);
            services.TryAddSingleton<ICertificateStoreResolver>(provider =>
                provider.GetRequiredService<ICertificateManager>() as ICertificateStoreResolver
                ?? throw new InvalidOperationException(
                    "The configured certificate manager does not expose a certificate store resolver."));
            services.AddSingleton<IProvisionableDeviceSecurityInstaller>(provider =>
                new ProvisionableDeviceSecurityInstaller(
                    provider.GetRequiredService<ISecretStore>(),
                    scope,
                    applicationUri,
                    provider.GetRequiredService<ICertificateManager>(),
                    provider.GetRequiredService<ICertificateStoreResolver>(),
                    provider.GetRequiredService<IPendingCertificateKeyStore>(),
                    provider.GetRequiredService<IServerApplicationCertificateActivator>(),
                    provider.GetRequiredService<ProvisionableDeviceSecurityInstallerOptions>(),
                    provider.GetRequiredService<ITelemetryContext>()));
            return services.AddProvisionableDeviceSecurity(scope, applicationUri);
        }

        /// <summary>
        /// Completes device recovery before the standard server creates node managers or opens listeners.
        /// </summary>
        internal sealed class DeviceSecurityStartupTask : IServerPreStartupTask
        {
            /// <summary>
            /// Defers security resolution until the server's pre-startup phase.
            /// </summary>
            public DeviceSecurityStartupTask(Func<IProvisionableDeviceSecurity> security)
            {
                m_security = security ?? throw new ArgumentNullException(nameof(security));
            }

            /// <inheritdoc/>
            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                if (server == null)
                {
                    throw new ArgumentNullException(nameof(server));
                }
                cancellationToken.ThrowIfCancellationRequested();
                return m_security().RecoverAsync(cancellationToken);
            }

            /// <summary>The container-owned security provider, resolved after configuration initialization.</summary>
            private readonly Func<IProvisionableDeviceSecurity> m_security;
        }

        /// <summary>
        /// Registers constructor-injected runtime and authorization singletons without permissive providers.
        /// Provider registrations must be unkeyed. This does not start onboarding, create memory stores,
        /// load model nodes, or bind registrar methods.
        /// Custom runtime and authorization registrations are preserved.
        /// </summary>
        public static IServiceCollection AddDeviceRegistrarRuntime(
            this IServiceCollection services,
            Action<IServiceCollection> configureProviders)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configureProviders == null)
            {
                throw new ArgumentNullException(nameof(configureProviders));
            }
            configureProviders(services);

            RequireProvider<IApplicationsDatabase>(services);
            RequireProvider<IDeviceRegistrarLifecycle>(services);
            RequireProvider<IDeviceRegistrarApplicationAuthorization>(services);
            RequireProvider<IDeviceRegistrarPushScheduler>(services);
            RequireProvider<IDeviceRegistrarManagerProvider>(services);
            RequireProvider<IDeviceRegistrarAuditReporter>(services);
            RequireProvider<IOnboardingIdentitySelector>(services);
            RequireProvider<IDeviceRegistrarIdentityLifecycle>(services);
            RequireProvider<IDeviceRegistrarOnboardingPolicy>(services);

            services.TryAddSingleton<OnboardingPushIdentityValidator>();
            services.TryAddSingleton(sp => new DeviceRegistrarAuthorization(
                sp.GetRequiredService<IDeviceRegistrarLifecycle>(),
                sp.GetRequiredService<IDeviceRegistrarApplicationAuthorization>(),
                sp.GetRequiredService<IDeviceRegistrarIdentityLifecycle>()));
            services.TryAddSingleton<IDeviceRegistrarAuthorization>(
                sp => sp.GetRequiredService<DeviceRegistrarAuthorization>());
            services.TryAddSingleton<IDeviceRegistrarCertificateAuthorization>(sp =>
                sp.GetRequiredService<IDeviceRegistrarAuthorization>() as IDeviceRegistrarCertificateAuthorization ??
                throw new InvalidOperationException(
                    "Custom registrar authorization must provide an explicit certificate-issuance gate."));
            services.TryAddSingleton(sp => new DeviceRegistrarRuntime(
                sp.GetRequiredService<IApplicationsDatabase>(),
                sp.GetRequiredService<IDeviceRegistrarLifecycle>(),
                sp.GetRequiredService<IDeviceRegistrarAuthorization>(),
                sp.GetRequiredService<IDeviceRegistrarPushScheduler>(),
                sp.GetRequiredService<IDeviceRegistrarManagerProvider>(),
                sp.GetRequiredService<IDeviceRegistrarAuditReporter>(),
                sp.GetRequiredService<IOnboardingIdentitySelector>(),
                sp.GetRequiredService<IDeviceRegistrarIdentityLifecycle>(),
                sp.GetRequiredService<IDeviceRegistrarOnboardingPolicy>())
            {
                SoftwareUpdateAdminRoleId = sp.GetService<SoftwareUpdateRoleRegistration>() is { } role ?
                    role.RoleId : ExpandedNodeId.Null
            });
            services.TryAddSingleton<IDeviceRegistrarRuntime>(sp => sp.GetRequiredService<DeviceRegistrarRuntime>());
            return services;
        }

        /// <summary>
        /// Selects the host-defined SoftwareUpdateAdmin role before constructing the registrar runtime.
        /// The role must identify a local node by namespace URI; the host publishes it and grants it to sessions.
        /// Repeating the same selection is allowed; conflicting selections require separate service collections.
        /// Existing custom runtimes retain their own role configuration.
        /// </summary>
        public static IServiceCollection AddDeviceRegistrarSoftwareUpdateRole(
            this IServiceCollection services,
            ExpandedNodeId roleId)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (roleId.IsNull || roleId.ServerIndex != 0 || string.IsNullOrEmpty(roleId.NamespaceUri))
            {
                throw new ArgumentException("A local role identified by namespace URI is required.", nameof(roleId));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    descriptor.ImplementationInstance is SoftwareUpdateRoleRegistration registration)
                {
                    if (registration.RoleId != roleId)
                    {
                        throw new InvalidOperationException("The registrar software-update role is already selected.");
                    }
                    return services;
                }
            }
            services.AddSingleton(new SoftwareUpdateRoleRegistration(roleId));
            return services;
        }

        /// <summary>
        /// Selects model-bound restricted audit delivery before registering the registrar runtime.
        /// The standard registrar factory attaches the reporter before exposing its namespace and detaches it
        /// during shutdown. Existing custom audit providers are retained; direct hosts bind their own reporter.
        /// This registration supports one registrar host per service collection.
        /// </summary>
        public static IServiceCollection AddHostedDeviceRegistrarAuditing(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton<HostedDeviceRegistrarAuditReporter>();
            services.TryAddSingleton<IDeviceRegistrarAuditReporter>(provider =>
                provider.GetRequiredService<HostedDeviceRegistrarAuditReporter>());
            return services;
        }

        /// <summary>
        /// Selects fresh authenticated stack sessions for registrar push attempts.
        /// Target selection supplies authorized encrypted endpoints, credentials and namespace-stable model addresses.
        /// This does not register enrollment, discover Resources or accept untrusted certificates.
        /// </summary>
        public static IServiceCollection AddDeviceRegistrarPushConnections(
            this IServiceCollection services,
            ApplicationConfiguration configuration,
            Func<ApplicationDescription, CancellationToken, ValueTask<DeviceRegistrarPushTarget>> selectTarget,
            ISessionFactory? sessionFactory = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (selectTarget == null)
            {
                throw new ArgumentNullException(nameof(selectTarget));
            }
            services.AddSingleton<IDeviceRegistrarPushConnectionProvider>(provider =>
                new SessionDeviceRegistrarPushConnectionProvider(
                    configuration,
                    sessionFactory ?? provider.GetService<ISessionFactory>() ??
                        new DefaultSessionFactory(provider.GetRequiredService<ITelemetryContext>()),
                    selectTarget));
            return services;
        }

        /// <summary>
        /// Selects the concrete generated-client push processor and its supervised bounded queue.
        /// The host must explicitly provide authenticated connections, durable enrollment/request recovery,
        /// the shared lifecycle, and the existing GDS issuance store. No trust or software approval is inferred.
        /// Register before AddDeviceRegistrarRuntime validates the remaining registrar providers.
        /// </summary>
        public static IServiceCollection AddDeviceRegistrarPush(
            this IServiceCollection services,
            int capacity = 32)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            RequireProvider<IDeviceRegistrarPushConnectionProvider>(services);
            RequireProvider<IDeviceRegistrarPushEnrollmentProvider>(services);
            RequireProvider<IDeviceRegistrarCertificateLifecycle>(services);
            RequireProvider<ICertificateRequestIssuanceStore>(services);
            services.TryAddSingleton<OnboardingPushIdentityValidator>();
            return services.AddDeviceRegistrarPushScheduler<DeviceRegistrarPushProcessor>(capacity);
        }

        /// <summary>
        /// Selects an in-memory bounded push queue and registers its processor and host-owned worker as singletons.
        /// Register before AddDeviceRegistrarRuntime validates its provider set. The processor owns authentication,
        /// persistent provisioning state, and recovery of uncertain remote effects; this queue does not retry them.
        /// Durable queue acceptance requires a different IDeviceRegistrarPushScheduler implementation.
        /// </summary>
        /// <typeparam name="TProcessor">The injectable processor whose public constructors are retained.</typeparam>
        public static IServiceCollection AddDeviceRegistrarPushScheduler<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
            this IServiceCollection services,
            int capacity = 32)
            where TProcessor : class, IDeviceRegistrarPushProcessor
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (capacity < 1 || capacity > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(IDeviceRegistrarPushScheduler) ||
                    descriptor.ServiceType == typeof(DeviceRegistrarPushScheduler) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarPushProcessor)))
                {
                    throw new InvalidOperationException("Select only one registrar push scheduler and processor.");
                }
            }
            services.AddSingleton<IDeviceRegistrarPushProcessor, TProcessor>();
            services.AddSingleton(provider => new DeviceRegistrarPushScheduler(
                provider.GetRequiredService<IDeviceRegistrarPushProcessor>(),
                provider.GetRequiredService<ITelemetryContext>(), capacity));
            services.AddSingleton<IDeviceRegistrarPushScheduler>(provider =>
                provider.GetRequiredService<DeviceRegistrarPushScheduler>());
            services.AddSingleton<IHostedService>(provider =>
                provider.GetRequiredService<DeviceRegistrarPushScheduler>());
            return services;
        }

        /// <summary>
        /// Selects one persistent lifecycle instance for identity candidates and approved device relations.
        /// The namespace table must be the hosting server's table. The checkpoint path has one writer instance;
        /// distributed hosts supply their own transactional lifecycle provider instead.
        /// </summary>
        public static IServiceCollection AddFileSystemDeviceRegistrarLifecycle(
            this IServiceCollection services,
            string path,
            NamespaceTable namespaceUris,
            IFileSystem? fileSystem = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A lifecycle checkpoint path is required.", nameof(path));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(FileSystemDeviceRegistrarLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarIdentityLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarCertificateLifecycle) ||
                    descriptor.ServiceType == typeof(IDeviceRegistrarApplicationScopeLifecycle))
                {
                    throw new InvalidOperationException(
                        "Select persistent lifecycle storage before registering another lifecycle provider.");
                }
            }
            services.AddSingleton(provider => new FileSystemDeviceRegistrarLifecycle(
                path, namespaceUris, fileSystem ?? provider.GetService<IFileSystem>()));
            services.AddSingleton<IDeviceRegistrarLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarIdentityLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarCertificateLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            services.AddSingleton<IDeviceRegistrarApplicationScopeLifecycle>(provider =>
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>());
            return services;
        }

        /// <summary>
        /// Registers receipts whose namespace table is bound by this GDS host before its address space is created.
        /// The supplied secret store remains host-owned and its scope requires one exclusive writer.
        /// </summary>
        public static IGdsServerBuilder AddSecretStoreCertificateRequestIssuanceStore(
            this IGdsServerBuilder builder,
            ISecretStore secretStore,
            string scope)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (secretStore == null)
            {
                throw new ArgumentNullException(nameof(secretStore));
            }
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            if (scope.Length == 0)
            {
                throw new ArgumentException("A unique GDS database scope is required.", nameof(scope));
            }
            RequireNoIssuanceStore(builder.Services);
            builder.Services.AddSingleton(_ => new SecretStoreCertificateRequestIssuanceStore(secretStore, scope));
            builder.Services.AddSingleton<ICertificateRequestIssuanceStore>(provider =>
                provider.GetRequiredService<SecretStoreCertificateRequestIssuanceStore>());
            return builder.AddPreStartupTask<IssuanceNamespaceStartupTask>();
        }

        /// <summary>
        /// Registers one receipt adapter for an explicitly supplied secret store and unique GDS database scope.
        /// The namespace table must belong to the hosting server. Only this adapter instance may write its scope;
        /// distributed hosts must register an atomic cross-writer issuance provider instead.
        /// The secret store remains host-owned and must publish complete values before completing SetAsync.
        /// </summary>
        public static IServiceCollection AddSecretStoreCertificateRequestIssuanceStore(
            this IServiceCollection services,
            ISecretStore secretStore,
            NamespaceTable namespaceUris,
            string scope)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (secretStore == null)
            {
                throw new ArgumentNullException(nameof(secretStore));
            }
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            if (scope.Length == 0)
            {
                throw new ArgumentException("A unique GDS database scope is required.", nameof(scope));
            }
            RequireNoIssuanceStore(services);
            services.AddSingleton<ICertificateRequestIssuanceStore>(_ =>
                new SecretStoreCertificateRequestIssuanceStore(secretStore, namespaceUris, scope));
            return services;
        }

        /// <summary>
        /// Binds receipt persistence only to its selected GDS, not to a regular server sharing the container.
        /// </summary>
        internal sealed class IssuanceNamespaceStartupTask : IServerPreStartupTask
        {
            /// <summary>
            /// Borrows the container-owned receipt adapter without reading any secrets before startup.
            /// </summary>
            public IssuanceNamespaceStartupTask(SecretStoreCertificateRequestIssuanceStore store)
            {
                m_store = store ?? throw new ArgumentNullException(nameof(store));
            }

            /// <inheritdoc/>
            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                if (server == null)
                {
                    throw new ArgumentNullException(nameof(server));
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_store.BindNamespaceTable(server.DefaultSystemContext.NamespaceUris);
                return default;
            }

            /// <summary>The exclusive adapter bound during GDS startup.</summary>
            private readonly SecretStoreCertificateRequestIssuanceStore m_store;
        }

        /// <summary>
        /// Rejects competing issuance stores before mutating provider registrations.
        /// </summary>
        private static void RequireNoIssuanceStore(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(ICertificateRequestIssuanceStore) ||
                    descriptor.ServiceType == typeof(SecretStoreCertificateRequestIssuanceStore)))
                {
                    throw new InvalidOperationException("Select only one certificate issuance receipt provider.");
                }
            }
        }

        /// <summary>
        /// Registers the standard model host without constructing server-bound nodes during DI registration.
        /// </summary>
        private static void AddNodeManagerFactory(IServiceCollection services, bool isGds)
        {
            services.TryAddSingleton<DeviceRegistrarNodeManagerFactory>();
            if (isGds)
            {
                services.TryAddSingleton(provider => new GdsDeviceRegistrarRegistration(
                    provider.GetRequiredService<DeviceRegistrarNodeManagerFactory>(),
                    provider.GetRequiredService<IDeviceRegistrarCertificateAuthorization>()));
            }
            else
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IAsyncNodeManagerFactory, DeviceRegistrarNodeManagerFactory>());
            }
        }

        /// <summary>
        /// Requires an unkeyed provider usable by the default runtime.
        /// </summary>
        /// <typeparam name="T">The required provider contract.</typeparam>
        private static void RequireProvider<T>(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(T) && !descriptor.IsKeyedService)
                {
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Device registrar requires an explicit {typeof(T).Name} provider registration.");
        }

        /// <summary>
        /// Retains the role selection without constructing server nodes during container registration.
        /// </summary>
        private sealed record SoftwareUpdateRoleRegistration(ExpandedNodeId RoleId);
    }
}
