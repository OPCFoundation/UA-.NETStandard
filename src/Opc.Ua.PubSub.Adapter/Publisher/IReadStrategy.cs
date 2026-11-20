// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// Supplies current values for a set of external-server node attributes to the
    /// external-server published-dataset source. Implementations differ only in how the
    /// values are obtained: a cyclic Read service call per publish cycle, or a latest-value
    /// cache maintained by a client Subscription with monitored items.
    /// </summary>
    public interface IReadStrategy
    {
        /// <summary>
        /// Returns the current <see cref="DataValue"/> for each requested node attribute,
        /// aligned positionally to <paramref name="nodesToRead"/>.
        /// </summary>
        /// <param name="nodesToRead">
        /// The node attributes to resolve (typically a PublishedDataSet's published variables).
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken cancellationToken = default);
    }
}
