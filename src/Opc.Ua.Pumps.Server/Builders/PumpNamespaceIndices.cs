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

namespace Opc.Ua.Pumps.Server.Builders
{
    /// <summary>
    /// The namespace indices a pump's children can live in, resolved once
    /// against the server's namespace table.
    /// </summary>
    /// <remarks>
    /// An OPC 40223 group is not confined to one namespace. The nameplate is
    /// the extreme case - eleven of its fields are OPC 10000-100, four are
    /// OPC 40001-1 and eleven are OPC 40223 - and a builder that assumed the
    /// Pumps namespace would silently fail to find two thirds of it. Children
    /// are therefore looked up in each of these in turn.
    /// </remarks>
    /// <param name="Pumps">The OPC 40223 namespace index.</param>
    /// <param name="Di">The OPC 10000-100 namespace index.</param>
    /// <param name="Machinery">The OPC 40001-1 namespace index.</param>
    public readonly record struct PumpNamespaceIndices(
        ushort Pumps,
        ushort Di,
        ushort Machinery)
    {
        /// <summary>
        /// Resolves the three namespaces against a server namespace table.
        /// </summary>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <exception cref="ServiceResultException">
        /// One of the three namespaces is not registered, which means the node
        /// manager did not load the models it declares.
        /// </exception>
        public static PumpNamespaceIndices Resolve(NamespaceTable namespaceUris)
        {
            return new PumpNamespaceIndices(
                IndexOf(namespaceUris, Namespaces.Pumps),
                IndexOf(namespaceUris, Opc.Ua.Di.Namespaces.OpcUaDi),
                IndexOf(namespaceUris, Opc.Ua.Machinery.Namespaces.Machinery));
        }

        private static ushort IndexOf(NamespaceTable namespaceUris, string namespaceUri)
        {
            int index = namespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The namespace '{0}' is not registered with this server.",
                    namespaceUri);
            }
            return (ushort)index;
        }

        /// <summary>
        /// Gets the namespace indices a child browse name is searched in, in
        /// order: OPC 40223 first because most children are its own, then the
        /// two base models, then the OPC UA namespace for properties such as
        /// <c>EngineeringUnits</c>.
        /// </summary>
        public ArrayOf<ushort> SearchOrder => [Pumps, Di, Machinery, 0];
    }
}
