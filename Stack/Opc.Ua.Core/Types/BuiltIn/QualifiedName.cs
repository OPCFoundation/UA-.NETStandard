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
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// A name qualified with a namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The QualifiedName is defined in <b>Part 3 - Address Space Model, Section 7.3</b>, titled
    /// <b>Qualified Name</b>.
    /// <br/></para>
    /// <para>
    /// The QualifiedName is a simple wrapper class that is used to generate a fully-qualified name
    /// for any type that has a name.
    /// <br/></para>
    /// <para>
    /// A <i>Fully Qualified</i> name is one that consists of a name, and an index of which namespace
    /// (within a namespace table) this name belongs to.
    /// For example<br/>
    /// <b>Namespace Index</b> = 1<br/>
    /// <b>Name</b> = MyName<br/>
    /// becomes:<br/>
    /// <i>1:MyName</i>
    /// <br/></para>
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class QualifiedName : ICloneable, IFormattable, IComparable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        internal QualifiedName()
        {
            XmlEncodedNamespaceIndex = 0;
            XmlEncodedName = null;
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <param name="value">The qualified name to copy</param>
        /// <exception cref="ArgumentNullException">Thrown if the provided value is null</exception>
        public QualifiedName(QualifiedName value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            XmlEncodedName = value.XmlEncodedName;
            XmlEncodedNamespaceIndex = value.XmlEncodedNamespaceIndex;
        }

        /// <summary>
        /// Initializes the object with a name.
        /// </summary>
        /// <param name="name">The name-portion to store as part of the fully qualified name</param>
        public QualifiedName(string name)
        {
            XmlEncodedNamespaceIndex = 0;
            XmlEncodedName = name;
        }

        /// <summary>
        /// Initializes the object with a name and a namespace index.
        /// </summary>
        /// <param name="name">The name-portion of the fully qualified name</param>
        /// <param name="namespaceIndex">The index of the namespace within the namespace-table</param>
        public QualifiedName(string name, ushort namespaceIndex)
        {
            XmlEncodedNamespaceIndex = namespaceIndex;
            XmlEncodedName = name;
        }

        /// <summary>
        /// The index of the namespace that qualifies the name.
        /// </summary>
        public ushort NamespaceIndex => XmlEncodedNamespaceIndex;

        /// <inheritdoc/>
        [DataMember(Name = "NamespaceIndex", Order = 1)]
        internal ushort XmlEncodedNamespaceIndex { get; set; }

        /// <summary>
        /// The unqualified name.
        /// </summary>
        public string Name => XmlEncodedName;

        /// <summary>
        /// Xml encoded name
        /// </summary>
        [DataMember(Name = "Name", Order = 2)]
        internal string XmlEncodedName { get; set; }

        /// <summary>
        /// Compares two QualifiedNames.
        /// </summary>
        /// <param name="obj">The object to compare to.</param>
        /// <returns>
        /// Less than zero if the instance is less than the object.
        /// Zero if the instance is equal to the object.
        /// Greater than zero if the instance is greater than the object.
        /// </returns>
        public int CompareTo(object obj)
        {
            if (obj is null)
            {
                return -1;
            }

            if (ReferenceEquals(this, obj))
            {
                return 0;
            }

            if (obj is not QualifiedName qname)
            {
                return typeof(QualifiedName).GetTypeInfo().GUID
                    .CompareTo(obj.GetType().GetTypeInfo().GUID);
            }

            if (qname.XmlEncodedNamespaceIndex != XmlEncodedNamespaceIndex)
            {
                return XmlEncodedNamespaceIndex.CompareTo(qname.XmlEncodedNamespaceIndex);
            }

            if (XmlEncodedName != null)
            {
                return string.CompareOrdinal(XmlEncodedName, qname.XmlEncodedName);
            }

            return 0;
        }

        /// <summary>
        /// Implements the operator &gt;.
        /// </summary>
        /// <param name="value1">The value1.</param>
        /// <param name="value2">The value2.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator >(QualifiedName value1, QualifiedName value2)
        {
            if (value1 is not null)
            {
                return value1.CompareTo(value2) > 0;
            }

            return false;
        }

        /// <summary>
        /// Implements the operator &lt;.
        /// </summary>
        /// <param name="value1">The value1.</param>
        /// <param name="value2">The value2.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator <(QualifiedName value1, QualifiedName value2)
        {
            if (value1 is not null)
            {
                return value1.CompareTo(value2) < 0;
            }

            return true;
        }

        /// <summary>
        /// Returns a suitable hash value for the instance.
        /// </summary>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            if (XmlEncodedName != null)
            {
                hash.Add(XmlEncodedName);
            }

            hash.Add(XmlEncodedNamespaceIndex);

            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="obj">The object to compare to this/me</param>
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is not QualifiedName qname)
            {
                return false;
            }

            if (qname.XmlEncodedNamespaceIndex != XmlEncodedNamespaceIndex)
            {
                return false;
            }

            return qname.XmlEncodedName == XmlEncodedName;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator ==(QualifiedName value1, QualifiedName value2)
        {
            if (value1 is not null)
            {
                return value1.Equals(value2);
            }

            return value2 is null;
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator !=(QualifiedName value1, QualifiedName value2)
        {
            if (value1 is not null)
            {
                return !value1.Equals(value2);
            }

            return value2 is not null;
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused) Always pass null</param>
        /// <param name="formatProvider">(Unused) Always pass null</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are passed</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                int capacity = (XmlEncodedName?.Length) ?? 0;

                var builder = new StringBuilder(capacity + 10);

                if (XmlEncodedNamespaceIndex == 0)
                {
                    // prepend the namespace index if the name contains a colon.
                    if (XmlEncodedName != null &&
                        XmlEncodedName.Contains(':', StringComparison.Ordinal))
                    {
                        builder.Append("0:");
                    }
                }
                else
                {
                    builder.Append(XmlEncodedNamespaceIndex)
                        .Append(':');
                }

                if (XmlEncodedName != null)
                {
                    builder.Append(XmlEncodedName);
                }

                return builder.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }

        /// <summary>
        /// Converts an expanded node id to a node id using a namespace table.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static QualifiedName Create(
            string name,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            // check for null.
            if (string.IsNullOrEmpty(name))
            {
                return Null;
            }

            // return a name using the default namespace.
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return new QualifiedName(name);
            }

            // find the namespace index.
            int namespaceIndex = -1;

            if (namespaceTable != null)
            {
                namespaceIndex = namespaceTable.GetIndex(namespaceUri);
            }

            // oops - not found.
            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "NamespaceUri ({0}) is not in the NamespaceTable.",
                    namespaceUri);
            }

            // return the name.
            return new QualifiedName(name, (ushort)namespaceIndex);
        }

        /// <summary>
        /// Parses a string containing a QualifiedName with the syntax n:qname
        /// </summary>
        /// <param name="text">The QualifiedName value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of circumstances, each time with a specific message.</exception>
        public static QualifiedName Parse(string text)
        {
            // check for null.
            if (string.IsNullOrEmpty(text))
            {
                return Null;
            }

            // extract local namespace index.
            ushort namespaceIndex = 0;
            int start = -1;

            for (int ii = 0; ii < text.Length; ii++)
            {
                char ch = text[ii];

                if (ch == ':')
                {
                    start = ii + 1;
                    break;
                }

                if (char.IsDigit(ch))
                {
                    namespaceIndex *= 10;
                    namespaceIndex += (ushort)(ch - '0');
                }
            }

            // no prefix found.
            if (start == -1)
            {
                return new QualifiedName(text);
            }

            return new QualifiedName(text[start..], namespaceIndex);
        }

        /// <summary>
        /// Parses a string containing a QualifiedName with the syntax n:qname
        /// </summary>
        /// <param name="context">The QualifiedName value as a string.</param>
        /// <param name="text">The QualifiedName value as a string.</param>
        /// <param name="updateTables">Whether the NamespaceTable should be updated with the NamespaceUri.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of circumstances, each time with a specific message.</exception>
        public static QualifiedName Parse(
            IServiceMessageContext context,
            string text,
            bool updateTables)
        {
            // check for null.
            if (string.IsNullOrEmpty(text))
            {
                return Null;
            }

            string originalText = text;
            int namespaceIndex = 0;

            if (text.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        $"Invalid QualifiedName ({originalText}).");
                }

                string namespaceUri = Utils.UnescapeUri(text.AsSpan()[4..index]);
                namespaceIndex = updateTables
                    ? context.NamespaceUris.GetIndexOrAppend(namespaceUri)
                    : context.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        $"No mapping to NamespaceIndex for NamespaceUri ({namespaceUri}).");
                }

                text = text[(index + 1)..];
            }
            else
            {
                int index = text.IndexOf(':', StringComparison.Ordinal);

                if (index > 0)
                {
                    if (ushort.TryParse(text[..index], out ushort nsIndex))
                    {
                        namespaceIndex = nsIndex;
                    }
                    else
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid,
                            $"Invalid QualifiedName ({originalText}).");
                    }
                }

                text = text[(index + 1)..];
            }

            return new QualifiedName(text, (ushort)namespaceIndex);
        }

        /// <summary>
        /// Formats a QualifiedName as a string.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="useNamespaceUri">The NamespaceUri is used instead of the NamespaceIndex.</param>
        /// <returns>The formatted identifier.</returns>
        public string Format(IServiceMessageContext context, bool useNamespaceUri = false)
        {
            if (string.IsNullOrEmpty(XmlEncodedName))
            {
                return null;
            }

            var buffer = new StringBuilder();

            if (XmlEncodedNamespaceIndex > 0)
            {
                if (useNamespaceUri)
                {
                    string namespaceUri = context.NamespaceUris.GetString(XmlEncodedNamespaceIndex);

                    if (!string.IsNullOrEmpty(namespaceUri))
                    {
                        buffer.Append("nsu=")
                            .Append(Utils.EscapeUri(namespaceUri))
                            .Append(';');
                    }
                    else
                    {
                        buffer.Append(XmlEncodedNamespaceIndex)
                            .Append(':');
                    }
                }
                else
                {
                    buffer.Append(XmlEncodedNamespaceIndex)
                        .Append(':');
                }
            }

            buffer.Append(XmlEncodedName);

            return buffer.ToString();
        }

        /// <summary>
        /// Returns true if the value is null.
        /// </summary>
        /// <param name="value">The qualified name to check</param>
        public static bool IsNull(QualifiedName value)
        {
            if (value != null)
            {
                if (value.XmlEncodedNamespaceIndex != 0 ||
                    !string.IsNullOrEmpty(value.XmlEncodedName))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Converts a string to a qualified name.
        /// </summary>
        /// <param name="value">The string to turn into a fully qualified name</param>
        public static implicit operator QualifiedName(string value)
        {
            return new QualifiedName(value);
        }

        /// <summary>
        /// Returns an instance of a null QualifiedName.
        /// </summary>
        public static QualifiedName Null { get; } = new QualifiedName();
    }

    /// <summary>
    /// A collection of QualifiedName objects.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of QualifiedName objects.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfQualifiedName",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "QualifiedName")]
    public class QualifiedNameCollection : List<QualifiedName>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public QualifiedNameCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The enumerated collection of qualified names to add to this new collection</param>
        public QualifiedNameCollection(IEnumerable<QualifiedName> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of this collection</param>
        public QualifiedNameCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The array to turn into a collection</param>
        public static QualifiedNameCollection ToQualifiedNameCollection(QualifiedName[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The array to turn into a collection</param>
        public static implicit operator QualifiedNameCollection(QualifiedName[] values)
        {
            return ToQualifiedNameCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new QualifiedNameCollection(Count);

            foreach (QualifiedName element in this)
            {
                clone.Add(Utils.Clone(element));
            }

            return clone;
        }
    }
}
