/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Opc.Ua.Test
{
    /// <summary>
    /// An interface to a source of random numbers.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>
        /// Fills a range in array of bytes with random numbers.
        /// </summary>
        /// <param name="bytes">The array to update.</param>
        /// <param name="offset">The start of the range generate.</param>
        /// <param name="count">The number of bytes to generate.</param>
        /// <exception cref="ArgumentNullException">Thrown if the bytes parameter is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the offset or count parameters do not specify a valid range within the bytes parameter.</exception>
        void NextBytes(byte[] bytes, int offset, int count);

        /// <summary>
        /// Returns a random non-negative integer which does not exeed the specified maximum.
        /// </summary>
        /// <param name="max">The maximum value to return.</param>
        /// <returns>A random value greater than 0 but less than or equal to max.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the max parameter is less than zero.</exception>
        int NextInt32(int max);
    }

    /// <summary>
    /// Uses the Pseudo random generator as a source.
    /// </summary>
    public class RandomSource : IRandomSource
    {
        /// <summary>
        /// Initializes the source with a time dependent seed.
        /// </summary>
        public RandomSource()
        {
            m_random = new Random();
        }

        /// <summary>
        /// Initializes the source with a seed.
        /// </summary>
        /// <param name="seed">The number used to initialize the Pseudo random sequence.</param>
        public RandomSource(int seed)
        {
            m_random = new Random(seed);
        }

        /// <summary cref="IRandomSource.NextBytes" />
        public void NextBytes(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            if (offset < 0 || (offset != 0 && offset >= bytes.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (bytes.Length == 0)
            {
                return;
            }

            if (offset == 0 && count == bytes.Length)
            {
                m_random.NextBytes(bytes);
            }
            else
            {
                byte[] buffer = new byte[count];
                m_random.NextBytes(buffer);
                Array.Copy(buffer, 0, bytes, offset, count);
            }
        }

        /// <summary cref="IRandomSource.NextInt32" />
        public int NextInt32(int max)
        {
            if (max < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            if (max < Int32.MaxValue)
            {
                max++;
            }

            return m_random.Next(max);
        }

        private Random m_random;
    }

    /// <summary>
    /// A class that generates data.
    /// </summary>
    public class DataGenerator
    {
        #region Constructors
        /// <summary>
        /// Initializes the data generator.
        /// </summary>
        public DataGenerator(IRandomSource random)
        {
            m_maxArrayLength = 100;
            m_maxStringLength = 100;
            m_maxXmlAttributeCount = 10;
            m_maxXmlElementCount = 10;
            m_minDateTimeValue = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            m_maxDateTimeValue = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            m_random = random;
            m_boundaryValueFrequency = 20;
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();

            // create a random source if none provided.
            if (m_random == null)
            {
                m_random = new RandomSource();
            }

            // load the boundary values.
            m_boundaryValues = new SortedDictionary<string, object[]>();

            for (int ii = 0; ii < s_AvailableBoundaryValues.Length; ii++)
            {
                m_boundaryValues[s_AvailableBoundaryValues[ii].SystemType.Name] = s_AvailableBoundaryValues[ii].Values.ToArray();
            }

            // load the localized tokens.
            m_tokenValues = LoadStringData("Opc.Ua.Types.Utils.LocalizedData.txt");
            if (m_tokenValues.Count == 0)
            {
                m_tokenValues = LoadStringData("Opc.Ua.Utils.LocalizedData.txt");
            }

            // index the available locales.
            m_availableLocales = new string[m_tokenValues.Count];

            int index = 0;

            foreach (string locale in m_tokenValues.Keys)
            {
                m_availableLocales[index++] = locale;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The maximum length for generated arrays.
        /// </summary>
        public int MaxArrayLength
        {
            get { return m_maxArrayLength; }
            set { m_maxArrayLength = value; }
        }

        /// <summary>
        /// The maximum length for generated strings.
        /// </summary>
        public int MaxStringLength
        {
            get { return m_maxStringLength; }
            set { m_maxStringLength = value; }
        }

        /// <summary>
        /// The minimum value for generated date time values.
        /// </summary>
        public DateTime MinDateTimeValue
        {
            get { return m_minDateTimeValue; }
            set { m_minDateTimeValue = value; }
        }

        /// <summary>
        /// The maximum value for generated date time values.
        /// </summary>
        public DateTime MaxDateTimeValue
        {
            get { return m_maxDateTimeValue; }
            set { m_maxDateTimeValue = value; }
        }

        /// <summary>
        /// The maximum number of attributes in generated XML elements.
        /// </summary>
        public int MaxXmlAttributeCount
        {
            get { return m_maxXmlAttributeCount; }
            set { m_maxXmlAttributeCount = value; }
        }

        /// <summary>
        /// The maximum number of child elements in generated XML elements.
        /// </summary>
        public int MaxXmlElementCount
        {
            get { return m_maxXmlElementCount; }
            set { m_maxXmlElementCount = value; }
        }

        /// <summary>
        /// The table namespace uris to use when generating NodeIds.
        /// </summary>
        public NamespaceTable NamespaceUris
        {
            get { return m_namespaceUris; }
            set { m_namespaceUris = value; }
        }

        /// <summary>
        /// The table server uris to use when generating NodeIds.
        /// </summary>
        public StringTable ServerUris
        {
            get { return m_serverUris; }
            set { m_serverUris = value; }
        }

        /// <summary>
        /// How frequently boundary values should be used expressed as percentage between 0 and 100.
        /// </summary>
        public int BoundaryValueFrequency
        {
            get { return m_boundaryValueFrequency; }
            set { m_boundaryValueFrequency = value; }
        }

        /// <summary>
        /// Returns true of a boundary value should be used.
        /// </summary>
        private bool UseBoundaryValue()
        {
            return m_random.NextInt32(99) < m_boundaryValueFrequency;
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public object GetRandom(NodeId dataType, int valueRank, IList<uint> arrayDimensions, ITypeTable typeTree)
        {
            BuiltInType expectedType = TypeInfo.GetBuiltInType(dataType, typeTree);

            // calculate total number of dimensions.
            int dimensions = 0;

            switch (valueRank)
            {
                case ValueRanks.Any:
                {
                    if (arrayDimensions != null && arrayDimensions.Count > 0)
                    {
                        dimensions = arrayDimensions.Count;
                        break;
                    }

                    dimensions = this.GetRandomRange(0, 1);
                    break;
                }

                case ValueRanks.ScalarOrOneDimension:
                {
                    dimensions = this.GetRandomRange(0, 1);
                    break;
                }

                case ValueRanks.OneOrMoreDimensions:
                {
                    if (arrayDimensions != null && arrayDimensions.Count > 0)
                    {
                        dimensions = arrayDimensions.Count;
                        break;
                    }

                    dimensions = this.GetRandomRange(1, 1);
                    break;
                }

                case ValueRanks.Scalar:
                {
                    dimensions = 0;
                    break;
                }

                default:
                {
                    dimensions = valueRank;
                    break;
                }
            }

            // return a random scalar.
            if (dimensions == 0)
            {
                if (expectedType == BuiltInType.Variant)
                {
                    // randomly choose a built-in type.
                    BuiltInType builtInType = BuiltInType.Variant;

                    while (builtInType == BuiltInType.Variant || builtInType == BuiltInType.DataValue)
                    {
                        builtInType = (BuiltInType)m_random.NextInt32((int)BuiltInType.Variant);
                    }

                    return GetRandomVariant(builtInType, false);
                }

                return GetRandom(expectedType);
            }

            // calculate the length of each dimension.
            int[] actualDimensions = new int[dimensions];

            for (int ii = 0; ii < dimensions; ii++)
            {
                if (arrayDimensions != null && arrayDimensions.Count > ii)
                {
                    actualDimensions[ii] = (int)arrayDimensions[ii];
                }

                while (actualDimensions[ii] == 0)
                {
                    actualDimensions[ii] = m_random.NextInt32(m_maxArrayLength);
                }
            }

            // create an array.
            Array output = TypeInfo.CreateArray(expectedType, actualDimensions);

            // generate random values for each element in the array.
            int length = output.Length;
            int[] indexes = new int[actualDimensions.Length];

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = output.Length;

                for (int jj = 0; jj < indexes.Length; jj++)
                {
                    divisor /= actualDimensions[jj];
                    indexes[jj] = (ii / divisor) % actualDimensions[jj];
                }

                object value = GetRandom(dataType, ValueRanks.Scalar, null, typeTree);

                if (value != null)
                {
                    if (expectedType == BuiltInType.Guid &&
                        value is Guid)
                    {
                        value = new Uuid((Guid)value);
                    }

                    output.SetValue(value, indexes);
                }
            }

            // return array value.
            return output;
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public object GetRandom(BuiltInType expectedType)
        {
            switch (expectedType)
            {
                case BuiltInType.Boolean: { return GetRandomBoolean(); }
                case BuiltInType.SByte: { return GetRandomSByte(); }
                case BuiltInType.Byte: { return GetRandomByte(); }
                case BuiltInType.Int16: { return GetRandomInt16(); }
                case BuiltInType.UInt16: { return GetRandomUInt16(); }
                case BuiltInType.Int32: { return GetRandomInt32(); }
                case BuiltInType.UInt32: { return GetRandomUInt32(); }
                case BuiltInType.Int64: { return GetRandomInt64(); }
                case BuiltInType.UInt64: { return GetRandomUInt64(); }
                case BuiltInType.Float: { return GetRandomFloat(); }
                case BuiltInType.Double: { return GetRandomDouble(); }
                case BuiltInType.String: { return GetRandomString(); }
                case BuiltInType.DateTime: { return GetRandomDateTime(); }
                case BuiltInType.Guid: { return GetRandomUuid(); }
                case BuiltInType.ByteString: { return GetRandomByteString(); }
                case BuiltInType.XmlElement: { return GetRandomXmlElement(); }
                case BuiltInType.NodeId: { return GetRandomNodeId(); }
                case BuiltInType.ExpandedNodeId: { return GetRandomExpandedNodeId(); }
                case BuiltInType.QualifiedName: { return GetRandomQualifiedName(); }
                case BuiltInType.LocalizedText: { return GetRandomLocalizedText(); }
                case BuiltInType.StatusCode: { return GetRandomStatusCode(); }
                case BuiltInType.Variant: { return GetRandomVariant(); }
                case BuiltInType.Enumeration: { return GetRandomInt32(); }
                case BuiltInType.ExtensionObject: { return GetRandomExtensionObject(); }
                case BuiltInType.DataValue: { return GetRandomDataValue(); }
                case BuiltInType.DiagnosticInfo: { return GetRandomDiagnosticInfo(); }

                case BuiltInType.Number:
                {
                    BuiltInType builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false);
                }

                case BuiltInType.Integer:
                {
                    BuiltInType builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false);
                }

                case BuiltInType.UInteger:
                {
                    BuiltInType builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.Byte);
                    return GetRandomVariant(builtInType, false);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public Array GetRandomArray(BuiltInType expectedType, bool useBoundaryValues, int length, bool fixedLength)
        {
            switch (expectedType)
            {
                case BuiltInType.Null: { return GetNullArray<object>(length, fixedLength); }
                case BuiltInType.Boolean: { return GetRandomArray<bool>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.SByte: { return GetRandomArray<sbyte>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Byte: { return GetRandomArray<byte>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Int16: { return GetRandomArray<short>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.UInt16: { return GetRandomArray<ushort>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Int32: { return GetRandomArray<int>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.UInt32: { return GetRandomArray<uint>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Int64: { return GetRandomArray<long>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.UInt64: { return GetRandomArray<ulong>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Float: { return GetRandomArray<float>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Double: { return GetRandomArray<double>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.String: { return GetRandomArray<string>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.DateTime: { return GetRandomArray<DateTime>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Guid: { return GetRandomArray<Uuid>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.ByteString: { return GetRandomArray<byte[]>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.XmlElement: { return GetRandomArray<XmlElement>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.NodeId: { return GetRandomArray<NodeId>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.ExpandedNodeId: { return GetRandomArray<ExpandedNodeId>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.QualifiedName: { return GetRandomArray<QualifiedName>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.LocalizedText: { return GetRandomArray<LocalizedText>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.StatusCode: { return GetRandomArray<StatusCode>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Variant: { return GetRandomArray<Variant>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.ExtensionObject: { return GetRandomArray<ExtensionObject>(useBoundaryValues, length, fixedLength); }
                case BuiltInType.Number:
                {
                    BuiltInType builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
                    return GetRandomArrayInVariant(builtInType, useBoundaryValues, length, fixedLength);
                }

                case BuiltInType.Integer:
                {
                    BuiltInType builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.SByte);
                    return GetRandomArrayInVariant(builtInType, useBoundaryValues, length, fixedLength);
                }

                case BuiltInType.UInteger:
                {
                    BuiltInType builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.Byte);
                    return GetRandomArrayInVariant(builtInType, useBoundaryValues, length, fixedLength);
                }
                case BuiltInType.Enumeration: { return GetRandomArray<int>(useBoundaryValues, length, fixedLength); }
            }

            return null;
        }

        /// <summary>
        /// Returns an array wrapped in a variant.
        /// </summary>
        private Variant[] GetRandomArrayInVariant(BuiltInType builtInType, bool useBoundaryValues, int length, bool fixedLength)
        {
            Array array = GetRandomArray(builtInType, useBoundaryValues, length, fixedLength);
            Variant[] variants = new Variant[array.Length];
            TypeInfo typeInfo = new TypeInfo(builtInType, ValueRanks.Scalar);

            for (int ii = 0; ii < variants.Length; ii++)
            {
                variants[ii] = new Variant(array.GetValue(ii), typeInfo);
            }

            return variants;
        }

        /// <summary>
        /// This method returns a random value of values for the type.
        /// </summary>
        public T GetRandom<T>(bool useBoundaryValues)
        {
            if (useBoundaryValues && UseBoundaryValue())
            {
                object boundaryValue = GetBoundaryValue(typeof(T));

                if (boundaryValue != null || !typeof(T).GetTypeInfo().IsValueType)
                {
                    return (T)boundaryValue;
                }
            }

            return (T)GetRandom(typeof(T));
        }

        /// <summary>
        /// This method returns a random array of values for the type.
        /// </summary>
        public T[] GetNullArray<T>(int length, bool fixedLength)
        {
            if (length < 0)
            {
                return null;
            }

            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }

            T[] value = new T[length];

            for (int ii = 0; ii < value.Length; ii++)
            {
                value[ii] = default(T);
            }

            return value;
        }

        /// <summary>
        /// This method returns a random array of values for the type.
        /// </summary>
        public T[] GetRandomArray<T>(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return null;
            }

            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }

            T[] value = new T[length];

            for (int ii = 0; ii < value.Length; ii++)
            {
                object element = null;

                if (useBoundaryValues && UseBoundaryValue())
                {
                    element = GetBoundaryValue(typeof(T));
                }
                else
                {
                    element = GetRandom(typeof(T));
                }

                if (element == null)
                {
                    element = default(T);
                    if (element == null)
                    {
                        // ensure a valid null type is returned
                        Type t = typeof(T);
                        if (t == typeof(ExpandedNodeId)) { element = ExpandedNodeId.Null; }
                        else if (t == typeof(NodeId)) { element = NodeId.Null; }
                        else if (t == typeof(LocalizedText)) { element = LocalizedText.Null; }
                        else if (t == typeof(QualifiedName)) { element = QualifiedName.Null; }
                    }
                }

                value[ii] = (T)element;
            }

            return value;
        }

        #region Boolean
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public bool GetRandomBoolean()
        {
            return m_random.NextInt32(1) != 0;
        }
        #endregion

        #region SByte
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public sbyte GetRandomSByte()
        {
            int buffer = m_random.NextInt32(Byte.MaxValue);

            if (buffer > SByte.MaxValue)
            {
                return (sbyte)(SByte.MinValue + (buffer - SByte.MaxValue) - 1);
            }

            return (sbyte)buffer;
        }
        #endregion

        #region Byte
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public byte GetRandomByte()
        {
            return (byte)m_random.NextInt32(Byte.MaxValue);
        }
        #endregion

        #region Int16
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public short GetRandomInt16()
        {
            int buffer = m_random.NextInt32(UInt16.MaxValue);

            if (buffer > Int16.MaxValue)
            {
                return (short)(Int16.MinValue + (buffer - Int16.MaxValue) - 1);
            }

            return (short)buffer;
        }
        #endregion

        #region UInt16
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public ushort GetRandomUInt16()
        {
            return (ushort)m_random.NextInt32(UInt16.MaxValue);
        }
        #endregion

        #region Int32
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public int GetRandomInt32()
        {
            return (int)m_random.NextInt32(Int32.MaxValue);
        }
        #endregion

        #region UInt32
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public uint GetRandomUInt32()
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToUInt32(bytes, 0);
        }
        #endregion

        #region Int64
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public long GetRandomInt64()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToInt64(bytes, 0);
        }
        #endregion

        #region UInt64
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public ulong GetRandomUInt64()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToUInt64(bytes, 0);
        }
        #endregion

        #region Float
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public float GetRandomFloat()
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToSingle(bytes, 0);
        }
        #endregion

        #region Double
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public double GetRandomDouble()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToSingle(bytes, 0);
        }
        #endregion

        #region String
        /// <summary>
        /// Creates a random string with a random locale.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public string GetRandomString()
        {
            return CreateString(GetRandomLocale(), false);
        }

        /// <summary>
        /// Creates a random string for the locale.
        /// </summary>
        public string GetRandomString(string locale)
        {
            return CreateString(locale, false);
        }

        /// <summary>
        /// Creates a random symbol with a random locale.
        /// </summary>
        public string GetRandomSymbol()
        {
            return CreateString(GetRandomLocale(), true);
        }

        /// <summary>
        /// Creates a random symbol for the locale.
        /// </summary>
        public string GetRandomSymbol(string locale)
        {
            return CreateString(locale, false);
        }
        #endregion

        #region DateTime
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public DateTime GetRandomDateTime()
        {
            int minTicks = (int)(m_minDateTimeValue.Ticks >> 32);
            int maxTicks = (int)(m_maxDateTimeValue.Ticks >> 32);

            long delta = GetRandomRange(minTicks, maxTicks);

            long higherTicks = (delta << 32);

            uint lowerTicks = GetRandomUInt32();

            return new DateTime(higherTicks + lowerTicks, DateTimeKind.Utc);
        }
        #endregion

        #region Guid
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public Guid GetRandomGuid()
        {
            byte[] bytes = new byte[16];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public Uuid GetRandomUuid()
        {
            byte[] bytes = new byte[16];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return new Uuid(new Guid(bytes));
        }
        #endregion

        #region ByteString
        /// <summary cref="GetRandom(Type)" />
        public byte[] GetRandomByteString()
        {
            int length = m_random.NextInt32(m_maxStringLength);

            byte[] bytes = new byte[length];
            m_random.NextBytes(bytes, 0, bytes.Length);

            return bytes;
        }
        #endregion

        #region XmlElement
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public XmlElement GetRandomXmlElement()
        {
            string locale1 = GetRandomLocale();
            string locale2 = GetRandomLocale();

            // create the root element.
            XmlDocument document = new XmlDocument();

            XmlElement element = document.CreateElement(
                "n0",
                CreateString(locale1, true),
                Utils.Format("http://{0}", CreateString(locale1, true)));

            document.AppendChild(element);

            // add the attributes.
            int attributeCount = m_random.NextInt32(m_maxXmlAttributeCount);

            for (int ii = 0; ii < attributeCount; ii++)
            {
                string attributeName = CreateString(locale1, true);
                XmlAttribute attribute = document.CreateAttribute(attributeName);
                attribute.Value = CreateString(locale2, true);
                element.SetAttributeNode(attribute);
            }

            // add the elements.
            int elementCount = m_random.NextInt32(m_maxXmlElementCount);

            for (int ii = 0; ii < elementCount; ii++)
            {
                string elementName = CreateString(locale1, true);

                XmlElement childElement = document.CreateElement(
                    element.Prefix,
                    elementName,
                    element.NamespaceURI);

                childElement.InnerText = CreateString(locale2, false);

                element.AppendChild(childElement);
            }

            return element;
        }
        #endregion

        #region NodeId
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public NodeId GetRandomNodeId()
        {
            ushort ns = (ushort)m_random.NextInt32(m_namespaceUris.Count - 1);

            IdType idType = (IdType)m_random.NextInt32(4);

            switch (idType)
            {
                case IdType.String:
                {
                    return new NodeId(CreateString(GetRandomLocale(), true), ns);
                }

                case IdType.Guid:
                {
                    return new NodeId(GetRandomGuid(), ns);
                }

                case IdType.Opaque:
                {
                    return new NodeId(GetRandomByteString(), ns);
                }
            }

            return new NodeId(GetRandomUInt32(), ns);
        }
        #endregion

        #region ExpandedNodeId
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public ExpandedNodeId GetRandomExpandedNodeId()
        {
            NodeId nodeId = GetRandomNodeId();
            ushort serverIndex = m_serverUris.Count == 0 ? (ushort)0 : (ushort)m_random.NextInt32(m_serverUris.Count - 1);
            return new ExpandedNodeId(nodeId, m_namespaceUris.GetString(nodeId.NamespaceIndex), serverIndex);
        }
        #endregion

        #region QualifiedName
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public QualifiedName GetRandomQualifiedName()
        {
            ushort ns = (ushort)m_random.NextInt32(m_namespaceUris.Count - 1);
            return new QualifiedName(CreateString(GetRandomLocale(), true), ns);
        }
        #endregion

        #region LocalizedText
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public LocalizedText GetRandomLocalizedText()
        {
            string locale = GetRandomLocale();
            return new LocalizedText(locale, CreateString(locale, false));
        }
        #endregion

        #region StatusCode
        /// <summary cref="GetRandom(Type)" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public StatusCode GetRandomStatusCode()
        {
            int offset = GetRandomRange((int)(StatusCodes.BadUnexpectedError >> 16), (int)(StatusCodes.BadMaxConnectionsReached >> 16));
            return (uint)(StatusCodes.BadUnexpectedError + (offset << 16));
        }
        #endregion

        #region Variant
        /// <summary cref="GetRandom(Type)" />
        public Variant GetRandomVariant()
        {
            return GetRandomVariant(true);
        }

        /// <summary cref="GetRandom(Type)" />
        public Variant GetRandomVariant(bool allowArrays)
        {
            // randomly choose a built-in type.
            BuiltInType builtInType = BuiltInType.Variant;

            while (builtInType == BuiltInType.Variant || builtInType == BuiltInType.DataValue)
            {
                builtInType = (BuiltInType)m_random.NextInt32((int)BuiltInType.Variant);
            }

            return GetRandomVariant(builtInType, (allowArrays) ? (m_random.NextInt32(1) == 1) : false);
        }

        /// <summary>
        /// Returns a random variant containing a scalar or array value. 
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private Variant GetRandomVariant(BuiltInType builtInType, bool isArray)
        {
            if (builtInType == BuiltInType.Null)
            {
                return Variant.Null;
            }

            int length = -1;

            if (isArray)
            {
                length = m_random.NextInt32(m_maxArrayLength - 1);
            }
            else if (builtInType == BuiltInType.Variant)
            {
                length = 1;
            }

            if (length >= 0)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean: { return new Variant(GetRandomArray<bool>(true, length, true)); }
                    case BuiltInType.SByte: { return new Variant(GetRandomArray<sbyte>(true, length, true)); }
                    case BuiltInType.Byte: { return new Variant(GetRandomArray<byte>(true, length, true)); }
                    case BuiltInType.Int16: { return new Variant(GetRandomArray<short>(true, length, true)); }
                    case BuiltInType.UInt16: { return new Variant(GetRandomArray<ushort>(true, length, true)); }
                    case BuiltInType.Int32: { return new Variant(GetRandomArray<int>(true, length, true)); }
                    case BuiltInType.UInt32: { return new Variant(GetRandomArray<uint>(true, length, true)); }
                    case BuiltInType.Int64: { return new Variant(GetRandomArray<long>(true, length, true)); }
                    case BuiltInType.UInt64: { return new Variant(GetRandomArray<ulong>(true, length, true)); }
                    case BuiltInType.Float: { return new Variant(GetRandomArray<float>(true, length, true)); }
                    case BuiltInType.Double: { return new Variant(GetRandomArray<double>(true, length, true)); }
                    case BuiltInType.String: { return new Variant(GetRandomArray<string>(true, length, true)); }
                    case BuiltInType.DateTime: { return new Variant(GetRandomArray<DateTime>(true, length, true)); }
                    case BuiltInType.Guid: { return new Variant(GetRandomArray<Uuid>(true, length, true)); }
                    case BuiltInType.ByteString: { return new Variant(GetRandomArray<byte[]>(true, length, true)); }
                    case BuiltInType.XmlElement: { return new Variant(GetRandomArray<XmlElement>(true, length, true)); }
                    case BuiltInType.NodeId: { return new Variant(GetRandomArray<NodeId>(true, length, true)); }
                    case BuiltInType.ExpandedNodeId: { return new Variant(GetRandomArray<ExpandedNodeId>(true, length, true)); }
                    case BuiltInType.QualifiedName: { return new Variant(GetRandomArray<QualifiedName>(true, length, true)); }
                    case BuiltInType.LocalizedText: { return new Variant(GetRandomArray<LocalizedText>(true, length, true)); }
                    case BuiltInType.StatusCode: { return new Variant(GetRandomArray<StatusCode>(true, length, true)); }
                    case BuiltInType.Variant: { return new Variant(GetRandomArray<Variant>(true, length, true)); }
                }
            }

            return new Variant(GetRandom(builtInType));
        }
        #endregion

        #region ExtensionObject
        /// <summary cref="GetRandom(Type)" />
        public ExtensionObject GetRandomExtensionObject()
        {
            NodeId typeId = GetRandomNodeId();

            if (NodeId.IsNull(typeId))
            {
                return ExtensionObject.Null;
            }
            object body = null;

            if (m_random.NextInt32(1) != 0)
            {
                body = GetRandomByteString();
            }
            else
            {
                body = GetRandomXmlElement();
            }

            return new ExtensionObject(typeId, body);
        }
        #endregion

        #region DataValue
        /// <summary>
        /// Get a random DataValue.
        /// </summary>
        public DataValue GetRandomDataValue()
        {
            Variant variant = GetRandomVariant();
            StatusCode statusCode = GetRandomStatusCode();
            DateTime sourceTimeStamp = GetRandomDateTime();
            DateTime serverTimeStamp = GetRandomDateTime();
            return new DataValue(variant, statusCode, sourceTimeStamp, DateTime.UtcNow);
        }
        #endregion

        #region DiagnosticInfo
        /// <summary>
        /// Get random diagnostic info.
        /// </summary>
        public DiagnosticInfo GetRandomDiagnosticInfo()
        {
            // TODO: return random values
            return new DiagnosticInfo(
                ServiceResult.Good,
                DiagnosticsMasks.NoInnerStatus,
                true,
                new StringTable());
        }
        #endregion

        #endregion

        #region BoundaryValues Class
        /// <summary>
        /// Stores the boundary values for a data type.
        /// </summary>
        private class BoundaryValues
        {
            public BoundaryValues(Type systemType, params object[] values)
            {
                SystemType = systemType;

                if (values != null)
                {
                    Values = new List<object>(values);
                }
                else
                {
                    Values = new List<object>();
                }
            }

            public Type SystemType;
            public List<object> Values;
        }

        /// <summary>
        /// Boundary values used or testing.
        /// </summary>
        private static readonly BoundaryValues[] s_AvailableBoundaryValues = new BoundaryValues[]
        {
            new BoundaryValues(typeof(sbyte), SByte.MinValue, (sbyte)0, SByte.MaxValue),
            new BoundaryValues(typeof(byte), Byte.MinValue, Byte.MaxValue),
            new BoundaryValues(typeof(short), Int16.MinValue, (short)0, Int16.MaxValue),
            new BoundaryValues(typeof(ushort), UInt16.MinValue, UInt16.MaxValue),
            new BoundaryValues(typeof(int), Int32.MinValue, (int)0, Int32.MaxValue),
            new BoundaryValues(typeof(uint), UInt32.MinValue, UInt32.MaxValue),
            new BoundaryValues(typeof(long), Int64.MinValue, (long)0, Int64.MaxValue),
            new BoundaryValues(typeof(ulong), UInt64.MinValue, UInt64.MaxValue),
            new BoundaryValues(typeof(float), Single.Epsilon, Single.MaxValue, Single.MinValue, Single.NaN, Single.NegativeInfinity, Single.PositiveInfinity, (float)0 ),
            new BoundaryValues(typeof(double), Double.Epsilon, Double.MaxValue, Double.MinValue, Double.NaN, Double.NegativeInfinity, Double.PositiveInfinity, (double)0 ),
            new BoundaryValues(typeof(string), null, String.Empty ),
            new BoundaryValues(typeof(DateTime), DateTime.MinValue, DateTime.MaxValue, new DateTime(1099, 1, 1), Utils.TimeBase, new DateTime(2039, 4, 4), new DateTime(2001, 9, 11, 9, 15, 0, DateTimeKind.Local)),
            new BoundaryValues(typeof(Guid), Guid.Empty),
            new BoundaryValues(typeof(Uuid), Uuid.Empty),
            new BoundaryValues(typeof(byte[]), null, new byte[0]),
            new BoundaryValues(typeof(XmlElement), null ),
            new BoundaryValues(typeof(NodeId), null, NodeId.Null, new NodeId(Guid.Empty), new NodeId(String.Empty), new NodeId(new byte[0]) ),
            new BoundaryValues(typeof(ExpandedNodeId), null, ExpandedNodeId.Null, new ExpandedNodeId(Guid.Empty), new ExpandedNodeId(String.Empty), new ExpandedNodeId(new byte[0]) ),
            new BoundaryValues(typeof(QualifiedName), null, QualifiedName.Null ),
            new BoundaryValues(typeof(LocalizedText), null, LocalizedText.Null ),
            new BoundaryValues(typeof(StatusCode), StatusCodes.Good, StatusCodes.Uncertain, StatusCodes.Bad ),
            new BoundaryValues(typeof(ExtensionObject), ExtensionObject.Null),
        };
        #endregion

        #region Private Methods
        /// <summary>
        /// Loads some string data from a resource.
        /// </summary>
        private static SortedDictionary<string, string[]> LoadStringData(string resourceName)
        {
            SortedDictionary<string, string[]> dictionary = new SortedDictionary<string, string[]>();

            try
            {
                string locale = null;
                List<string> tokens = null;

                Stream istrm = typeof(DataGenerator).GetTypeInfo().Assembly.GetManifestResourceStream(resourceName);
                if (istrm == null)
                {
                    // try to load from app directory
                    FileInfo file = new FileInfo(resourceName);
                    istrm = file.OpenRead();
                }

                using (StreamReader reader = new StreamReader(istrm))
                {
                    for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                    {
                        string token = line.Trim();

                        if (String.IsNullOrEmpty(token))
                        {
                            continue;
                        }

                        if (token.StartsWith("=", StringComparison.Ordinal))
                        {
                            if (locale != null)
                            {
                                dictionary.Add(locale, tokens.ToArray());
                            }

                            locale = token.Substring(1);
                            tokens = new List<string>();
                            continue;
                        }

                        tokens.Add(token);
                    }
                }

                return dictionary;
            }
            catch (Exception)
            {
                return dictionary;
            }
        }

        /// <summary>
        /// Randomly selects a boundary value for type.
        /// </summary>
        private object GetBoundaryValue(Type type)
        {
            if (type == null)
            {
                return null;
            }

            object[] boundaryValues = null;

            if (!m_boundaryValues.TryGetValue(type.Name, out boundaryValues))
            {
                return null;
            }

            if (boundaryValues == null || boundaryValues.Length == 0)
            {
                return null;
            }

            int index = m_random.NextInt32(boundaryValues.Length - 1);

            if (type.IsInstanceOfType(boundaryValues[index]))
            {
                return boundaryValues[index];
            }

            return null;
        }

        /// <summary>
        /// Returns a positive integer between the specified values.
        /// </summary>
        private int GetRandomRange(int min, int max)
        {
            if (min < 0)
            {
                min = 0;
            }

            if (max < 0)
            {
                max = 0;
            }

            if (min >= max)
            {
                return min;
            }

            return m_random.NextInt32(max - min) + min;
        }


        /// <summary>
        /// Returns a random value of the specified type.
        /// </summary>
        private object GetRandom(Type expectedType)
        {
            var builtInType = TypeInfo.Construct(expectedType).BuiltInType;
            object value = GetRandom(builtInType);

            if (builtInType == BuiltInType.Guid &&
                expectedType == typeof(Guid))
            {
                return (Guid)(Uuid)value;
            }

            return value;
        }

        /// <summary>
        /// Returns a random locale.
        /// </summary>
        private string GetRandomLocale()
        {
            int index = m_random.NextInt32(m_availableLocales.Length - 1);
            return m_availableLocales[index];
        }

        /// <summary>
        /// Creates a string from the tokens for the locale.
        /// </summary>
        private string CreateString(string locale, bool isSymbol)
        {
            string[] tokens = null;

            if (!m_tokenValues.TryGetValue(locale, out tokens))
            {
                tokens = m_tokenValues["en-US"];
            }

            int length = 0;

            if (isSymbol)
            {
                length = m_random.NextInt32(2) + 1;
            }
            else
            {
                length = m_random.NextInt32(m_maxStringLength) + 1;
            }

            StringBuilder buffer = new StringBuilder();

            while (buffer.Length < length)
            {
                if (!isSymbol && buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                int index = m_random.NextInt32(tokens.Length - 1);
                buffer.Append(tokens[index]);

                if (!isSymbol && m_random.NextInt32(1) != 0)
                {
                    index = m_random.NextInt32(kPunctuation.Length - 1);
                    buffer.Append(kPunctuation[index]);
                }
            }

            return buffer.ToString();
        }
        #endregion

        #region Private Fields
        private IRandomSource m_random;
        private int m_maxArrayLength;
        private int m_maxStringLength;
        private DateTime m_minDateTimeValue;
        private DateTime m_maxDateTimeValue;
        private int m_boundaryValueFrequency;
        private int m_maxXmlAttributeCount;
        private int m_maxXmlElementCount;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private SortedDictionary<string, object[]> m_boundaryValues;
        private string[] m_availableLocales;
        private SortedDictionary<string, string[]> m_tokenValues;
        private const string kPunctuation = "`~!@#$%^&*()_-+={}[]:\"';?><,./";
        #endregion
    }
}
