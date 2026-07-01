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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Optional probe filter sent inside a
    /// <see cref="UadpDiscoveryRequestMessage"/> when
    /// <see cref="UadpDiscoveryRequestMessage.DiscoveryType"/> is
    /// <see cref="UadpDiscoveryType.Probe"/>.
    /// </summary>
    /// <remarks>
    /// Implements the probe filter from
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.12">
    /// Part 14 §7.2.4.6.12 Table 180</see>.
    /// </remarks>
    public sealed record UadpDiscoveryProbeFilter
    {
        /// <summary>
        /// Optional ApplicationUri filter; empty means no constraint.
        /// </summary>
        public string ApplicationUri { get; init; } = string.Empty;

        /// <summary>
        /// Optional ProductUri filter; empty means no constraint.
        /// </summary>
        public string ProductUri { get; init; } = string.Empty;

        /// <summary>
        /// Optional capability filter (single token); empty means no
        /// constraint.
        /// </summary>
        public string Capability { get; init; } = string.Empty;

        /// <summary>
        /// Optional WriterGroupId for WriterGroup configuration probes.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Requests WriterGroups in PubSubConnection announcements.
        /// </summary>
        public bool IncludeWriterGroups { get; init; }

        /// <summary>
        /// Requests DataSetWriters in WriterGroup or PubSubConnection announcements.
        /// </summary>
        public bool IncludeDataSetWriters { get; init; }

        /// <summary>
        /// Optional TransportProfileUri filters for PubSubConnection announcements.
        /// </summary>
        public ArrayOf<string> TransportProfileUris { get; init; } = [];
    }
}
