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

using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Variant.Equals(Variant) compares two scalars of the same numeric or
    /// string type without the type switches. The result matches the typed
    /// Equals(T) overloads, including NaN equal to NaN and +0 equal to -0.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [Parallelizable(ParallelScope.All)]
    public class VariantScalarEqualityTests
    {
        public static readonly object[] Pairs =
        [
            new object[] { new Variant(true), new Variant(true), true },
            new object[] { new Variant(true), new Variant(false), false },
            new object[] { new Variant((sbyte)-1), new Variant((sbyte)-1), true },
            new object[] { new Variant((byte)1), new Variant((byte)2), false },
            new object[] { new Variant((short)-7), new Variant((short)-7), true },
            new object[] { new Variant((ushort)7), new Variant((ushort)8), false },
            new object[] { new Variant(42), new Variant(42), true },
            new object[] { new Variant(42), new Variant(43), false },
            new object[] { new Variant(42u), new Variant(42u), true },
            new object[] { new Variant(-5L), new Variant(-5L), true },
            new object[] { new Variant(5UL), new Variant(6UL), false },
            new object[] { new Variant(1.5f), new Variant(1.5f), true },
            new object[] { new Variant(float.NaN), new Variant(float.NaN), true },
            new object[] { new Variant(0.0f), new Variant(-0.0f), true },
            new object[] { new Variant(1.5), new Variant(1.5), true },
            new object[] { new Variant(1.5), new Variant(2.5), false },
            new object[] { new Variant(double.NaN), new Variant(double.NaN), true },
            new object[] { new Variant(double.NaN), new Variant(1.0), false },
            new object[] { new Variant(0.0), new Variant(-0.0), true },
            new object[] { new Variant("a"), new Variant("a"), true },
            new object[] { new Variant("a"), new Variant("A"), false },
            new object[] { new Variant(string.Empty), new Variant(string.Empty), true },
            // different types take the general path
            new object[] { new Variant(42), new Variant(42L), new Variant(42).Equals(42L) },
            new object[] { new Variant(1), new Variant(true), new Variant(1).Equals(true) }
        ];

        [TestCaseSource(nameof(Pairs))]
        public void ScalarEqualityMatchesTheTypedOverloads(Variant left, Variant right, bool expected)
        {
            Assert.That(left.Equals(right), Is.EqualTo(expected));
            Assert.That(left.Equals((object)right), Is.EqualTo(expected));
            if (expected)
            {
                Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
            }
        }
    }
}
