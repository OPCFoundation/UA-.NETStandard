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
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Wraps a multi-dimensional array for use within a Variant.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Matrix : ICloneable, IFormattable
    {
        /// <summary>
        /// Initializes the matrix with a multidimensional array.
        /// </summary>
        public Matrix(Array value, BuiltInType builtInType)
        {
            Elements = value ?? throw new ArgumentNullException(nameof(value));
            Dimensions = new int[value.Rank];

            for (int ii = 0; ii < Dimensions.Length; ii++)
            {
                Dimensions[ii] = value.GetLength(ii);
            }

            Elements = Utils.FlattenArray(value);
            TypeInfo = new TypeInfo(builtInType, Dimensions.Length);
        }

        /// <summary>
        /// Initializes the matrix with a one dimensional array and a list of dimensions.
        /// </summary>
        public Matrix(Array elements, BuiltInType builtInType, params int[] dimensions)
        {
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
            Dimensions = dimensions;

            if (dimensions != null && dimensions.Length > 0)
            {
                (_, int length) = ValidateDimensions(dimensions);

                if (length != elements.Length)
                {
                    throw new ArgumentException(
                        "The number of elements in the array does not match the dimensions.");
                }
            }
            else
            {
                Dimensions = [elements.Length];
            }

            TypeInfo = new TypeInfo(builtInType, Dimensions.Length);

            SanityCheckArrayElements(Elements, builtInType);
        }

        /// <summary>
        /// The elements of the matrix.
        /// </summary>
        /// <value>An array of elements.</value>
        public Array Elements { get; }

        /// <summary>
        /// The dimensions of the matrix.
        /// </summary>
        /// <value>The dimensions of the array.</value>
        public int[] Dimensions { get; }

        /// <summary>
        /// The type information for the matrix.
        /// </summary>
        /// <value>The type information.</value>
        public TypeInfo TypeInfo { get; }

        /// <summary>
        /// Returns the flattened array as a multi-dimensional array.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Array ToArray()
        {
            try
            {
                var array = Array.CreateInstance(Elements.GetType().GetElementType(), Dimensions);

                int[] indexes = new int[Dimensions.Length];

                for (int ii = 0; ii < Elements.Length; ii++)
                {
                    array.SetValue(Elements.GetValue(ii), indexes);

                    for (int jj = indexes.Length - 1; jj >= 0; jj--)
                    {
                        indexes[jj]++;

                        if (indexes[jj] < Dimensions[jj])
                        {
                            break;
                        }

                        indexes[jj] = 0;
                    }
                }
                return array;
            }
            catch (OutOfMemoryException oom)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    oom.Message);
            }
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is Matrix matrix)
            {
                if (!TypeInfo.Equals(matrix.TypeInfo))
                {
                    return false;
                }
                if (!Utils.IsEqual(Dimensions, matrix.Dimensions))
                {
                    return false;
                }
                return Utils.IsEqual(Elements, matrix.Elements);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            if (Elements != null)
            {
                hash.Add(Elements);
            }
            if (TypeInfo != null)
            {
                hash.Add(TypeInfo);
            }
            if (Dimensions != null)
            {
                hash.Add(Dimensions);
            }
            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused) Always pass a NULL value</param>
        /// <param name="formatProvider">The format-provider to use. If unsure, pass an empty string or null</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <exception cref="FormatException">Thrown when the 'format' argument is NOT null.</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var buffer = new StringBuilder();

                buffer.AppendFormat(
                    formatProvider,
                    "{0}[",
                    Elements.GetType().GetElementType().Name);

                for (int ii = 0; ii < Dimensions.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(',');
                    }

                    buffer.AppendFormat(formatProvider, "{0}", Dimensions[ii]);
                }

                buffer.AppendFormat(formatProvider, "]");

                return buffer.ToString();
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
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            return new Matrix(Utils.Clone(Elements), TypeInfo.BuiltInType, Utils.Clone(Dimensions));
        }

        /// <summary>
        /// Debug.Assert if the elements are assigned a valid BuiltInType.
        /// </summary>
        /// <param name="elements">An array of elements to sanity check.</param>
        /// <param name="builtInType">The builtInType used for the elements.</param>
        [Conditional("DEBUG")]
        private static void SanityCheckArrayElements(Array elements, BuiltInType builtInType)
        {
#if DEBUG
            var sanityCheck = TypeInfo.Construct(elements);
            Debug.Assert(
                sanityCheck.BuiltInType == builtInType ||
                builtInType == BuiltInType.Enumeration ||
                (sanityCheck.BuiltInType == BuiltInType.ExtensionObject &&
                    builtInType == BuiltInType.Null) ||
                (sanityCheck.BuiltInType == BuiltInType.Int32 &&
                    builtInType == BuiltInType.Enumeration) ||
                (sanityCheck.BuiltInType == BuiltInType.ByteString &&
                    builtInType == BuiltInType.Byte) ||
                (builtInType == BuiltInType.Variant));
#endif
        }

        /// <summary>
        /// A function that performs a validation on a given index into the dimensions array
        /// </summary>
        /// <param name="idx">The index into the dimensions array</param>
        /// <param name="dimensions">The dimensions collection describing a matrix</param>
        /// <returns>The validation result</returns>
        public delegate bool ValidateDimensionsFunction(int idx, Int32Collection dimensions);

        /// <summary>
        /// Validate the dimensions of a given matrix.
        /// As a side effect will bring to 0 negative dimensions.
        /// Throws ArgumentException if dimensions overflow and ServiceResultException if maxArrayLength is exceeded
        /// </summary>
        /// <param name="allowZeroDimension">Allow zero value dimensions </param>
        /// <param name="dimensions">Dimensions to be validated</param>
        /// <param name="maxArrayLength">The limit representing the maximum array length</param>
        /// <param name="logger"></param>
        /// <returns>Tuple with validation result and the calculated length of the flattended matrix</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static (bool valid, int flatLength) ValidateDimensions(
            bool allowZeroDimension,
            Int32Collection dimensions,
            int maxArrayLength,
            ILogger logger)
        {
            bool ValidateWithSideEffect(int i, Int32Collection dimCollection)
            {
                bool zeroCompFails = allowZeroDimension
                    ? dimCollection[i] < 0
                    : dimCollection[i] <= 0;

                if (zeroCompFails)
                {
                    /* The number of values is 0 if one or more dimension is less than or equal to 0.*/
                    logger.LogTrace(
                        "ReadArray read dimensions[{Index}] = {Dimensions}. Matrix will have 0 elements.",
                        i,
                        dimCollection);
                    dimCollection[i] = 0;
                    return false;
                }
                else if ((maxArrayLength > 0) && (dimCollection[i] > maxArrayLength))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "ArrayDimensions [{0}] = {1} is greater than MaxArrayLength {2}.",
                        i,
                        dimCollection[i],
                        maxArrayLength);
                }
                return true;
            }

            return ValidateDimensions(dimensions, maxArrayLength, ValidateWithSideEffect);
        }

        /// <summary>
        /// Validate the dimensions of a matrix with a provided expected flattened length.
        /// Throws ArgumentException if dimensions overflow and ServiceResultException if maxArrayLength is exceeded
        /// </summary>
        /// <param name="dimensions">Dimensions to be validated</param>
        /// <param name="flatLength">The flatLength against the validation occurs</param>
        /// <param name="maxArrayLength">The limit representing the maximum array length</param>
        /// <returns>Tuple with validation result and the calculated length of the flattended matrix</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static (bool valid, int flatLength) ValidateDimensions(
            Int32Collection dimensions,
            int flatLength,
            int maxArrayLength)
        {
            bool ValidateAgainstExpectedFlatLength(int i, Int32Collection dimCollection)
            {
                if (dimCollection[i] == 0 && flatLength > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "ArrayDimensions [{0}] is zero in Variant object.",
                        i);
                }
                else if (dimCollection[i] > flatLength && flatLength > 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "ArrayDimensions [{0}] = {1} is greater than length {2}.",
                        i,
                        dimCollection[i],
                        flatLength);
                }
                return true;
            }

            return ValidateDimensions(
                dimensions,
                maxArrayLength,
                ValidateAgainstExpectedFlatLength);
        }

        /// <summary>
        /// Validate that dimensions do not overflow
        /// </summary>
        /// <param name="dimensions">Collection of dimensions</param>
        /// <returns>Tuple with validation result and the calculated length of the flattended matrix</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static (bool valid, int flatLength) ValidateDimensions(Int32Collection dimensions)
        {
            return ValidateDimensions(dimensions, maxArrayLength: 0, customValidation: null);
        }

        /// <summary>
        /// Validate the dimensions of a matrix against a given validation function.
        /// Throws ArgumentException if dimensions overflow and ServiceResultException if maxArrayLength is exceeded
        /// </summary>
        /// <param name="dimensions">Dimensions to be validated</param>
        /// <param name="maxArrayLength">The limit representing the maximum array length</param>
        /// <param name="customValidation">A custom validation method</param>
        /// <returns>The calculated length of the flattended matrix</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        private static (bool valid, int flatLength) ValidateDimensions(
            Int32Collection dimensions,
            int maxArrayLength,
            ValidateDimensionsFunction customValidation)
        {
            (bool valid, int flatLength) = (false, 1);
            try
            {
                for (int ii = 0; ii < dimensions.Count; ii++)
                {
                    if (customValidation != null)
                    {
                        valid = customValidation(ii, dimensions);
                        if (!valid)
                        {
                            return (valid, 0);
                        }
                    }
                    checked
                    {
                        flatLength *= dimensions[ii];
                    }
                }
            }
            catch (OverflowException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The dimensions of the matrix are invalid and overflow when used to calculate the size.");
            }
            if ((maxArrayLength > 0) && (flatLength > maxArrayLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum array length of {0} was exceeded while summing up to {1} from the array dimensions",
                    maxArrayLength,
                    flatLength);
            }

            return (valid, flatLength);
        }
    }
}
