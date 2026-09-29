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

namespace Opc.Ua.Scales.Server.Builders
{
    /// <summary>
    /// The namespace indices the children of an OPC 40200 node can live in,
    /// resolved once against the server's namespace table.
    /// </summary>
    /// <remarks>
    /// A scale spans five models: its own members are OPC 40200, its
    /// identification is DI and Machinery, its state machines are PackML and
    /// Machinery, and its statistics carry IA members. A child browse name is
    /// therefore looked up in each of these in turn.
    /// </remarks>
    /// <param name="Scales">The OPC 40200 namespace index.</param>
    /// <param name="Di">The OPC 10000-100 namespace index.</param>
    /// <param name="Machinery">The OPC 40001-1 namespace index.</param>
    /// <param name="IA">The OPC 10000-200 namespace index.</param>
    /// <param name="PackML">The OPC 30050 namespace index.</param>
    public readonly record struct ScaleNamespaceIndices(
        ushort Scales,
        ushort Di,
        ushort Machinery,
        ushort IA,
        ushort PackML)
    {
        /// <summary>
        /// Resolves the five namespaces against a server namespace table.
        /// </summary>
        /// <param name="namespaceUris">The server's namespace table.</param>
        /// <exception cref="ServiceResultException">
        /// One of the namespaces is not registered, which means the node
        /// manager did not load the models it declares.
        /// </exception>
        public static ScaleNamespaceIndices Resolve(NamespaceTable namespaceUris)
        {
            return new ScaleNamespaceIndices(
                IndexOf(namespaceUris, Namespaces.Scales),
                IndexOf(namespaceUris, Opc.Ua.Di.Namespaces.OpcUaDi),
                IndexOf(namespaceUris, Opc.Ua.Machinery.Namespaces.Machinery),
                IndexOf(namespaceUris, Opc.Ua.IA.Namespaces.IA),
                IndexOf(namespaceUris, Opc.Ua.PackML.Namespaces.PackML));
        }

        /// <summary>
        /// Gets the namespace indices a child browse name is searched in:
        /// OPC 40200 first, then the base models, then the OPC UA namespace
        /// for properties such as <c>EngineeringUnits</c>.
        /// </summary>
        public ArrayOf<ushort> SearchOrder => [Scales, Di, Machinery, IA, PackML, 0];

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
    }
}
