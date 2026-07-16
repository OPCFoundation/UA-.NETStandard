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
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration for cross-replica session mirroring.
    /// </summary>
    public static class DistributedSessionMirroringBuilderExtensions
    {
        /// <summary>
        /// Registers distributed session mirroring for HotAndMirrored or Transparent failover.
        /// </summary>
        /// <remarks>
        /// The mirrored session record is keyed by a digest of the session authentication token and is protected
        /// with the configured <see cref="IRecordProtector"/>. Enable
        /// <see cref="DistributedSessionOptions.EnableFastReconnect"/> to allow a backup replica to materialize the
        /// session during <c>ActivateSession</c>; validation still runs through the core activation path and consumes
        /// the mirrored server nonce once across the replica set.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional session mirroring options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedSessionMirroring(
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
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp),
                    options));

            return builder;
        }
    }
}
