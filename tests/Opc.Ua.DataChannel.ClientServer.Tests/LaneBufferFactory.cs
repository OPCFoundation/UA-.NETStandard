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
using System.Threading;
using Opc.Ua.Bindings;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Observes the actual injectable UASC pools without replacing their allocation or ownership semantics.
    /// </summary>
    internal sealed class LaneBufferFactory : IBufferManagerFactory
    {
        /// <summary>
        /// Gets currently rented, not yet returned buffers.
        /// </summary>
        internal int Outstanding => m_rentals.Count;

        /// <summary>
        /// Gets the total observed rents, proving the pool path was exercised.
        /// </summary>
        internal int Rents => Volatile.Read(ref m_rents);

        /// <inheritdoc/>
        public IBufferManager Create(string name, int maxBufferSize, ITelemetryContext telemetry)
        {
            return new Manager(this, DefaultBufferManagerFactory.Instance.Create(name, maxBufferSize, telemetry));
        }

        /// <summary>
        /// Delegates every pool operation while checking exactly-once returns.
        /// </summary>
        private sealed class Manager(LaneBufferFactory owner, IBufferManager inner) : IBufferManager
        {
            /// <inheritdoc/>
            public string Name => inner.Name;

            /// <inheritdoc/>
            public int MaxSuggestedBufferSize => inner.MaxSuggestedBufferSize;

            /// <inheritdoc/>
            public int GetSuggestedBufferSize(int size) => inner.GetSuggestedBufferSize(size);

            /// <inheritdoc/>
            public int GetExpectedBufferSize(int size) => inner.GetExpectedBufferSize(size);

            /// <inheritdoc/>
            public byte[] TakeBuffer(int size, string name) => TakeBuffer(size, name, CancellationToken.None);

            /// <inheritdoc/>
            public byte[] TakeBuffer(int size, string name, CancellationToken ct)
            {
                byte[] buffer = inner.TakeBuffer(size, name, ct);
                if (!owner.m_rentals.TryAdd(buffer, name))
                {
                    throw new InvalidOperationException("The pool rented a buffer that is still in use.");
                }
                Interlocked.Increment(ref owner.m_rents);
                return buffer;
            }

            /// <inheritdoc/>
            public void TransferBuffer(byte[]? buffer, string name) => inner.TransferBuffer(buffer, name);

            /// <inheritdoc/>
            public void Lock(byte[] buffer) => inner.Lock(buffer);

            /// <inheritdoc/>
            public void Unlock(byte[] buffer) => inner.Unlock(buffer);

            /// <inheritdoc/>
            public void ReturnBuffer(byte[]? buffer, string name)
            {
                if (buffer != null && !owner.m_rentals.TryRemove(buffer, out _))
                {
                    throw new InvalidOperationException("UASC returned an unowned or already returned buffer.");
                }
                inner.ReturnBuffer(buffer, name);
            }
        }

        private readonly ConcurrentDictionary<byte[], string> m_rentals = new();
        private int m_rents;
    }
}
