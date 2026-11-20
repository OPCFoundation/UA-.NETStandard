// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Supplies configured application instances rather than metadata-only application records.
    /// </summary>
    public interface IManagedApplicationPushProvider
    {
        /// <summary>
        /// Returns the applications and authorization policies to bind before the address space is published.
        /// Providers retain the applications' runtime and certificate-manager lifetimes.
        /// </summary>
        ValueTask<ArrayOf<ManagedApplicationPushRegistration>> GetApplicationsAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Supplies applications to publish as components of one non-singleton provisionable device.
    /// </summary>
    public interface IProvisionableDeviceApplicationsProvider
    {
        /// <summary>
        /// Returns application runtimes and their authorization policies before the device address space is published.
        /// The provider retains ownership of each runtime and certificate-manager lifetime.
        /// </summary>
        ValueTask<ArrayOf<ManagedApplicationPushRegistration>> GetApplicationsAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Pairs an independent target application with its additional push access policy.
    /// </summary>
    public sealed class ManagedApplicationPushRegistration
    {
        /// <summary>
        /// Creates a registration whose authorization is evaluated in addition to standard push checks.
        /// </summary>
        public ManagedApplicationPushRegistration(
            IApplicationConfigurationPushTarget target,
            IPushConfigurationAuthorization authorization,
            IPendingCertificateKeyStore? pendingKeyStore = null,
            IPushCertificateKeyGenerator? keyGenerator = null,
            IApplicationConfigurationFileProvider? configurationFileProvider = null)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            PendingKeyStore = pendingKeyStore;
            KeyGenerator = keyGenerator;
            ConfigurationFileProvider = configurationFileProvider;
        }

        /// <summary>
        /// Gets the independent target application's runtime.
        /// </summary>
        public IApplicationConfigurationPushTarget Target { get; }

        /// <summary>
        /// Gets the target-specific access policy.
        /// </summary>
        public IPushConfigurationAuthorization Authorization { get; }

        /// <summary>
        /// Gets the application's pending-key store, or null to create an independent directory-backed store.
        /// A supplied store remains provider-owned and cannot be shared with another application's binding.
        /// </summary>
        public IPendingCertificateKeyStore? PendingKeyStore { get; }

        /// <summary>
        /// Gets the regenerated-key provider, or null to use the standard additional-entropy generator.
        /// </summary>
        public IPushCertificateKeyGenerator? KeyGenerator { get; }

        /// <summary>
        /// Gets the optional application-specific configuration-file provider.
        /// The provider remains externally owned and must not be shared with another application or the host.
        /// Confirmation-required updates use an <see cref="IRecoverableApplicationConfigurationFileProvider"/>.
        /// </summary>
        public IApplicationConfigurationFileProvider? ConfigurationFileProvider { get; }
    }
}
