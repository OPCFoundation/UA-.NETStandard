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

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// A pump discovered on a server.
    /// </summary>
    /// <param name="NodeId">The pump object.</param>
    /// <param name="BrowseName">The pump's browse name.</param>
    /// <param name="DisplayName">The pump's display name.</param>
    /// <param name="TypeDefinitionId">
    /// The pump's type definition - <c>PumpType</c> or a vendor subtype
    /// of it.
    /// </param>
    public sealed record PumpEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);

    /// <summary>
    /// Everything one OPC 40223 pump publishes, read in one pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole <c>PumpType</c> surface: the nameplate, the three
    /// configuration groups, the operational groups, the seven supervision
    /// groups, the four maintenance groups and every port. A server fills it
    /// from its process and a client reads it back, and the two never have to
    /// agree on anything but this package.
    /// </para>
    /// <para>
    /// Reading all of it is expensive on a pump that publishes a lot, so the
    /// client exposes each group separately as well - take the snapshot when
    /// you want the whole picture, the individual reads when you want one
    /// number on a cycle.
    /// </para>
    /// </remarks>
    public sealed record PumpSnapshot
    {
        /// <summary>
        /// The pump object.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The pump's browse name.
        /// </summary>
        public QualifiedName BrowseName { get; init; }

        /// <summary>
        /// The pump's display name.
        /// </summary>
        public LocalizedText DisplayName { get; init; }

        /// <summary>
        /// The pump's type definition.
        /// </summary>
        public NodeId TypeDefinitionId { get; init; }

        /// <summary>
        /// The pump's nameplate, or <see langword="null"/> when it publishes
        /// no <c>Identification</c> add-in. OPC 40223 makes it mandatory, so
        /// <see langword="null"/> means a non-conforming server.
        /// </summary>
        public PumpNameplate? Nameplate { get; init; }

        /// <summary>
        /// The <c>Configuration</c> group, when published.
        /// </summary>
        public PumpConfigurationData? Configuration { get; init; }

        /// <summary>
        /// The <c>Operational</c> group, when published.
        /// </summary>
        public PumpOperationalData? Operational { get; init; }

        /// <summary>
        /// The <c>Events</c> supervision group, when published.
        /// </summary>
        public PumpSupervisionStatus? Supervision { get; init; }

        /// <summary>
        /// The <c>Maintenance</c> group, when published.
        /// </summary>
        public PumpMaintenanceData? Maintenance { get; init; }

        /// <summary>
        /// The <c>Documentation</c> group, when published. Its members are
        /// pairs of a <c>FileType</c> and a link, so what is read here are the
        /// links; the files themselves are transferred over the File API.
        /// </summary>
        public PumpValueSet Documentation { get; init; } = PumpValueSet.Empty;

        /// <summary>
        /// The ports below the <c>Ports</c> group. Empty when the pump
        /// publishes none.
        /// </summary>
        public ArrayOf<PumpPortDescriptor> Ports { get; init; }
    }
}
