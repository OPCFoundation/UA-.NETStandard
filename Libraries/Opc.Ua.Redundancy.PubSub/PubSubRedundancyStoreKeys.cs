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
    /// Stable <see cref="Opc.Ua.Redundancy.ISharedKeyValueStore"/> key prefixes used by the
    /// distributed PubSub redundancy bridges so every store agrees on its keyspace.
    /// </summary>
    /// <remarks>
    /// The <c>lease/</c> prefix routes to the strong (linearizable) keyspace of a
    /// <see cref="Opc.Ua.Redundancy.HybridSharedKeyValueStore"/> because leases require
    /// compare-and-swap; the remaining prefixes tolerate eventual consistency.
    /// </remarks>
    internal static class PubSubRedundancyStoreKeys
    {
        /// <summary>
        /// Prefix for leadership leases (strong keyspace, compare-and-swap).
        /// </summary>
        public const string LeasePrefix = "lease/pubsub/";

        /// <summary>
        /// Prefix for mirrored PubSub component runtime state.
        /// </summary>
        public const string RuntimeStatePrefix = "pubsub/runtime/";

        /// <summary>
        /// Prefix for per-writer sequence/keep-alive checkpoints (Hot standby).
        /// </summary>
        public const string CheckpointPrefix = "pubsub/checkpoint/";

        /// <summary>
        /// Prefix for shared PubSub security-group keys (SKS).
        /// </summary>
        public const string SecurityKeyPrefix = "pubsub/sks/";
    }
}
