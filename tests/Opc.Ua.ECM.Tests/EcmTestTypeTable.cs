// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Provides the ECM data type inheritance required by in-memory server tests.
    /// </summary>
    internal static class EcmTestTypeTable
    {
        /// <summary>
        /// Creates a type table containing the ECM structured measurement data types.
        /// </summary>
        internal static TypeTable Create(NamespaceTable namespaces)
        {
            _ = namespaces.GetIndexOrAppend(Opc.Ua.ECM.Namespaces.ECM);
            var typeTable = new TypeTable(namespaces);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(Opc.Ua.ECM.DataTypeIds.AcPeDataType, namespaces),
                Opc.Ua.DataTypeIds.Structure);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(Opc.Ua.ECM.DataTypeIds.AcPpDataType, namespaces),
                Opc.Ua.DataTypeIds.Structure);
            return typeTable;
        }
    }
}
