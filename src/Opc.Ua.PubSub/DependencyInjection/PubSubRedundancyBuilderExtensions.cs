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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.PubSub.Redundancy;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IPubSubBuilder"/> extensions for PubSub high-availability
    /// (redundancy): register an activation coordinator or lease store by type,
    /// or enable lease-based activation in one call (OPC UA Part 14 §9.1.6).
    /// </summary>
    /// <remarks>
    /// Complements the instance-based
    /// <see cref="IPubSubBuilder.WithActivationCoordinator(IPubSubActivationCoordinator)"/>
    /// and <see cref="IPubSubBuilder.WithLeaseStore(IPubSubLeaseStore)"/> with
    /// container-constructed (type) registrations and a lease-election preset,
    /// keeping the redundancy surface consistent with the rest of the PubSub
    /// DI builder.
    /// </remarks>
    public static class PubSubRedundancyBuilderExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TCoordinator"/> as the PubSub
        /// activation coordinator, constructed by the container.
        /// </summary>
        /// <typeparam name="TCoordinator">Coordinator implementation.</typeparam>
        /// <param name="builder">PubSub builder.</param>
        /// <returns>The same <paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IPubSubBuilder WithActivationCoordinator<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCoordinator>(
            this IPubSubBuilder builder)
            where TCoordinator : class, IPubSubActivationCoordinator
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<IPubSubActivationCoordinator, TCoordinator>();
            return builder;
        }

        /// <summary>
        /// Registers <typeparamref name="TStore"/> as the PubSub lease store,
        /// constructed by the container.
        /// </summary>
        /// <typeparam name="TStore">Lease store implementation.</typeparam>
        /// <param name="builder">PubSub builder.</param>
        /// <returns>The same <paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IPubSubBuilder WithLeaseStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(
            this IPubSubBuilder builder)
            where TStore : class, IPubSubLeaseStore
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<IPubSubLeaseStore, TStore>();
            return builder;
        }

        /// <summary>
        /// Enables lease-based PubSub activation (leader election) using a
        /// <see cref="LeaseActivationCoordinator"/> over the registered
        /// <see cref="IPubSubLeaseStore"/> — or an in-memory store when none is
        /// registered. Register a distributed <see cref="IPubSubLeaseStore"/>
        /// (via <see cref="WithLeaseStore{TStore}(IPubSubBuilder)"/> or
        /// <see cref="IPubSubBuilder.WithLeaseStore(IPubSubLeaseStore)"/>) for
        /// genuine cross-instance failover.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional lease-activation options callback.</param>
        /// <returns>The same <paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IPubSubBuilder WithLeaseActivation(
            this IPubSubBuilder builder,
            Action<LeaseActivationOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new LeaseActivationOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<IPubSubLeaseStore, InMemoryPubSubLeaseStore>();
            builder.Services.AddSingleton<IPubSubActivationCoordinator>(sp =>
                new LeaseActivationCoordinator(
                    sp.GetRequiredService<IPubSubLeaseStore>(),
                    sp.GetService<ITelemetryContext>(),
                    options.OwnerId,
                    options.LeaseDuration,
                    options.RenewInterval,
                    options.RetryInterval,
                    sp.GetService<TimeProvider>()));
            return builder;
        }
    }
}
