// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// The umbrella interface implemented by every historian provider.
    /// Providers opt into specific Part 11 features by additionally
    /// implementing one or more of the narrow capability interfaces:
    /// <see cref="IHistorianDataProvider"/>,
    /// <see cref="IHistorianModifiedProvider"/>,
    /// <see cref="IHistorianAtTimeProvider"/>,
    /// <see cref="IHistorianProcessedProvider"/>,
    /// <see cref="IHistorianAnnotationProvider"/>, or
    /// <see cref="IHistorianEventProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This umbrella exists so consumers can resolve "the historian for a
    /// node" without first knowing which capability they need. The
    /// dispatcher inspects which sub-interfaces the concrete provider
    /// implements and routes calls accordingly. Capability advertising
    /// goes through <see cref="GetCapabilitiesAsync"/> — a provider may
    /// implement <see cref="IHistorianDataProvider"/> but advertise
    /// <see cref="HistorianNodeCapabilities.InsertData"/> = <c>false</c>
    /// for a specific read-only node.
    /// </para>
    /// </remarks>
    public interface IHistorianProvider
    {
        /// <summary>
        /// Returns <c>true</c> when the provider can serve history for the
        /// specified node. False values let the registry continue resolving
        /// against other providers / the default fallback.
        /// </summary>
        /// <param name="nodeId">The historizing variable or notifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Whether this provider serves history for the requested node.</returns>
        ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct);

        /// <summary>
        /// Returns the per-node capability set.
        /// </summary>
        /// <param name="nodeId">The historizing variable or notifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The supported history operations and limits for the requested node.</returns>
        ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct);
    }
}
