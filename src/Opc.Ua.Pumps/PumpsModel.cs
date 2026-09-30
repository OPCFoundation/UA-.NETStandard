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

namespace Opc.Ua.Pumps
{
    /// <summary>
    /// The parts of the OPC 40223 model that the generated code does not
    /// carry, plus helpers for resolving its types against a server namespace
    /// table.
    /// </summary>
    /// <remarks>
    /// The generated <see cref="ObjectTypeIds"/>, <see cref="BrowseNames"/>
    /// and <see cref="Namespaces"/> classes are the source of truth for the
    /// model itself. What is missing from them are the browse names of the
    /// four groups OPC 40223 reuses from OPC 10000-100 §5.6 -
    /// <c>Identification</c>, <c>Configuration</c>, <c>Maintenance</c> and
    /// <c>Operational</c> are instance names in the DI namespace, not nodes in
    /// the Pumps model, so the Pumps generator never sees them. Their three
    /// siblings (<c>Events</c>, <c>Ports</c>, <c>Documentation</c>) are Pumps
    /// nodes and do have generated constants.
    /// </remarks>
    public static class PumpsModel
    {
        /// <summary>
        /// The browse names of the <c>PumpType</c> groups that live in the
        /// Device Integration namespace, per OPC 10000-100 §5.6.
        /// </summary>
        /// <remarks>
        /// A browse path to any of these has to be qualified with the DI
        /// namespace index, not the Pumps one - the single most common way to
        /// get a Pumps browse path wrong.
        /// </remarks>
        public static class DiGroups
        {
            /// <summary>The <c>Identification</c> nameplate add-in.</summary>
            public const string Identification = "Identification";

            /// <summary>The <c>Configuration</c> group: design, implementation, requirements.</summary>
            public const string Configuration = "Configuration";

            /// <summary>The <c>Maintenance</c> group.</summary>
            public const string Maintenance = "Maintenance";

            /// <summary>The <c>Operational</c> group: live process data.</summary>
            public const string Operational = "Operational";
        }

        /// <summary>
        /// Resolves a numeric Pumps identifier - one of the generated
        /// <see cref="ObjectTypes"/> or <see cref="Objects"/> constants -
        /// into the Pumps namespace of the supplied table.
        /// </summary>
        /// <param name="identifier">A Pumps numeric identifier.</param>
        /// <param name="namespaceUris">The namespace table to resolve against.</param>
        /// <returns>
        /// The resolved NodeId, or <see cref="NodeId.Null"/> when the table
        /// does not contain the Pumps namespace.
        /// </returns>
        public static NodeId TypeNodeId(uint identifier, NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            int index = namespaceUris.GetIndex(Namespaces.Pumps);
            return index < 0 ? NodeId.Null : new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Classifies a port type definition as one of the three OPC 40223
        /// port types.
        /// </summary>
        /// <param name="typeDefinitionId">
        /// The port's type definition, as browsed from the <c>Ports</c> group.
        /// </param>
        /// <param name="namespaceUris">The namespace table to resolve against.</param>
        /// <returns>
        /// The port kind, or <see cref="PumpPortKind.Unknown"/> when the type
        /// is none of the three - a vendor subtype of <c>PortType</c>, for
        /// instance.
        /// </returns>
        public static PumpPortKind ClassifyPort(
            NodeId typeDefinitionId,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            if (typeDefinitionId.IsNull)
            {
                return PumpPortKind.Unknown;
            }
            int index = namespaceUris.GetIndex(Namespaces.Pumps);
            if (index < 0 || typeDefinitionId.NamespaceIndex != index)
            {
                return PumpPortKind.Unknown;
            }
            if (!typeDefinitionId.TryGetValue(out uint identifier))
            {
                return PumpPortKind.Unknown;
            }
            return identifier switch
            {
                ObjectTypes.DrivePortType => PumpPortKind.Drive,
                ObjectTypes.InletConnectionPortType => PumpPortKind.InletConnection,
                ObjectTypes.OutletConnectionPortType => PumpPortKind.OutletConnection,
                _ => PumpPortKind.Unknown
            };
        }
    }
}
