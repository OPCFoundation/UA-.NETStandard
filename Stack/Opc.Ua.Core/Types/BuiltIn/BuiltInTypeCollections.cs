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
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A collection of Boolean values.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed collection of Boolean values.
    /// </remarks>
    /// <example>
    /// <code lang="C#">
    /// BooleanCollection bools = new BooleanCollection();
    /// bools.AddRange( new bool[]{true, false, true, false} );
    /// </code>
    /// <code lang="Visual Basic">
    /// Dim bools As New BooleanCollection()
    /// bools.AddRange( New Boolean(){ True, False, True, False } )
    /// </code>
    /// </example>
    [CollectionDataContract(
        Name = "ListOfBoolean",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Boolean")]
    public class BooleanCollection : List<bool>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public BooleanCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Creates a new collection and populates the collection with the
        /// values passed in.
        /// </remarks>
        /// <param name="collection">A collection of boolean values to add to this new collection</param>
        public BooleanCollection(IEnumerable<bool> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        public BooleanCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of boolean values to convert to a strong-collection of this class type</param>
        public static BooleanCollection ToBooleanCollection(bool[] values)
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
        /// <param name="values">An array of boolean values to convert to a strong-collection of this class type</param>
        public static implicit operator BooleanCollection(bool[] values)
        {
            return ToBooleanCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Returns a new instance of this object type, while copying its contents.
        /// </remarks>
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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SByteCollection()
        {
        }

        /// <summary>
        /// Initializes the collection and sets the maximum capacity
        /// </summary>
        /// <param name="capacity">The maximum size of this collection</param>
        public SByteCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection and populates it with the specified collection of SByte values
        /// </summary>
        /// <param name="collection">A collection of <see cref="sbyte"/> values to populate this collection with</param>
        public SByteCollection(IEnumerable<sbyte> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="sbyte"/> values to convert from</param>
        public static SByteCollection ToSByteCollection(sbyte[] values)
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
        /// <remarks>
        /// Converts an array of SByte to a collection.
        /// </remarks>
        /// <param name="values">An array of SByte values to convert</param>
        public static implicit operator SByteCollection(sbyte[] values)
        {
            return ToSByteCollection(values);
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
            return new SByteCollection(this);
        }
    }

    /// <summary>
    /// A collection of Byte values.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed list of <see cref="byte"/> values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfByte",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Byte")]
    public class ByteCollection : List<byte>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ByteCollection()
        {
        }

        /// <summary>
        /// Initializes the collection and sets the maximum capacity
        /// </summary>
        /// <param name="capacity">The maximum size of this collection</param>
        public ByteCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection and populates it with the specified collection of Byte values
        /// </summary>
        /// <param name="collection">A collection of <see cref="byte"/> values to populate this collection with</param>
        public ByteCollection(IEnumerable<byte> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="byte"/> values to convert from</param>
        public static ByteCollection ToByteCollection(byte[] values)
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
        /// <remarks>
        /// Converts an array of Byte to a collection.
        /// </remarks>
        /// <param name="values">An array of Byte values to convert</param>
        public static implicit operator ByteCollection(byte[] values)
        {
            return ToByteCollection(values);
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
            return new ByteCollection(this);
        }
    }

    /// <summary>
    /// A collection of Int16 values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of Int16 values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfInt16",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Int16")]
    public class Int16Collection : List<short>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public Int16Collection()
        {
        }

        /// <summary>
        /// Initializes the collection and specifies the capacity
        /// </summary>
        /// <param name="capacity">The max size of the collection</param>
        public Int16Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection and populates it with the values passed-in
        /// </summary>
        /// <param name="collection">A collection of <see cref="short"/> values to populate the collection with</param>
        public Int16Collection(IEnumerable<short> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="short"/> values to convert to a collection</param>
        public static Int16Collection ToInt16Collection(short[] values)
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
        /// <param name="values">An array of <see cref="short"/> values to convert to a collection</param>
        public static implicit operator Int16Collection(short[] values)
        {
            return ToInt16Collection(values);
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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public UInt16Collection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max capacity size of this collection</param>
        public UInt16Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A Collection of <see cref="ushort"/> to pre-populate the collection with</param>
        public UInt16Collection(IEnumerable<ushort> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="ushort"/> values to convert to a collection</param>
        public static UInt16Collection ToUInt16Collection(ushort[] values)
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
        /// <param name="values">An array of <see cref="ushort"/> values to convert to a collection</param>
        public static implicit operator UInt16Collection(ushort[] values)
        {
            return ToUInt16Collection(values);
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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="int"/>.
        /// </remarks>
        public Int32Collection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of this collection</param>
        public Int32Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection
        /// </summary>
        /// <param name="collection">A collection of <see cref="int"/> to pre-populate this collection with</param>
        public Int32Collection(IEnumerable<int> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="int"/> to convert to a collection</param>
        public static Int32Collection ToInt32Collection(int[] values)
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
        /// <param name="values">An array of <see cref="int"/> to convert to a collection</param>
        public static implicit operator Int32Collection(int[] values)
        {
            return ToInt32Collection(values);
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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="uint"/>.
        /// </remarks>
        public UInt32Collection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of the collection</param>
        public UInt32Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of <see cref="uint"/> to pre-populate the collection with</param>
        public UInt32Collection(IEnumerable<uint> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">
        /// An array of <see cref="uint"/> values to return as a strongly-typed collection
        /// </param>
        public static UInt32Collection ToUInt32Collection(uint[] values)
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
        /// <param name="values">
        /// An array of <see cref="uint"/> values to return as a strongly-typed collection
        /// </param>
        public static implicit operator UInt32Collection(uint[] values)
        {
            return ToUInt32Collection(values);
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
            return new UInt32Collection(this);
        }
    }

    /// <summary>
    /// A collection of Int64 values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of Int64 values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfInt64",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Int64")]
    public class Int64Collection : List<long>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="long"/>.
        /// </remarks>
        public Int64Collection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of the collection</param>
        public Int64Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A colleciton of <see cref="long"/> to pre-populate the collection with</param>
        public Int64Collection(IEnumerable<long> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="long"/> to convert to a collection</param>
        public static Int64Collection ToInt64Collection(long[] values)
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
        /// <param name="values">An array of <see cref="long"/> to convert to a collection</param>
        public static implicit operator Int64Collection(long[] values)
        {
            return ToInt64Collection(values);
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
            return new Int64Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt64 values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of UInt64 values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfUInt64",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "UInt64")]
    public class UInt64Collection : List<ulong>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public UInt64Collection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of collection</param>
        public UInt64Collection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of <see cref="ulong"/> to pre-populate the collection with</param>
        public UInt64Collection(IEnumerable<ulong> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="ulong"/> to return as a collection</param>
        public static UInt64Collection ToUInt64Collection(ulong[] values)
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
        /// <param name="values">An array of <see cref="ulong"/> to return as a collection</param>
        public static implicit operator UInt64Collection(ulong[] values)
        {
            return ToUInt64Collection(values);
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
            return new UInt64Collection(this);
        }
    }

    /// <summary>
    /// A collection of Float values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of Float values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfFloat",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Float")]
    public class FloatCollection : List<float>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public FloatCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The max capacity of this collection</param>
        public FloatCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The collection of values to add into this collection</param>
        public FloatCollection(IEnumerable<float> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of floats to return as a collection</param>
        public static FloatCollection ToFloatCollection(float[] values)
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
        /// <param name="values">An array of floats to return as a collection</param>
        public static implicit operator FloatCollection(float[] values)
        {
            return ToFloatCollection(values);
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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public DoubleCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">max capacity of collection</param>
        public DoubleCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of doubles to add into this collection</param>
        public DoubleCollection(IEnumerable<double> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">an array of doubles to return as a collection</param>
        public static DoubleCollection ToDoubleCollection(double[] values)
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
        /// <param name="values">an array of doubles to return as a collection</param>
        public static implicit operator DoubleCollection(double[] values)
        {
            return ToDoubleCollection(values);
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
            return new DoubleCollection(this);
        }
    }

    /// <summary>
    /// A collection of String values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of String values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "String")]
    public class StringCollection : List<string>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public StringCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of collection</param>
        public StringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of strings to add to this collection</param>
        public StringCollection(IEnumerable<string> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">A collection of strings to add to this collection</param>
        public static StringCollection ToStringCollection(string[] values)
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
        /// <param name="values">A collection of strings to add to this collection</param>
        public static implicit operator StringCollection(string[] values)
        {
            return ToStringCollection(values);
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
            return new StringCollection(this);
        }
    }

    /// <summary>
    /// A collection of DateTime values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of DateTime values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfDateTime",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DateTime")]
    public class DateTimeCollection : List<DateTime>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public DateTimeCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max capacity of collection</param>
        public DateTimeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        ///<param name="collection">A collection of DateTime to add to this collection</param>
        public DateTimeCollection(IEnumerable<DateTime> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of DateTime to return as a collection</param>
        public static DateTimeCollection ToDateTimeCollection(DateTime[] values)
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
        /// <param name="values">Array of DateTime to return as a collection</param>
        public static implicit operator DateTimeCollection(DateTime[] values)
        {
            return ToDateTimeCollection(values);
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
            return new DateTimeCollection(this);
        }
    }

    /// <summary>
    /// A collection of ByteString values.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfByteString",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ByteString")]
    public class ByteStringCollection : List<byte[]>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ByteStringCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max size of collection</param>
        public ByteStringCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of byte to add to this collection</param>
        public ByteStringCollection(IEnumerable<byte[]> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">Array of bytes to return as a collection</param>
        public static ByteStringCollection ToByteStringCollection(byte[][] values)
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
        /// <param name="values">Array of bytes to return as a collection</param>
        public static implicit operator ByteStringCollection(byte[][] values)
        {
            return ToByteStringCollection(values);
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
            var clone = new ByteStringCollection(Count);

            foreach (byte[] element in this)
            {
                clone.Add(Utils.Clone(element));
            }

            return clone;
        }
    }

    /// <summary>
    /// A collection of XmlElement values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of XmlElement values.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfXmlElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "XmlElement")]
    public class XmlElementCollection : List<XmlElement>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public XmlElementCollection()
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">Max size of collection</param>
        public XmlElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of XmlElement's to add to this collection</param>
        public XmlElementCollection(IEnumerable<XmlElement> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of XmlElement's to return as a collection</param>
        public static XmlElementCollection ToXmlElementCollection(XmlElement[] values)
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
        /// <param name="values">An array of XmlElement's to return as a collection</param>
        public static implicit operator XmlElementCollection(XmlElement[] values)
        {
            return ToXmlElementCollection(values);
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
            var clone = new XmlElementCollection(Count);

            foreach (XmlElement element in this)
            {
                clone.Add(Utils.Clone(element));
            }

            return clone;
        }
    }
}
