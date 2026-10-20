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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// A deterministic single-host observation store for reference applications and tests.
    /// It neither executes CNC commands nor generates measurements.
    /// </summary>
    public sealed class InMemoryMachineToolProvider : IMachineToolProvider
    {
        /// <summary>
        /// Creates an isolated store retaining only its latest supplied observation.
        /// </summary>
        /// <param name="snapshot">The initial equipment observation.</param>
        public InMemoryMachineToolProvider(MachineToolSnapshot snapshot)
        {
            m_snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <inheritdoc/>
        public ValueTask<MachineToolSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<MachineToolSnapshot>(m_snapshot);
            }
        }

        /// <summary>
        /// Replaces the observation and assigns its next revision without altering any other provider.
        /// </summary>
        /// <param name="snapshot">The observation to retain.</param>
        /// <param name="cancellationToken">Cancels before committing the observation.</param>
        public ValueTask SetSnapshotAsync(
            MachineToolSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                m_snapshot = snapshot with { Revision = checked(m_snapshot.Revision + 1) };
            }
            return default;
        }

        /// <summary>Serializes replacement of the single immutable observation.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The retained per-provider observation.</summary>
        private MachineToolSnapshot m_snapshot;
    }
}
