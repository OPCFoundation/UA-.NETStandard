// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
