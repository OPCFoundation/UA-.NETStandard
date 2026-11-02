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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
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
            AddNodeManagerFactory(builder.Services);
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
            AddNodeManagerFactory(builder.Services);
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
            ArrayOf<string> identifiers = ticketIds.ToArray();
            services.TryAddSingleton<IProvisionableDeviceRuntime>(sp => new ProvisionableDeviceRuntime(
                isSingleton, identifiers, sp.GetRequiredService<ITicketStore>(),
                sp.GetRequiredService<IOnboardingTicketValidator>(),
                sp.GetRequiredService<IProvisionableDeviceRegistrarStore>(),
                sp.GetService<IProvisionableDeviceSecurity>()));
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
                sp.GetRequiredService<IDeviceRegistrarOnboardingPolicy>()));
            services.TryAddSingleton<IDeviceRegistrarRuntime>(sp => sp.GetRequiredService<DeviceRegistrarRuntime>());
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
                    descriptor.ServiceType == typeof(IDeviceRegistrarCertificateLifecycle))
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
            return services;
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
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(ICertificateRequestIssuanceStore) ||
                    descriptor.ServiceType == typeof(SecretStoreCertificateRequestIssuanceStore)))
                {
                    throw new InvalidOperationException("Select only one certificate issuance receipt provider.");
                }
            }
            services.AddSingleton<ICertificateRequestIssuanceStore>(_ =>
                new SecretStoreCertificateRequestIssuanceStore(secretStore, namespaceUris, scope));
            return services;
        }

        /// <summary>
        /// Registers the standard model host without constructing server-bound nodes during DI registration.
        /// </summary>
        private static void AddNodeManagerFactory(IServiceCollection services)
        {
            services.TryAddSingleton<DeviceRegistrarNodeManagerFactory>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IAsyncNodeManagerFactory, DeviceRegistrarNodeManagerFactory>());
        }

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
    }
}
