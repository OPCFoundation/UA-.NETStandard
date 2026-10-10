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
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
        INullable,
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
        /// Is null
        /// </summary>
        public bool IsNull => m_outerXml == null;

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
            m_outerXml = string.IsNullOrEmpty(outerXml) ? null : outerXml;
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
            return Equals(From(other));
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
            return Equals(other.OuterXml);
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
            // DeepEquals descends only while both trees match, so bounding
            // ours bounds the recursion.
            XElement? ours = AsComparableXElement();
            return ours != null && XNode.DeepEquals(other, ours);
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
            if (other.IsEmpty)
            {
                return IsEmpty;
            }
            if (IsEmpty)
            {
                return false;
            }
            XElement? ours = AsComparableXElement();
            XElement? theirs = other.AsComparableXElement();
            if (ours == null || theirs == null)
            {
                // At least one document is malformed, or nested too deep, and
                // cannot be compared structurally. Compare the raw text instead - two different
                // malformed documents must not report equality while hashing
                // differently.
                return string.Equals(m_outerXml, other.m_outerXml, StringComparison.Ordinal);
            }
            return XNode.DeepEquals(theirs, ours);
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
            if (IsEmpty)
            {
                return 0;
            }

            XElement? element = AsComparableXElement();
            if (element == null)
            {
                // A malformed or too deeply nested document is compared by its
                // raw text, so hash it the same way.
                return m_outerXml!.GetHashCode(StringComparison.Ordinal);
            }

            // Equals compares structurally through XNode.DeepEquals, which
            // ignores attribute order and the spelling of an empty element, so
            // the hash may only use properties DeepEquals requires to match.
            // Hashing the raw text put two equal elements in different buckets.
            var hash = new HashCode();
            hash.Add(element.Name);
            hash.Add(element.Value, StringComparer.Ordinal);
            return hash.ToHashCode();
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
            // XElement.Load(Stream) parses DTDs and expands entities, which
            // lets a tiny untrusted payload expand to megabytes on every
            // comparison. Use the safe defaults (no resolver) and keep the
            // whitespace handling of XElement.Load. The DTD is skipped rather
            // than rejected so that a harmless DOCTYPE keeps the value valid;
            // its entities are never defined, so they cannot expand.
            XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.IgnoreWhitespace = true;
            using var reader = XmlReader.Create(stream, settings);
            return XElement.Load(reader, LoadOptions.SetBaseUri);
        }

        /// <summary>
        /// Returns the element for a structural comparison, or null when it is
        /// malformed or nested deeper than <see cref="kMaxComparisonDepth"/>.
        /// XNode.DeepEquals and XElement.Value recurse once per element level,
        /// so a deeply nested value would otherwise exhaust the stack.
        /// </summary>
        private XElement? AsComparableXElement()
        {
            // measure the depth with a streaming pass first: loading a deep
            // tree into LINQ to XML is itself slow (the base URI of every
            // element is resolved through its ancestors).
            try
            {
                using var stream = new MemoryStream(
                    Encoding.UTF8.GetBytes(OuterXml ?? string.Empty));
                XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Ignore;
                using var reader = XmlReader.Create(stream, settings);
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.Depth > kMaxComparisonDepth)
                    {
                        return null;
                    }
                }
            }
            catch (XmlException)
            {
                return null;
            }

            return AsXElement();
        }

        /// <summary>
        /// The deepest element nesting that is compared structurally. It covers
        /// the XML element depth the decoders accept by default
        /// (<see cref="DefaultEncodingLimits.MaxEncodingNestingLevels"/>).
        /// </summary>
        private const int kMaxComparisonDepth = 256;

#pragma warning disable IDE0032 // Use auto property
        private readonly string? m_outerXml;
#pragma warning restore IDE0032 // Use auto property
    }
}
