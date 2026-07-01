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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration for OPC 10000-4 §6.6.2.2 cross-replica Subscription definition mirroring.
    /// </summary>
    public static class DistributedSubscriptionBuilderExtensions
    {
        /// <summary>
        /// Registers the shared-store backed Subscription definition mirror.
        /// </summary>
        /// <remarks>
        /// The registered <see cref="ISubscriptionStore"/> persists subscription and monitored-item definitions,
        /// retransmission state for <c>Republish</c>, and continuation-point envelopes. This provides the
        /// HighAvailability capability boundary for continuation points: transferred sessions can identify and release
        /// mirrored opaque continuation tokens, but the local Browse/Query/History continuation enumerator state is not
        /// rebuilt on another replica. Monitored-item data/event queues remain outside this registration's scope.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedSubscriptionMirroring(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            builder.Services.TryAddSingleton<ISubscriptionStore>(sp =>
                new SharedKeyValueSubscriptionStore(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetRequiredService<IServiceMessageContext>(),
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp),
                    sp.GetService<ILogger<SharedKeyValueSubscriptionStore>>()));

            return builder;
        }
    }
}
