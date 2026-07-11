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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// A fresh, per-Server view of the direction inputs for one member of a <c>RedundantServerSet</c>: its health
    /// <c>ServiceLevel</c> (eligibility) and, when known, its load weight (tie-breaking).
    /// </summary>
    public sealed record PeerDirectionRecord
    {
        /// <summary>
        /// The peer ServerUri / ApplicationUri that identifies the member.
        /// </summary>
        public string ServerUri { get; init; } = string.Empty;

        /// <summary>
        /// The peer's health <c>ServiceLevel</c> (OPC 10000-4 Table 105). Direction eligibility is decided by this
        /// value only.
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// The peer's load weight (0 = idle .. 255 = fully loaded), used only to break ties among peers already tied
        /// at the highest health band. Valid only when <see cref="LoadKnown"/> is <c>true</c>.
        /// </summary>
        public byte LoadWeight { get; init; }

        /// <summary>
        /// Whether a fresh load weight was available for this peer. When <c>false</c> the load weight is unknown
        /// (stale or never published) and the peer is treated as least-preferred for tie-breaking.
        /// </summary>
        public bool LoadKnown { get; init; }
    }
}
