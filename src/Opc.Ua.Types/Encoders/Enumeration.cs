/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Xml;

namespace Opc.Ua.Encoders
{
    /// <summary>
    /// Enumeration wrapping an enum definition
    /// </summary>
    public sealed class Enumeration : IEnumeratedType, IDataTypeDefinitionSource
    {
        /// <summary>
        /// Create enumeration
        /// </summary>
        public Enumeration(XmlQualifiedName name, EnumDefinition definition)
        {
            m_definition = definition;
            XmlName = name;
        }

        /// <inheritdoc/>
        public EnumValue Default
        {
            get
            {
                if (m_definition.Fields.IsEmpty)
                {
                    return default;
                }
                return new EnumValue((int)m_definition.Fields[0].Value, this);
            }
        }

        /// <inheritdoc/>
        public Type Type => typeof(Enumeration);

        /// <inheritdoc/>
        public XmlQualifiedName XmlName { get; }

        /// <inheritdoc/>
        public bool TryGetSymbol(int value, out string? symbol)
        {
            EnumField found = m_definition.Fields.Find(e => e.Value == value);
            if (found?.Name != null)
            {
                symbol = found.Name;
                return true;
            }
            symbol = default;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetValue(string symbol, out int value)
        {
            EnumField found = m_definition.Fields.Find(e => e.Name == symbol);
            if (found != null)
            {
                value = (int)found.Value;
                return true;
            }
            value = default;
            return false;
        }

        /// <inheritdoc/>
        public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            return m_definition;
        }

        private readonly EnumDefinition m_definition;
    }
}
