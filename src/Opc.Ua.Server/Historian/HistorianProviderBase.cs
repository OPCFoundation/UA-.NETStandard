/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Convenience abstract base class for <see cref="IHistorianProvider"/>
    /// implementations that want to opt into individual capability
    /// interfaces via overrides instead of explicit interface
    /// implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class deliberately does <strong>not</strong> implement the
    /// optional capability interfaces. Concrete providers add the
    /// interface declarations to their class signature themselves:
    /// the dispatcher inspects the concrete type when routing reads
    /// and updates, so opting in is a matter of writing
    /// <c>class MyProvider : HistorianProviderBase, IHistorianDataProvider</c>.
    /// </para>
    /// </remarks>
    public abstract class HistorianProviderBase : IHistorianProvider
    {
        /// <inheritdoc/>
        public virtual ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
        {
            return new(true);
        }

        /// <inheritdoc/>
        public virtual ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            return new(HistorianNodeCapabilities.ReadOnly);
        }

        /// <summary>
        /// Helper that returns a status list pre-filled with the same value.
        /// </summary>
        protected static ArrayOf<StatusCode> RepeatStatus(
            StatusCode code,
            int count)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < count; i++)
            {
                statuses[i] = code;
            }
            return statuses;
        }
    }
}
