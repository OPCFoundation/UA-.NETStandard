// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Supplies the location of one or more identified sources to any companion
    /// model that publishes location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single seam a server author implements to answer "where is
    /// it?". One implementation serves every consumer: OPC 10000-211 (GPOS)
    /// builds structured global locations from it, and OPC 10030 (ISA-95) fills
    /// its <c>GeoSpatialLocationType</c> variables from it. The contract is
    /// deliberately free of any companion-model type so that
    /// <c>Opc.Ua.Server</c> stays independent of the models built on top of it.
    /// </para>
    /// <para>
    /// A provider serves many sources, each named by a stable, provider-local
    /// <c>sourceId</c> that the host supplies when it binds a variable.
    /// Implementations should be safe for concurrent calls; the binding layer
    /// does not serialise access.
    /// </para>
    /// </remarks>
    public interface IGeoLocationProvider
    {
        /// <summary>
        /// Whether <see cref="WatchAsync"/> delivers updates. When
        /// <c>false</c>, the host never subscribes and instead polls
        /// <see cref="ReadAsync"/> at the bound variable's sampling interval.
        /// </summary>
        bool SupportsPush { get; }

        /// <summary>
        /// Reads the current location of a source.
        /// </summary>
        /// <param name="sourceId">
        /// The stable, provider-local identifier of the source.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The current sample. A provider that cannot serve
        /// <paramref name="sourceId"/> throws rather than returning a sample
        /// for a different source.
        /// </returns>
        ValueTask<GeoLocationSample> ReadAsync(
            string sourceId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams location updates for a source until the token is cancelled.
        /// Only called when <see cref="SupportsPush"/> is <c>true</c>.
        /// </summary>
        /// <param name="sourceId">
        /// The stable, provider-local identifier of the source.
        /// </param>
        /// <param name="cancellationToken">
        /// A token that ends the stream when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of samples.
        /// </returns>
        IAsyncEnumerable<GeoLocationSample> WatchAsync(
            string sourceId,
            CancellationToken cancellationToken = default);
    }
}
