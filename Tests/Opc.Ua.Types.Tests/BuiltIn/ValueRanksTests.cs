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
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ValueRanksTests
    {
        [Test]
        [TestCase(ValueRanks.ScalarOrOneDimension)]
        [TestCase(ValueRanks.Scalar)]
        [TestCase(ValueRanks.OneOrMoreDimensions)]
        [TestCase(ValueRanks.OneDimension)]
        [TestCase(ValueRanks.TwoDimensions)]
        public void TestValueRanks(int actualValueRank)
        {
            Assert.That(ValueRanks.IsValid(actualValueRank, actualValueRank), Is.True);
            Assert.That(ValueRanks.IsValid(actualValueRank, ValueRanks.Any), Is.True);
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.ScalarOrOneDimension),
                Is.EqualTo(actualValueRank is ValueRanks.Scalar or ValueRanks.OneDimension or ValueRanks
                    .ScalarOrOneDimension));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions),
                Is.EqualTo(actualValueRank >= 0));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.TwoDimensions),
                Is.EqualTo(actualValueRank == ValueRanks.TwoDimensions));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneDimension),
                Is.EqualTo(actualValueRank == ValueRanks.OneDimension));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.OneOrMoreDimensions),
                Is.EqualTo(actualValueRank >= 0));
            Assert.That(
                ValueRanks.IsValid(actualValueRank, ValueRanks.Scalar),
                Is.EqualTo(actualValueRank == ValueRanks.Scalar));
        }
    }
}
