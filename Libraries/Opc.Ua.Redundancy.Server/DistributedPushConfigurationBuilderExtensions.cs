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
        /// <see cref="IPendingCertificateKeyStore"/>, both backed by the shared
        /// <see cref="ISharedKeyValueStore"/> already used by the other
        /// distributed features (address space, sessions). The coordinator uses
        /// a shared-store compare-and-swap lease to guarantee that at most one
        /// replica has an active PushManagement transaction at a time, and the
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

            // Share the same shared key/value store as the other distributed
            // features. TryAdd so a store already registered by
            // UseDistributedAddressSpace / UseDistributedSessions (for example a
            // Redis-backed store) is reused rather than replaced.
            builder.Services.TryAddSingleton<ISharedKeyValueStore>(
                _ => new InMemorySharedKeyValueStore());

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
                    store: sp.GetRequiredService<ISharedKeyValueStore>(),
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
