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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Bindings;

namespace Opc.Ua.Stress.Tests.Channels.Fakes
{
    /// <summary>
    /// Channel binding registry that creates <see cref="FakeTransport"/> instances.
    /// </summary>
    public sealed class FakeChannelBindings : ITransportChannelBindings, IDisposable
    {
        /// <summary>
        /// Initializes a new fake binding registry with the default transport factory.
        /// </summary>
        public FakeChannelBindings()
        {
        }

        /// <summary>
        /// Initializes a new fake binding registry with a custom transport factory.
        /// </summary>
        /// <param name="factory">Factory called for each requested URI scheme.</param>
        public FakeChannelBindings(Func<string, FakeTransport> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Gets a snapshot of all fake transports created by this registry.
        /// </summary>
        public IReadOnlyList<FakeTransport> Created => [.. m_created];

        /// <summary>
        /// Configures the next open-capable operation to block on the supplied barrier.
        /// </summary>
        /// <param name="barrier">The barrier that releases the transport operation.</param>
        public void ConfigureNextOpenToBlockOn(ChaosBarrier barrier)
        {
            if (barrier == null)
            {
                throw new ArgumentNullException(nameof(barrier));
            }

            lock (m_lock)
            {
                FakeTransport[] transports = [.. m_created];
                if (transports.Length == 0)
                {
                    m_pendingOpenBarrier = barrier;
                    return;
                }

                foreach (FakeTransport transport in transports)
                {
                    transport.OpenBarrier = barrier;
                }
            }
        }

        /// <inheritdoc/>
        public ITransportChannel Create(string uriScheme, ITelemetryContext telemetry)
        {
            if (uriScheme == null)
            {
                throw new ArgumentNullException(nameof(uriScheme));
            }

            FakeTransport? transport = null;
            try
            {
                // CA2000: Created transports are owned by m_created and disposed by Dispose.
                // TODO: remove this suppression when CA2000 understands the ownership transfer.
#pragma warning disable CA2000
                transport = m_factory?.Invoke(uriScheme) ?? new FakeTransport(telemetry);
#pragma warning restore CA2000
                ChaosBarrier? openBarrier;
                lock (m_lock)
                {
                    openBarrier = m_pendingOpenBarrier;
                    m_pendingOpenBarrier = null;
                }

                if (openBarrier != null)
                {
                    transport.OpenBarrier = openBarrier;
                }

                m_created.Enqueue(transport);
                FakeTransport result = transport;
                transport = null;
                return result;
            }
            finally
            {
                transport?.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            while (m_created.TryDequeue(out FakeTransport? transport))
            {
                transport.Dispose();
            }
        }

        private readonly Lock m_lock = new();
        private readonly ConcurrentQueue<FakeTransport> m_created = [];
        private readonly Func<string, FakeTransport>? m_factory;
        private ChaosBarrier? m_pendingOpenBarrier;
    }
}
