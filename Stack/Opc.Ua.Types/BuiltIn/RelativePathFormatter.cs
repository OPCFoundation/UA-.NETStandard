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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A class that stores a relative path string
    /// </summary>
    public class RelativePathFormatter : IFormattable
    {
        /// <summary>
        /// Initializes the object the default values.
        /// </summary>
        public RelativePathFormatter(RelativePath relativePath, ITypeTable typeTree)
        {
            Elements = [];

            if (relativePath != null)
            {
                foreach (RelativePathElement element in relativePath.Elements)
                {
                    Elements.Add(new Element(element, typeTree));
                }
            }
        }

        /// <summary>
        /// Initializes the object the default values.
        /// </summary>
        public RelativePathFormatter()
        {
            Elements = [];
        }

        /// <summary>
        /// The elements in the relative path.
        /// </summary>
        /// <remarks>
        /// The elements in the relative path.
        /// </remarks>
        public List<Element> Elements { get; }

        /// <summary>
        /// Updates the namespace table with URI used in the relative path.
        /// </summary>
        /// <param name="currentTable">The current table.</param>
        /// <param name="targetTable">The target table.</param>
        public void UpdateNamespaceTable(NamespaceTable currentTable, NamespaceTable targetTable)
        {
            // build mapping table.
            int[] mappings = new int[currentTable.Count];
            mappings[0] = 0;

            if (mappings.Length > 0)
            {
                mappings[1] = 1;
            }

            // ensure a placeholder for the local namespace.
            if (targetTable.Count <= 1)
            {
                targetTable.Append("---");
            }

            string[] uris = new string[mappings.Length];

            for (int ii = 2; ii < mappings.Length; ii++)
            {
                uris[ii] = currentTable.GetString((uint)ii);

                if (uris[ii] != null)
                {
                    mappings[ii] = targetTable.GetIndex(uris[ii]);
                }
            }

            // update each element.
            foreach (Element element in Elements)
            {
                // check reference type name.
                QualifiedName qname = element.ReferenceTypeName;

                if (!qname.IsNull &&
                    qname.NamespaceIndex > 1 &&
                    qname.NamespaceIndex < mappings.Length &&
                    mappings[qname.NamespaceIndex] == -1)
                {
                    mappings[qname.NamespaceIndex] = targetTable.GetIndexOrAppend(
                        uris[qname.NamespaceIndex]);
                }

                // check target name.
                qname = element.TargetName;

                if (!qname.IsNull &&
                    qname.NamespaceIndex > 1 &&
                    qname.NamespaceIndex < mappings.Length &&
                    mappings[qname.NamespaceIndex] == -1)
                {
                    mappings[qname.NamespaceIndex] = targetTable.GetIndexOrAppend(
                        uris[qname.NamespaceIndex]);
                }
            }
        }

        /// <summary>
        /// Updates the path to use the indexes from the target table.
        /// </summary>
        /// <param name="currentTable">The NamespaceTable which the RelativePathString currently references</param>
        /// <param name="targetTable">The NamespaceTable which the RelativePathString should reference</param>
        /// <exception cref="ServiceResultException"></exception>
        public void TranslateNamespaceIndexes(
            NamespaceTable currentTable,
            NamespaceTable targetTable)
        {
            // build mapping table.
            int[] mappings = new int[currentTable.Count];
            mappings[0] = 0;

            // copy mappings.
            string[] uris = new string[mappings.Length];

            for (int ii = 1; ii < mappings.Length; ii++)
            {
                uris[ii] = currentTable.GetString((uint)ii);

                if (uris[ii] != null)
                {
                    mappings[ii] = targetTable.GetIndex(uris[ii]);
                }
            }

            // update each element.
            foreach (Element element in Elements)
            {
                QualifiedName qname = element.ReferenceTypeName;

                if (!qname.IsNull && qname.NamespaceIndex > 0)
                {
                    if (qname.NamespaceIndex < mappings.Length &&
                        mappings[qname.NamespaceIndex] > 0)
                    {
                        element.ReferenceTypeName = new QualifiedName(
                            qname.Name,
                            (ushort)mappings[qname.NamespaceIndex]);
                    }
                    else
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadIndexRangeInvalid,
                            CoreUtils.Format(
                                "Cannot translate namespace index '{0}' to target namespace table.",
                                qname.NamespaceIndex));
                    }
                }

                qname = element.TargetName;

                if (!qname.IsNull && qname.NamespaceIndex > 0)
                {
                    if (qname.NamespaceIndex < mappings.Length &&
                        mappings[qname.NamespaceIndex] > 0)
                    {
                        element.TargetName = new QualifiedName(
                            qname.Name,
                            (ushort)mappings[qname.NamespaceIndex]);
                    }
                    else
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadIndexRangeInvalid,
                            CoreUtils.Format(
                                "Cannot translate namespace index '{0}' to target namespace table.",
                                qname.NamespaceIndex));
                    }
                }
            }
        }

        /// <summary>
        /// Formats the relative path as a string.
        /// </summary>
        /// <remarks>
        /// Formats the relative path as a string.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Formats the relative path as a string.
        /// </summary>
        /// <remarks>
        /// Formats the relative path as a string.
        /// </remarks>
        /// <param name="format">(Unused) Always pass null</param>
        /// <param name="formatProvider">(Unused) Always pass null</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are passed</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var path = new StringBuilder();

                foreach (Element element in Elements)
                {
                    path.AppendFormat(formatProvider, "{0}", element);
                }

                return path.ToString();
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns true if the relative path does not specify any elements.
        /// </summary>
        public static bool IsEmpty(RelativePathFormatter relativePath)
        {
            if (relativePath != null)
            {
                return relativePath.Elements.Count == 0;
            }

            return true;
        }

        /// <summary>
        /// Parses a string representing a relative path and translates the namespace indexes.
        /// </summary>
        /// <remarks>
        /// Parses a string representing a relative path.
        /// </remarks>
        /// <exception cref="ServiceResultException">Thrown if any errors occur during parsing</exception>
        public static RelativePathFormatter Parse(
            string textToParse,
            NamespaceTable currentTable,
            NamespaceTable targetTable)
        {
            RelativePathFormatter path = Parse(textToParse);

            path?.TranslateNamespaceIndexes(currentTable, targetTable);

            return path;
        }

        /// <summary>
        /// Parses a string representing a relative path.
        /// </summary>
        /// <remarks>
        /// Parses a string representing a relative path.
        /// </remarks>
        /// <exception cref="ServiceResultException">Thrown if any errors occur during parsing</exception>
        public static RelativePathFormatter Parse(string textToParse)
        {
            if (string.IsNullOrEmpty(textToParse))
            {
                return new RelativePathFormatter();
            }

            var path = new RelativePathFormatter();

            try
            {
                var reader = new StringReader(textToParse);

                while (reader.Peek() != -1)
                {
                    var element = Element.Parse(reader);
                    path.Elements.Add(element);
                }
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIndexRangeInvalid,
                    CoreUtils.Format("Cannot parse relative path: '{0}'.", textToParse),
                    e);
            }

            return path;
        }

        /// <summary>
        /// A element in a relative path string.
        /// </summary>
        public class Element : IFormattable
        {
            /// <summary>
            /// Initializes the object from a RelativePathElement
            /// </summary>
            public Element(RelativePathElement element, ITypeTable typeTree)
            {
                if (element == null)
                {
                    throw new ArgumentNullException(nameof(element));
                }

                if (typeTree == null)
                {
                    throw new ArgumentNullException(nameof(typeTree));
                }

                ReferenceTypeName = default;
                TargetName = element.TargetName;
                ElementType = ElementType.ForwardReference;
                IncludeSubtypes = element.IncludeSubtypes;

                if (!element.IsInverse && element.IncludeSubtypes)
                {
                    if (element.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences)
                    {
                        ElementType = ElementType.AnyHierarchical;
                    }
                    else if (element.ReferenceTypeId == ReferenceTypeIds.Aggregates)
                    {
                        ElementType = ElementType.AnyComponent;
                    }
                    else
                    {
                        ReferenceTypeName = typeTree.FindReferenceTypeName(element.ReferenceTypeId);
                    }
                }
                else
                {
                    if (element.IsInverse)
                    {
                        ElementType = ElementType.InverseReference;
                    }

                    ReferenceTypeName = typeTree.FindReferenceTypeName(element.ReferenceTypeId);
                }
            }

            /// <summary>
            /// Initializes the object the default values.
            /// </summary>
            public Element()
            {
                ElementType = ElementType.AnyHierarchical;
                ReferenceTypeName = default;
                IncludeSubtypes = true;
                TargetName = default;
            }

            /// <summary>
            /// The type of element.
            /// </summary>
            public ElementType ElementType { get; set; }

            /// <summary>
            /// The browse name of the reference type to follow.
            /// </summary>
            public QualifiedName ReferenceTypeName { get; set; }

            /// <summary>
            /// Whether to include subtypes of the reference type.
            /// </summary>
            public bool IncludeSubtypes { get; set; }

            /// <summary>
            /// The browse name of the target to find.
            /// </summary>
            public QualifiedName TargetName { get; set; }

            /// <summary>
            /// Formats the relative path element as a string.
            /// </summary>
            public override string ToString()
            {
                return ToString(null, null);
            }

            /// <summary>
            /// Formats the numeric range as a string.
            /// </summary>
            /// <param name="format">(Unused) Always pass null</param>
            /// <param name="formatProvider">(Unused) Always pass null</param>
            /// <exception cref="FormatException">Thrown if non-null parameters are passed</exception>
            /// <exception cref="ServiceResultException"></exception>
            public string ToString(string format, IFormatProvider formatProvider)
            {
                if (format == null)
                {
                    var path = new StringBuilder();

                    // write the reference type component.
                    switch (ElementType)
                    {
                        case ElementType.AnyHierarchical:
                            path.Append('/');
                            break;
                        case ElementType.AnyComponent:
                            path.Append('.');
                            break;
                        case ElementType.ForwardReference:
                        case ElementType.InverseReference:
                            if (!ReferenceTypeName.IsNull &&
                                !string.IsNullOrEmpty(ReferenceTypeName.Name))
                            {
                                path.Append('<');

                                if (!IncludeSubtypes)
                                {
                                    path.Append('#');
                                }

                                if (ElementType == ElementType.InverseReference)
                                {
                                    path.Append('!');
                                }

                                if (ReferenceTypeName.NamespaceIndex != 0)
                                {
                                    path.AppendFormat(
                                        formatProvider,
                                        "{0}:",
                                        ReferenceTypeName.NamespaceIndex);
                                }

                                EncodeName(path, ReferenceTypeName.Name);
                                path.Append('>');
                            }

                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                "Unexpected ElementType value: {0}", ElementType);
                    }

                    // write the target browse name component.
                    if (!TargetName.IsNull && !string.IsNullOrEmpty(TargetName.Name))
                    {
                        if (TargetName.NamespaceIndex != 0)
                        {
                            path.AppendFormat(formatProvider, "{0}:", TargetName.NamespaceIndex);
                        }

                        EncodeName(path, TargetName.Name);
                    }

                    return path.ToString();
                }

                throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
            }

            /// <summary>
            /// Extracts a relative path element from a string.
            /// </summary>
            /// <param name="reader">The string read stream containing the text to convert to a RelativePathStringElement</param>
            public static Element Parse(StringReader reader)
            {
                var element = new Element();

                switch (reader.Peek())
                {
                    case '/':
                        element.ElementType = ElementType.AnyHierarchical;
                        reader.Read();
                        break;
                    case '.':
                        element.ElementType = ElementType.AnyComponent;
                        reader.Read();
                        break;
                    case '<':
                        element.ElementType = ElementType.ForwardReference;
                        reader.Read();

                        if (reader.Peek() == '#')
                        {
                            element.IncludeSubtypes = false;
                            reader.Read();
                        }

                        if (reader.Peek() == '!')
                        {
                            element.ElementType = ElementType.InverseReference;
                            reader.Read();
                        }

                        element.ReferenceTypeName = ParseName(reader, true);
                        break;
                    default:
                        element.ElementType = ElementType.AnyHierarchical;
                        break;
                }

                element.TargetName = ParseName(reader, false);

                return element;
            }

            /// <summary>
            /// Extracts a browse name with an optional namespace prefix from the reader.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            private static QualifiedName ParseName(StringReader reader, bool referenceName)
            {
                ushort namespaceIndex = 0;

                // extract namespace index if present.
                var buffer = new StringBuilder();

                int last = reader.Peek();

                for (int next = last; next != -1; next = reader.Peek(), last = next)
                {
                    if (!char.IsDigit((char)next))
                    {
                        if (next == ':')
                        {
                            reader.Read();
                            namespaceIndex = Convert.ToUInt16(
                                buffer.ToString(),
                                CultureInfo.InvariantCulture);
                            buffer.Length = 0;

                            // fetch next character.
                            last = reader.Peek();
                        }

                        break;
                    }

                    buffer.Append((char)next);
                    reader.Read();
                }

                // extract rest of name.
                for (int next = last; next != -1; next = reader.Peek())
                {
                    last = next;

                    // check for terminator.
                    if (referenceName)
                    {
                        if (next == '>')
                        {
                            reader.Read();
                            break;
                        }
                    }
                    else if (next is '<' or '/' or '.')
                    {
                        break;
                    }

                    // check for escape character.
                    if (next == '&')
                    {
                        next = reader.Read();
                        if (next == -1)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadSyntaxError,
                                "Unexpected end after escape character '&'.");
                        }
                        next = reader.Read();

                        if (next is '!' or ':' or '<' or '>' or '/' or '.' or '#' or '&')
                        {
                            buffer.Append((char)next);
                            continue;
                        }

                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            CoreUtils.Format("Invalid escape sequence '&{0}' in browse path.", next));
                    }

                    // check for invalid character.
                    if (next is '!' or ':' or '<' or '>' or '/' or '.' or '#' or '&')
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            CoreUtils.Format("Unexpected character '{0}' in browse path.", next));
                    }

                    // append character.
                    buffer.Append((char)next);
                    reader.Read();
                }

                // check for enclosing bracket.
                if (referenceName && last != '>')
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSyntaxError,
                        CoreUtils.Format(
                            "Missing closing '>' for reference type name in browse path."));
                }

                if (buffer.Length == 0)
                {
                    if (referenceName)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            CoreUtils.Format("Reference type name is null in browse path."));
                    }

                    if (namespaceIndex == 0)
                    {
                        return default;
                    }
                }

                return new QualifiedName(buffer.ToString(), namespaceIndex);
            }

            /// <summary>
            /// Encodes a name using the relative path syntax.
            /// </summary>
            private static void EncodeName(StringBuilder path, string name)
            {
                for (int ii = 0; ii < name.Length; ii++)
                {
                    switch (name[ii])
                    {
                        case '/':
                        case '.':
                        case '<':
                        case '>':
                        case ':':
                        case '!':
                        case '&':
                            path.Append('&');
                            break;
                    }

                    path.Append(name[ii]);
                }
            }
        }

        /// <summary>
        /// The type of relative path element.
        /// </summary>
        public enum ElementType
        {
            /// <summary>
            /// Any hierarchial reference should be followed ('/').
            /// </summary>
            AnyHierarchical = 0x01,

            /// <summary>
            /// Any component reference should be followed ('.').
            /// </summary>
            AnyComponent = 0x02,

            /// <summary>
            /// The forward reference identified by the browse name should be followed.
            /// </summary>
            ForwardReference = 0x03,

            /// <summary>
            /// The inverse reference identified by the browse name should be followed.
            /// </summary>
            InverseReference = 0x04
        }
    }
}
