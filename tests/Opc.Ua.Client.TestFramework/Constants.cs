/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Constants for compliance test NodeIds in the ReferenceServer address space.
    /// </summary>
    public static class Constants
    {
        public static readonly string ReferenceServerNamespaceUri =
            Quickstarts.ReferenceServer.Namespaces.ReferenceServer;

        /// <summary>
        /// Static Scalar Nodes
        /// </summary>
        public static readonly ExpandedNodeId ScalarStaticBoolean =
            new("Scalar_Static_Boolean", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticSByte =
            new("Scalar_Static_SByte", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticByte =
            new("Scalar_Static_Byte", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticInt16 =
            new("Scalar_Static_Int16", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticUInt16 =
            new("Scalar_Static_UInt16", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticInt32 =
            new("Scalar_Static_Int32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticUInt32 =
            new("Scalar_Static_UInt32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticInt64 =
            new("Scalar_Static_Int64", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticUInt64 =
            new("Scalar_Static_UInt64", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticFloat =
            new("Scalar_Static_Float", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticDouble =
            new("Scalar_Static_Double", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticString =
            new("Scalar_Static_String", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticDateTime =
            new("Scalar_Static_DateTime", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticGuid =
            new("Scalar_Static_Guid", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticByteString =
            new("Scalar_Static_ByteString", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticNodeId =
            new("Scalar_Static_NodeId", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticLocalizedText =
            new("Scalar_Static_LocalizedText", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticQualifiedName =
            new("Scalar_Static_QualifiedName", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticVariant =
            new("Scalar_Static_Variant", ReferenceServerNamespaceUri);

        /// <summary>
        /// Collection of static scalar node IDs for batch read/write tests.
        /// </summary>
        public static readonly ExpandedNodeId[] ScalarStaticNodes =
        [
            ScalarStaticBoolean,
            ScalarStaticSByte,
            ScalarStaticByte,
            ScalarStaticInt16,
            ScalarStaticUInt16,
            ScalarStaticInt32,
            ScalarStaticUInt32,
            ScalarStaticInt64,
            ScalarStaticUInt64,
            ScalarStaticFloat,
            ScalarStaticDouble,
            ScalarStaticString,
            ScalarStaticDateTime,
            ScalarStaticGuid,
            ScalarStaticByteString,
            ScalarStaticNodeId,
            ScalarStaticLocalizedText,
            ScalarStaticQualifiedName,
            ScalarStaticVariant
        ];

        /// <summary>
        /// Static Array Nodes
        /// </summary>
        public static readonly ExpandedNodeId ScalarStaticArrayBoolean =
            new("Scalar_Static_Arrays_Boolean", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrayInt32 =
            new("Scalar_Static_Arrays_Int32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrayString =
            new("Scalar_Static_Arrays_String", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrayDouble =
            new("Scalar_Static_Arrays_Double", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrayDateTime =
            new("Scalar_Static_Arrays_DateTime", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrayByteString =
            new("Scalar_Static_Arrays_ByteString", ReferenceServerNamespaceUri);

        /// <summary>
        /// One-dimensional Variant array node whose elements are themselves
        /// arrays/scalars of mixed built-in types.
        /// </summary>
        public static readonly ExpandedNodeId ScalarStaticArrayVariant =
            new("Scalar_Static_Arrays_Variant", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId[] ScalarStaticArrayNodes =
        [
            ScalarStaticArrayBoolean,
            ScalarStaticArrayInt32,
            ScalarStaticArrayString,
            ScalarStaticArrayDouble,
            ScalarStaticArrayDateTime
        ];

        /// <summary>
        /// Static multi-dimensional (2D) Array Nodes
        /// </summary>
        public static readonly ExpandedNodeId ScalarStaticArrays2DBoolean =
            new("Scalar_Static_Arrays2D_Boolean", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrays2DInt32 =
            new("Scalar_Static_Arrays2D_Int32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrays2DDouble =
            new("Scalar_Static_Arrays2D_Double", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId ScalarStaticArrays2DString =
            new("Scalar_Static_Arrays2D_String", ReferenceServerNamespaceUri);

        /// <summary>
        /// Collection of static multi-dimensional (2D) array node IDs.
        /// </summary>
        public static readonly ExpandedNodeId[] ScalarStaticArrays2DNodes =
        [
            ScalarStaticArrays2DBoolean,
            ScalarStaticArrays2DInt32,
            ScalarStaticArrays2DDouble,
            ScalarStaticArrays2DString
        ];

        /// <summary>
        /// Simulation Nodes
        /// </summary>
        public static readonly ExpandedNodeId SimulationInt32 =
            new("Scalar_Simulation_Int32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId SimulationDouble =
            new("Scalar_Simulation_Double", ReferenceServerNamespaceUri);

        /// <summary>
        /// DataAccess AnalogType Nodes
        /// </summary>
        public static readonly ExpandedNodeId AnalogTypeDouble =
            new("DataAccess_AnalogType_Double", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId AnalogTypeInt32 =
            new("DataAccess_AnalogType_Int32", ReferenceServerNamespaceUri);

        /// <summary>
        /// Method Nodes
        /// </summary>
        public static readonly ExpandedNodeId MethodsFolder =
            new("Methods", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodVoid =
            new("Methods_Void", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodAdd =
            new("Methods_Add", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodHello =
            new("Methods_Hello", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodMultiply =
            new("Methods_Multiply", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodInput =
            new("Methods_Input", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodOutput =
            new("Methods_Output", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId MethodInputOutput =
            new("Methods_InputOutput", ReferenceServerNamespaceUri);

        /// <summary>
        /// Historical Access Nodes
        /// </summary>
        public static readonly ExpandedNodeId HistoricalDouble =
            new("Scalar_Static_Double", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId HistoricalInt32 =
            new("Scalar_Static_Int32", ReferenceServerNamespaceUri);

        public static readonly ExpandedNodeId HistoricalFloat =
            new("Scalar_Static_Float", ReferenceServerNamespaceUri);

        /// <summary>
        /// An invalid NodeId that should not exist in the server address space.
        /// </summary>
        public static readonly NodeId InvalidNodeId =
            new("NonExistent_InvalidNode_12345", 2);
    }
}
