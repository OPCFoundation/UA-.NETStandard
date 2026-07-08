/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Election mechanism used to elect the active instance of a redundant PubSub set.
    /// </summary>
    public enum PubSubRedundancyElection
    {
        /// <summary>
        /// Whole-instance active/standby driven by a shared
        /// <see cref="Ua.Redundancy.ILeaderElection"/> (Raft or Kubernetes lease).
        /// </summary>
        LeaderElection,

        /// <summary>
        /// Per-component leases held in a shared-store
        /// <see cref="IPubSubLeaseStore"/> (compare-and-swap with fencing tokens).
        /// </summary>
        LeaseStore
    }

    /// <summary>
    /// Options for wiring distributed PubSub high-availability (OPC UA Part 14 §9.1.6).
    /// </summary>
    public sealed class PubSubRedundancyOptions
    {
        /// <summary>
        /// Gets or sets the redundancy behaviour a standby instance keeps warm.
        /// </summary>
        public PubSubRedundancyMode Mode { get; set; } = PubSubRedundancyMode.Warm;

        /// <summary>
        /// Gets or sets the election mechanism used to pick the active instance.
        /// </summary>
        public PubSubRedundancyElection Election { get; set; } = PubSubRedundancyElection.LeaderElection;

        /// <summary>
        /// Gets or sets the stable identity of this instance used for lease ownership.
        /// </summary>
        public string OwnerId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the lease time-to-live used by the lease-store election.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);
    }
}
