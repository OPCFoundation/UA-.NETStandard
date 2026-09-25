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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Registers one synchronization job using caller-owned endpoints and state.
    /// Hosts supply ITelemetryContext and may register a TimeProvider.
    /// </summary>
    public static class XRegistrySyncServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the options, state store, synchronizer, and offline state manager as singletons for one job.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="options">The job identity, endpoint scopes, conflict policy, and operation limits.</param>
        /// <param name="opcUa">The caller-owned endpoint representing the OPC UA registry.</param>
        /// <param name="http">The caller-owned endpoint representing the HTTP registry.</param>
        /// <param name="stateStore">The caller-owned store for the job's baselines and retained evidence.</param>
        /// <returns>The same service collection for further registration.</returns>
        /// <remarks>
        /// Register <see cref="ITelemetryContext"/> before resolving the synchronizer. An optional registered
        /// <see cref="TimeProvider"/> supplies deadlines and timestamps; otherwise the system provider is used.
        /// This method does not start a polling loop or transfer ownership of the supplied endpoints or store.
        /// The host schedules passes and disposes those instances after their use.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// A supplied argument or an options caller context is null.
        /// </exception>
        /// <exception cref="ArgumentException">The synchronization limits are invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The configured conflict policy is not recognized.</exception>
        public static IServiceCollection AddXRegistrySynchronization(
            this IServiceCollection services,
            XRegistrySyncOptions options,
            IXRegistryEndpoint opcUa,
            IXRegistryEndpoint http,
            IXRegistrySyncStateStore stateStore)
        {
            services.ThrowIfNull(nameof(services));
            options.ThrowIfNull(nameof(options));
            opcUa.ThrowIfNull(nameof(opcUa));
            http.ThrowIfNull(nameof(http));
            stateStore.ThrowIfNull(nameof(stateStore));
            options.Validate();
            services.AddSingleton(options);
            services.AddSingleton(stateStore);
            services.AddSingleton(provider => new XRegistrySynchronizer(
                opcUa, http, stateStore, options, provider.GetRequiredService<ITelemetryContext>(),
                provider.GetService<TimeProvider>()));
            services.AddSingleton(provider => new XRegistrySyncStateManager(
                stateStore, options.JobId, provider.GetService<TimeProvider>(),
                options.MaximumStateBytes, options.MaximumJsonDepth));
            return services;
        }
    }
}
