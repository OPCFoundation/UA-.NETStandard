/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
