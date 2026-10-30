/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Generated descriptors for complete shared metadata, model and capabilities documents.
    /// Domain catalogs reuse these without a dependency back to a domain assembly.
    /// </summary>
    public static partial class RegistrySharedNativeCatalog
    {
        /// <summary>
        /// Gets the complete shared native document catalog.
        /// </summary>
        public static RegistryNativeCatalog Catalog => s_catalog.Value;

        /// <summary>
        /// Creates a shared catalog builder for a domain extension.
        /// </summary>
        public static RegistryNativeCatalog.Builder CreateBuilder()
        {
            RegistryNativeCatalog.Builder builder = RegistryNativeCatalog.CreateBuilder();
            AddGeneratedTypes(builder);
            return builder;
        }

        private static RegistryNativeTypeDescriptor Native(
            string name, ExpandedNodeId id, string baseType, bool isAbstract,
            Func<IEncodeable>? factory, params RegistryNativeFieldDescriptor[] fields)
        {
            return new RegistryNativeTypeDescriptor(name, id, baseType)
            {
                IsAbstract = isAbstract,
                Factory = factory,
                Fields = fields
            };
        }

        private static RegistryNativeFieldDescriptor Field(
            string name, string dataType, string source, bool array, bool subtypes, RegistrySourceShape? shape)
        {
            return new RegistryNativeFieldDescriptor(name, dataType)
            {
                Source = source,
                IsArray = array,
                AllowSubtypes = subtypes,
                Shape = shape
            };
        }

        private static readonly Lazy<RegistryNativeCatalog> s_catalog = new(() => CreateBuilder().Build());
    }
}
