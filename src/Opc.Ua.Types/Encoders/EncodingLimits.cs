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

using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The one place that decides what the encoding limits count, shared by
    /// every codec so that a single <see cref="IServiceMessageContext"/> cannot
    /// give a different answer over Binary, XML and JSON for the same value.
    /// </summary>
    internal static class EncodingLimits
    {
        /// <summary>
        /// Returns true when a string does not fit into MaxStringLength.
        /// </summary>
        /// <remarks>
        /// MaxStringLength counts the <b>bytes</b> of the encoded string
        /// (OPC 10000-3 5.6.4 and OPC 10000-5 6.3.2), not UTF-16 code units.
        /// Measuring code units instead lets a peer encode a non ASCII string
        /// that the receiver then refuses to decode, and makes the limit depend
        /// on the alphabet the value happens to be written in. Zero, and any
        /// negative value, mean unlimited as in every other limit check.
        /// </remarks>
        /// <param name="maxStringLength">The configured limit.</param>
        /// <param name="value">The string to measure.</param>
        /// <param name="byteLength">The measured length, only meaningful when
        /// this method returns true.</param>
        public static bool StringExceedsLimit(
            int maxStringLength,
            string? value,
            out int byteLength)
        {
            byteLength = 0;

            if (maxStringLength <= 0 || value == null)
            {
                return false;
            }

            // UTF-8 never needs more than three bytes per UTF-16 code unit - a
            // surrogate pair is two code units and four bytes - so the string
            // only has to be measured when even that worst case does not fit.
            if (value.Length <= maxStringLength / 3)
            {
                return false;
            }

            byteLength = Encoding.UTF8.GetByteCount(value);
            return byteLength > maxStringLength;
        }

        /// <summary>
        /// Throws if a string does not fit into MaxStringLength.
        /// </summary>
        /// <param name="maxStringLength">The configured limit.</param>
        /// <param name="value">The string to check.</param>
        /// <exception cref="ServiceResultException">Thrown with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the string
        /// is over the limit.</exception>
        public static void CheckStringLength(int maxStringLength, string? value)
        {
            if (StringExceedsLimit(maxStringLength, value, out int byteLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    maxStringLength,
                    byteLength);
            }
        }

        /// <summary>
        /// Throws if the bytes a base64 text decodes to do not fit into
        /// MaxByteStringLength, before the text is decoded.
        /// </summary>
        /// <remarks>
        /// Every four significant characters (whitespace and padding are not)
        /// decode to three bytes, so the count is exact for valid base64.
        /// </remarks>
        /// <exception cref="ServiceResultException">Thrown with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the decoded
        /// value would be over the limit.</exception>
        public static void CheckBase64Length(int maxByteStringLength, string base64)
        {
            // no base64 text of this length can decode to more than the limit.
            if (maxByteStringLength <= 0 || base64.Length * 3L / 4 <= maxByteStringLength)
            {
                return;
            }

            long significant = 0;
            foreach (char c in base64)
            {
                if (c != '=' && !char.IsWhiteSpace(c))
                {
                    significant++;
                }
            }

            long byteLength = significant * 3 / 4;
            if (byteLength > maxByteStringLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxByteStringLength {0} < {1}",
                    maxByteStringLength,
                    byteLength);
            }
        }

        /// <summary>
        /// Returns the deepest element nesting accepted inside XML content that
        /// the codecs keep as raw XML (XmlElement values and ExtensionObject
        /// bodies of unknown types).
        /// </summary>
        /// <remarks>
        /// XML element depth is not counted by the decoder nesting level, but
        /// System.Xml and LINQ to XML operations that are later applied to such
        /// content (XmlNode.InnerXml/InnerText, ImportNode, XNode.DeepEquals,
        /// XElement.Value) recurse once per level and would exhaust the stack.
        /// The limit is MaxEncodingNestingLevels, or its default when the
        /// context sets none.
        /// </remarks>
        public static int GetMaxXmlElementDepth(IServiceMessageContext context)
        {
            return context.MaxEncodingNestingLevels > 0
                ? context.MaxEncodingNestingLevels
                : DefaultEncodingLimits.MaxEncodingNestingLevels;
        }

        /// <summary>
        /// Copies the element the reader is positioned on, with its content, to
        /// a string and leaves the reader on the node that follows it.
        /// </summary>
        /// <remarks>
        /// The copy is made node by node, so it never recurses, and fails as
        /// soon as an element is nested deeper than <paramref name="maxDepth"/>
        /// below the copied element. Attributes keep their prefix and namespace.
        /// </remarks>
        /// <exception cref="ServiceResultException">Thrown with
        /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> when the content
        /// is nested too deep or exceeds <paramref name="maxStringLength"/>.</exception>
        /// <exception cref="XmlException">Thrown when the content is not
        /// well-formed.</exception>
        public static string ReadXmlElementContent(
            XmlReader reader,
            int maxDepth,
            int maxStringLength)
        {
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                NewLineHandling = NewLineHandling.Entitize
            };

            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = XmlWriter.Create(text, settings))
            {
                int startDepth = reader.Depth;
                do
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (reader.Depth - startDepth > maxDepth)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadEncodingLimitsExceeded,
                                    "XML element nesting exceeds the maximum depth of {0}.",
                                    maxDepth);
                            }
                            writer.WriteStartElement(
                                reader.Prefix,
                                reader.LocalName,
                                reader.NamespaceURI);
                            writer.WriteAttributes(reader, true);
                            if (reader.IsEmptyElement)
                            {
                                writer.WriteEndElement();
                            }
                            break;
                        case XmlNodeType.Text:
                            writer.WriteString(reader.Value);
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            writer.WriteWhitespace(reader.Value);
                            break;
                        case XmlNodeType.CDATA:
                            writer.WriteCData(reader.Value);
                            break;
                        case XmlNodeType.EntityReference:
                            writer.WriteEntityRef(reader.Name);
                            break;
                        case XmlNodeType.ProcessingInstruction:
                            writer.WriteProcessingInstruction(reader.Name, reader.Value);
                            break;
                        case XmlNodeType.Comment:
                            writer.WriteComment(reader.Value);
                            break;
                        case XmlNodeType.EndElement:
                            writer.WriteFullEndElement();
                            break;
                    }
                } while (reader.Read() &&
                    (startDepth < reader.Depth ||
                        (startDepth == reader.Depth &&
                            reader.NodeType == XmlNodeType.EndElement)));
            }

            string result = text.ToString();
            CheckStringLength(maxStringLength, result);
            return result;
        }
    }
}
