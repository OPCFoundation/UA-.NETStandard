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
using System.Collections.Frozen;
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
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the offset or count parameters
        /// do not specify a valid range within the bytes parameter.</exception>
        void NextBytes(byte[] bytes, int offset, int count);

        /// <summary>
        /// Returns a random non-negative integer which does not exceed the specified maximum.
        /// </summary>
        /// <param name="max">The maximum value to return.</param>
        /// <returns>A random value greater than 0 but less than or equal to max.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the max parameter is less
        /// than zero.</exception>
        int NextInt32(int max);
    }

    /// <summary>
    /// Uses the Pseudo random generator as a source.
    /// </summary>
    public class RandomSource : IRandomSource
    {
        /// <summary>
        /// Default random source.
        /// </summary>
        public static RandomSource Default { get; } = new();

        /// <summary>
        /// Initializes the source with a time dependent seed.
        /// </summary>
        public RandomSource()
        {
            m_random = UnsecureRandom.Shared;
        }

        /// <summary>
        /// Initializes the source with a seed.
        /// </summary>
        /// <param name="seed">The number used to initialize the Pseudo random sequence.</param>
        public RandomSource(int seed)
        {
            m_random = new UnsecureRandom(seed);
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

        private readonly UnsecureRandom m_random;
    }

    /// <summary>
    /// A class that generates data.
    /// </summary>
    public class DataGenerator
    {
        /// <summary>
        /// Initializes the data generator.
        /// </summary>
        public DataGenerator(IRandomSource? random, ITelemetryContext telemetry)
        {
            MaxArrayLength = 100;
            MaxStringLength = 100;
            MaxXmlAttributeCount = 10;
            MaxXmlElementCount = 10;
            MinDateTimeValue = new DateTimeUtc(1900, 1, 1, 0, 0, 0);
            MaxDateTimeValue = new DateTimeUtc(2100, 1, 1, 0, 0, 0);
            m_random = random ?? new RandomSource();
            m_logger = telemetry.CreateLogger<DataGenerator>();
            BoundaryValueFrequency = 20;
            NamespaceUris = new NamespaceTable();
            ServerUris = new StringTable();

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
        public DateTimeUtc MinDateTimeValue { get; set; }

        /// <summary>
        /// The maximum value for generated date time values.
        /// </summary>
        public DateTimeUtc MaxDateTimeValue { get; set; }

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
        /// Returns a random value of the specified built-in type.
        /// </summary>
        public Variant GetRandom(
            NodeId dataType,
            int valueRank,
            uint[]? arrayDimensions,
            ITypeTable? typeTree)
        {
            BuiltInType expectedType = TypeInfo.GetBuiltInType(dataType, typeTree);
            // calculate total number of dimensions.
            int dimensions;
            switch (valueRank)
            {
                case ValueRanks.Any:
                    if (arrayDimensions != null && arrayDimensions.Length > 0)
                    {
                        dimensions = arrayDimensions.Length;
                        break;
                    }

                    dimensions = GetRandomRange(0, 1);
                    break;
                case ValueRanks.ScalarOrOneDimension:
                    dimensions = GetRandomRange(0, 1);
                    break;
                case ValueRanks.OneOrMoreDimensions:
                    if (arrayDimensions != null && arrayDimensions.Length > 0)
                    {
                        dimensions = arrayDimensions.Length;
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
                    return GetRandomVariant(GetRandomBuiltInType(), false);
                }
                return GetRandomScalar(expectedType);
            }

            // calculate the length of each dimension and total length.
            int[] actualDimensions = new int[dimensions];
            int length = 1;
            for (int ii = 0; ii < dimensions; ii++)
            {
                if (arrayDimensions != null && arrayDimensions.Length > ii)
                {
                    actualDimensions[ii] = (int)arrayDimensions[ii];
                }

                while (actualDimensions[ii] == 0)
                {
                    actualDimensions[ii] = m_random.NextInt32(MaxArrayLength);
                }
                length *= actualDimensions[ii];
            }
            // Get random array or matrix.
            if (dimensions == 1)
            {
                return GetRandomArray(expectedType, length);
            }
            return GetRandomMatrix(expectedType, length, actualDimensions);
        }

        /// <summary>
        /// Returns a random value of the specified built-in type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomScalar(
            BuiltInType builtInType,
            bool useBoundaryValues = false)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return GetRandomBoolean(useBoundaryValues);
                case BuiltInType.SByte:
                    return GetRandomSByte(useBoundaryValues);
                case BuiltInType.Byte:
                    return GetRandomByte(useBoundaryValues);
                case BuiltInType.Int16:
                    return GetRandomInt16(useBoundaryValues);
                case BuiltInType.UInt16:
                    return GetRandomUInt16(useBoundaryValues);
                case BuiltInType.Int32:
                    return GetRandomInt32(useBoundaryValues);
                case BuiltInType.UInt32:
                    return GetRandomUInt32(useBoundaryValues);
                case BuiltInType.Int64:
                    return GetRandomInt64(useBoundaryValues);
                case BuiltInType.UInt64:
                    return GetRandomUInt64(useBoundaryValues);
                case BuiltInType.Float:
                    return GetRandomFloat(useBoundaryValues);
                case BuiltInType.Double:
                    return GetRandomDouble(useBoundaryValues);
                case BuiltInType.String:
                    return GetRandomString(useBoundaryValues);
                case BuiltInType.DateTime:
                    return GetRandomDateTime(useBoundaryValues);
                case BuiltInType.Guid:
                    return GetRandomGuid(useBoundaryValues);
                case BuiltInType.ByteString:
                    return GetRandomByteString(useBoundaryValues);
                case BuiltInType.XmlElement:
                    return GetRandomXmlElement(useBoundaryValues);
                case BuiltInType.NodeId:
                    return GetRandomNodeId(useBoundaryValues);
                case BuiltInType.ExpandedNodeId:
                    return GetRandomExpandedNodeId(useBoundaryValues);
                case BuiltInType.QualifiedName:
                    return GetRandomQualifiedName(useBoundaryValues);
                case BuiltInType.LocalizedText:
                    return GetRandomLocalizedText(useBoundaryValues);
                case BuiltInType.StatusCode:
                    return GetRandomStatusCode(useBoundaryValues);
                case BuiltInType.Variant:
                    return GetRandomVariant(useBoundaryValues);
                case BuiltInType.Enumeration:
                    return GetRandomInt32(useBoundaryValues);
                case BuiltInType.ExtensionObject:
                    return GetRandomExtensionObject(useBoundaryValues);
                case BuiltInType.DataValue:
                    return GetRandomDataValue(useBoundaryValues);
                case BuiltInType.Number:
                    builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false, useBoundaryValues);
                case BuiltInType.Integer:
                    builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.SByte);
                    return GetRandomVariant(builtInType, false, useBoundaryValues);
                case BuiltInType.UInteger:
                    builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.Byte);
                    return GetRandomVariant(builtInType, false, useBoundaryValues);
                case BuiltInType.DiagnosticInfo:
                case BuiltInType.Null:
                    return Variant.Null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {builtInType}");
            }
        }

        /// <summary>
        /// Get random array value wrapped in a variant.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomArray(
            BuiltInType builtInType,
            int length,
            bool useBoundaryValues = true,
            bool fixedLength = true)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return Variant.From(GetRandomBooleanArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.SByte:
                    return Variant.From(GetRandomSByteArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Byte:
                    return Variant.From(GetRandomByteArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Int16:
                    return Variant.From(GetRandomInt16Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.UInt16:
                    return Variant.From(GetRandomUInt16Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.Int32:
                    return Variant.From(GetRandomInt32Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.UInt32:
                    return Variant.From(GetRandomUInt32Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.Int64:
                    return Variant.From(GetRandomInt64Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.UInt64:
                    return Variant.From(GetRandomUInt64Array(useBoundaryValues, length, fixedLength));
                case BuiltInType.Float:
                    return Variant.From(GetRandomFloatArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Double:
                    return Variant.From(GetRandomDoubleArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.String:
                    return Variant.From(GetRandomStringArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.DateTime:
                    return Variant.From(GetRandomDateTimeArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Guid:
                    return Variant.From(GetRandomGuidArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.ByteString:
                    return Variant.From(GetRandomByteStringArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.XmlElement:
                    return Variant.From(GetRandomXmlElementArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.NodeId:
                    return Variant.From(GetRandomNodeIdArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.ExpandedNodeId:
                    return Variant.From(GetRandomExpandedNodeIdArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.QualifiedName:
                    return Variant.From(GetRandomQualifiedNameArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.LocalizedText:
                    return Variant.From(GetRandomLocalizedTextArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.StatusCode:
                    return Variant.From(GetRandomStatusCodeArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Variant:
                    return Variant.From(GetRandomVariantArray(useBoundaryValues, length, fixedLength));
                case BuiltInType.Enumeration:
                    return Variant.From(EnumValue.From(GetRandomInt32Array(useBoundaryValues, length, fixedLength)));
                case BuiltInType.Null:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    return Variant.Null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {builtInType}");
            }
        }

        /// <summary>
        /// Get random matrix value wrapped in a variant.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomMatrix(
            BuiltInType builtInType,
            int length,
            int[] dimensions,
            bool useBoundaryValues = true,
            bool fixedLength = true)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return Variant.From(GetRandomBooleanArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.SByte:
                    return Variant.From(GetRandomSByteArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Byte:
                    return Variant.From(GetRandomByteArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Int16:
                    return Variant.From(GetRandomInt16Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.UInt16:
                    return Variant.From(GetRandomUInt16Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Int32:
                    return Variant.From(GetRandomInt32Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.UInt32:
                    return Variant.From(GetRandomUInt32Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Int64:
                    return Variant.From(GetRandomInt64Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.UInt64:
                    return Variant.From(GetRandomUInt64Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Float:
                    return Variant.From(GetRandomFloatArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Double:
                    return Variant.From(GetRandomDoubleArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.String:
                    return Variant.From(GetRandomStringArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.DateTime:
                    return Variant.From(GetRandomDateTimeArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Guid:
                    return Variant.From(GetRandomGuidArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.ByteString:
                    return Variant.From(GetRandomByteStringArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.XmlElement:
                    return Variant.From(GetRandomXmlElementArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.NodeId:
                    return Variant.From(GetRandomNodeIdArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.ExpandedNodeId:
                    return Variant.From(GetRandomExpandedNodeIdArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.QualifiedName:
                    return Variant.From(GetRandomQualifiedNameArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.LocalizedText:
                    return Variant.From(GetRandomLocalizedTextArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.StatusCode:
                    return Variant.From(GetRandomStatusCodeArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Variant:
                    return Variant.From(GetRandomVariantArray(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions));
                case BuiltInType.Enumeration:
                    return Variant.From(EnumValue.From(GetRandomInt32Array(useBoundaryValues, length, fixedLength)
                        .ToMatrixOf(dimensions)));
                case BuiltInType.Null:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                case BuiltInType.DiagnosticInfo:
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    return Variant.Null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {builtInType}");
            }
        }

        /// <inheritdoc/>
        public bool GetRandomBoolean(bool useBoundaryValues = false)
        {
            return GetBoundaryValue(
                useBoundaryValues,
                m_random.NextInt32(1) != 0,
                []);
        }

        /// <inheritdoc/>
        public sbyte GetRandomSByte(bool useBoundaryValues = false)
        {
            int buffer = m_random.NextInt32(byte.MaxValue);

            if (buffer > sbyte.MaxValue)
            {
                return (sbyte)(sbyte.MinValue + (buffer - sbyte.MaxValue) - 1);
            }

            return GetBoundaryValue(
                useBoundaryValues,
                (sbyte)buffer,
                [sbyte.MinValue, (sbyte)0, sbyte.MaxValue]);
        }

        /// <inheritdoc/>
        public byte GetRandomByte(bool useBoundaryValues = false)
        {
            return GetBoundaryValue(
                useBoundaryValues,
                (byte)m_random.NextInt32(byte.MaxValue),
                [byte.MinValue, byte.MaxValue]);
        }

        /// <inheritdoc/>
        public short GetRandomInt16(bool useBoundaryValues = false)
        {
            int buffer = m_random.NextInt32(ushort.MaxValue);

            if (buffer > short.MaxValue)
            {
                return (short)(short.MinValue + (buffer - short.MaxValue) - 1);
            }

            return GetBoundaryValue(
                useBoundaryValues,
                (short)buffer,
                [short.MinValue, (short)0, short.MaxValue]);
        }

        /// <inheritdoc/>
        public ushort GetRandomUInt16(bool useBoundaryValues = false)
        {
            return GetBoundaryValue(
                useBoundaryValues,
                (ushort)m_random.NextInt32(ushort.MaxValue),
                [ushort.MinValue, ushort.MaxValue]);
        }

        /// <inheritdoc/>
        public int GetRandomInt32(bool useBoundaryValues = false)
        {
            return GetBoundaryValue(
                useBoundaryValues,
                m_random.NextInt32(int.MaxValue),
                [int.MinValue, 0, int.MaxValue]);
        }

        /// <inheritdoc/>
        public uint GetRandomUInt32(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(
                useBoundaryValues,
                BitConverter.ToUInt32(bytes, 0),
                [uint.MinValue, uint.MaxValue]);
        }

        /// <inheritdoc/>
        public long GetRandomInt64(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(
                useBoundaryValues,
                BitConverter.ToInt64(bytes, 0),
                [long.MinValue, 0L, long.MaxValue]);
        }

        /// <inheritdoc/>
        public ulong GetRandomUInt64(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(
                useBoundaryValues,
                BitConverter.ToUInt64(bytes, 0),
                [ulong.MinValue, ulong.MaxValue]);
        }

        /// <inheritdoc/>
        public float GetRandomFloat(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[4];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(
                useBoundaryValues,
                BitConverter.ToSingle(bytes, 0),
                [
                    float.Epsilon,
                    float.MaxValue,
                    float.MinValue,
                    float.NaN,
                    float.NegativeInfinity,
                    float.PositiveInfinity,
                    0f
                ]);
        }

        /// <inheritdoc/>
        public double GetRandomDouble(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[8];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(
                useBoundaryValues,
                BitConverter.ToSingle(bytes, 0),
                [
                    double.Epsilon,
                    double.MaxValue,
                    double.MinValue,
                    double.NaN,
                    double.NegativeInfinity,
                    double.PositiveInfinity,
                    0.0
                ]);
        }

        /// <summary>
        /// Creates a random string with a random locale.
        /// </summary>
        public string GetRandomString(bool useBoundaryValues = false)
        {
            return CreateString(GetRandomLocale(), false, useBoundaryValues);
        }

        /// <summary>
        /// Creates a random string for the locale.
        /// </summary>
        public string GetRandomString(string locale, bool useBoundaryValues = false)
        {
            return CreateString(locale, false, useBoundaryValues);
        }

        /// <summary>
        /// Creates a random symbol with a random locale.
        /// </summary>
        public string GetRandomSymbol(bool useBoundaryValues = false)
        {
            return CreateString(GetRandomLocale(), true, useBoundaryValues);
        }

        /// <summary>
        /// Creates a random symbol for the locale.
        /// </summary>
        public string GetRandomSymbol(string locale, bool useBoundaryValues = false)
        {
            return CreateString(locale, false, useBoundaryValues);
        }

        /// <inheritdoc/>
        public DateTimeUtc GetRandomDateTime(bool useBoundaryValues = false)
        {
            int minTicks = (int)(MinDateTimeValue.Value >> 32);
            int maxTicks = (int)(MaxDateTimeValue.Value >> 32);

            long delta = GetRandomRange(minTicks, maxTicks);

            long higherTicks = delta << 32;

            uint lowerTicks = GetRandomUInt32();

            return GetBoundaryValue(useBoundaryValues, new DateTimeUtc(higherTicks + lowerTicks),
            [
                DateTimeUtc.MinValue,
                DateTimeUtc.MaxValue,
                new DateTimeUtc(1099, 1, 1),
                new DateTimeUtc(2039, 4, 4),
                new DateTimeUtc(new DateTime(2001, 9, 11, 9, 15, 0, DateTimeKind.Local))
            ]);
        }

        /// <inheritdoc/>
        public Uuid GetRandomGuid(bool useBoundaryValues = false)
        {
            byte[] bytes = new byte[16];
            m_random.NextBytes(bytes, 0, bytes.Length);
            return GetBoundaryValue(useBoundaryValues, new Uuid(bytes), [Uuid.Empty]);
        }

        /// <inheritdoc/>
        public ByteString GetRandomByteString(bool useBoundaryValues = false)
        {
            int length = m_random.NextInt32(MaxStringLength);

            byte[] bytes = new byte[length];
            m_random.NextBytes(bytes, 0, bytes.Length);

            return GetBoundaryValue(
                useBoundaryValues,
                ByteString.From(bytes),
                [ByteString.Empty, default]);
        }

        /// <inheritdoc/>
        public XmlElement GetRandomXmlElement(bool useBoundaryValues = false)
        {
            string locale1 = GetRandomLocale();
            string locale2 = GetRandomLocale();

            // create the root element.
            var document = new XmlDocument();

            System.Xml.XmlElement element = document.CreateElement(
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

                System.Xml.XmlElement childElement = document.CreateElement(
                    element.Prefix,
                    elementName,
                    element.NamespaceURI);

                childElement.InnerText = CreateString(locale2, false);

                element.AppendChild(childElement);
            }

            return GetBoundaryValue(
                useBoundaryValues,
                XmlElement.From(element),
                [XmlElement.Empty, default]);
        }

        /// <inheritdoc/>
        public NodeId GetRandomNodeId(bool useBoundaryValues = false)
        {
            ushort ns = (ushort)m_random.NextInt32(NamespaceUris.Count - 1);

            NodeId nodeId;
            switch ((IdType)m_random.NextInt32(3))
            {
                case IdType.Numeric:
                    nodeId = new NodeId(GetRandomUInt32(), ns);
                    break;
                case IdType.String:
                    nodeId = new NodeId(CreateString(GetRandomLocale(), true), ns);
                    break;
                case IdType.Guid:
                    nodeId = new NodeId(GetRandomGuid(), ns);
                    break;
                case IdType.Opaque:
                    nodeId = new NodeId(GetRandomByteString(), ns);
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        "Unexpected IdType value");
            }
            return GetBoundaryValue(
                useBoundaryValues,
                nodeId,
                [
                    NodeId.Null,
                    new NodeId(Guid.Empty),
                    new NodeId(string.Empty, 0),
                    new NodeId(ByteString.Empty)
                ]);
        }

        /// <inheritdoc/>
        public ExpandedNodeId GetRandomExpandedNodeId(bool useBoundaryValues = false)
        {
            NodeId nodeId = GetRandomNodeId();
            ushort serverIndex = ServerUris.Count == 0
                ? (ushort)0
                : (ushort)m_random.NextInt32(ServerUris.Count - 1);
            return GetBoundaryValue(useBoundaryValues,
                new ExpandedNodeId(
                    nodeId,
                    nodeId.NamespaceIndex > 0 ? NamespaceUris.GetString(nodeId.NamespaceIndex) : null,
                    serverIndex),
                [
                    ExpandedNodeId.Null,
                    new ExpandedNodeId(Guid.Empty),
                    new ExpandedNodeId(string.Empty, 0),
                    new ExpandedNodeId(ByteString.Empty)
                ]);
        }

        /// <inheritdoc/>
        public QualifiedName GetRandomQualifiedName(bool useBoundaryValues = false)
        {
            ushort ns = (ushort)m_random.NextInt32(NamespaceUris.Count - 1);
            return GetBoundaryValue(
                useBoundaryValues,
                new QualifiedName(CreateString(GetRandomLocale(), true), ns),
                [QualifiedName.Null, default]);
        }

        /// <inheritdoc/>
        public LocalizedText GetRandomLocalizedText(bool useBoundaryValues = false)
        {
            string locale = GetRandomLocale();
            return GetBoundaryValue(
                useBoundaryValues,
                new LocalizedText(locale, CreateString(locale, false)),
                [LocalizedText.Null, default]);
        }

        /// <inheritdoc/>
        public StatusCode GetRandomStatusCode(bool useBoundaryValues = false)
        {
            ArrayOf<StatusCode> interned = StatusCode.InternedStatusCodes;
            int index = GetRandomRange(0, interned.Count - 1);
            return GetBoundaryValue(
                useBoundaryValues,
                interned[index],
                [StatusCodes.Good, StatusCodes.Uncertain, StatusCodes.Bad]);
        }

        /// <inheritdoc/>
        public Variant GetRandomVariant(bool useBoundaryValues = false)
        {
            return GetRandomVariant(true, useBoundaryValues);
        }

        /// <inheritdoc/>
        public Variant GetRandomVariant(
            bool allowArrays,
            bool useBoundaryValues = false)
        {
            return GetRandomVariant(
                GetRandomBuiltInType(),
                allowArrays && m_random.NextInt32(1) == 1,
                useBoundaryValues);
        }

        /// <summary>
        /// Returns a random variant containing a scalar or array value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomVariant(
            BuiltInType builtInType,
            bool isArray,
            bool useBoundaryValues = false)
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

            if (length < 0)
            {
                return GetRandomScalar(builtInType);
            }
            return GetRandomArray(builtInType, length, useBoundaryValues);
        }

        /// <inheritdoc/>
        public ExtensionObject GetRandomExtensionObject(bool useBoundaryValues = false)
        {
            NodeId typeId = GetRandomNodeId();
            ExtensionObject extensionObject;
            if (typeId.IsNull)
            {
                extensionObject = ExtensionObject.Null;
            }
            else if (m_random.NextInt32(1) != 0)
            {
                extensionObject = new ExtensionObject(typeId, GetRandomByteString());
            }
            else
            {
                extensionObject = new ExtensionObject(typeId, GetRandomXmlElement());
            }
            return GetBoundaryValue(useBoundaryValues, extensionObject, [ExtensionObject.Null, default]);
        }

        /// <summary>
        /// Get a random DataValue.
        /// </summary>
        public DataValue GetRandomDataValue(bool useBoundaryValues = false)
        {
            Variant variant = GetRandomVariant();
            StatusCode statusCode = GetRandomStatusCode();
            DateTimeUtc sourceTimeStamp = GetRandomDateTime();

            return GetBoundaryValue(useBoundaryValues,
                new DataValue(variant, statusCode, sourceTimeStamp, DateTimeUtc.Now),
                [new DataValue(), default!])!;
        }

        /// <summary>
        /// Get random diagnostic info.
        /// </summary>
        public DiagnosticInfo GetRandomDiagnosticInfo(bool useBoundaryValues = false)
        {
            // TODO: return random values
            return GetBoundaryValue(useBoundaryValues, new DiagnosticInfo(
                ServiceResult.Good,
                DiagnosticsMasks.NoInnerStatus,
                true,
                new StringTable(),
                m_logger), [new DiagnosticInfo(), default!])!;
        }

        /// <summary>
        /// Get random number variant
        /// </summary>
        public Variant GetRandomNumber(bool useBoundaryValues = false)
        {
            switch (m_random.NextInt32(5))
            {
                case 0:
                case 1:
                    return GetRandomInteger(useBoundaryValues);
                case 2:
                case 3:
                    return GetRandomUInteger(useBoundaryValues);
                case 4:
                    return GetRandomFloat(useBoundaryValues);
                //case 6: return GetRandomDecimal();
                default:
                    return GetRandomDouble(useBoundaryValues);
            }
        }

        /// <summary>
        /// Get random integer variant
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomInteger(bool useBoundaryValues = false)
        {
            switch (m_random.NextInt32(3))
            {
                case 0:
                    return GetRandomSByte(useBoundaryValues);
                case 1:
                    return GetRandomInt16(useBoundaryValues);
                case 2:
                    return GetRandomInt32(useBoundaryValues);
                case 3:
                    return GetRandomInt64(useBoundaryValues);
                default:
                    throw ServiceResultException.Unexpected(
                        "Unexpected random value");
            }
        }

        /// <summary>
        /// Get random unsigned integer variant
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant GetRandomUInteger(bool useBoundaryValues = false)
        {
            switch (m_random.NextInt32(3))
            {
                case 0:
                    return GetRandomByte(useBoundaryValues);
                case 1:
                    return GetRandomUInt16(useBoundaryValues);
                case 2:
                    return GetRandomUInt32(useBoundaryValues);
                case 3:
                    return GetRandomUInt64(useBoundaryValues);
                default:
                    throw ServiceResultException.Unexpected(
                        "Unexpected random value");
            }
        }

        /// <inheritdoc/>
        public bool[] GetRandomBooleanArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            bool[] values = new bool[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomBoolean(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public sbyte[] GetRandomSByteArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            sbyte[] values = new sbyte[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomSByte(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public byte[] GetRandomByteArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            byte[] values = new byte[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomByte(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public short[] GetRandomInt16Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            short[] values = new short[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomInt16(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public ushort[] GetRandomUInt16Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            ushort[] values = new ushort[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomUInt16(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public int[] GetRandomInt32Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            int[] values = new int[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomInt32(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public uint[] GetRandomUInt32Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            uint[] values = new uint[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomUInt32(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public long[] GetRandomInt64Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            long[] values = new long[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomInt64(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public ulong[] GetRandomUInt64Array(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            ulong[] values = new ulong[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomUInt64(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public float[] GetRandomFloatArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            float[] values = new float[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomFloat(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public double[] GetRandomDoubleArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            double[] values = new double[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomDouble(useBoundaryValues);
            }
            return values;
        }

        /// <summary>
        /// Creates a random string with a random locale.
        /// </summary>
        public string[] GetRandomStringArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            string[] values = new string[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomString(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public DateTimeUtc[] GetRandomDateTimeArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new DateTimeUtc[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomDateTime(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public Uuid[] GetRandomGuidArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new Uuid[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomGuid(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public ByteString[] GetRandomByteStringArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new ByteString[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomByteString(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public XmlElement[] GetRandomXmlElementArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new XmlElement[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomXmlElement(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public NodeId[] GetRandomNodeIdArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new NodeId[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomNodeId(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public ExpandedNodeId[] GetRandomExpandedNodeIdArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new ExpandedNodeId[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomExpandedNodeId(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public QualifiedName[] GetRandomQualifiedNameArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new QualifiedName[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomQualifiedName(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public LocalizedText[] GetRandomLocalizedTextArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new LocalizedText[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomLocalizedText(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public StatusCode[] GetRandomStatusCodeArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new StatusCode[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomStatusCode(useBoundaryValues);
            }
            return values;
        }

        /// <inheritdoc/>
        public ExtensionObject[] GetRandomExtensionObjectArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new ExtensionObject[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomExtensionObject(useBoundaryValues);
            }
            return values;
        }

        /// <summary>
        /// Get a random DataValue.
        /// </summary>
        public DataValue[] GetRandomDataValueArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new DataValue[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomDataValue(useBoundaryValues);
            }
            return values;
        }

        /// <summary>
        /// Get random diagnostic info.
        /// </summary>
        public DiagnosticInfo[] GetRandomDiagnosticInfoArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            if (length < 0)
            {
                return [];
            }
            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }
            var values = new DiagnosticInfo[length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = GetRandomDiagnosticInfo(useBoundaryValues);
            }
            return values;
        }

        /// <summary>
        /// Get random unsigned integer variant array
        /// </summary>
        public Variant[] GetRandomUIntegerArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.Byte);
            return GetRandomArrayInVariant(
                builtInType,
                useBoundaryValues,
                length,
                fixedLength);
        }

        /// <summary>
        /// Get random integer variant array
        /// </summary>
        public Variant[] GetRandomIntegerArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            var builtInType = (BuiltInType)((m_random.NextInt32(3) * 2) + (int)BuiltInType.SByte);
            return GetRandomArrayInVariant(
                builtInType,
                useBoundaryValues,
                length,
                fixedLength);
        }

        /// <summary>
        /// Get random number variant array
        /// </summary>
        public Variant[] GetRandomNumberArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            var builtInType = (BuiltInType)(m_random.NextInt32(9) + (int)BuiltInType.SByte);
            return GetRandomArrayInVariant(
                builtInType,
                useBoundaryValues,
                length,
                fixedLength);
        }

        /// <summary>
        /// Get random variant array
        /// </summary>
        public Variant[] GetRandomVariantArray(bool useBoundaryValues, int length, bool fixedLength)
        {
            var builtInType = (BuiltInType)(m_random.NextInt32(22) + (int)BuiltInType.Boolean);
            return GetRandomArrayInVariant(
                builtInType,
                useBoundaryValues,
                length,
                fixedLength);
        }

        /// <summary>
        /// Helper to get a boundary value from the provided list if the flag is passed.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private T GetBoundaryValue<T>(bool useBoundaryValues, T value, T[]? boundaryValues)
        {
            if (useBoundaryValues)
            {
                // Determine if a boundary value should be used or default returned.
                useBoundaryValues = m_random.NextInt32(99) < BoundaryValueFrequency;
            }

            if (!useBoundaryValues)
            {
                return value;
            }

            if (boundaryValues == null || boundaryValues.Length == 0)
            {
                return value;
            }

            return boundaryValues[m_random.NextInt32(boundaryValues.Length - 1)];
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
            if (length < 0)
            {
                return [];
            }

            if (!fixedLength)
            {
                length = m_random.NextInt32(length);
            }

            var variants = new Variant[length];
            var typeInfo = TypeInfo.CreateScalar(builtInType);

            for (int ii = 0; ii < variants.Length; ii++)
            {
                variants[ii] = GetRandomVariant(false);
            }

            return variants;
        }

        /// <summary>
        /// Loads some string data from a resource.
        /// </summary>
        private static FrozenDictionary<string, string[]> LoadStringData(string resourceName)
        {
            var dictionary = new Dictionary<string, string[]>();

            try
            {
                string? locale = null;
                List<string>? tokens = null;

                Stream? istrm = typeof(DataGenerator).GetTypeInfo().Assembly
                    .GetManifestResourceStream(resourceName);
                if (istrm == null)
                {
                    // try to load from app directory
                    var file = new FileInfo(resourceName);
                    istrm = file.OpenRead();
                }

                using (var reader = new StreamReader(istrm))
                {
                    for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
                    {
                        string token = line.Trim();

                        if (string.IsNullOrEmpty(token))
                        {
                            continue;
                        }

                        if (token.StartsWith('='))
                        {
                            if (locale != null && tokens != null)
                            {
                                dictionary.Add(locale, [.. tokens]);
                            }

                            locale = token[1..];
                            tokens = [];
                            continue;
                        }

                        tokens?.Add(token);
                    }
                }

                return dictionary.ToFrozenDictionary();
            }
            catch (Exception)
            {
                return dictionary.ToFrozenDictionary();
            }
        }

        /// <summary>
        /// Returns a random valid built in type
        /// </summary>
        private BuiltInType GetRandomBuiltInType()
        {
            // randomly choose a built-in type.
            BuiltInType builtInType = BuiltInType.Variant;
            while (builtInType is
                BuiltInType.Variant or
                BuiltInType.DataValue or
                BuiltInType.Null or
                BuiltInType.XmlElement or
                BuiltInType.DiagnosticInfo) // Invalid
            {
                builtInType = (BuiltInType)m_random.NextInt32((int)BuiltInType.Variant);
            }
            return builtInType;
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
        private string CreateString(string locale, bool isSymbol, bool useBoundaryValues = false)
        {
            if (!m_tokenValues.TryGetValue(locale, out string[]? tokens))
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

            return GetBoundaryValue(useBoundaryValues, buffer.ToString(), [string.Empty, default!]);
        }

        private readonly ILogger m_logger;
        private readonly IRandomSource m_random;
        private readonly string[] m_availableLocales;
        private readonly FrozenDictionary<string, string[]> m_tokenValues;
        private const string kPunctuation = "`~!@#$%^&*()_-+={}[]:\"';?><,./";
    }
}
