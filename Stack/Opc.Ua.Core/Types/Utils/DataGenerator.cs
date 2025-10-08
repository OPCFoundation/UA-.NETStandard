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
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

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
        /// Returns a random non-negative integer which does not exceed the specified maximum.
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

        /// <inheritdoc/>
        public void NextBytes(byte[] bytes, int offset, int count)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

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

        /// <inheritdoc/>
        public int NextInt32(int max)
        {
            if (max < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            if (max < int.MaxValue)
            {
                max++;
            }

            return m_random.Next(max);
        }

        private readonly Random m_random;
    }

    /// <summary>
    /// A class that generates data.
    /// </summary>
    public class DataGenerator
    {
        /// <summary>
        /// Obsolete constructor
        /// </summary>
        [Obsolete("Use DataGenerator(ITelemetryContext) instead.")]
        public DataGenerator(IRandomSource random)
            : this(random, null)
        {
        }

        /// <summary>
        /// Initializes the data generator.
        /// </summary>
        public DataGenerator(IRandomSource random, ITelemetryContext telemetry)
        {
            MaxArrayLength = 100;
            MaxStringLength = 100;
            MaxXmlAttributeCount = 10;
            MaxXmlElementCount = 10;
            MinDateTimeValue = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            MaxDateTimeValue = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            m_random = random;
            m_logger = telemetry.CreateLogger<DataGenerator>();
            BoundaryValueFrequency = 20;
            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();

            // create a random source if none provided.
            m_random ??= new RandomSource();

            // load the boundary values.
            m_boundaryValues = [];

            for (int ii = 0; ii < s_availableBoundaryValues.Length; ii++)
            {
                m_boundaryValues[s_availableBoundaryValues[ii].SystemType.Name] =
                [
                    .. s_availableBoundaryValues[ii].Values
                ];
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

        /// <summary>
        /// The maximum length for generated arrays.
        /// </summary>
        public int MaxArrayLength { get; set; }

        /// <summary>
        /// The maximum length for generated strings.
        /// </summary>
        public int MaxStringLength { get; set; }

        /// <summary>
        /// The minimum value for generated date time values.
        /// </summary>
        public DateTime MinDateTimeValue { get; set; }

        /// <summary>
        /// The maximum value for generated date time values.
        /// </summary>
        public DateTime MaxDateTimeValue { get; set; }

        /// <summary>
        /// The maximum number of attributes in generated XML elements.
        /// </summary>
        public int MaxXmlAttributeCount { get; set; }

        /// <summary>
        /// The maximum number of child elements in generated XML elements.
        /// </summary>
        public int MaxXmlElementCount { get; set; }

        /// <summary>
        /// The table namespace uris to use when generating NodeIds.
        /// </summary>
        public NamespaceTable NamespaceUris { get; set; }

        /// <summary>
        /// The table server uris to use when generating NodeIds.
        /// </summary>
        public StringTable ServerUris { get; set; }

        /// <summary>
        /// How frequently boundary values should be used expressed as percentage between 0 and 100.
        /// </summary>
        public int BoundaryValueFrequency { get; set; }

        /// <summary>
        /// Returns true if a boundary value should be used.
        /// </summary>
        private bool UseBoundaryValue()
        {
            return m_random.NextInt32(99) < BoundaryValueFrequency;
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public object GetRandom(
            NodeId dataType,
            int valueRank,
            IList<uint> arrayDimensions,
            ITypeTable typeTree)
        {
            BuiltInType expectedType = TypeInfo.GetBuiltInType(dataType, typeTree);

            // calculate total number of dimensions.
            int dimensions;
            switch (valueRank)
            {
                case ValueRanks.Any:
                    if (arrayDimensions != null && arrayDimensions.Count > 0)
                    {
                        dimensions = arrayDimensions.Count;
                        break;
                    }

                    dimensions = GetRandomRange(0, 1);
                    break;
                case ValueRanks.ScalarOrOneDimension:
                    dimensions = GetRandomRange(0, 1);
                    break;
                case ValueRanks.OneOrMoreDimensions:
                    if (arrayDimensions != null && arrayDimensions.Count > 0)
                    {
                        dimensions = arrayDimensions.Count;
                        break;
                    }

                    dimensions = GetRandomRange(1, 1);
                    break;
                case ValueRanks.Scalar:
                    dimensions = 0;
                    break;
                default:
                    dimensions = valueRank;
                    break;
            }

            // return a random scalar.
            if (dimensions == 0)
            {
                if (expectedType == BuiltInType.Variant)
                {
                    // randomly choose a built-in type.
                    BuiltInType builtInType = BuiltInType.Variant;

                    while (builtInType is BuiltInType.Variant or BuiltInType.DataValue)
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
                    actualDimensions[ii] = m_random.NextInt32(MaxArrayLength);
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
                    indexes[jj] = ii / divisor % actualDimensions[jj];
                }

                object value = GetRandom(dataType, ValueRanks.Scalar, null, typeTree);

                if (value != null)
                {
                    if (expectedType == BuiltInType.Guid && value is Guid guidValue)
                    {
                        value = new Uuid(guidValue);
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
                case BuiltInType.Boolean:
                    return GetRandomBoolean();
                case BuiltInType.SByte:
                    return GetRandomSByte();
                case BuiltInType.Byte:
                    return GetRandomByte();
                case BuiltInType.Int16:
                    return GetRandomInt16();
                case BuiltInType.UInt16:
                    return GetRandomUInt16();
                case BuiltInType.Int32:
                    return GetRandomInt32();
                case BuiltInType.UInt32:
                    return GetRandomUInt32();
                case BuiltInType.Int64:
                    return GetRandomInt64();
                case BuiltInType.UInt64:
                    return GetRandomUInt64();
                case BuiltInType.Float:
                    return GetRandomFloat();
                case BuiltInType.Double:
                    return GetRandomDouble();
                case BuiltInType.String:
                    return GetRandomString();
                case BuiltInType.DateTime:
                    return GetRandomDateTime();
                case BuiltInType.Guid:
                    return GetRandomUuid();
                case BuiltInType.ByteString:
                    return GetRandomByteString();
                case BuiltInType.XmlElement:
                    return GetRandomXmlElement();
                case BuiltInType.NodeId:
                    return GetRandomNodeId();
                case BuiltInType.ExpandedNodeId:
                    return GetRandomExpandedNodeId();
                case BuiltInType.QualifiedName:
                    return GetRandomQualifiedName();
                case BuiltInType.LocalizedText:
                    return GetRandomLocalizedText();
                case BuiltInType.StatusCode:
                    return GetRandomStatusCode();
                case BuiltInType.Variant:
                    return GetRandomVariant();
                case BuiltInType.Enumeration:
                    return GetRandomInt32();
                case BuiltInType.ExtensionObject:
                    return GetRandomExtensionObject();
                case BuiltInType.DataValue:
                    return GetRandomDataValue();
                case BuiltInType.DiagnosticInfo:
                    return GetRandomDiagnosticInfo();
                case BuiltInType.Number:
                {
                    var builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false);
                }
                case BuiltInType.Integer:
                {
                    var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) +
                        (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false);
                }
                case BuiltInType.UInteger:
                {
                    var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) +
                        (int)BuiltInType.Byte);
                    return GetRandomVariant(builtInType, false);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public Array GetRandomArray(
            BuiltInType expectedType,
            bool useBoundaryValues,
            int length,
            bool fixedLength)
        {
            switch (expectedType)
            {
                case BuiltInType.Null:
                    return GetNullArray<object>(length, fixedLength);
                case BuiltInType.Boolean:
                    return GetRandomArray<bool>(useBoundaryValues, length, fixedLength);
                case BuiltInType.SByte:
                    return GetRandomArray<sbyte>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Byte:
                    return GetRandomArray<byte>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int16:
                    return GetRandomArray<short>(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt16:
                    return GetRandomArray<ushort>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int32:
                    return GetRandomArray<int>(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt32:
                    return GetRandomArray<uint>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Int64:
                    return GetRandomArray<long>(useBoundaryValues, length, fixedLength);
                case BuiltInType.UInt64:
                    return GetRandomArray<ulong>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Float:
                    return GetRandomArray<float>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Double:
                    return GetRandomArray<double>(useBoundaryValues, length, fixedLength);
                case BuiltInType.String:
                    return GetRandomArray<string>(useBoundaryValues, length, fixedLength);
                case BuiltInType.DateTime:
                    return GetRandomArray<DateTime>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Guid:
                    return GetRandomArray<Uuid>(useBoundaryValues, length, fixedLength);
                case BuiltInType.ByteString:
                    return GetRandomArray<byte[]>(useBoundaryValues, length, fixedLength);
                case BuiltInType.XmlElement:
                    return GetRandomArray<XmlElement>(useBoundaryValues, length, fixedLength);
                case BuiltInType.NodeId:
                    return GetRandomArray<NodeId>(useBoundaryValues, length, fixedLength);
                case BuiltInType.ExpandedNodeId:
                    return GetRandomArray<ExpandedNodeId>(useBoundaryValues, length, fixedLength);
                case BuiltInType.QualifiedName:
                    return GetRandomArray<QualifiedName>(useBoundaryValues, length, fixedLength);
                case BuiltInType.LocalizedText:
                    return GetRandomArray<LocalizedText>(useBoundaryValues, length, fixedLength);
                case BuiltInType.StatusCode:
                    return GetRandomArray<StatusCode>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Variant:
                    return GetRandomArray<Variant>(useBoundaryValues, length, fixedLength);
                case BuiltInType.ExtensionObject:
                    return GetRandomArray<ExtensionObject>(useBoundaryValues, length, fixedLength);
                case BuiltInType.Number:
                {
                    var builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
                    return GetRandomArrayInVariant(
                        builtInType,
                        useBoundaryValues,
                        length,
                        fixedLength);
                }
                case BuiltInType.Integer:
                {
                    var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) +
                        (int)BuiltInType.SByte);
                    return GetRandomArrayInVariant(
                        builtInType,
                        useBoundaryValues,
                        length,
                        fixedLength);
                }
                case BuiltInType.UInteger:
                {
                    var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) +
                        (int)BuiltInType.Byte);
                    return GetRandomArrayInVariant(
                        builtInType,
                        useBoundaryValues,
                        length,
                        fixedLength);
                }
                case BuiltInType.Enumeration:
                    return GetRandomArray<int>(useBoundaryValues, length, fixedLength);
            }

            return null;
        }

        /// <summary>
        /// Returns an array wrapped in a variant.
        /// </summary>
        private Variant[] GetRandomArrayInVariant(
            BuiltInType builtInType,
            bool useBoundaryValues,
            int length,
            bool fixedLength)
        {
            Array array = GetRandomArray(builtInType, useBoundaryValues, length, fixedLength);
            var variants = new Variant[array.Length];
            var typeInfo = TypeInfo.CreateScalar(builtInType);

            for (int ii = 0; ii < variants.Length; ii++)
            {
                variants[ii] = new Variant(array.GetValue(ii), typeInfo);
            }

            return variants;
        }

        /// <summary>
        /// This method returns a random value of values for the type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
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
        /// <typeparam name="T"></typeparam>
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

            var value = new T[length];

            for (int ii = 0; ii < value.Length; ii++)
            {
                value[ii] = default;
            }

            return value;
        }

        /// <summary>
        /// This method returns a random array of values for the type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
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

            var value = new T[length];

            for (int ii = 0; ii < value.Length; ii++)
            {
                object element;
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
                        if (t == typeof(ExpandedNodeId))
                        {
                            element = ExpandedNodeId.Null;
                        }
                        else if (t == typeof(NodeId))
                        {
                            element = NodeId.Null;
                        }
                        else if (t == typeof(LocalizedText))
                        {
                            element = LocalizedText.Null;
                        }
                        else if (t == typeof(QualifiedName))
                        {
                            element = QualifiedName.Null;
                        }
                    }
                }

                value[ii] = (T)element;
            }

            return value;
        }

        /// <inheritdoc/>
        public bool GetRandomBoolean()
        {
            return m_random.NextInt32(1) != 0;
        }

        /// <inheritdoc/>
        public sbyte GetRandomSByte()
        {
            int buffer = m_random.NextInt32(byte.MaxValue);

            if (buffer > sbyte.MaxValue)
            {
                return (sbyte)(sbyte.MinValue + (buffer - sbyte.MaxValue) - 1);
            }

            return (sbyte)buffer;
        }

        /// <inheritdoc/>
        public byte GetRandomByte()
        {
            return (byte)m_random.NextInt32(byte.MaxValue);
        }

        /// <inheritdoc/>
        public short GetRandomInt16()
        {
            int buffer = m_random.NextInt32(ushort.MaxValue);

            if (buffer > short.MaxValue)
            {
                return (short)(short.MinValue + (buffer - short.MaxValue) - 1);
            }

            return (short)buffer;
        }

        /// <inheritdoc/>
        public ushort GetRandomUInt16()
        {
            return (ushort)m_random.NextInt32(ushort.MaxValue);
        }

        /// <inheritdoc/>
        public int GetRandomInt32()
        {
            return m_random.NextInt32(int.MaxValue);
        }

        /// <inheritdoc/>
        public uint GetRandomUInt32()
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <inheritdoc/>
        public long GetRandomInt64()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToInt64(bytes, 0);
        }

        /// <inheritdoc/>
        public ulong GetRandomUInt64()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToUInt64(bytes, 0);
        }

        /// <inheritdoc/>
        public float GetRandomFloat()
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <inheritdoc/>
        public double GetRandomDouble()
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// Creates a random string with a random locale.
        /// </summary>
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

        /// <inheritdoc/>
        public DateTime GetRandomDateTime()
        {
            int minTicks = (int)(MinDateTimeValue.Ticks >> 32);
            int maxTicks = (int)(MaxDateTimeValue.Ticks >> 32);

            long delta = GetRandomRange(minTicks, maxTicks);

            long higherTicks = delta << 32;

            uint lowerTicks = GetRandomUInt32();

            return new DateTime(higherTicks + lowerTicks, DateTimeKind.Utc);
        }

        /// <inheritdoc/>
        public Guid GetRandomGuid()
        {
            byte[] bytes = new byte[16];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        /// <inheritdoc/>
        public Uuid GetRandomUuid()
        {
            byte[] bytes = new byte[16];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return new Uuid(new Guid(bytes));
        }

        /// <inheritdoc/>
        public byte[] GetRandomByteString()
        {
            int length = m_random.NextInt32(MaxStringLength);

            byte[] bytes = new byte[length];
            m_random.NextBytes(bytes, 0, bytes.Length);

            return bytes;
        }

        /// <inheritdoc/>
        public XmlElement GetRandomXmlElement()
        {
            string locale1 = GetRandomLocale();
            string locale2 = GetRandomLocale();

            // create the root element.
            var document = new XmlDocument();

            XmlElement element = document.CreateElement(
                "n0",
                CreateString(locale1, true),
                Utils.Format("http://{0}", CreateString(locale1, true)));

            document.AppendChild(element);

            // add the attributes.
            int attributeCount = m_random.NextInt32(MaxXmlAttributeCount);

            for (int ii = 0; ii < attributeCount; ii++)
            {
                string attributeName = CreateString(locale1, true);
                XmlAttribute attribute = document.CreateAttribute(attributeName);
                attribute.Value = CreateString(locale2, true);
                element.SetAttributeNode(attribute);
            }

            // add the elements.
            int elementCount = m_random.NextInt32(MaxXmlElementCount);

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

        /// <inheritdoc/>
        public NodeId GetRandomNodeId()
        {
            ushort ns = (ushort)m_random.NextInt32(NamespaceUris.Count - 1);

            switch ((IdType)m_random.NextInt32(4))
            {
                case IdType.String:
                    return new NodeId(CreateString(GetRandomLocale(), true), ns);
                case IdType.Guid:
                    return new NodeId(GetRandomGuid(), ns);
                case IdType.Opaque:
                    return new NodeId(GetRandomByteString(), ns);
            }

            return new NodeId(GetRandomUInt32(), ns);
        }

        /// <inheritdoc/>
        public ExpandedNodeId GetRandomExpandedNodeId()
        {
            NodeId nodeId = GetRandomNodeId();
            ushort serverIndex = ServerUris.Count == 0
                ? (ushort)0
                : (ushort)m_random.NextInt32(ServerUris.Count - 1);
            return new ExpandedNodeId(
                nodeId,
                nodeId.NamespaceIndex > 0 ? NamespaceUris.GetString(nodeId.NamespaceIndex) : null,
                serverIndex);
        }

        /// <inheritdoc/>
        public QualifiedName GetRandomQualifiedName()
        {
            ushort ns = (ushort)m_random.NextInt32(NamespaceUris.Count - 1);
            return new QualifiedName(CreateString(GetRandomLocale(), true), ns);
        }

        /// <inheritdoc/>
        public LocalizedText GetRandomLocalizedText()
        {
            string locale = GetRandomLocale();
            return new LocalizedText(locale, CreateString(locale, false));
        }

        private readonly List<KeyValuePair<uint, string>> m_knownStatusCodes = [];

        /// <inheritdoc/>
        public StatusCode GetRandomStatusCode()
        {
            if (m_knownStatusCodes.Count == 0)
            {
                foreach (FieldInfo field in typeof(StatusCodes).GetFields(
                    BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.FieldType == typeof(uint) &&
                        (field.Name.StartsWith("Good") ||
                        field.Name.StartsWith("Uncertain") ||
                        field.Name.StartsWith("Bad")))
                    {
                        uint value = Convert.ToUInt32(
                            field.GetValue(null),
                            System.Globalization.CultureInfo.InvariantCulture);
                        m_knownStatusCodes.Add(
                            new KeyValuePair<uint, string>(value, field.Name));
                    }
                }
            }

            int index = GetRandomRange(0, m_knownStatusCodes.Count - 1);
            return m_knownStatusCodes[index].Key;
        }

        /// <inheritdoc/>
        public Variant GetRandomVariant()
        {
            return GetRandomVariant(true);
        }

        /// <inheritdoc/>
        public Variant GetRandomVariant(bool allowArrays)
        {
            // randomly choose a built-in type.
            BuiltInType builtInType = BuiltInType.Variant;

            while (builtInType is BuiltInType.Variant or BuiltInType.DataValue)
            {
                builtInType = (BuiltInType)m_random.NextInt32((int)BuiltInType.Variant);
            }

            return GetRandomVariant(builtInType, allowArrays && m_random.NextInt32(1) == 1);
        }

        /// <summary>
        /// Returns a random variant containing a scalar or array value.
        /// </summary>
        private Variant GetRandomVariant(BuiltInType builtInType, bool isArray)
        {
            if (builtInType == BuiltInType.Null)
            {
                return Variant.Null;
            }

            int length = -1;

            if (isArray)
            {
                length = m_random.NextInt32(MaxArrayLength - 1);
            }
            else if (builtInType == BuiltInType.Variant)
            {
                length = 1;
            }

            if (length >= 0)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        return new Variant(GetRandomArray<bool>(true, length, true));
                    case BuiltInType.SByte:
                        return new Variant(GetRandomArray<sbyte>(true, length, true));
                    case BuiltInType.Byte:
                        return new Variant(GetRandomArray<byte>(true, length, true));
                    case BuiltInType.Int16:
                        return new Variant(GetRandomArray<short>(true, length, true));
                    case BuiltInType.UInt16:
                        return new Variant(GetRandomArray<ushort>(true, length, true));
                    case BuiltInType.Int32:
                        return new Variant(GetRandomArray<int>(true, length, true));
                    case BuiltInType.UInt32:
                        return new Variant(GetRandomArray<uint>(true, length, true));
                    case BuiltInType.Int64:
                        return new Variant(GetRandomArray<long>(true, length, true));
                    case BuiltInType.UInt64:
                        return new Variant(GetRandomArray<ulong>(true, length, true));
                    case BuiltInType.Float:
                        return new Variant(GetRandomArray<float>(true, length, true));
                    case BuiltInType.Double:
                        return new Variant(GetRandomArray<double>(true, length, true));
                    case BuiltInType.String:
                        return new Variant(GetRandomArray<string>(true, length, true));
                    case BuiltInType.DateTime:
                        return new Variant(GetRandomArray<DateTime>(true, length, true));
                    case BuiltInType.Guid:
                        return new Variant(GetRandomArray<Uuid>(true, length, true));
                    case BuiltInType.ByteString:
                        return new Variant(GetRandomArray<byte[]>(true, length, true));
                    case BuiltInType.XmlElement:
                        return new Variant(GetRandomArray<XmlElement>(true, length, true));
                    case BuiltInType.NodeId:
                        return new Variant(GetRandomArray<NodeId>(true, length, true));
                    case BuiltInType.ExpandedNodeId:
                        return new Variant(GetRandomArray<ExpandedNodeId>(true, length, true));
                    case BuiltInType.QualifiedName:
                        return new Variant(GetRandomArray<QualifiedName>(true, length, true));
                    case BuiltInType.LocalizedText:
                        return new Variant(GetRandomArray<LocalizedText>(true, length, true));
                    case BuiltInType.StatusCode:
                        return new Variant(GetRandomArray<StatusCode>(true, length, true));
                    case BuiltInType.Variant:
                        return new Variant(GetRandomArray<Variant>(true, length, true));
                }
            }

            return new Variant(GetRandom(builtInType));
        }

        /// <inheritdoc/>
        public ExtensionObject GetRandomExtensionObject()
        {
            NodeId typeId = GetRandomNodeId();

            if (NodeId.IsNull(typeId))
            {
                return ExtensionObject.Null;
            }
            object body;
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

        /// <summary>
        /// Get a random DataValue.
        /// </summary>
        public DataValue GetRandomDataValue()
        {
            Variant variant = GetRandomVariant();
            StatusCode statusCode = GetRandomStatusCode();
            DateTime sourceTimeStamp = GetRandomDateTime();

            return new DataValue(variant, statusCode, sourceTimeStamp, DateTime.UtcNow);
        }

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
                new StringTable(),
                m_logger);
        }

        /// <inheritdoc/>
        public object GetRandomNumber()
        {
            switch (m_random.NextInt32(5))
            {
                case 0:
                case 1:
                    return GetRandomInteger();
                case 2:
                case 3:
                    return GetRandomUInteger();
                case 4:
                    return GetRandomFloat();
                //case 6: return GetRandomDecimal();
                default:
                    return GetRandomDouble();
            }
        }

        /// <inheritdoc/>
        public object GetRandomInteger()
        {
            switch (m_random.NextInt32(3))
            {
                case 0:
                    return GetRandomSByte();
                case 1:
                    return GetRandomInt16();
                case 2:
                    return GetRandomInt32();
                default:
                    return GetRandomInt64();
            }
        }

        /// <inheritdoc/>
        public object GetRandomUInteger()
        {
            switch (m_random.NextInt32(3))
            {
                case 0:
                    return GetRandomByte();
                case 1:
                    return GetRandomUInt16();
                case 2:
                    return GetRandomUInt32();
                default:
                    return GetRandomUInt64();
            }
        }

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
                    Values = [.. values];
                }
                else
                {
                    Values = [];
                }
            }

            public Type SystemType;
            public List<object> Values;
        }

        /// <summary>
        /// Boundary values used or testing.
        /// </summary>
        private static readonly BoundaryValues[] s_availableBoundaryValues =
        [
            new(typeof(sbyte), sbyte.MinValue, (sbyte)0, sbyte.MaxValue),
            new(typeof(byte), byte.MinValue, byte.MaxValue),
            new(typeof(short), short.MinValue, (short)0, short.MaxValue),
            new(typeof(ushort), ushort.MinValue, ushort.MaxValue),
            new(typeof(int), int.MinValue, 0, int.MaxValue),
            new(typeof(uint), uint.MinValue, uint.MaxValue),
            new(typeof(long), long.MinValue, (long)0, long.MaxValue),
            new(typeof(ulong), ulong.MinValue, ulong.MaxValue),
            new(
                typeof(float),
                float.Epsilon,
                float.MaxValue,
                float.MinValue,
                float.NaN,
                float.NegativeInfinity,
                float.PositiveInfinity,
                (float)0
            ),
            new(
                typeof(double),
                double.Epsilon,
                double.MaxValue,
                double.MinValue,
                double.NaN,
                double.NegativeInfinity,
                double.PositiveInfinity,
                (double)0
            ),
            new(typeof(string), null, string.Empty),
            new(
                typeof(DateTime),
                DateTime.MinValue,
                DateTime.MaxValue,
                new DateTime(1099, 1, 1),
                Utils.TimeBase,
                new DateTime(2039, 4, 4),
                new DateTime(2001, 9, 11, 9, 15, 0, DateTimeKind.Local)
            ),
            new(typeof(Guid), Guid.Empty),
            new(typeof(Uuid), Uuid.Empty),
            new(typeof(byte[]), null, Array.Empty<byte>()),
            new(typeof(XmlElement), null),
            new(
                typeof(NodeId),
                null,
                NodeId.Null,
                new NodeId(Guid.Empty),
                new NodeId(string.Empty),
                new NodeId([])),
            new(
                typeof(ExpandedNodeId),
                null,
                ExpandedNodeId.Null,
                new ExpandedNodeId(Guid.Empty),
                new ExpandedNodeId(string.Empty),
                new ExpandedNodeId([])
            ),
            new(typeof(QualifiedName), null, QualifiedName.Null),
            new(typeof(LocalizedText), null, LocalizedText.Null),
            new(typeof(StatusCode), StatusCodes.Good, StatusCodes.Uncertain, StatusCodes.Bad),
            new(typeof(ExtensionObject), ExtensionObject.Null)
        ];

        /// <summary>
        /// Loads some string data from a resource.
        /// </summary>
        private static SortedDictionary<string, string[]> LoadStringData(string resourceName)
        {
            var dictionary = new SortedDictionary<string, string[]>();

            try
            {
                string locale = null;
                List<string> tokens = null;

                Stream istrm = typeof(DataGenerator).GetTypeInfo().Assembly
                    .GetManifestResourceStream(resourceName);
                if (istrm == null)
                {
                    // try to load from app directory
                    var file = new FileInfo(resourceName);
                    istrm = file.OpenRead();
                }

                using (var reader = new StreamReader(istrm))
                {
                    for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                    {
                        string token = line.Trim();

                        if (string.IsNullOrEmpty(token))
                        {
                            continue;
                        }

                        if (token.StartsWith('='))
                        {
                            if (locale != null)
                            {
                                dictionary.Add(locale, [.. tokens]);
                            }

                            locale = token[1..];
                            tokens = [];
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

            if (!m_boundaryValues.TryGetValue(type.Name, out object[] boundaryValues))
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
            BuiltInType builtInType = TypeInfo.Construct(expectedType).BuiltInType;
            object value = GetRandom(builtInType);

            if (builtInType == BuiltInType.Guid && expectedType == typeof(Guid))
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
            if (!m_tokenValues.TryGetValue(locale, out string[] tokens))
            {
                tokens = m_tokenValues["en-US"];
            }

            int length;
            if (isSymbol)
            {
                length = m_random.NextInt32(2) + 1;
            }
            else
            {
                length = m_random.NextInt32(MaxStringLength) + 1;
            }

            var buffer = new StringBuilder();

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

        private readonly ILogger m_logger;
        private readonly IRandomSource m_random;
        private readonly SortedDictionary<string, object[]> m_boundaryValues;
        private readonly string[] m_availableLocales;
        private readonly SortedDictionary<string, string[]> m_tokenValues;
        private const string kPunctuation = "`~!@#$%^&*()_-+={}[]:\"';?><,./";
    }
}
