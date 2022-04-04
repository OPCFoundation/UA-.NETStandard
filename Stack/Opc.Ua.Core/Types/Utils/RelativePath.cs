/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// A class that stores a relative path
    /// </summary>
    public partial class RelativePath
    {
        #region Constructors
        /// <summary>
        /// Creates a relative path to follow any hierarchial references to find the specified browse name.
        /// </summary>
        public RelativePath(QualifiedName browseName) : this(ReferenceTypeIds.HierarchicalReferences, false, true, browseName)
        {
        }

        /// <summary>
        /// Creates a relative path to follow the forward reference type to find the specified browse name.
        /// </summary>
        public RelativePath(NodeId referenceTypeId, QualifiedName browseName) : this(referenceTypeId, false, true, browseName)
        {
        }

        /// <summary>
        /// Creates a relative path to follow the forward reference type to find the specified browse name.
        /// </summary>
        public RelativePath(NodeId referenceTypeId, bool isInverse, bool includeSubtypes, QualifiedName browseName)
        {
            Initialize();

            RelativePathElement element = new RelativePathElement();

            element.ReferenceTypeId = referenceTypeId;
            element.IsInverse = isInverse;
            element.IncludeSubtypes = includeSubtypes;
            element.TargetName = browseName;

            m_elements.Add(element);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Formats the relative path as a string.
        /// </summary>
        public string Format(ITypeTable typeTree)
        {
            RelativePathFormatter formatter = new RelativePathFormatter(this, typeTree);
            return formatter.ToString();
        }

        /// <summary>
        /// Returns true if the relative path does not specify any elements.
        /// </summary>
        public static bool IsEmpty(RelativePath relativePath)
        {
            if (relativePath != null)
            {
                return relativePath.Elements.Count == 0;
            }

            return true;
        }

        /// <summary>
        /// Parses a relative path formatted as a string. 
        /// </summary>
        public static RelativePath Parse(string browsePath, ITypeTable typeTree)
        {
            if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

            // parse the string.
            RelativePathFormatter formatter = RelativePathFormatter.Parse(browsePath);

            // convert the browse names to node ids.
            RelativePath relativePath = new RelativePath();

            foreach (RelativePathFormatter.Element element in formatter.Elements)
            {
                RelativePathElement parsedElement = new RelativePathElement();

                parsedElement.ReferenceTypeId = null;
                parsedElement.IsInverse = false;
                parsedElement.IncludeSubtypes = element.IncludeSubtypes;
                parsedElement.TargetName = element.TargetName;

                switch (element.ElementType)
                {
                    case RelativePathFormatter.ElementType.AnyHierarchical:
                    {
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    }

                    case RelativePathFormatter.ElementType.AnyComponent:
                    {
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                        break;
                    }

                    case RelativePathFormatter.ElementType.ForwardReference:
                    {
                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(element.ReferenceTypeName);
                        break;
                    }

                    case RelativePathFormatter.ElementType.InverseReference:
                    {
                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(element.ReferenceTypeName);
                        parsedElement.IsInverse = true;
                        break;
                    }
                }

                if (NodeId.IsNull(parsedElement.ReferenceTypeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSyntaxError,
                        "Could not convert BrowseName to a ReferenceTypeId: {0}",
                        element.ReferenceTypeName);
                }

                relativePath.Elements.Add(parsedElement);
            }

            return relativePath;
        }

        /// <summary>
        /// Parses a relative path formatted as a string. 
        /// </summary>
        public static RelativePath Parse(
            string browsePath,
            ITypeTable typeTree,
            NamespaceTable currentTable,
            NamespaceTable targetTable)

        {
            // parse the string.
            RelativePathFormatter formatter = RelativePathFormatter.Parse(browsePath, currentTable, targetTable);

            // convert the browse names to node ids.
            RelativePath relativePath = new RelativePath();

            foreach (RelativePathFormatter.Element element in formatter.Elements)
            {
                RelativePathElement parsedElement = new RelativePathElement();

                parsedElement.ReferenceTypeId = null;
                parsedElement.IsInverse = false;
                parsedElement.IncludeSubtypes = element.IncludeSubtypes;
                parsedElement.TargetName = element.TargetName;

                switch (element.ElementType)
                {
                    case RelativePathFormatter.ElementType.AnyHierarchical:
                    {
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    }

                    case RelativePathFormatter.ElementType.AnyComponent:
                    {
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                        break;
                    }

                    case RelativePathFormatter.ElementType.ForwardReference:
                    case RelativePathFormatter.ElementType.InverseReference:
                    {
                        if (typeTree == null)
                        {
                            throw new InvalidOperationException("Cannot parse path with reference names without a type table.");
                        }

                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(element.ReferenceTypeName);
                        parsedElement.IsInverse = element.ElementType == RelativePathFormatter.ElementType.InverseReference;
                        break;
                    }
                }

                if (NodeId.IsNull(parsedElement.ReferenceTypeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSyntaxError,
                        "Could not convert BrowseName to a ReferenceTypeId: {0}",
                        element.ReferenceTypeName);
                }

                relativePath.Elements.Add(parsedElement);
            }

            return relativePath;
        }
        #endregion
    }

    /// <summary>
    /// A class that stores a relative path string
    /// </summary>
    public class RelativePathFormatter : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object the default values.
        /// </summary>
        public RelativePathFormatter(RelativePath relativePath, ITypeTable typeTree)
        {
            m_elements = new List<Element>();

            if (relativePath != null)
            {
                foreach (RelativePathElement element in relativePath.Elements)
                {
                    m_elements.Add(new Element(element, typeTree));
                }
            }
        }

        /// <summary>
        /// Initializes the object the default values.
        /// </summary>
        public RelativePathFormatter()
        {
            m_elements = new List<Element>();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The elements in the relative path.
        /// </summary>
        /// <remarks>
        /// The elements in the relative path.
        /// </remarks>
        public List<Element> Elements => m_elements;

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
            foreach (Element element in m_elements)
            {
                // check reference type name.
                QualifiedName qname = element.ReferenceTypeName;

                if (qname != null && qname.NamespaceIndex > 1)
                {
                    if (qname.NamespaceIndex < mappings.Length)
                    {
                        if (mappings[qname.NamespaceIndex] == -1)
                        {
                            mappings[qname.NamespaceIndex] = targetTable.GetIndexOrAppend(uris[qname.NamespaceIndex]);
                        }
                    }
                }

                // check target name.
                qname = element.TargetName;

                if (qname != null && qname.NamespaceIndex > 1)
                {
                    if (qname.NamespaceIndex < mappings.Length)
                    {
                        if (mappings[qname.NamespaceIndex] == -1)
                        {
                            mappings[qname.NamespaceIndex] = targetTable.GetIndexOrAppend(uris[qname.NamespaceIndex]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates the path to use the indexes from the target table.
        /// </summary>
        /// <param name="currentTable">The NamespaceTable which the RelativePathString currently references</param>
        /// <param name="targetTable">The NamespaceTable which the RelativePathString should reference</param>
        public void TranslateNamespaceIndexes(NamespaceTable currentTable, NamespaceTable targetTable)
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
            foreach (Element element in m_elements)
            {
                QualifiedName qname = element.ReferenceTypeName;

                if (qname != null && qname.NamespaceIndex > 0)
                {
                    if (qname.NamespaceIndex < mappings.Length && mappings[qname.NamespaceIndex] > 0)
                    {
                        element.ReferenceTypeName = new QualifiedName(qname.Name, (ushort)mappings[qname.NamespaceIndex]);
                    }
                }

                qname = element.TargetName;

                if (qname != null && qname.NamespaceIndex > 0)
                {
                    if (qname.NamespaceIndex < mappings.Length && mappings[qname.NamespaceIndex] > 0)
                    {
                        element.TargetName = new QualifiedName(qname.Name, (ushort)mappings[qname.NamespaceIndex]);
                    }
                }
            }
        }
        #endregion

        #region Overriden Members
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
        #endregion

        #region IFormattable Members
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
                StringBuilder path = new StringBuilder();

                foreach (Element element in m_elements)
                {
                    path.AppendFormat("{0}", element);
                }

                return path.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Static Members
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
        public static RelativePathFormatter Parse(string textToParse, NamespaceTable currentTable, NamespaceTable targetTable)
        {
            RelativePathFormatter path = Parse(textToParse);

            if (path != null)
            {
                path.TranslateNamespaceIndexes(currentTable, targetTable);
            }

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
            if (String.IsNullOrEmpty(textToParse))
            {
                return new RelativePathFormatter();
            }

            RelativePathFormatter path = new RelativePathFormatter();

            try
            {
                StringReader reader = new StringReader(textToParse);

                while (reader.Peek() != -1)
                {
                    Element element = Element.Parse(reader);
                    path.m_elements.Add(element);
                }
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIndexRangeInvalid,
                    Utils.Format("Cannot parse relative path: '{0}'.", textToParse),
                    e);
            }

            return path;
        }
        #endregion

        #region Element class
        /// <summary>
        /// A element in a relative path string.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
        public class Element : IFormattable
        {
            #region Constructors
            /// <summary>
            /// Initializes the object from a RelativePathElement
            /// </summary>
            public Element(RelativePathElement element, ITypeTable typeTree)
            {
                if (element == null) throw new ArgumentNullException(nameof(element));
                if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

                m_referenceTypeName = null;
                m_targetName = element.TargetName;
                m_elementType = RelativePathFormatter.ElementType.ForwardReference;
                m_includeSubtypes = element.IncludeSubtypes;

                if (!element.IsInverse && element.IncludeSubtypes)
                {
                    if (element.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences)
                    {
                        m_elementType = RelativePathFormatter.ElementType.AnyHierarchical;
                    }
                    else if (element.ReferenceTypeId == ReferenceTypeIds.Aggregates)
                    {
                        m_elementType = RelativePathFormatter.ElementType.AnyComponent;
                    }
                    else
                    {
                        m_referenceTypeName = typeTree.FindReferenceTypeName(element.ReferenceTypeId);
                    }
                }
                else
                {
                    if (element.IsInverse)
                    {
                        m_elementType = RelativePathFormatter.ElementType.InverseReference;
                    }

                    m_referenceTypeName = typeTree.FindReferenceTypeName(element.ReferenceTypeId);
                }
            }

            /// <summary>
            /// Initializes the object the default values.
            /// </summary>
            public Element()
            {
                m_elementType = RelativePathFormatter.ElementType.AnyHierarchical;
                m_referenceTypeName = null;
                m_includeSubtypes = true;
                m_targetName = null;
            }
            #endregion

            #region Public Properties
            /// <summary>
            /// The type of element.
            /// </summary>
            public ElementType ElementType
            {
                get { return m_elementType; }
                set { m_elementType = value; }
            }

            /// <summary>
            /// The browse name of the reference type to follow.
            /// </summary>
            public QualifiedName ReferenceTypeName
            {
                get { return m_referenceTypeName; }
                set { m_referenceTypeName = value; }
            }

            /// <summary>
            /// Whether to include subtypes of the reference type.
            /// </summary>
            public bool IncludeSubtypes
            {
                get { return m_includeSubtypes; }
                set { m_includeSubtypes = value; }
            }

            /// <summary>
            /// The browse name of the target to find.
            /// </summary>
            public QualifiedName TargetName
            {
                get { return m_targetName; }
                set { m_targetName = value; }
            }
            #endregion

            #region Overriden Members
            /// <summary>
            /// Formats the relative path element as a string.
            /// </summary>
            public override string ToString()
            {
                return ToString(null, null);
            }
            #endregion

            #region IFormattable Members
            /// <summary>
            /// Formats the numeric range as a string.
            /// </summary>
            /// <param name="format">(Unused) Always pass null</param>
            /// <param name="formatProvider">(Unused) Always pass null</param>
            /// <exception cref="FormatException">Thrown if non-null parameters are passed</exception>
            public string ToString(string format, IFormatProvider formatProvider)
            {
                if (format == null)
                {
                    StringBuilder path = new StringBuilder();

                    // write the reference type component.
                    switch (m_elementType)
                    {
                        case ElementType.AnyHierarchical:
                        {
                            path.Append('/');
                            break;
                        }

                        case ElementType.AnyComponent:
                        {
                            path.Append('.');
                            break;
                        }

                        case ElementType.ForwardReference:
                        case ElementType.InverseReference:
                        {
                            if (m_referenceTypeName != null && !String.IsNullOrEmpty(m_referenceTypeName.Name))
                            {
                                path.Append('<');

                                if (!m_includeSubtypes)
                                {
                                    path.Append('#');
                                }

                                if (m_elementType == ElementType.InverseReference)
                                {
                                    path.Append('!');
                                }

                                if (m_referenceTypeName.NamespaceIndex != 0)
                                {
                                    path.AppendFormat("{0}:", m_referenceTypeName.NamespaceIndex);
                                }

                                EncodeName(path, m_referenceTypeName.Name);
                                path.Append('>');
                            }

                            break;
                        }
                    }

                    // write the target browse name component.
                    if (m_targetName != null && !String.IsNullOrEmpty(m_targetName.Name))
                    {
                        if (m_targetName.NamespaceIndex != 0)
                        {
                            path.AppendFormat("{0}:", m_targetName.NamespaceIndex);
                        }

                        EncodeName(path, m_targetName.Name);
                    }

                    return path.ToString();
                }

                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }
            #endregion

            #region Static Methods
            /// <summary>
            /// Extracts a relative path element from a string.
            /// </summary>
            /// <param name="reader">The string read stream containing the text to convert to a RelativePathStringElement</param>
            public static Element Parse(StringReader reader)
            {
                Element element = new Element();

                int next = reader.Peek();

                switch (next)
                {
                    case '/':
                    {
                        element.ElementType = ElementType.AnyHierarchical;
                        reader.Read();
                        break;
                    }

                    case '.':
                    {
                        element.ElementType = ElementType.AnyComponent;
                        reader.Read();
                        break;
                    }

                    case '<':
                    {
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
                    }

                    default:
                    {
                        element.ElementType = ElementType.AnyHierarchical;
                        break;
                    }
                }

                element.TargetName = ParseName(reader, false);

                return element;
            }
            #endregion

            #region Private Methods
            /// <summary>
            /// Extracts a browse name with an optional namespace prefix from the reader.
            /// </summary>
            private static QualifiedName ParseName(
                StringReader reader,
                bool referenceName)
            {
                ushort namespaceIndex = 0;

                // extract namespace index if present.
                StringBuilder buffer = new StringBuilder();

                int last = reader.Peek();

                for (int next = last; next != -1; next = reader.Peek(), last=next)
                {
                    if (!Char.IsDigit((char)next))
                    {
                        if (next == ':')
                        {
                            reader.Read();
                            namespaceIndex = Convert.ToUInt16(buffer.ToString(), CultureInfo.InvariantCulture);
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
                    else
                    {
                        if (next == '<' || next == '/' || next == '.')
                        {
                            break;
                        }
                    }

                    // check for invalid character.            
                    if (next == '!' || next == ':' || next == '<' || next == '>' || next == '/' || next == '.')
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            Utils.Format("Unexpected character '{0}' in browse path.", next));

                    }

                    // check for escape character.
                    if (next == '&')
                    {
                        next = reader.Read();
                        next = reader.Read();
                        buffer.Append((char)next);
                        continue;
                    }

                    // append character.
                    buffer.Append((char)next);
                    reader.Read();
                }

                // check for enclosing bracket.
                if (referenceName)
                {
                    if (last != '>')
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            Utils.Format("Missing file '>' for reference type name in browse path."));
                    }
                }

                if (buffer.Length == 0)
                {
                    if (referenceName)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSyntaxError,
                            Utils.Format("Reference type name is null in browse path."));
                    }

                    if (namespaceIndex == 0)
                    {
                        return null;
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
                        {
                            path.Append('&');
                            break;
                        }
                    }

                    path.Append(name[ii]);
                }
            }
            #endregion

            #region Private Fields
            private ElementType m_elementType;
            private bool m_includeSubtypes;
            private QualifiedName m_referenceTypeName;
            private QualifiedName m_targetName;
            #endregion
        }
        #endregion

        #region ElementType enumeration
        /// <summary>
        /// The type of relative path element.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
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
        #endregion

        #region Private Fields
        private List<Element> m_elements;
        #endregion
    }
}
