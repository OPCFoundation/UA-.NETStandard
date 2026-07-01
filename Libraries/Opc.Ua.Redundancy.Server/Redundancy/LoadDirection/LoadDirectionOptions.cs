/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Options for the extension-beyond-§6.6 <c>GetEndpoints</c> load-direction feature that publishes and reads the
    /// per-peer health <c>ServiceLevel</c> and load weight used to direct a Client to the best Server in a
    /// <c>RedundantServerSet</c>.
    /// </summary>
    public sealed class LoadDirectionOptions
    {
        /// <summary>
        /// The shared-store key prefix for the per-peer health <c>ServiceLevel</c> signal (eligibility). This keyspace
        /// can be routed to the strongly-consistent store (Raft) for a deterministic redirect target.
        /// </summary>
        public string ServiceLevelKeyPrefix { get; set; } = "svc/";

        /// <summary>
        /// The shared-store key prefix for the per-peer load weight (tie-breaking only). This keyspace is always
        /// eventually consistent and coalesced, because it is high-churn and a stale load tie-break is harmless.
        /// </summary>
        public string LoadKeyPrefix { get; set; } = "load/";

        /// <summary>
        /// The shared-store key prefix for the per-peer published <c>EndpointDescription</c>s that the local Server
        /// returns when it directs a Client to a peer. Low-churn (published at startup and on certificate rotation),
        /// so it can be routed to the strongly-consistent store alongside the health signal.
        /// </summary>
        public string EndpointKeyPrefix { get; set; } = "endpoint/";

        /// <summary>
        /// How long a gossiped per-peer record is considered fresh. A peer whose health record is older than this is
        /// excluded from direction (fail-safe); a load record older than this is treated as unknown for tie-breaking.
        /// </summary>
        public TimeSpan StalenessWindow { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The minimum interval between load-weight publishes. Coalesces high-churn load updates into at most one
        /// write per interval so a per-tick shared-store write (and, under strong consistency, per-tick quorum) is
        /// avoided.
        /// </summary>
        public TimeSpan LoadPublishInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Sub-band size (in <c>ServiceLevel</c> units) applied <b>within</b> the Healthy range (200–255) when ranking
        /// eligibility. The default <c>0</c> treats the entire Healthy range as a single eligibility tier, so equally
        /// healthy peers are load-balanced by the separate load weight rather than redirected on minor health jitter.
        /// A positive value sub-divides the Healthy range so a meaningfully healthier peer is preferred.
        /// </summary>
        public int HealthSubBandSize { get; set; }

        /// <summary>
        /// Quantization band size (in load-weight units) applied when tie-breaking equally-eligible peers by load.
        /// Peers whose load falls in the same band are treated as tied and one is chosen at random, damping the
        /// herd/oscillation that a strict least-loaded choice would cause.
        /// </summary>
        public int LoadBandSize { get; set; } = 16;

        /// <summary>
        /// The dedicated balancing discovery URL that opts a Client into load direction. A <c>GetEndpoints</c> request
        /// whose <c>endpointUrl</c> matches this value may be answered with a peer's endpoints; requests to any other
        /// (normal) discovery URL are unaffected. When empty, load direction never redirects (publish-only).
        /// </summary>
        public string BalancingEndpointUrl { get; set; } = string.Empty;

        /// <summary>
        /// When <c>true</c>, the eligibility keyspaces (health <c>ServiceLevel</c> and the endpoint directory) are
        /// routed to the linearizable (Raft) store in the eventual/hybrid consistency mode, giving a deterministic
        /// redirect target; the high-churn load weight always stays eventual. Only takes effect when the shared store
        /// is configured with <c>UseRedundancyConsistency</c>. Default <c>false</c> (all direction keyspaces eventual).
        /// </summary>
        public bool StrongEligibility { get; set; }
    }
}
