// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
