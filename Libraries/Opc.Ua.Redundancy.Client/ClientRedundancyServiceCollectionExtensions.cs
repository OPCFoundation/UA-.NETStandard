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
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Dependency-injection registration for the CRDT components used by client
    /// replica sets (the cross-process gossip store that shares the leader's
    /// session secrets between cooperating clients).
    /// </summary>
    public static class ClientRedundancyServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a CRDT-backed <see cref="ISharedKeyValueStore"/> for a client
        /// replica set, gossiping over the supplied transport. The same shared
        /// <see cref="CrdtSharedKeyValueStore"/> is used by the server side, so a
        /// single CRDT implementation backs both.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddCrdtClientSharedStore(
            this IServiceCollection services,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport> transportFactory)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (transportFactory == null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }

            services.TryAddSingleton<ISharedKeyValueStore>(sp =>
            {
                return new CrdtSharedKeyValueStore(
                    replicaId,
                    transportFactory(sp),
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    CrdtReaderOptions.Default);
            });
            return services;
        }
    }
}
