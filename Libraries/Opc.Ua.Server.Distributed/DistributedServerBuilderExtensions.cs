/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Fluent registration of distributed / high-availability server
    /// features on the <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class DistributedServerBuilderExtensions
    {
        /// <summary>
        /// Drives the live <c>Server.ServiceLevel</c> node from the supplied
        /// <see cref="IServiceLevelProvider"/> (e.g.
        /// <see cref="LeaderServiceLevelProvider"/> for active/passive). Use a
        /// <see cref="ConstantServiceLevelProvider"/> to report a fixed level.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="serviceLevelProvider">The service-level source.</param>
        public static IOpcUaServerBuilder AddServerServiceLevel(
            this IOpcUaServerBuilder builder,
            IServiceLevelProvider serviceLevelProvider)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (serviceLevelProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceLevelProvider));
            }

            builder.Services.AddSingleton<IServerStartupTask>(
                new ServiceLevelStartupTask(serviceLevelProvider));
            return builder;
        }

        /// <summary>
        /// Registers dependency-injection building blocks for a distributed
        /// address space.
        /// </summary>
        /// <remarks>
        /// This registers the shared key/value store, leader election,
        /// service-level provider, and service-level startup task. Node-state
        /// stores and per-node-manager synchronizers are wired later at server
        /// startup, when the server message context is available.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional distributed address-space options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedAddressSpace(
            this IOpcUaServerBuilder builder,
            Action<DistributedAddressSpaceOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new DistributedAddressSpaceOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(sp =>
                options.KeyValueStoreFactory?.Invoke(sp) ?? new InMemorySharedKeyValueStore());

            if (options.RecordProtectorFactory != null)
            {
                builder.Services.TryAddSingleton<IRecordProtector>(
                    sp => options.RecordProtectorFactory(sp));
            }

            builder.Services.TryAddSingleton<ILeaderElection>(sp =>
                options.UseLeaderElection
                    ? new SharedStoreLeaseElection(
                        sp.GetRequiredService<ISharedKeyValueStore>(),
                        options.LeaseKey,
                        options.NodeId,
                        options.LeaseDuration,
                        options.RenewInterval)
                    : new StaticLeaderElection(true));

            builder.Services.TryAddSingleton<IServiceLevelProvider>(sp =>
                new LeaderServiceLevelProvider(
                    sp.GetRequiredService<ILeaderElection>(),
                    options.LeaderServiceLevel,
                    options.StandbyServiceLevel));

            // The service-level startup task updates Server.ServiceLevel from
            // the provider.
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new ServiceLevelStartupTask(sp.GetRequiredService<IServiceLevelProvider>()));

            // The address-space startup task builds the node-state store with
            // the server message context (only available at startup), starts
            // leader election, and attaches a synchronizer to every node
            // manager that opts in via ILocalAddressSpaceSource.
            builder.Services.AddSingleton<IServerStartupTask>(sp =>
                new DistributedAddressSpaceStartupTask(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetRequiredService<ILeaderElection>(),
                    sp.GetService<IRecordProtector>()));

            return builder;
        }

        /// <summary>
        /// Registers a distributed session manager so a client can fail over to
        /// a standby replica and reconnect by re-running <c>ActivateSession</c>
        /// (OPC UA HotAndMirrored fast reconnect).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This shares the same <see cref="ISharedKeyValueStore"/> and
        /// <see cref="IRecordProtector"/> as
        /// <see cref="UseDistributedAddressSpace"/> when composed with it; used
        /// on its own it falls back to an in-memory store and a no-op protector
        /// (suitable for development only). The mirrored session record is
        /// encrypted at rest and the server nonce is single-use across the
        /// replica set.
        /// </para>
        /// <para>
        /// The safe default is re-authentication on failover. Set
        /// <see cref="DistributedSessionOptions.EnableFastReconnect"/> to opt
        /// into the mirrored fast reconnect; even then a reconnect performs the
        /// full <c>ActivateSession</c> client-signature validation — the token
        /// is never an authenticator on its own. See
        /// <c>Docs/HighAvailability.md</c>.
        /// </para>
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional distributed session options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedSessions(
            this IOpcUaServerBuilder builder,
            Action<DistributedSessionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new DistributedSessionOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());

            builder.Services.TryAddSingleton<ISessionManagerFactory>(sp =>
                new DistributedSessionManagerFactory(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetService<IRecordProtector>(),
                    options));

            return builder;
        }
    }
}
