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
    /// Angle unit conversion helpers. All internal trigonometry works in
    /// radians; these helpers translate to and from the caller supplied
    /// <see cref="AngleUnit"/>.
    /// </summary>
    internal static class AngleMath
    {
        private const double kDegreesToRadians = Math.PI / 180.0;
        private const double kRadiansToDegrees = 180.0 / Math.PI;

        /// <summary>
        /// Converts a value expressed in <paramref name="unit"/> to radians.
        /// </summary>
        public static double ToRadians(double value, AngleUnit unit)
        {
            return unit == AngleUnit.Degrees ? value * kDegreesToRadians : value;
        }

        /// <summary>
        /// Converts a value expressed in radians to <paramref name="unit"/>.
        /// </summary>
        public static double FromRadians(double radians, AngleUnit unit)
        {
            return unit == AngleUnit.Degrees ? radians * kRadiansToDegrees : radians;
        }
    }
}
