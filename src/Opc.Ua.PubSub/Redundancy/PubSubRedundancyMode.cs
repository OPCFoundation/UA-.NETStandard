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

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// PubSub redundancy behaviour for a redundant set of publishers or
    /// subscribers, per OPC UA Part 14 §9.1.6.
    /// </summary>
    /// <remarks>
    /// The mode selects how much runtime state a standby instance keeps
    /// warm so it can take over from a failed active instance. Cold rebuilds
    /// from the shared configuration/runtime-state stores on failover, warm
    /// keeps the configuration enabled but paused, and hot additionally
    /// tracks live sequence state so take-over introduces no gap.
    /// </remarks>
    public enum PubSubRedundancyMode
    {
        /// <summary>
        /// No redundancy — the instance is always active.
        /// </summary>
        None = 0,

        /// <summary>
        /// Cold standby: a standby instance rebuilds configuration and
        /// runtime state from the shared stores only after the active
        /// instance fails.
        /// </summary>
        Cold = 1,

        /// <summary>
        /// Warm standby: a standby instance keeps its configuration loaded
        /// and paused, ready to resume quickly on failover.
        /// </summary>
        Warm = 2,

        /// <summary>
        /// Hot standby: a standby instance additionally tracks live
        /// sequence/keep-alive state so take-over is seamless.
        /// </summary>
        Hot = 3
    }
}
