// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of the
    /// distributed (high-availability) PushManagement transaction coordinator
    /// and pending-certificate-key store on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class DistributedPushConfigurationBuilderExtensions
    {
        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: opts a server into a distributed
        /// PushManagement transaction that is single across a
        /// <c>RedundantServerSet</c> (OPC 10000-12 §§7.10.2-7.10.11).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registers a
        /// <see cref="DistributedPushConfigurationTransactionCoordinator"/> as
        /// the server's <see cref="IPushConfigurationTransactionCoordinator"/>
        /// and a <see cref="SharedKeyValuePendingCertificateKeyStore"/> as its
        /// <see cref="IPendingCertificateKeyStore"/>. Register an independently enrolled
        /// <see cref="FreshnessProtectedKeyValueStore"/> (for example with
        /// <c>AddSharedRecordFreshness</c>) for the transaction lease and configure
        /// explicit lease membership on each replica. The pending-key store uses the
        /// configured <see cref="ISharedKeyValueStore"/> and
        /// pending-key store keeps a regenerated private key available across
        /// replicas until a matching <c>UpdateCertificate</c> consumes it.
        /// </para>
        /// <para>
        /// This composes transparently with the configured leader election. When
        /// <see cref="DistributedPushConfigurationOptions.RequireLeadership"/> is
        /// set, transaction ownership is additionally restricted to the elected
        /// leader; with the Kubernetes extension configured
        /// (<c>UseKubernetesLeaderElection</c>) that leader is decided by the
        /// Kubernetes-native <c>Lease</c> already registered as the
        /// <see cref="ILeaderElection"/> service - no additional Kubernetes
        /// client is created. Raft and shared-store leader election compose the
        /// same way.
        /// </para>
        /// <para>
        /// The distributed coordinator does not support trust-list updates or issuer imports through
        /// the built-in per-entry certificate and CRL APIs; those APIs cannot atomically verify the
        /// current grant when making the change visible. Trust-list reads remain available.
        /// </para>
        /// <para>
        /// Distributed behaviour is opt-in: a server that does not call this
        /// method keeps the default per-server coordinator and directory-backed
        /// pending-key store, so its behaviour is unchanged. When the shared
        /// store is an external (non in-memory) backend, an
        /// <see cref="IRecordProtector"/> must be configured (via
        /// <see cref="DistributedPushConfigurationOptions.RecordProtectorFactory"/>,
        /// <see cref="DistributedAddressSpaceOptions.RecordProtectorFactory"/> or
        /// a directly-registered service); otherwise the pending-key store fails
        /// closed rather than persisting private keys unprotected.
        /// </para>
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional distributed PushManagement options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedPushConfiguration(
            this IOpcUaServerBuilder builder,
            Action<DistributedPushConfigurationOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new DistributedPushConfigurationOptions();
            configure?.Invoke(options);
            options.Validate();

            if (options.RecordProtectorFactory != null)
            {
                builder.Services.TryAddSingleton(sp => options.RecordProtectorFactory(sp));
            }

            // The coordinator holds mutable per-server transaction state and
            // owns a background lease-renewal loop; it must replace any
            // previously registered coordinator so DependencyInjectionStandardServer
            // resolves exactly this instance.
            builder.Services.RemoveAll<IPushConfigurationTransactionCoordinator>();
            builder.Services.AddSingleton<IPushConfigurationTransactionCoordinator>(sp =>
                new DistributedPushConfigurationTransactionCoordinator(
                    inner: null,
                    store: sp.GetRequiredService<FreshnessProtectedKeyValueStore>(),
                    options: options,
                    telemetry: sp.GetRequiredService<ITelemetryContext>(),
                    timeProvider: sp.GetService<TimeProvider>(),
                    leaderElection: options.RequireLeadership
                        ? sp.GetRequiredService<ILeaderElection>()
                        : null));

            // Replace the default directory-backed pending-key store with the
            // shared-store one so a regenerated key persists across replicas.
            // Fail closed when an external store lacks a record protector.
            builder.Services.RemoveAll<IPendingCertificateKeyStore>();
            builder.Services.AddSingleton<IPendingCertificateKeyStore>(sp =>
                new SharedKeyValuePendingCertificateKeyStore(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    options,
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp)));

            return builder;
        }
    }
}
