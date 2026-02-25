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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XmlElementNode = System.Xml.XmlElement;

namespace Opc.Ua
{
    /// <summary>
    /// Xml Element
    /// </summary>
    public readonly struct XmlElement :
        IEquatable<string>,
        IEquatable<XmlElementNode>,
        IEquatable<XElement>,
        IEquatable<XmlElement>
    {
        /// <summary>
        /// The xml string
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public string? OuterXml => m_outerXml;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Returns <c>true</c> if this element is
        /// null, <c>false</c> otherwise.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(m_outerXml);

        /// <summary>
        /// Returns <c>true</c> if this element is
        /// valid xml, <c>false</c> otherwise.
        /// </summary>
        public bool IsValid => !IsEmpty && AsXElement() != null;

        /// <summary>
        /// Constructs a <see cref="XmlElement" /> from a Utf8 string.
        /// The xml in the string is not validated.
        /// </summary>
        /// <param name="outerXml"></param>
        internal XmlElement(string? outerXml)
        {
            m_outerXml = outerXml;
        }

        /// <summary>
        /// Constructs a <see cref="XmlElement" /> from an
        /// <see cref="XmlElementNode" /> of an <see cref="XmlDocument" />.
        /// </summary>
        internal XmlElement(XmlElementNode? xml)
        {
            m_outerXml = xml?.OuterXml;
        }

        /// <summary>
        /// Constructs a <see cref="XmlElement" /> from an
        /// <see cref="XElement" />.
        /// </summary>
        internal XmlElement(XElement? xml)
        {
            m_outerXml = xml?.ToString();
        }

        /// <summary>
        /// Returns a null xml element.
        /// </summary>
        public static XmlElement Empty
            => new(string.Empty);

        /// <inheritdoc/>
        public bool Equals(string? other)
        {
            return string.Equals(m_outerXml, other, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public static bool operator ==(XmlElement left, string? right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(XmlElement left, string? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static explicit operator string?(XmlElement xml)
        {
            return xml.m_outerXml;
        }

        /// <inheritdoc/>
        public static explicit operator XmlElement(string? xml)
        {
            return From(xml);
        }

        /// <inheritdoc/>
        public static XmlElement From(string? xml)
        {
            return new(xml);
        }

        /// <inheritdoc/>
        public bool Equals(XmlElementNode? other)
        {
            if (other == null)
            {
                return false;
            }
            return string.Equals(
                m_outerXml,
                other.OuterXml,
                StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public static bool operator ==(XmlElement left, XmlElementNode? right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(XmlElement left, XmlElementNode? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static explicit operator XmlElementNode?(XmlElement xml)
        {
            return xml.AsXmlElement();
        }

        /// <inheritdoc/>
        public static explicit operator XmlElement(XmlElementNode? xml)
        {
            return From(xml);
        }

        /// <inheritdoc/>
        public static XmlElement From(XmlElementNode? xml)
        {
            return new(xml);
        }

        /// <inheritdoc/>
        public bool Equals(XElement? other)
        {
            if (other == null)
            {
                return false;
            }
            return XNode.DeepEquals(other, AsXElement());
        }

        /// <inheritdoc/>
        public static bool operator ==(XmlElement left, XElement? right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(XmlElement left, XElement? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static explicit operator XElement?(XmlElement xml)
        {
            return xml.AsXElement();
        }

        /// <inheritdoc/>
        public static explicit operator XmlElement(XElement? xml)
        {
            return From(xml);
        }

        /// <inheritdoc/>
        public static XmlElement From(XElement? xml)
        {
            return new(xml);
        }

        /// <inheritdoc/>
        public bool Equals(XmlElement other)
        {
            return string.Equals(m_outerXml, other.OuterXml, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public static bool operator ==(XmlElement left, XmlElement right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(XmlElement left, XmlElement right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return m_outerXml ?? string.Empty;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return m_outerXml?.GetHashCode(StringComparison.Ordinal) ?? 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => IsEmpty,
                string str => Equals(str),
                XmlElementNode n => Equals(n),
                XElement x => Equals(x),
                XmlElement element => Equals(element),
                _ => false
            };
        }

        /// <summary>
        /// Try to convert to <see cref="XmlElementNode"/> or
        /// return null
        /// </summary>
        /// <returns></returns>
        public XmlElementNode? AsXmlElement()
        {
            try
            {
                return ToXmlElement();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// Convert to <see cref="XmlElementNode"/>
        /// </summary>
        /// <returns></returns>
        /// <exception cref="XmlException"></exception>
        public XmlElementNode ToXmlElement()
        {
            string outerXml = OuterXml ?? string.Empty;
            var document = new XmlDocument();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(outerXml)))
            using (var reader = XmlReader.Create(stream, CoreUtils.DefaultXmlReaderSettings()))
            {
                document.Load(reader);
            }
            return ExtractNodeOrThrow(document, outerXml);

            [ExcludeFromCodeCoverage] // Because document element will never be null
            static XmlElementNode ExtractNodeOrThrow(XmlDocument document, string xml)
            {
                return document.DocumentElement ??
                    throw new XmlException($"Failed to convert {xml} to xml Element.");
            }
        }

        /// <summary>
        /// Convert to <see cref="XElement"/>or
        /// return null in case of error
        /// </summary>
        /// <returns></returns>
        public XElement? AsXElement()
        {
            try
            {
                return ToXElement();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// Convert to <see cref="XElement"/>
        /// </summary>
        /// <exception cref="XmlException"></exception>
        /// <returns></returns>
        public XElement ToXElement()
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(OuterXml ?? string.Empty));
            return XElement.Load(stream, LoadOptions.SetBaseUri);
        }

#pragma warning disable IDE0032 // Use auto property
        private readonly string? m_outerXml;
#pragma warning restore IDE0032 // Use auto property
    }
}
