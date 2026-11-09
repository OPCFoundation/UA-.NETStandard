/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers Part 21 ticket signing and validation with explicit authority stores.
    /// </summary>
    public static class OnboardingTicketBuilderExtensions
    {
        /// <summary>
        /// Selects one filesystem ticket store without replacing an existing custom store.
        /// Register before the registrar runtime. One process exclusively owns the checkpoint path;
        /// this registration does not change ticket validation or grant authority to persisted envelopes.
        /// </summary>
        public static IServiceCollection AddFileSystemOnboardingTicketStore(
            this IServiceCollection services,
            string path,
            IFileSystem? fileSystem = null,
            TimeProvider? timeProvider = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A ticket checkpoint path is required.", nameof(path));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(ITicketStore) ||
                    descriptor.ServiceType == typeof(FileSystemTicketStore)))
                {
                    throw new InvalidOperationException("Select only one onboarding ticket store.");
                }
            }
            services.AddSingleton(provider => new FileSystemTicketStore(
                path, fileSystem ?? provider.GetService<IFileSystem>(),
                timeProvider ?? provider.GetService<TimeProvider>()));
            services.AddSingleton<ITicketStore>(provider => provider.GetRequiredService<FileSystemTicketStore>());
            return services;
        }

        /// <summary>
        /// Adds ticket services to a server using its injected certificate manager.
        /// Custom ticket validators and trust providers registered beforehand are retained.
        /// The registrar trust list stores public ticket authorities, not device identity keys.
        /// </summary>
        public static IOpcUaServerBuilder AddOnboardingTickets(
            this IOpcUaServerBuilder builder,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterServices(builder.Services, trustedStorePath, issuerStorePath, isGds: false);
            return builder;
        }

        /// <summary>
        /// Adds ticket services to a GDS using its injected certificate manager.
        /// Custom ticket validators and trust providers registered beforehand are retained.
        /// </summary>
        public static IGdsServerBuilder AddOnboardingTickets(
            this IGdsServerBuilder builder,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterServices(builder.Services, trustedStorePath, issuerStorePath, isGds: true);
            return builder;
        }

        /// <summary>
        /// Adds ticket-scoped identity validation and selection using explicit issuer and CRL stores.
        /// The injected ticket store and ticket validator are required when resolving the selector.
        /// </summary>
        public static IOpcUaServerBuilder AddOnboardingIdentities(
            this IOpcUaServerBuilder builder,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterIdentityServices(builder.Services, trustedStorePath, issuerStorePath, isGds: false);
            return builder;
        }

        /// <summary>
        /// Adds ticket-scoped identity validation and selection to a GDS host.
        /// Managed certificates supply issuer material only; the matching ticket supplies trust anchors.
        /// </summary>
        public static IGdsServerBuilder AddOnboardingIdentities(
            this IGdsServerBuilder builder,
            string trustedStorePath,
            string? issuerStorePath = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterIdentityServices(builder.Services, trustedStorePath, issuerStorePath, isGds: true);
            return builder;
        }

        /// <summary>
        /// Registers the default identity policy without granting global managed roots ticket trust.
        /// </summary>
        private static void RegisterIdentityServices(
            IServiceCollection services,
            string trustedStorePath,
            string? issuerStorePath,
            bool isGds)
        {
            ValidateStorePaths(trustedStorePath, issuerStorePath);
            RequireSharedStoreConfiguration(services, trustedStorePath, issuerStorePath, identityAuthorities: true);
            services.TryAddSingleton<IOnboardingIdentityTrustProvider>(provider =>
            {
                ICertificateManager manager = provider.GetRequiredService<ICertificateManager>();
                manager.RegisterTrustList(
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities, trustedStorePath, issuerStorePath);
                return new OnboardingIdentityTrustProvider(
                    manager,
                    provider.GetRequiredService<ITelemetryContext>().CreateLogger<OnboardingIdentityTrustProvider>());
            });
            services.TryAddSingleton<IOnboardingIdentitySelector>(provider =>
                new OnboardingIdentitySelector(
                    provider.GetRequiredService<ITicketStore>(),
                    provider.GetRequiredService<IOnboardingTicketValidator>(),
                    provider.GetRequiredService<IOnboardingIdentityTrustProvider>()));
            RegisterStartupTask<IOnboardingIdentityTrustProvider>(services, isGds);
        }

        /// <summary>
        /// Registers injectable services without constructing a separate certificate subsystem.
        /// </summary>
        private static void RegisterServices(
            IServiceCollection services,
            string trustedStorePath,
            string? issuerStorePath,
            bool isGds)
        {
            ValidateStorePaths(trustedStorePath, issuerStorePath);
            RequireSharedStoreConfiguration(services, trustedStorePath, issuerStorePath, identityAuthorities: false);
            services.TryAddSingleton<IOnboardingTicketTrustProvider>(provider =>
            {
                ICertificateManager manager = provider.GetRequiredService<ICertificateManager>();
                manager.RegisterTrustList(
                    OnboardingTicketTrustProvider.TicketAuthorities, trustedStorePath, issuerStorePath);
                return new OnboardingTicketTrustProvider(manager);
            });
            services.TryAddSingleton<IOnboardingTicketValidator>(provider =>
                new OnboardingTicketValidator(
                    provider.GetRequiredService<IOnboardingTicketTrustProvider>(),
                    CreateMessageContext(provider.GetRequiredService<ITelemetryContext>())));
            services.TryAddSingleton(provider =>
                new OnboardingTicketWriter(
                    provider.GetRequiredService<ICertificateManager>().CertificateProvider,
                    CreateMessageContext(provider.GetRequiredService<ITelemetryContext>())));
            RegisterStartupTask<IOnboardingTicketTrustProvider>(services, isGds);
        }

        /// <summary>
        /// Rejects incompatible store paths instead of silently using the first singleton's configuration.
        /// Separate default registrar trust contexts require separate service collections.
        /// </summary>
        private static void RequireSharedStoreConfiguration(
            IServiceCollection services,
            string trustedStorePath,
            string? issuerStorePath,
            bool identityAuthorities)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService &&
                    descriptor.ImplementationInstance is AuthorityStoreRegistration registration &&
                    registration.IdentityAuthorities == identityAuthorities)
                {
                    if (!string.Equals(registration.TrustedStorePath, trustedStorePath, StringComparison.Ordinal) ||
                        !string.Equals(registration.IssuerStorePath, issuerStorePath, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Onboarding authority providers are shared by this service collection. " +
                            "Conflicting authority store configurations require separate service collections.");
                    }
                    return;
                }
            }
            services.AddSingleton(new AuthorityStoreRegistration(
                trustedStorePath, issuerStorePath, identityAuthorities));
        }

        /// <summary>
        /// Keeps GDS authority initialization on the GDS when another server shares the service collection.
        /// </summary>
        /// <typeparam name="T">The authority service initialized before its host starts.</typeparam>
        private static void RegisterStartupTask<T>(IServiceCollection services, bool isGds)
            where T : class
        {
            if (isGds)
            {
                services.AddSingleton(new GdsServerPreStartupTaskRegistration(
                    provider => new AuthorityStartupTask<T>(provider.GetRequiredService<T>),
                    typeof(AuthorityStartupTask<T>)));
            }
            else
            {
                services.AddSingleton<IServerPreStartupTask>(provider =>
                    new AuthorityStartupTask<T>(provider.GetRequiredService<T>));
            }
        }

        /// <summary>
        /// Requires an explicit trust-material location instead of using application peer stores.
        /// </summary>
        private static void ValidateStorePaths(string trustedStorePath, string? issuerStorePath)
        {
            if (string.IsNullOrWhiteSpace(trustedStorePath))
            {
                throw new ArgumentException(
                    "A trusted ticket authority store path is required.", nameof(trustedStorePath));
            }
            if (issuerStorePath != null && string.IsNullOrWhiteSpace(issuerStorePath))
            {
                throw new ArgumentException("The issuer store path cannot be empty.", nameof(issuerStorePath));
            }
        }

        /// <summary>
        /// Creates an isolated codec context populated by the generated onboarding model.
        /// </summary>
        private static ServiceMessageContext CreateMessageContext(ITelemetryContext telemetry)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            context.Factory.Builder.AddOpcUaOnboarding().Commit();
            return context;
        }

        /// <summary>
        /// Retains the store configuration shared by the default authority provider of one kind.
        /// </summary>
        private sealed record AuthorityStoreRegistration(
            string TrustedStorePath,
            string? IssuerStorePath,
            bool IdentityAuthorities);

        /// <summary>
        /// Initializes authority services before model bindings open their managed trust-list stores.
        /// Custom providers retain ownership of their own trust registration.
        /// </summary>
        /// <typeparam name="T">The authority service initialized by the callback.</typeparam>
        internal sealed class AuthorityStartupTask<T> : IServerPreStartupTask
            where T : class
        {
            /// <summary>
            /// Creates a startup task without resolving the authority service during container construction.
            /// </summary>
            public AuthorityStartupTask(Func<T> initialize)
            {
                m_initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
            }

            /// <inheritdoc/>
            public ValueTask OnServerStartingAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = m_initialize();
                return default;
            }

            /// <summary>
            /// The container-owned authority service initializer.
            /// </summary>
            private readonly Func<T> m_initialize;
        }
    }
}
