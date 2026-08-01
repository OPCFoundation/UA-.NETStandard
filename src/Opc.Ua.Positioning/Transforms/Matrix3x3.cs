/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Minimal immutable row-major 3x3 matrix of doubles used internally by the
    /// positioning transforms. Column vectors are transformed as <c>M * v</c>.
    /// </summary>
    internal readonly struct Matrix3x3 : IEquatable<Matrix3x3>
    {
        /// <summary>
        /// Row 0, column 0.
        /// </summary>
        public double M00 { get; }

        /// <summary>
        /// Row 0, column 1.
        /// </summary>
        public double M01 { get; }

        /// <summary>
        /// Row 0, column 2.
        /// </summary>
        public double M02 { get; }

        /// <summary>
        /// Row 1, column 0.
        /// </summary>
        public double M10 { get; }

        /// <summary>
        /// Row 1, column 1.
        /// </summary>
        public double M11 { get; }

        /// <summary>
        /// Row 1, column 2.
        /// </summary>
        public double M12 { get; }

        /// <summary>
        /// Row 2, column 0.
        /// </summary>
        public double M20 { get; }

        /// <summary>
        /// Row 2, column 1.
        /// </summary>
        public double M21 { get; }

        /// <summary>
        /// Row 2, column 2.
        /// </summary>
        public double M22 { get; }

        /// <summary>
        /// The identity matrix.
        /// </summary>
        public static Matrix3x3 Identity => new(
            1.0, 0.0, 0.0,
            0.0, 1.0, 0.0,
            0.0, 0.0, 1.0);

        /// <summary>
        /// Creates a new matrix from its elements in row-major order.
        /// </summary>
        public Matrix3x3(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        /// <summary>
        /// Returns the determinant of the matrix.
        /// </summary>
        public double Determinant
            => (M00 * ((M11 * M22) - (M12 * M21))) -
                (M01 * ((M10 * M22) - (M12 * M20))) +
                (M02 * ((M10 * M21) - (M11 * M20)));

        /// <summary>
        /// Returns the transpose of the matrix.
        /// </summary>
        public Matrix3x3 Transpose()
        {
            return new(
                        M00, M10, M20,
                        M01, M11, M21,
                        M02, M12, M22);
        }

        /// <summary>
        /// Multiplies the matrix by a column vector.
        /// </summary>
        public Vector3 Transform(Vector3 v)
        {
            return new(
                        (M00 * v.X) + (M01 * v.Y) + (M02 * v.Z),
                        (M10 * v.X) + (M11 * v.Y) + (M12 * v.Z),
                        (M20 * v.X) + (M21 * v.Y) + (M22 * v.Z));
        }

        /// <summary>
        /// Multiplies two matrices.
        /// </summary>
        public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b)
        {
            return new(
                        (a.M00 * b.M00) + (a.M01 * b.M10) + (a.M02 * b.M20),
                        (a.M00 * b.M01) + (a.M01 * b.M11) + (a.M02 * b.M21),
                        (a.M00 * b.M02) + (a.M01 * b.M12) + (a.M02 * b.M22),
                        (a.M10 * b.M00) + (a.M11 * b.M10) + (a.M12 * b.M20),
                        (a.M10 * b.M01) + (a.M11 * b.M11) + (a.M12 * b.M21),
                        (a.M10 * b.M02) + (a.M11 * b.M12) + (a.M12 * b.M22),
                        (a.M20 * b.M00) + (a.M21 * b.M10) + (a.M22 * b.M20),
                        (a.M20 * b.M01) + (a.M21 * b.M11) + (a.M22 * b.M21),
                        (a.M20 * b.M02) + (a.M21 * b.M12) + (a.M22 * b.M22));
        }

        /// <summary>
        /// Scales every element of the matrix.
        /// </summary>
        public static Matrix3x3 operator *(Matrix3x3 a, double s)
        {
            return new(
                        a.M00 * s, a.M01 * s, a.M02 * s,
                        a.M10 * s, a.M11 * s, a.M12 * s,
                        a.M20 * s, a.M21 * s, a.M22 * s);
        }

        /// <inheritdoc/>
        public bool Equals(Matrix3x3 other)
        {
            return M00.Equals(other.M00) &&
                M01.Equals(other.M01) &&
                M02.Equals(other.M02) &&
                M10.Equals(other.M10) &&
                M11.Equals(other.M11) &&
                M12.Equals(other.M12) &&
                M20.Equals(other.M20) &&
                M21.Equals(other.M21) &&
                M22.Equals(other.M22);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Matrix3x3 other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(M00);
            hash.Add(M01);
            hash.Add(M02);
            hash.Add(M10);
            hash.Add(M11);
            hash.Add(M12);
            hash.Add(M20);
            hash.Add(M21);
            hash.Add(M22);
            return hash.ToHashCode();
        }
    }
}
