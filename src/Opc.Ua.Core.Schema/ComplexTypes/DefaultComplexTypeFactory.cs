/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Default complex type factory
    /// </summary>
    public sealed class DefaultComplexTypeFactory : IComplexTypeFactory
    {
        /// <inheritdoc/>
        public DefaultComplexTypeFactory()
        {
        }

        /// <inheritdoc/>
        public IComplexTypeBuilder Create(
            string targetNamespace,
            int targetNamespaceIndex,
            string? moduleName = null)
        {
            return new DefaultComplexTypeBuilder(
                this,
                targetNamespace,
                targetNamespaceIndex);
        }

        /// <inheritdoc/>
        public IReadOnlyList<IType> GetTypes()
        {
            return [.. m_types.Values];
        }

        /// <summary>
        /// Called when a new type was created during complex type system
        /// loading
        /// </summary>
        internal void OnTypeCreated(IType type)
        {
            m_types[type.XmlName] = type;
        }

        private readonly Dictionary<XmlQualifiedName, IType> m_types = [];
    }
}
