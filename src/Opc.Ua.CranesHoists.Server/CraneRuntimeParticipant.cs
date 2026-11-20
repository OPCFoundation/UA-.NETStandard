// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Validates equipment data before publication and advertises only committed crane systems.
    /// </summary>
    internal sealed class CraneRuntimeParticipant : ICompanionRegistrationParticipant
    {
        /// <summary>
        /// Enlists one crane runtime in its existing owner's registration.
        /// </summary>
        internal CraneRuntimeParticipant(CranesHoistsBuildContext context, CraneSystemHandle handle)
        {
            m_context = context;
            m_handle = handle;
        }

        /// <inheritdoc/>
        public DiNodeManager Manager => m_context.Robotics.Manager;

        /// <inheritdoc/>
        public BaseObjectState Root => m_handle.State;

        /// <inheritdoc/>
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
        {
            return m_handle.RefreshAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask BindAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        public void Commit()
        {
            m_committed = true;
            m_context.Commit(m_handle);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_committed ? default : m_handle.DisposeAsync();
        }

        private readonly CranesHoistsBuildContext m_context;
        private readonly CraneSystemHandle m_handle;
        private bool m_committed;
    }
}
