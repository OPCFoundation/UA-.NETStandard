/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Reads the OPC 10000-100 identification an asset publishes, in the two
    /// places OPC 10000-110 §7 allows: directly on the asset, or in its
    /// <c>2:Identification</c> functional group.
    /// </summary>
    internal static class AssetIdentification
    {
        /// <summary>The DI browse name of the identification group.</summary>
        public const string IdentificationGroup = "Identification";

        /// <summary>The DI browse name of the product instance URI.</summary>
        public const string ProductInstanceUri = "ProductInstanceUri";

        /// <summary>The DI browse name of the asset id.</summary>
        public const string AssetId = "AssetId";

        /// <summary>
        /// Finds an identification property, on the asset first and then in
        /// its identification group.
        /// </summary>
        /// <remarks>
        /// A Device Integration device lets its identification group
        /// organize the properties it declares on the device itself, so the
        /// device's own property is the one found. A Machinery machine keeps
        /// them as components of the group, which the second lookup finds.
        /// </remarks>
        /// <param name="context">The context of the manager that owns the asset.</param>
        /// <param name="asset">The asset object.</param>
        /// <param name="diNamespaceIndex">The index of the DI namespace.</param>
        /// <param name="name">The DI browse name of the property.</param>
        /// <returns>The property, or <see langword="null"/>.</returns>
        public static BaseVariableState? FindProperty(
            ISystemContext context,
            BaseObjectState asset,
            ushort diNamespaceIndex,
            string name)
        {
            var browseName = new QualifiedName(name, diNamespaceIndex);
            if (asset.FindChildWithQualifiedName(context, browseName) is BaseVariableState direct)
            {
                return direct;
            }
            if (asset.FindChildWithQualifiedName(
                    context,
                    new QualifiedName(IdentificationGroup, diNamespaceIndex)) is BaseObjectState group &&
                group.FindChildWithQualifiedName(context, browseName) is BaseVariableState grouped)
            {
                return grouped;
            }
            return null;
        }

        /// <summary>
        /// Reads the string value of a property.
        /// </summary>
        /// <param name="property">The property, or <see langword="null"/>.</param>
        /// <returns>The value, or <see langword="null"/> when it is not a string.</returns>
        public static string? ReadString(BaseVariableState? property)
        {
            return property != null && property.WrappedValue.TryGetValue(out string? value)
                ? value
                : null;
        }
    }
}
