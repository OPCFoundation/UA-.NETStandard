// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Application-owned immutable observation source for simulations and equipment-adapter composition.
    /// It does not generate measurements, qualification time, calibration dates or result data.
    /// </summary>
    public sealed class InMemoryGmsProvider : IGmsProvider
    {
        /// <summary>
        /// Captures the first immutable provider observation.
        /// </summary>
        public InMemoryGmsProvider(GmsSystemSnapshot snapshot)
        {
            m_snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.MachineTool == null)
            {
                throw new ArgumentException("Inherited MachineTool observations are required.", nameof(snapshot));
            }
        }

        /// <inheritdoc/>
        public ValueTask<GmsSystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<GmsSystemSnapshot>(Volatile.Read(ref m_snapshot));
        }

        /// <summary>
        /// Replaces the immutable observation and assigns a strictly increasing inherited revision.
        /// </summary>
        public ValueTask SetSnapshotAsync(GmsSystemSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.MachineTool == null)
            {
                throw new ArgumentException("Inherited MachineTool observations are required.", nameof(snapshot));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_snapshot = snapshot with
                {
                    MachineTool = snapshot.MachineTool with { Revision = checked(m_snapshot.MachineTool.Revision + 1) }
                };
            }
            return default;
        }

        /// <summary>The short immutable-observation replacement gate.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The complete last application-supplied observation.</summary>
        private GmsSystemSnapshot m_snapshot;
    }
}
