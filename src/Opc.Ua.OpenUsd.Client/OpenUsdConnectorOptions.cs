/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Configures an <see cref="OpenUsdConnector"/>. The progressive-disclosure entry
    /// points (<c>new OpenUsdConnector(session, sink)</c> and the DI
    /// <c>AddOpenUsdConnector(...)</c> extensions) build one of these; advanced callers
    /// construct it directly.
    /// </summary>
    public sealed class OpenUsdConnectorOptions
    {
        /// <summary>
        /// Opt-in gate for actuating <c>UsdToUaCommand</c> bindings. Command bindings are
        /// always discovered, but the connector refuses to actuate one unless this is
        /// <c>true</c> (fail-closed). Read-only telemetry/alarm/history bindings are
        /// unaffected. Default <c>false</c>.
        /// </summary>
        public bool EnableCommands { get; set; }

        /// <summary>
        /// Optional factory that opens sessions to other servers for cross-server
        /// components (§5.14). When <c>null</c>, a cross-server component is composed
        /// structurally (its reference prim is authored) but its remote bindings are not
        /// driven.
        /// </summary>
        public Func<string, CancellationToken, Task<ISession>>? RemoteSessionFactory { get; set; }

        /// <summary>
        /// Maximum number of bytes read for a single served USD asset. Fails closed
        /// against a server that streams unbounded data. Default 64 MiB.
        /// </summary>
        public int MaxAssetBytes { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Maximum total number of bytes read across a whole asset-closure fetch. Default
        /// 256 MiB.
        /// </summary>
        public long MaxTotalAssetBytes { get; set; } = 256L * 1024 * 1024;

        /// <summary>
        /// §5.15 requires the connector to verify each delivered asset's digest and to
        /// never silently mix unverified bytes into the stage. When <c>true</c> (the
        /// default) an asset delivered without a digest is refused; when <c>false</c> it
        /// is cached but reported with <c>DigestVerified = false</c> and is never
        /// composed. Fail-closed by default.
        /// </summary>
        public bool RequireAssetDigests { get; set; } = true;

        /// <summary>
        /// Publishing interval of the live subscription, in milliseconds. This is the
        /// upper bound on how often the twin can move, so it is chosen for animation
        /// rather than for the usual telemetry cadence. Default 50 ms (20 Hz).
        /// </summary>
        public int PublishingIntervalMilliseconds { get; set; } = 50;

        /// <summary>
        /// Sampling interval requested for each live monitored item, in milliseconds.
        /// Sampling faster than the publishing interval only fills the queue, so this
        /// normally matches it. Default 50 ms (20 Hz).
        /// </summary>
        public int SamplingIntervalMilliseconds { get; set; } = 50;
    }
}
