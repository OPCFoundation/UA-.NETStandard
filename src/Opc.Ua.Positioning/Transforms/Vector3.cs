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
    /// Minimal immutable double precision 3D vector used by the positioning
    /// transforms. Kept internal so the public surface only exposes the OPC UA
    /// core <see cref="ThreeDCartesianCoordinates"/> type.
    /// </summary>
    internal readonly struct Vector3 : IEquatable<Vector3>
    {
        /// <summary>
        /// The X component.
        /// </summary>
        public double X { get; }

        /// <summary>
        /// The Y component.
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// The Z component.
        /// </summary>
        public double Z { get; }

        /// <summary>
        /// The zero vector.
        /// </summary>
        public static Vector3 Zero => new(0.0, 0.0, 0.0);

        /// <summary>
        /// Creates a new vector from its components.
        /// </summary>
        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Returns the squared Euclidean length.
        /// </summary>
        public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        /// <summary>
        /// Returns the Euclidean length.
        /// </summary>
        public double Length => Math.Sqrt(LengthSquared);

        /// <summary>
        /// Returns true when all components are finite.
        /// </summary>
        public bool IsFinite
            => X.IsFinite() && Y.IsFinite() && Z.IsFinite();

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        /// <summary>
        /// Subtracts two vectors.
        /// </summary>
        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        /// <summary>
        /// Scales a vector.
        /// </summary>
        public static Vector3 operator *(Vector3 a, double s)
        {
            return new(a.X * s, a.Y * s, a.Z * s);
        }

        /// <summary>
        /// Scales a vector.
        /// </summary>
        public static Vector3 operator *(double s, Vector3 a)
        {
            return a * s;
        }

        /// <summary>
        /// Returns the dot product of two vectors.
        /// </summary>
        public static double Dot(Vector3 a, Vector3 b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        /// <summary>
        /// Returns the cross product of two vectors.
        /// </summary>
        public static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new(
                        (a.Y * b.Z) - (a.Z * b.Y),
                        (a.Z * b.X) - (a.X * b.Z),
                        (a.X * b.Y) - (a.Y * b.X));
        }

        /// <summary>
        /// Returns the component at the given zero based index (0=X, 1=Y, 2=Z).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public double this[int index]
            => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

        /// <inheritdoc/>
        public bool Equals(Vector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Vector3 other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }
    }
}
