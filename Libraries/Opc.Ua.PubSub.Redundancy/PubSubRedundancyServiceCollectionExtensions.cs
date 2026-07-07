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
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Dependency-injection registration that backs the OPC UA PubSub high-availability
    /// seams with the distributed redundancy building blocks (OPC UA Part 14 §9.1.6).
    /// </summary>
    /// <remarks>
    /// Register an <see cref="ISharedKeyValueStore"/> (and, for
    /// <see cref="PubSubRedundancyElection.LeaderElection"/>, an
    /// <see cref="ILeaderElection"/>) plus an <see cref="IRecordProtector"/> in the same
    /// container, then call <see cref="AddPubSubRedundancy"/>. It replaces the default
    /// in-process PubSub activation coordinator, runtime-state store, and security-key store
    /// with distributed, shared-store-backed implementations so multiple PubSub instances run
    /// as an active/standby redundant set.
    /// </remarks>
    public static class PubSubRedundancyServiceCollectionExtensions
    {
        /// <summary>
        /// Backs the PubSub high-availability seams with the distributed redundancy
        /// implementations, resolving the shared store, leader election, and record protector
        /// from the container.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional configuration of the redundancy options.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <see langword="null"/>.
        /// </exception>
        public static IServiceCollection AddPubSubRedundancy(
            this IServiceCollection services,
            Action<PubSubRedundancyOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new PubSubRedundancyOptions();
            configure?.Invoke(options);

            if (options.Election == PubSubRedundancyElection.LeaseStore)
            {
                services.Replace(ServiceDescriptor.Singleton<IPubSubLeaseStore>(sp =>
                    new SharedStorePubSubLeaseStore(
                        sp.GetRequiredService<ISharedKeyValueStore>(),
                        sp.GetService<TimeProvider>())));
                services.Replace(ServiceDescriptor.Singleton<IPubSubActivationCoordinator>(sp =>
                    new LeaseActivationCoordinator(
                        sp.GetRequiredService<IPubSubLeaseStore>(),
                        sp.GetService<ITelemetryContext>(),
                        options.OwnerId,
                        options.LeaseDuration,
                        timeProvider: sp.GetService<TimeProvider>())));
            }
            else
            {
                services.Replace(ServiceDescriptor.Singleton<IPubSubActivationCoordinator>(sp =>
                    new LeaderElectionActivationCoordinator(
                        sp.GetRequiredService<ILeaderElection>(),
                        sp.GetService<ITelemetryContext>())));
            }

            services.Replace(ServiceDescriptor.Singleton<IPubSubRuntimeStateStore>(sp =>
                new SharedStorePubSubRuntimeStateStore(sp.GetRequiredService<ISharedKeyValueStore>())));

            services.Replace(ServiceDescriptor.Singleton<IPubSubSecurityKeyStore>(sp =>
            {
                ISharedKeyValueStore store = sp.GetRequiredService<ISharedKeyValueStore>();
                return new SharedStorePubSubSecurityKeyStore(
                    store,
                    ResolveProtectorOrThrow(sp, store),
                    sp.GetRequiredService<IServiceMessageContext>());
            }));

            if (options.Mode == PubSubRedundancyMode.Hot)
            {
                services.Replace(ServiceDescriptor.Singleton<IPubSubWriterCheckpointStore>(sp =>
                    new SharedStorePubSubWriterCheckpointStore(
                        sp.GetRequiredService<ISharedKeyValueStore>())));
            }

            return services;
        }

        private static IRecordProtector ResolveProtectorOrThrow(
            IServiceProvider serviceProvider,
            ISharedKeyValueStore store)
        {
            IRecordProtector? protector = serviceProvider.GetService<IRecordProtector>();
            if (store is InMemorySharedKeyValueStore)
            {
                return protector ?? NullRecordProtector.Instance;
            }

            if (protector is null or NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "A networked ISharedKeyValueStore backing the shared PubSub security-key store requires a " +
                    "real IRecordProtector so SecurityGroup keys are encrypted at rest. Register an " +
                    "AesCbcHmacRecordProtector, or use an in-memory store for a single-instance / test setup.");
            }

            return protector;
        }
    }
}
