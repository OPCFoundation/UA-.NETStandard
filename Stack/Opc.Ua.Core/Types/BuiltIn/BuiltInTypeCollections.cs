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
    [CollectionDataContract(Name = "ListOfBoolean", Namespace = Namespaces.OpcUaXsd, ItemName = "Boolean")]
    public partial class BooleanCollection : List<bool>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public BooleanCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Creates a new collection and populates the collection with the
        /// values passed in.
        /// </remarks>
        /// <param name="collection">A collection of boolean values to add to this new collection</param>
        public BooleanCollection(IEnumerable<bool> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        public BooleanCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of boolean values to convert to a strong-collection of this class type</param>
        public static BooleanCollection ToBooleanCollection(bool[] values)
        {
            if (values != null)
            {
                return new BooleanCollection(values);
            }

            return new BooleanCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of boolean values to convert to a strong-collection of this class type</param>
        public static implicit operator BooleanCollection(bool[] values)
        {
            return ToBooleanCollection(values);
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
    /// Provides a strongly-typed list of <see cref="SByte"/> values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfSByte", Namespace = Namespaces.OpcUaXsd, ItemName = "SByte")]
    public partial class SByteCollection : List<sbyte>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public SByteCollection() { }

        /// <summary>
        /// Initializes the collection and sets the maximum capacity
        /// </summary>
        /// <remarks>
        /// Initializes the collection and sets the maximum capacity
        /// </remarks>
        /// <param name="capacity">The maximum size of this collection</param>
        public SByteCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection and populates it with the specified collection of SByte values
        /// </summary>
        /// <remarks>
        /// Initializes the collection and populates it with the specified collection of SByte values
        /// </remarks>
        /// <param name="collection">A collection of <see cref="SByte"/> values to populate this collection with</param>
        public SByteCollection(IEnumerable<sbyte> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="SByte"/> values to convert from</param>
        public static SByteCollection ToSByteCollection(sbyte[] values)
        {
            if (values != null)
            {
                return new SByteCollection(values);
            }

            return new SByteCollection();
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

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new SByteCollection(this);
        }
    }

    /// <summary>
    /// A collection of Byte values.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed list of <see cref="Byte"/> values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfByte", Namespace = Namespaces.OpcUaXsd, ItemName = "Byte")]
    public partial class ByteCollection : List<byte>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public ByteCollection() { }

        /// <summary>
        /// Initializes the collection and sets the maximum capacity
        /// </summary>
        /// <remarks>
        /// Initializes the collection and sets the maximum capacity
        /// </remarks>
        /// <param name="capacity">The maximum size of this collection</param>
        public ByteCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection and populates it with the specified collection of Byte values
        /// </summary>
        /// <remarks>
        /// Initializes the collection and populates it with the specified collection of Byte values
        /// </remarks>
        /// <param name="collection">A collection of <see cref="Byte"/> values to populate this collection with</param>
        public ByteCollection(IEnumerable<byte> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="Byte"/> values to convert from</param>
        public static ByteCollection ToByteCollection(byte[] values)
        {
            if (values != null)
            {
                return new ByteCollection(values);
            }

            return new ByteCollection();
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

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfInt16", Namespace = Namespaces.OpcUaXsd, ItemName = "Int16")]
    public partial class Int16Collection : List<short>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public Int16Collection() { }

        /// <summary>
        /// Initializes the collection and specifies the capacity
        /// </summary>
        /// <remarks>
        /// Initializes the collection and specifies the capacity
        /// </remarks>
        /// <param name="capacity">The max size of the collection</param>
        public Int16Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection and populates it with the values passed-in
        /// </summary>
        /// <remarks>
        /// Initializes the collection and populates it with the values passed-in
        /// </remarks>
        /// <param name="collection">A collection of <see cref="short"/> values to populate the collection with</param>
        public Int16Collection(IEnumerable<short> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="short"/> values to convert to a collection</param>
        public static Int16Collection ToInt16Collection(short[] values)
        {
            if (values != null)
            {
                return new Int16Collection(values);
            }

            return new Int16Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="short"/> values to convert to a collection</param>
        public static implicit operator Int16Collection(short[] values)
        {
            return ToInt16Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new Int16Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt16 values.
    /// </summary>
    /// <remarks>
    /// A collection of UInt16 values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfUInt16", Namespace = Namespaces.OpcUaXsd, ItemName = "UInt16")]
    public partial class UInt16Collection : List<ushort>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public UInt16Collection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity size of this collection</param>
        public UInt16Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A Collection of <see cref="ushort"/> to pre-populate the collection with</param>
        public UInt16Collection(IEnumerable<ushort> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="ushort"/> values to conver to a collection</param>
        public static UInt16Collection ToUInt16Collection(ushort[] values)
        {
            if (values != null)
            {
                return new UInt16Collection(values);
            }

            return new UInt16Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="ushort"/> values to conver to a collection</param>
        public static implicit operator UInt16Collection(ushort[] values)
        {
            return ToUInt16Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new UInt16Collection(this);
        }
    }

    /// <summary>
    /// A collection of Int32 values.
    /// </summary>
    [CollectionDataContract(Name = "ListOfInt32", Namespace = Namespaces.OpcUaXsd, ItemName = "Int32")]
    public partial class Int32Collection : List<int>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="Int32"/>.
        /// </remarks>
        public Int32Collection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of this collection</param>
        public Int32Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection
        /// </remarks>
        /// <param name="collection">A collection of <see cref="int"/> to pre-populate this collection with</param>
        public Int32Collection(IEnumerable<int> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="int"/> to convert to a collection</param>
        public static Int32Collection ToInt32Collection(int[] values)
        {
            if (values != null)
            {
                return new Int32Collection(values);
            }

            return new Int32Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="int"/> to convert to a collection</param>
        public static implicit operator Int32Collection(int[] values)
        {
            return ToInt32Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new Int32Collection(this);
        }
    }

    /// <summary>
    /// A collection of UInt32 values.
    /// </summary>
    /// <remarks>
    /// A collection of UInt32 values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfUInt32", Namespace = Namespaces.OpcUaXsd, ItemName = "UInt32")]
    public partial class UInt32Collection : List<uint>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="UInt32"/>.
        /// </remarks>
        public UInt32Collection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of the collection</param>
        public UInt32Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of <see cref="uint"/> to pre-populate the collection with</param>
        public UInt32Collection(IEnumerable<uint> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">
        /// An array of <see cref="UInt32"/> values to return as a strongly-typed collection
        /// </param>
        public static UInt32Collection ToUInt32Collection(uint[] values)
        {
            if (values != null)
            {
                return new UInt32Collection(values);
            }

            return new UInt32Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">
        /// An array of <see cref="UInt32"/> values to return as a strongly-typed collection
        /// </param>
        public static implicit operator UInt32Collection(uint[] values)
        {
            return ToUInt32Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfInt64", Namespace = Namespaces.OpcUaXsd, ItemName = "Int64")]
    public class Int64Collection : List<long>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection of <see cref="Int64"/>.
        /// </remarks>
        public Int64Collection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of the collection</param>
        public Int64Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A colleciton of <see cref="long"/> to pre-populate the collection with</param>
        public Int64Collection(IEnumerable<long> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="long"/> to convert to a collection</param>
        public static Int64Collection ToInt64Collection(long[] values)
        {
            if (values != null)
            {
                return new Int64Collection(values);
            }

            return new Int64Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="long"/> to convert to a collection</param>
        public static implicit operator Int64Collection(long[] values)
        {
            return ToInt64Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfUInt64", Namespace = Namespaces.OpcUaXsd, ItemName = "UInt64")]
    public partial class UInt64Collection : List<ulong>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public UInt64Collection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of collection</param>
        public UInt64Collection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of <see cref="ulong"/> to pre-populate the collection with</param>
        public UInt64Collection(IEnumerable<ulong> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="ulong"/> to return as a collection</param>
        public static UInt64Collection ToUInt64Collection(ulong[] values)
        {
            if (values != null)
            {
                return new UInt64Collection(values);
            }

            return new UInt64Collection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="ulong"/> to return as a collection</param>
        public static implicit operator UInt64Collection(ulong[] values)
        {
            return ToUInt64Collection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfFloat", Namespace = Namespaces.OpcUaXsd, ItemName = "Float")]
    public partial class FloatCollection : List<float>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public FloatCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity of this collection</param>
        public FloatCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection of values to add into this collection</param>
        public FloatCollection(IEnumerable<float> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of floats to return as a collection</param>
        public static FloatCollection ToFloatCollection(float[] values)
        {
            if (values != null)
            {
                return new FloatCollection(values);
            }

            return new FloatCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of floats to return as a collection</param>
        public static implicit operator FloatCollection(float[] values)
        {
            return ToFloatCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new FloatCollection(this);
        }
    }

    /// <summary>
    /// A collection of Double values.
    /// </summary>
    /// <remarks>
    /// A collection of Double values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfDouble", Namespace = Namespaces.OpcUaXsd, ItemName = "Double")]
    public partial class DoubleCollection : List<double>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public DoubleCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">max capacity of collection</param>
        public DoubleCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of doubles to add into this collection</param>
        public DoubleCollection(IEnumerable<double> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">an array of doubles to return as a collection</param>
        public static DoubleCollection ToDoubleCollection(double[] values)
        {
            if (values != null)
            {
                return new DoubleCollection(values);
            }

            return new DoubleCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">an array of doubles to return as a collection</param>
        public static implicit operator DoubleCollection(double[] values)
        {
            return ToDoubleCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfString", Namespace = Namespaces.OpcUaXsd, ItemName = "String")]
    public partial class StringCollection : List<string>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public StringCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of collection</param>
        public StringCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of strings to add to this collection</param>
        public StringCollection(IEnumerable<string> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of strings to add to this collection</param>
        public static StringCollection ToStringCollection(string[] values)
        {
            if (values != null)
            {
                return new StringCollection(values);
            }

            return new StringCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of strings to add to this collection</param>
        public static implicit operator StringCollection(string[] values)
        {
            return ToStringCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
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
    [CollectionDataContract(Name = "ListOfDateTime", Namespace = Namespaces.OpcUaXsd, ItemName = "DateTime")]
    public partial class DateTimeCollection : List<DateTime>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public DateTimeCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of collection</param>
        public DateTimeCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        ///</remarks>
        ///<param name="collection">A collection of DateTime to add to this collection</param>
        public DateTimeCollection(IEnumerable<DateTime> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of DateTime to return as a collection</param>
        public static DateTimeCollection ToDateTimeCollection(DateTime[] values)
        {
            if (values != null)
            {
                return new DateTimeCollection(values);
            }

            return new DateTimeCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of DateTime to return as a collection</param>
        public static implicit operator DateTimeCollection(DateTime[] values)
        {
            return ToDateTimeCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new DateTimeCollection(this);
        }
    }

    /// <summary>
    /// A collection of ByteString values.
    /// </summary>
    /// <remarks>
    /// A collection of ByteString values.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfByteString", Namespace = Namespaces.OpcUaXsd, ItemName = "ByteString")]
    public partial class ByteStringCollection : List<byte[]>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public ByteStringCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max size of collection</param>
        public ByteStringCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of byte to add to this collection</param>
        public ByteStringCollection(IEnumerable<byte[]> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of bytes to return as a collection</param>
        public static ByteStringCollection ToByteStringCollection(byte[][] values)
        {
            if (values != null)
            {
                return new ByteStringCollection(values);
            }

            return new ByteStringCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of bytes to return as a collection</param>
        public static implicit operator ByteStringCollection(byte[][] values)
        {
            return ToByteStringCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            ByteStringCollection clone = new ByteStringCollection(this.Count);

            foreach (byte[] element in this)
            {
                clone.Add((byte[])Utils.Clone(element));
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
    [CollectionDataContract(Name = "ListOfXmlElement", Namespace = Namespaces.OpcUaXsd, ItemName = "XmlElement")]
    public partial class XmlElementCollection : List<XmlElement>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public XmlElementCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max size of collection</param>
        public XmlElementCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of XmlElement's to add to this collection</param>
        public XmlElementCollection(IEnumerable<XmlElement> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of XmlElement's to return as a collection</param>
        public static XmlElementCollection ToXmlElementCollection(XmlElement[] values)
        {
            if (values != null)
            {
                return new XmlElementCollection(values);
            }

            return new XmlElementCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of XmlElement's to return as a collection</param>
        public static implicit operator XmlElementCollection(XmlElement[] values)
        {
            return ToXmlElementCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            XmlElementCollection clone = new XmlElementCollection(this.Count);

            foreach (XmlElement element in this)
            {
                clone.Add((XmlElement)Utils.Clone(element));
            }

            return clone;
        }
    }//class
}//namespace
