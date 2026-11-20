// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Well-known identifiers of the abstract xRegistry base model that are <b>not</b> part of the
    /// compiled companion model. The model itself (its ObjectTypes, Methods, Variables and their
    /// NodeIds) is source-generated from <c>Opc.Ua.XRegistry.NodeSet2.xml</c> — use the generated
    /// <c>ObjectTypeIds</c>, <c>MethodIds</c> and <c>VariableIds</c> classes for those. The
    /// identifiers below address the <i>instances</i> a generic registry materializes at runtime,
    /// and therefore live above the range the model occupies (63000-63999) so they can never
    /// collide with a model node. Final NodeIds are assigned by the OPC Foundation.
    /// </summary>
    public static class XRegistryWellKnown
    {
        /// <summary>
        /// The abstract xRegistry base companion namespace URI.
        /// </summary>
        public const string XRegistryNamespaceUri = "http://opcfoundation.org/UA/xRegistry/";

        /// <summary>
        /// Provisional NodeId of the registry root Object instance.
        /// </summary>
        public const uint RegistryObject = 65000;

        /// <summary>
        /// First NodeId of the range the registry allocates to the groups and resources it creates
        /// at runtime. Everything below it is either the compiled model (63000-63999) or a fixed
        /// instance identifier.
        /// </summary>
        public const uint FirstDynamicInstance = 100000;

        /// <summary>
        /// Provisional NodeId of the federated resource proxy, a <c>ResourceType</c> instance whose
        /// <c>ExternalReference</c> and <c>ResourceUrl</c> carry the federation link.
        /// </summary>
        public const uint FederationProxyObject = 66001;
    }
}
