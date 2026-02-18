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
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;

namespace Opc.Ua
{
    /// <summary>
    /// A collection of Boolean values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfBoolean",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Boolean")]
    public class BooleanCollection : List<bool>, ICloneable
    {
        /// <inheritdoc/>
        public BooleanCollection()
        {
        }

        /// <inheritdoc/>
        public BooleanCollection(IEnumerable<bool> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public BooleanCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static BooleanCollection ToBooleanCollection(ArrayOf<bool> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator BooleanCollection(ArrayOf<bool> values)
        {
            return ToBooleanCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator BooleanCollection(bool[] values)
        {
            return ToBooleanCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<bool>(BooleanCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new BooleanCollection(this);
        }
    }

    /// <summary>
    /// A collection of SByte values.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed list of <see cref="sbyte"/> values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfSByte",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "SByte")]
    public class SByteCollection : List<sbyte>, ICloneable
    {
        /// <inheritdoc/>
        public SByteCollection()
        {
        }

        /// <inheritdoc/>
        public SByteCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public SByteCollection(IEnumerable<sbyte> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static SByteCollection ToSByteCollection(ArrayOf<sbyte> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator SByteCollection(ArrayOf<sbyte> values)
        {
            return ToSByteCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator SByteCollection(sbyte[] values)
        {
            return ToSByteCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<sbyte>(SByteCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new SByteCollection(this);
        }
    }

    /// <summary>
    /// A collection of Byte values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfByte",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Byte")]
    public class ByteCollection : List<byte>, ICloneable
    {
        /// <inheritdoc/>
        public ByteCollection()
        {
        }

        /// <inheritdoc/>
        public ByteCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ByteCollection(IEnumerable<byte> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ByteCollection ToByteCollection(ArrayOf<byte> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ByteCollection(ArrayOf<byte> values)
        {
            return ToByteCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ByteCollection(byte[] values)
        {
            return ToByteCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<byte>(ByteCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ByteCollection(this);
        }
    }

    /// <summary>
    /// A collection of Int16 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfInt16",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Int16")]
    public class Int16Collection : List<short>, ICloneable
    {
        /// <inheritdoc/>
        public Int16Collection()
        {
        }

        /// <inheritdoc/>
        public Int16Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public Int16Collection(IEnumerable<short> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static Int16Collection ToInt16Collection(ArrayOf<short> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator Int16Collection(ArrayOf<short> values)
        {
            return ToInt16Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator Int16Collection(short[] values)
        {
            return ToInt16Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<short>(Int16Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new Int16Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt16 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfUInt16",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "UInt16")]
    public class UInt16Collection : List<ushort>, ICloneable
    {
        /// <inheritdoc/>
        public UInt16Collection()
        {
        }

        /// <inheritdoc/>
        public UInt16Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public UInt16Collection(IEnumerable<ushort> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static UInt16Collection ToUInt16Collection(ArrayOf<ushort> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator UInt16Collection(ArrayOf<ushort> values)
        {
            return ToUInt16Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator UInt16Collection(ushort[] values)
        {
            return ToUInt16Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ushort>(UInt16Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UInt16Collection(this);
        }
    }

    /// <summary>
    /// A collection of Int32 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfInt32",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Int32")]
    public class Int32Collection : List<int>, ICloneable
    {
        /// <inheritdoc/>
        public Int32Collection()
        {
        }

        /// <inheritdoc/>
        public Int32Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public Int32Collection(IEnumerable<int> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static Int32Collection ToInt32Collection(ArrayOf<int> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator Int32Collection(ArrayOf<int> values)
        {
            return ToInt32Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator Int32Collection(int[] values)
        {
            return ToInt32Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<int>(Int32Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new Int32Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt32 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfUInt32",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "UInt32")]
    public class UInt32Collection : List<uint>, ICloneable
    {
        /// <inheritdoc/>
        public UInt32Collection()
        {
        }

        /// <inheritdoc/>
        public UInt32Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public UInt32Collection(IEnumerable<uint> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static UInt32Collection ToUInt32Collection(ArrayOf<uint> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator UInt32Collection(ArrayOf<uint> values)
        {
            return ToUInt32Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator UInt32Collection(uint[] values)
        {
            return ToUInt32Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<uint>(UInt32Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UInt32Collection(this);
        }
    }

    /// <summary>
    /// A collection of Int64 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfInt64",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Int64")]
    public class Int64Collection : List<long>, ICloneable
    {
        /// <inheritdoc/>
        public Int64Collection()
        {
        }

        /// <inheritdoc/>
        public Int64Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public Int64Collection(IEnumerable<long> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static Int64Collection ToInt64Collection(ArrayOf<long> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator Int64Collection(ArrayOf<long> values)
        {
            return ToInt64Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator Int64Collection(long[] values)
        {
            return ToInt64Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<long>(Int64Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new Int64Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt64 values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfUInt64",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "UInt64")]
    public class UInt64Collection : List<ulong>, ICloneable
    {
        /// <inheritdoc/>
        public UInt64Collection()
        {
        }

        /// <inheritdoc/>
        public UInt64Collection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public UInt64Collection(IEnumerable<ulong> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static UInt64Collection ToUInt64Collection(ArrayOf<ulong> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator UInt64Collection(ArrayOf<ulong> values)
        {
            return ToUInt64Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator UInt64Collection(ulong[] values)
        {
            return ToUInt64Collection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ulong>(UInt64Collection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UInt64Collection(this);
        }
    }

    /// <summary>
    /// A collection of Float values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfFloat",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Float")]
    public class FloatCollection : List<float>, ICloneable
    {
        /// <inheritdoc/>
        public FloatCollection()
        {
        }

        /// <inheritdoc/>
        public FloatCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public FloatCollection(IEnumerable<float> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static FloatCollection ToFloatCollection(ArrayOf<float> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator FloatCollection(ArrayOf<float> values)
        {
            return ToFloatCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator FloatCollection(float[] values)
        {
            return ToFloatCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<float>(FloatCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new FloatCollection(this);
        }
    }

    /// <summary>
    /// A collection of Double values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDouble",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Double")]
    public class DoubleCollection : List<double>, ICloneable
    {
        /// <inheritdoc/>
        public DoubleCollection()
        {
        }

        /// <inheritdoc/>
        public DoubleCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DoubleCollection(IEnumerable<double> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static DoubleCollection ToDoubleCollection(ArrayOf<double> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator DoubleCollection(ArrayOf<double> values)
        {
            return ToDoubleCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator DoubleCollection(double[] values)
        {
            return ToDoubleCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<double>(DoubleCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new DoubleCollection(this);
        }
    }

    /// <summary>
    /// A collection of String values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "String")]
    public class StringCollection : List<string>, ICloneable
    {
        /// <inheritdoc/>
        public StringCollection()
        {
        }

        /// <inheritdoc/>
        public StringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StringCollection(IEnumerable<string> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static StringCollection ToStringCollection(ArrayOf<string> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator StringCollection(ArrayOf<string> values)
        {
            return ToStringCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator StringCollection(string[] values)
        {
            return ToStringCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<string>(StringCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new StringCollection(this);
        }
    }

    /// <summary>
    /// A collection of DiagnosticInfo objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDiagnosticInfo",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DiagnosticInfo")]
    public class DiagnosticInfoCollection : List<DiagnosticInfo>, ICloneable
    {
        /// <inheritdoc/>
        public DiagnosticInfoCollection()
        {
        }

        /// <inheritdoc/>
        public DiagnosticInfoCollection(IEnumerable<DiagnosticInfo> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public DiagnosticInfoCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static DiagnosticInfoCollection ToDiagnosticInfoCollection(ArrayOf<DiagnosticInfo> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator DiagnosticInfoCollection(ArrayOf<DiagnosticInfo> values)
        {
            return ToDiagnosticInfoCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator DiagnosticInfoCollection(DiagnosticInfo[] values)
        {
            return ToDiagnosticInfoCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<DiagnosticInfo>(DiagnosticInfoCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DiagnosticInfoCollection(Count);

            foreach (DiagnosticInfo element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of StatusCodes.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStatusCode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StatusCode")]
    public class StatusCodeCollection : List<StatusCode>, ICloneable
    {
        /// <inheritdoc/>
        public StatusCodeCollection()
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(IEnumerable<StatusCode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public StatusCodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static StatusCodeCollection ToStatusCodeCollection(ArrayOf<StatusCode> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator StatusCodeCollection(ArrayOf<StatusCode> values)
        {
            return ToStatusCodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator StatusCodeCollection(StatusCode[] values)
        {
            return ToStatusCodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<StatusCode>(StatusCodeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new StatusCodeCollection(this);
        }
    }

    /// <summary>
    /// A collection of Uuids.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfGuid",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Guid")]
    public class UuidCollection : List<Uuid>, ICloneable
    {
        /// <inheritdoc/>
        public UuidCollection()
        {
        }

        /// <inheritdoc/>
        public UuidCollection(IEnumerable<Uuid> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public UuidCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static UuidCollection ToUuidCollection(ArrayOf<Uuid> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static UuidCollection ToUuidCollection(ArrayOf<Guid> values)
        {
            return values.ToArrayOf(g => new Uuid(g));
        }

        /// <inheritdoc/>
        public static implicit operator UuidCollection(ArrayOf<Guid> values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<Guid>(UuidCollection values)
        {
            return values != null ? [.. values.Select(g => g.Guid)] : [];
        }

        /// <inheritdoc/>
        public static implicit operator UuidCollection(Guid[] values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator UuidCollection(ArrayOf<Uuid> values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator UuidCollection(Uuid[] values)
        {
            return ToUuidCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<Uuid>(UuidCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new UuidCollection(this);
        }
    }

    /// <summary>
    /// A collection of DateTime values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDateTime",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DateTime")]
    public class DateTimeCollection : List<DateTime>, ICloneable
    {
        /// <inheritdoc/>
        public DateTimeCollection()
        {
        }

        /// <inheritdoc/>
        public DateTimeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DateTimeCollection(IEnumerable<DateTime> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static DateTimeCollection ToDateTimeCollection(ArrayOf<DateTime> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator DateTimeCollection(ArrayOf<DateTime> values)
        {
            return ToDateTimeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator DateTimeCollection(DateTime[] values)
        {
            return ToDateTimeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<DateTime>(DateTimeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new DateTimeCollection(this);
        }
    }

    /// <summary>
    /// A collection of Variant objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfVariant",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Variant")]
    public class VariantCollection : List<Variant>, ICloneable
    {
        /// <inheritdoc/>
        public VariantCollection()
        {
        }

        /// <inheritdoc/>
        public VariantCollection(IEnumerable<Variant> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public VariantCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static VariantCollection ToVariantCollection(ArrayOf<Variant> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator VariantCollection(ArrayOf<Variant> values)
        {
            return ToVariantCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator VariantCollection(Variant[] values)
        {
            return ToVariantCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<Variant>(VariantCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new VariantCollection(this);
        }
    }

    /// <summary>
    /// A collection of XmlElement values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfXmlElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "XmlElement")]
    public class XmlElementCollection : List<XmlElement>, ICloneable
    {
        /// <inheritdoc/>
        public XmlElementCollection()
        {
        }

        /// <inheritdoc/>
        public XmlElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public XmlElementCollection(IEnumerable<XmlElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static XmlElementCollection ToXmlElementCollection(ArrayOf<XmlElement> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator XmlElementCollection(ArrayOf<XmlElement> values)
        {
            return ToXmlElementCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator XmlElementCollection(XmlElement[] values)
        {
            return ToXmlElementCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<XmlElement>(XmlElementCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new XmlElementCollection(this);
        }
    }

    /// <summary>
    /// List of expanded node ids
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExpandedNodeId")]
    public class ExpandedNodeIdCollection : List<ExpandedNodeId>, ICloneable
    {
        /// <inheritdoc/>
        public ExpandedNodeIdCollection()
        {
        }

        /// <inheritdoc/>
        public ExpandedNodeIdCollection(IEnumerable<ExpandedNodeId> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public ExpandedNodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static ExpandedNodeIdCollection ToExpandedNodeIdCollection(ArrayOf<ExpandedNodeId> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ExpandedNodeIdCollection(ArrayOf<ExpandedNodeId> values)
        {
            return ToExpandedNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ExpandedNodeIdCollection(ExpandedNodeId[] values)
        {
            return ToExpandedNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ExpandedNodeId>(ExpandedNodeIdCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ExpandedNodeIdCollection(this);
        }
    }

    /// <summary>
    /// A collection of NodeIds.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "NodeId")]
    public class NodeIdCollection : List<NodeId>, ICloneable
    {
        /// <inheritdoc/>
        public NodeIdCollection()
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(IEnumerable<NodeId> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static NodeIdCollection ToNodeIdCollection(ArrayOf<NodeId> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator NodeIdCollection(ArrayOf<NodeId> values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator NodeIdCollection(NodeId[] values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<NodeId>(NodeIdCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new NodeIdCollection(this);
        }
    }

    /// <summary>
    /// A collection of DataValues.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDataValue",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataValue")]
    public class DataValueCollection : List<DataValue>, ICloneable
    {
        /// <inheritdoc/>
        public DataValueCollection()
        {
        }

        /// <inheritdoc/>
        public DataValueCollection(IEnumerable<DataValue> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public DataValueCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static DataValueCollection ToDataValueCollection(ArrayOf<DataValue> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator DataValueCollection(ArrayOf<DataValue> values)
        {
            return ToDataValueCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator DataValueCollection(DataValue[] values)
        {
            return ToDataValueCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<DataValue>(DataValueCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DataValueCollection(Count);

            foreach (DataValue element in this)
            {
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of QualifiedName objects.
    /// </summary>
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
        public static QualifiedNameCollection ToQualifiedNameCollection(ArrayOf<QualifiedName> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator QualifiedNameCollection(ArrayOf<QualifiedName> values)
        {
            return ToQualifiedNameCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator QualifiedNameCollection(QualifiedName[] values)
        {
            return ToQualifiedNameCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<QualifiedName>(QualifiedNameCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new QualifiedNameCollection(this);
        }
    }

    /// <summary>
    /// A strongly-typed collection of LocalizedText objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfLocalizedText",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "LocalizedText")]
    public class LocalizedTextCollection : List<LocalizedText>, ICloneable
    {
        /// <inheritdoc/>
        public LocalizedTextCollection()
        {
        }

        /// <inheritdoc/>
        public LocalizedTextCollection(IEnumerable<LocalizedText> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public LocalizedTextCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static LocalizedTextCollection ToLocalizedTextCollection(ArrayOf<LocalizedText> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator LocalizedTextCollection(ArrayOf<LocalizedText> values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator LocalizedTextCollection(LocalizedText[] values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<LocalizedText>(LocalizedTextCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new LocalizedTextCollection(this);
        }
    }

    /// <summary>
    /// A collection of ByteString values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfByteString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ByteString")]
    public class ByteStringCollection : List<ByteString>, ICloneable
    {
        /// <inheritdoc/>
        public ByteStringCollection()
        {
        }

        /// <inheritdoc/>
        public ByteStringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ByteStringCollection(IEnumerable<ByteString> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ByteStringCollection ToByteStringCollection(ArrayOf<ByteString> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ByteStringCollection(ArrayOf<ByteString> values)
        {
            return ToByteStringCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ByteStringCollection(ByteString[] values)
        {
            return ToByteStringCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ByteString>(ByteStringCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ByteStringCollection(this);
        }
    }

    /// <summary>
    /// A strongly-typed collection of ExtensionObjects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExtensionObject",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExtensionObject")]
    public class ExtensionObjectCollection : List<ExtensionObject>, ICloneable
    {
        /// <inheritdoc/>
        public ExtensionObjectCollection()
        {
        }

        /// <inheritdoc/>
        public ExtensionObjectCollection(
            IEnumerable<ExtensionObject> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public ExtensionObjectCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static ExtensionObjectCollection ToExtensionObjectCollection(ArrayOf<ExtensionObject> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ExtensionObjectCollection(ArrayOf<ExtensionObject> values)
        {
            return ToExtensionObjectCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ExtensionObjectCollection(ExtensionObject[] values)
        {
            return ToExtensionObjectCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ExtensionObject>(ExtensionObjectCollection values)
        {
            return values.ToArrayOf();
        }

        /// <summary>
        /// TODO Remove
        /// </summary>
        /// <param name="encodeables"></param>
        /// <returns></returns>
        public static ExtensionObjectCollection ToExtensionObjects(
            IEnumerable<IEncodeable> encodeables)
        {
            // return null if the input list is null.
            if (encodeables == null)
            {
                return null;
            }

            // convert each encodeable to an extension object.
            var extensibles = new ExtensionObjectCollection();
            foreach (IEncodeable encodeable in encodeables)
            {
                extensibles.Add(new ExtensionObject(encodeable));
            }

            return extensibles;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new ExtensionObjectCollection(this);
        }
    }

    /// <summary>
    /// Browse description collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfBrowseDescription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "BrowseDescription")]
    public class BrowseDescriptionCollection : List<BrowseDescription>, ICloneable
    {
        /// <inheritdoc/>
        public BrowseDescriptionCollection()
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(IEnumerable<BrowseDescription> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static BrowseDescriptionCollection ToBrowseDescriptionCollection(ArrayOf<BrowseDescription> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator BrowseDescriptionCollection(ArrayOf<BrowseDescription> values)
        {
            return ToBrowseDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator BrowseDescriptionCollection(BrowseDescription[] values)
        {
            return ToBrowseDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<BrowseDescription>(BrowseDescriptionCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (BrowseDescriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new BrowseDescriptionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Argument collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfArgument",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Argument")]
    public class ArgumentCollection : List<Argument>, ICloneable
    {
        /// <inheritdoc/>
        public ArgumentCollection()
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(IEnumerable<Argument> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ArgumentCollection ToArgumentCollection(ArrayOf<Argument> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ArgumentCollection(ArrayOf<Argument> values)
        {
            return ToArgumentCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArgumentCollection(Argument[] values)
        {
            return ToArgumentCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<Argument>(ArgumentCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ArgumentCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ArgumentCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Structure definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureDefinition")]
    public class StructureDefinitionCollection : List<StructureDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public StructureDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(IEnumerable<StructureDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static StructureDefinitionCollection ToStructureDefinitionCollection(ArrayOf<StructureDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator StructureDefinitionCollection(ArrayOf<StructureDefinition> values)
        {
            return ToStructureDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator StructureDefinitionCollection(StructureDefinition[] values)
        {
            return ToStructureDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<StructureDefinition>(StructureDefinitionCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (StructureDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of EnumField objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumField")]
    public class EnumFieldCollection : List<EnumField>, ICloneable
    {
        /// <inheritdoc/>
        public EnumFieldCollection()
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(IEnumerable<EnumField> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumFieldCollection ToEnumFieldCollection(ArrayOf<EnumField> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator EnumFieldCollection(ArrayOf<EnumField> values)
        {
            return ToEnumFieldCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator EnumFieldCollection(EnumField[] values)
        {
            return ToEnumFieldCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<EnumField>(EnumFieldCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Structure field collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureField")]
    public class StructureFieldCollection : List<StructureField>, ICloneable
    {
        /// <inheritdoc/>
        public StructureFieldCollection()
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(IEnumerable<StructureField> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static StructureFieldCollection ToStructureFieldCollection(ArrayOf<StructureField> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator StructureFieldCollection(ArrayOf<StructureField> values)
        {
            return ToStructureFieldCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator StructureFieldCollection(StructureField[] values)
        {
            return ToStructureFieldCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<StructureField>(StructureFieldCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (StructureFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of enum value types
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumValueType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumValueType")]
    public class EnumValueTypeCollection : List<EnumValueType>, ICloneable
    {
        /// <inheritdoc/>
        public EnumValueTypeCollection()
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(IEnumerable<EnumValueType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumValueTypeCollection ToEnumValueTypeCollection(ArrayOf<EnumValueType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator EnumValueTypeCollection(ArrayOf<EnumValueType> values)
        {
            return ToEnumValueTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator EnumValueTypeCollection(EnumValueType[] values)
        {
            return ToEnumValueTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<EnumValueType>(EnumValueTypeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumValueTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumValueTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Id type collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfIdType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "IdType")]
    public class IdTypeCollection : List<IdType>, ICloneable
    {
        /// <inheritdoc/>
        public IdTypeCollection()
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(IEnumerable<IdType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static IdTypeCollection ToIdTypeCollection(ArrayOf<IdType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator IdTypeCollection(ArrayOf<IdType> values)
        {
            return ToIdTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator IdTypeCollection(IdType[] values)
        {
            return ToIdTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<IdType>(IdTypeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (IdTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            return new IdTypeCollection(this);
        }
    }

    /// <summary>
    /// Data type definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDataTypeDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataTypeDefinition")]
    public class DataTypeDefinitionCollection : List<DataTypeDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public DataTypeDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(IEnumerable<DataTypeDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static DataTypeDefinitionCollection ToDataTypeDefinitionCollection(ArrayOf<DataTypeDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator DataTypeDefinitionCollection(ArrayOf<DataTypeDefinition> values)
        {
            return ToDataTypeDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator DataTypeDefinitionCollection(DataTypeDefinition[] values)
        {
            return ToDataTypeDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<DataTypeDefinition>(DataTypeDefinitionCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (DataTypeDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DataTypeDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Role permission collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRolePermissionType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RolePermissionType")]
    public class RolePermissionTypeCollection : List<RolePermissionType>, ICloneable
    {
        /// <inheritdoc/>
        public RolePermissionTypeCollection()
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(IEnumerable<RolePermissionType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static RolePermissionTypeCollection ToRolePermissionTypeCollection(ArrayOf<RolePermissionType> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator RolePermissionTypeCollection(ArrayOf<RolePermissionType> values)
        {
            return ToRolePermissionTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator RolePermissionTypeCollection(RolePermissionType[] values)
        {
            return ToRolePermissionTypeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<RolePermissionType>(RolePermissionTypeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (RolePermissionTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new RolePermissionTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Reference description collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReferenceDescription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ReferenceDescription")]
    public class ReferenceDescriptionCollection : List<ReferenceDescription>, ICloneable
    {
        /// <inheritdoc/>
        public ReferenceDescriptionCollection()
        {
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceDescriptionCollection(IEnumerable<ReferenceDescription> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ReferenceDescriptionCollection ToReferenceDescriptionCollection(ArrayOf<ReferenceDescription> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ReferenceDescriptionCollection(ArrayOf<ReferenceDescription> values)
        {
            return ToReferenceDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ReferenceDescriptionCollection(ReferenceDescription[] values)
        {
            return ToReferenceDescriptionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ReferenceDescription>(ReferenceDescriptionCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ReferenceDescriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ReferenceDescriptionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// List of RelativePathElement objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRelativePathElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RelativePathElement")]
    public class RelativePathElementCollection : List<RelativePathElement>, ICloneable
    {
        /// <inheritdoc/>
        public RelativePathElementCollection()
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(IEnumerable<RelativePathElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static RelativePathElementCollection ToRelativePathElementCollection(ArrayOf<RelativePathElement> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator RelativePathElementCollection(ArrayOf<RelativePathElement> values)
        {
            return ToRelativePathElementCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator RelativePathElementCollection(RelativePathElement[] values)
        {
            return ToRelativePathElementCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<RelativePathElement>(RelativePathElementCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (RelativePathElementCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new RelativePathElementCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Enum definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumDefinition")]
    public class EnumDefinitionCollection : List<EnumDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public EnumDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(IEnumerable<EnumDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static EnumDefinitionCollection ToEnumDefinitionCollection(ArrayOf<EnumDefinition> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator EnumDefinitionCollection(ArrayOf<EnumDefinition> values)
        {
            return ToEnumDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator EnumDefinitionCollection(EnumDefinition[] values)
        {
            return ToEnumDefinitionCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<EnumDefinition>(EnumDefinitionCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Node collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfNode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Node")]
    public class NodeCollection : List<Node>, ICloneable
    {
        /// <inheritdoc/>
        public NodeCollection()
        {
        }

        /// <inheritdoc/>
        public NodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public NodeCollection(IEnumerable<Node> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static NodeCollection ToNodeCollection(ArrayOf<Node> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator NodeCollection(ArrayOf<Node> values)
        {
            return ToNodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator NodeCollection(Node[] values)
        {
            return ToNodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<Node>(NodeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (NodeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new NodeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// Reference node collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReferenceNode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ReferenceNode")]
    public class ReferenceNodeCollection : List<ReferenceNode>, ICloneable
    {
        /// <inheritdoc/>
        public ReferenceNodeCollection()
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(IEnumerable<ReferenceNode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static ReferenceNodeCollection ToReferenceNodeCollection(ArrayOf<ReferenceNode> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator ReferenceNodeCollection(ArrayOf<ReferenceNode> values)
        {
            return ToReferenceNodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ReferenceNodeCollection(ReferenceNode[] values)
        {
            return ToReferenceNodeCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<ReferenceNode>(ReferenceNodeCollection values)
        {
            return values.ToArrayOf();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (ReferenceNodeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ReferenceNodeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of encodeable objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEncodeable",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Encodeable")]
    public class IEncodeableCollection : List<IEncodeable>
    {
        /// <inheritdoc/>
        public IEncodeableCollection()
        {
        }

        /// <inheritdoc/>
        public IEncodeableCollection(IEnumerable<IEncodeable> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public IEncodeableCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public static IEncodeableCollection ToIEncodeableCollection(ArrayOf<IEncodeable> values)
        {
            return [.. values];
        }

        /// <inheritdoc/>
        public static implicit operator IEncodeableCollection(IEncodeable[] values)
        {
            return ToIEncodeableCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator IEncodeableCollection(ArrayOf<IEncodeable> values)
        {
            return ToIEncodeableCollection(values);
        }

        /// <inheritdoc/>
        public static implicit operator ArrayOf<IEncodeable>(IEncodeableCollection values)
        {
            return values.ToArrayOf();
        }
    }
}
