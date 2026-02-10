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
using System.Runtime.Serialization;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A name qualified with a namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The QualifiedName is defined in <b>Part 3 - Address Space Model,
    /// Section 7.3</b>, titled
    /// <b>Qualified Name</b>.
    /// <br/></para>
    /// <para>
    /// The QualifiedName is a simple wrapper class that is used to
    /// generate a fully-qualified name for any type that has a name.
    /// <br/></para>
    /// <para>
    /// A <i>Fully Qualified</i> name is one that consists of a name,
    /// and an index of which namespace (within a namespace table) this
    /// name belongs to.
    /// For example<br/>
    /// <b>Namespace Index</b> = 1<br/>
    /// <b>Name</b> = MyName<br/>
    /// becomes:<br/>
    /// <i>1:MyName</i>
    /// <br/></para>
    /// </remarks>
    public readonly struct QualifiedName :
        IFormattable,
        IComparable,
        IEquatable<QualifiedName>, IComparable<QualifiedName>
    {
        /// <summary>
        /// Initializes the object with a name.
        /// </summary>
        /// <param name="name">The name-portion to store as part of
        /// the fully qualified name</param>
        public QualifiedName(string name)
        {
            NamespaceIndex = 0;
            Name = name;
        }

        /// <summary>
        /// Initializes the object with a name and a namespace index.
        /// </summary>
        /// <param name="name">The name-portion of the fully qualified
        /// name</param>
        /// <param name="namespaceIndex">The index of the namespace
        /// within the namespace-table</param>
        public QualifiedName(string name, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;
            Name = name;
        }

        /// <summary>
        /// Returns true if the item is null
        /// </summary>
        public bool IsNull => NamespaceIndex == 0 && string.IsNullOrEmpty(Name);

        /// <summary>
        /// The unqualified name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The index of the namespace that qualifies the name.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// Create a new QualifiedName with the specified NamespaceIndex
        /// </summary>
        public QualifiedName WithNamespaceIndex(ushort namespaceIndex)
        {
            return new QualifiedName(Name, namespaceIndex);
        }

        /// <summary>
        /// Create a new QualifiedName with the specified Name
        /// </summary>
        public QualifiedName WithName(string name)
        {
            return new QualifiedName(name, NamespaceIndex);
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            return obj switch
            {
                QualifiedName qname => CompareTo(qname),
                _ => -1
            };
        }

        /// <inheritdoc/>
        public int CompareTo(QualifiedName obj)
        {
            if (obj.NamespaceIndex != NamespaceIndex)
            {
                return NamespaceIndex.CompareTo(obj.NamespaceIndex);
            }

            if (Name != null)
            {
                return string.CompareOrdinal(Name, obj.Name);
            }
            return 0;
        }

        /// <inheritdoc/>
        public static bool operator >(QualifiedName value1, QualifiedName value2)
        {
            return value1.CompareTo(value2) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <(QualifiedName value1, QualifiedName value2)
        {
            return value1.CompareTo(value2) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(QualifiedName left, QualifiedName right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(QualifiedName left, QualifiedName right)
        {
            return right.CompareTo(left) <= 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                QualifiedName qname => Equals(qname),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(QualifiedName qname)
        {
            if (qname.NamespaceIndex != NamespaceIndex)
            {
                return false;
            }
            return qname.Name == Name;
        }

        /// <inheritdoc/>
        public static bool operator ==(QualifiedName value1, QualifiedName value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(QualifiedName value1, QualifiedName value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            if (Name != null)
            {
                hash.Add(Name);
            }
            hash.Add(NamespaceIndex);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                int capacity = (Name?.Length) ?? 0;

                var builder = new StringBuilder(capacity + 10);

                if (NamespaceIndex == 0)
                {
                    // prepend the namespace index if the name contains a colon.
                    if (Name != null &&
                        Name.Contains(':', StringComparison.Ordinal))
                    {
                        builder.Append("0:");
                    }
                }
                else
                {
                    builder.Append(NamespaceIndex)
                        .Append(':');
                }

                if (Name != null)
                {
                    builder.Append(Name);
                }

                return builder.ToString();
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
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
        /// Returns true if the QualifiedName is valid.
        /// </summary>
        /// <param name="value">The name to be validated.</param>
        /// <param name="namespaceUris">The table namespaces known to
        /// the server.</param>
        /// <returns>True if the name is value.</returns>
        public static bool IsValid(QualifiedName value, NamespaceTable namespaceUris)
        {
            if (string.IsNullOrEmpty(value.Name))
            {
                return false;
            }

            if (namespaceUris != null &&
                namespaceUris.GetString(value.NamespaceIndex) == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses a string containing a QualifiedName with the syntax n:qname
        /// </summary>
        /// <param name="text">The QualifiedName value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of
        /// circumstances, each time with a specific message.</exception>
        public static QualifiedName Parse(string text)
        {
            // check for null.
            if (string.IsNullOrEmpty(text))
            {
                return Null;
            }

            // extract local namespace index.
            int start = text.IndexOf(':', StringComparison.Ordinal);
            if (start < 0 ||
                !ushort.TryParse(text[..start], out ushort namespaceIndex) ||
                start + 1 == text.Length)
            {
                return new QualifiedName(text);
            }
            return new QualifiedName(text[(start + 1)..], namespaceIndex);
        }

        /// <summary>
        /// Parses a string containing a QualifiedName with the syntax n:qname
        /// </summary>
        /// <param name="context">The QualifiedName value as a string.</param>
        /// <param name="text">The QualifiedName value as a string.</param>
        /// <param name="updateTables">Whether the NamespaceTable should be updated
        /// with the NamespaceUri.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of
        /// circumstances, each time with a specific message.</exception>
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

                string namespaceUri = CoreUtils.UnescapeUri(text.AsSpan()[4..index]);
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
        /// <param name="useNamespaceUri">The NamespaceUri is used instead
        /// of the NamespaceIndex.</param>
        /// <returns>The formatted identifier.</returns>
        public string Format(IServiceMessageContext context, bool useNamespaceUri = false)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return null;
            }

            var buffer = new StringBuilder();

            if (NamespaceIndex > 0)
            {
                if (useNamespaceUri)
                {
                    string namespaceUri = context.NamespaceUris.GetString(NamespaceIndex);
                    if (!string.IsNullOrEmpty(namespaceUri))
                    {
                        buffer.Append("nsu=")
                            .Append(CoreUtils.EscapeUri(namespaceUri))
                            .Append(';');
                    }
                    else
                    {
                        buffer.Append(NamespaceIndex)
                            .Append(':');
                    }
                }
                else
                {
                    buffer.Append(NamespaceIndex)
                        .Append(':');
                }
            }

            buffer.Append(Name);
            return buffer.ToString();
        }

        /// <summary>
        /// Converts a string to a qualified name.
        /// </summary>
        /// <param name="value">The string to turn into a fully qualified name</param>
        public static QualifiedName ToQualifiedName(string value)
        {
            return new QualifiedName(value);
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
        public static readonly QualifiedName Null;
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
        /// <inheritdoc/>
        public QualifiedNameCollection()
        {
        }

        /// <inheritdoc/>
        public QualifiedNameCollection(IEnumerable<QualifiedName> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public QualifiedNameCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static implicit operator QualifiedNameCollection(QualifiedName[] values)
        {
            return [.. values ?? []];
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new QualifiedNameCollection(Count);

            foreach (QualifiedName element in this)
            {
                clone.Add(element);
            }

            return clone;
        }
    }

    /// <summary>
    /// Serializable representation of a QualifiedName
    /// </summary>
    [DataContract(
        Name = "QualifiedName",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableQualifiedName :
        ISurrogateFor<QualifiedName>
    {
        /// <inheritdoc/>
        public SerializableQualifiedName()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableQualifiedName(QualifiedName value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public QualifiedName Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// Namespace index
        /// </summary>
        [DataMember(Name = "NamespaceIndex", Order = 1)]
        internal ushort XmlEncodedNamespaceIndex
        {
            get => Value.NamespaceIndex;
            set => Value = Value.WithNamespaceIndex(value);
        }

        /// <summary>
        /// Xml encoded name
        /// </summary>
        [DataMember(Name = "Name", Order = 2)]
        internal string XmlEncodedName
        {
            get => Value.Name;
            set => Value = Value.WithName(value);
        }
    }
}
