/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Xml;

namespace Opc.Ua.Encoders
{
    /// <summary>
    /// Enumeration wrapping an enum definition
    /// </summary>
    public sealed class Enumeration : IEnumeratedType
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

        private readonly EnumDefinition m_definition;
    }
}
