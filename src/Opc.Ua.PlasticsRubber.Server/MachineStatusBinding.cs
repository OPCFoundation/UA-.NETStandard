// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Requests device sleep transitions without changing the server host's power state.
    /// </summary>
    public interface IMachineStatusProvider
    {
        /// <summary>
        /// Commits a request to activate or deactivate the machine's sleep mode.
        /// </summary>
        ValueTask SetSleepModeAsync(
            PlasticsRubberRequestContext context,
            bool enabled,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds optional sleep methods while the device remains authoritative for its actual mode.
    /// </summary>
    public sealed class MachineStatusBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the selected generated methods to the device provider.
        /// </summary>
        public MachineStatusBinding(
            MachineStatusState node,
            ISystemContext context,
            IMachineStatusProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            if (node.ActivateSleepMode != null)
            {
                m_binding.Bind(node.ActivateSleepMode, (request, ct) =>
                    provider.SetSleepModeAsync(request, true, ct));
            }
            if (node.DeactivateSleepMode != null)
            {
                m_binding.Bind(node.DeactivateSleepMode, (request, ct) =>
                    provider.SetSleepModeAsync(request, false, ct));
            }
        }

        /// <summary>
        /// Gets the generated status instance and its observed device mode.
        /// </summary>
        public MachineStatusState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// The common operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
