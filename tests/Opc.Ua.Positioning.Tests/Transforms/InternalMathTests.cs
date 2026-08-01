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
using NUnit.Framework;

namespace Opc.Ua.Positioning.Tests.Transforms
{
    [TestFixture]
    [Category("Positioning")]
    public sealed class InternalMathTests
    {
        [Test]
        public void MatrixEqualityChecksEveryElement()
        {
            var expected = new Matrix3x3(
                1.0, 2.0, 3.0,
                4.0, 5.0, 6.0,
                7.0, 8.0, 9.0);
            Matrix3x3[] different =
            [
                new(0.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0),
                new(1.0, 0.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0),
                new(1.0, 2.0, 0.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0),
                new(1.0, 2.0, 3.0, 0.0, 5.0, 6.0, 7.0, 8.0, 9.0),
                new(1.0, 2.0, 3.0, 4.0, 0.0, 6.0, 7.0, 8.0, 9.0),
                new(1.0, 2.0, 3.0, 4.0, 5.0, 0.0, 7.0, 8.0, 9.0),
                new(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 0.0, 8.0, 9.0),
                new(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 0.0, 9.0),
                new(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 0.0)
            ];

            Assert.Multiple(() =>
            {
                var same = new Matrix3x3(
                    1.0, 2.0, 3.0,
                    4.0, 5.0, 6.0,
                    7.0, 8.0, 9.0);
                Assert.That(
                    expected,
                    Is.EqualTo(same));
                Assert.That((object)expected, Is.EqualTo(same));
                Assert.That((object)expected, Is.Not.EqualTo("not a matrix"));
                Assert.That(
                    expected.GetHashCode(),
                    Is.EqualTo(same.GetHashCode()));
                for (int i = 0; i < different.Length; i++)
                {
                    Assert.That(expected, Is.Not.EqualTo(different[i]));
                }
            });
        }

        [Test]
        public void VectorOperationsAndIndexValidationAreDeterministic()
        {
            var left = new Vector3(1.0, 2.0, 3.0);
            var right = new Vector3(4.0, 5.0, 6.0);

            Assert.Multiple(() =>
            {
                Assert.That(left.LengthSquared, Is.EqualTo(14.0));
                Assert.That(left.Length, Is.EqualTo(Math.Sqrt(14.0)));
                Assert.That(left.IsFinite, Is.True);
                Assert.That(
                    left + right,
                    Is.EqualTo(new Vector3(5.0, 7.0, 9.0)));
                Assert.That(
                    right - left,
                    Is.EqualTo(new Vector3(3.0, 3.0, 3.0)));
                Assert.That(
                    left * 2.0,
                    Is.EqualTo(new Vector3(2.0, 4.0, 6.0)));
                Assert.That(
                    2.0 * left,
                    Is.EqualTo(new Vector3(2.0, 4.0, 6.0)));
                Assert.That(Vector3.Dot(left, right), Is.EqualTo(32.0));
                Assert.That(
                    Vector3.Cross(left, right),
                    Is.EqualTo(new Vector3(-3.0, 6.0, -3.0)));
                Assert.That(left[0], Is.EqualTo(1.0));
                Assert.That(left[1], Is.EqualTo(2.0));
                Assert.That(left[2], Is.EqualTo(3.0));
                Assert.That(
                    () => _ = left[3],
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That((object)left, Is.EqualTo(left));
                Assert.That((object)left, Is.Not.EqualTo("not a vector"));
                Assert.That(
                    new Vector3(double.NaN, 0.0, 0.0).IsFinite,
                    Is.False);
                Assert.That(left.GetHashCode(), Is.EqualTo(
                    new Vector3(1.0, 2.0, 3.0).GetHashCode()));
            });
        }
    }
}
