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
