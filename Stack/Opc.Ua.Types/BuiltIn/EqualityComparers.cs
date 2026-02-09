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
using System.Linq;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Helper which implements a NodeId IEqualityComparer for Linq queries.
    /// </summary>
    public class NodeIdComparer : IEqualityComparer<NodeId>
    {
        /// <inheritdoc/>
        public static NodeIdComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(NodeId x, NodeId y)
        {
            return x.Equals(y);
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeId nodeId)
        {
            return nodeId.GetHashCode();
        }
    }

    /// <summary>
    /// Node id comparer
    /// </summary>
    internal class ExpandedNodeIdComparer : IEqualityComparer<ExpandedNodeId>
    {
        /// <inheritdoc/>
        public static ExpandedNodeIdComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId x, ExpandedNodeId y)
        {
            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(ExpandedNodeId obj)
        {
            return obj.GetHashCode();
        }
    }

    /// <summary>
    /// Helper to compare arrays for deep equality
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class SequenceEqualityComparer<T> : IEqualityComparer<T[]>
        where T : unmanaged, IEquatable<T>
    {
        /// <inheritdoc/>
        public static SequenceEqualityComparer<T> Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(T[] x, T[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            return x.AsSpan().SequenceEqual(y.AsSpan());
        }

        /// <inheritdoc/>
        public int GetHashCode(T[] obj)
        {
            var hash = new HashCode();
            if (obj != null)
            {
                foreach (T item in obj)
                {
                    hash.Add(item);
                }
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Compare arrays of date time as per OPC UA rules
    /// </summary>
    public sealed class DateTimeArrayComparer : IEqualityComparer<DateTime[]>
    {
        /// <inheritdoc/>
        public static DateTimeArrayComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(DateTime[] x, DateTime[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!CoreUtils.IsEqual(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public int GetHashCode(DateTime[] obj)
        {
            var hash = new HashCode();
            if (obj != null)
            {
                foreach (DateTime item in obj)
                {
                    hash.Add(DateTimeComparer.Default.GetHashCode(item));
                }
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Compare date times
    /// </summary>
    public sealed class DateTimeComparer : IEqualityComparer<DateTime>
    {
        /// <inheritdoc/>
        public static DateTimeComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(DateTime x, DateTime y)
        {
            return CoreUtils.IsEqual(x, y);
        }

        /// <inheritdoc/>
        public int GetHashCode(DateTime obj)
        {
            if (obj <= CoreUtils.TimeBase)
            {
                return 0;
            }

            if (obj >= DateTime.MaxValue)
            {
                return int.MaxValue;
            }
            return obj.ToUniversalTime().GetHashCode();
        }
    }

    /// <summary>
    /// Helper to compare arrays for deep equality
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ArrayEqualityComparer<T> : IEqualityComparer<T[]>
    {
        /// <inheritdoc/>
        public static ArrayEqualityComparer<T> Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(T[] x, T[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!x[i].Equals(y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public int GetHashCode(T[] obj)
        {
            var hash = new HashCode();
            if (obj != null)
            {
                foreach (T item in obj)
                {
                    hash.Add(item);
                }
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Compares a byte string for deep equality
    /// </summary>
    public sealed class ByteStringEqualityComparer : IEqualityComparer<byte[]>
    {
        /// <inheritdoc/>
        public static ByteStringEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(byte[] x, byte[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            return x.SequenceEqual(y);
        }

        /// <inheritdoc/>
        public int GetHashCode(byte[] obj)
        {
            if (obj is null)
            {
                return 0;
            }
            var hash = new HashCode();
#if NET8_0_OR_GREATER
            hash.AddBytes(obj);
#else
            foreach (byte item in obj)
            {
                hash.Add(item);
            }
#endif
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Compares an array of byte strings for deep equality
    /// </summary>
    public sealed class ByteStringArrayEqualityComparer : IEqualityComparer<byte[][]>
    {
        /// <inheritdoc/>
        public static ByteStringArrayEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(byte[][] x, byte[][] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!ByteStringEqualityComparer.Default.Equals(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public int GetHashCode(byte[][] obj)
        {
            var hash = new HashCode();
            if (obj != null)
            {
                foreach (byte[] item in obj)
                {
                    hash.Add(ByteStringEqualityComparer.Default.GetHashCode(item));
                }
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Deep comparison of XmlElement array
    /// </summary>
    public sealed class XmlElementArrayStringEqualityComparer : IEqualityComparer<XmlElement[]>
    {
        /// <inheritdoc/>
        public static XmlElementArrayStringEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(XmlElement[] x, XmlElement[] y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!XmlElementStringEqualityComparer.Default.Equals(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public int GetHashCode(XmlElement[] obj)
        {
            var hash = new HashCode();
            if (obj != null)
            {
                foreach (XmlElement item in obj)
                {
                    hash.Add(XmlElementStringEqualityComparer.Default.GetHashCode(item));
                }
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// String comparison of xml element
    /// </summary>
    public sealed class XmlElementStringEqualityComparer : IEqualityComparer<XmlElement>
    {
        /// <inheritdoc/>
        public static XmlElementStringEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(XmlElement x, XmlElement y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            return string.Equals(x.OuterXml, y.OuterXml, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public int GetHashCode(XmlElement obj)
        {
            return EqualityComparer<string>.Default.GetHashCode(obj?.OuterXml);
        }
    }

    /// <summary>
    /// String comparison of xml element
    /// </summary>
    public sealed class XmlQualifiedNameEqualityComparer : IEqualityComparer<XmlQualifiedName>
    {
        /// <inheritdoc/>
        public static XmlQualifiedNameEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(XmlQualifiedName x, XmlQualifiedName y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            return
                string.Equals(x.Name, y.Name, StringComparison.Ordinal) &&
                string.Equals(x.Namespace, y.Namespace, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public int GetHashCode(XmlQualifiedName obj)
        {
            return HashCode.Combine(obj?.Name, obj?.Namespace);
        }
    }

    /// <summary>
    /// Compare reference equality
    /// </summary>
    public sealed class ReferenceEqualityComparer : IEqualityComparer<IReference>
    {
        /// <summary>
        /// Get an instance of the reference equality comparer.
        /// </summary>
        public static ReferenceEqualityComparer Default { get; } = new();

        /// <inheritdoc/>
        public bool Equals(IReference x, IReference y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(x.TargetId, y.TargetId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(x.ReferenceTypeId, y.ReferenceTypeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(x.IsInverse, y.IsInverse))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public int GetHashCode(IReference obj)
        {
            return HashCode.Combine(
                obj.TargetId,
                obj.ReferenceTypeId,
                obj.IsInverse);
        }
    }
}
