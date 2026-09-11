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

using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class VariantParserTests
{
    [TestCase("i=6", true)]
    [TestCase("ns=1;i=6", false)]
    [TestCase("s=6", false)]
    [TestCase("g=00112233-4455-6677-8899-aabbccddeeff", false)]
    [TestCase("b=AQID", false)]
    [TestCase("i=999999", false)]
    [TestCase("i=0", false)]
    public void OnlySupportedNamespaceZeroNumericIdentifiersResolveBuiltInTypes(string dataType, bool supported)
    {
        bool parsed = VariantParser.TryParse(
            NodeId.Parse(dataType), ValueRanks.Scalar, "42", out Variant value, out string? error);

        Assert.That(parsed, Is.EqualTo(supported));
        if (supported)
        {
            Assert.That(value.TryGetValue(out int number), Is.True);
            Assert.That(number, Is.EqualTo(42));
            Assert.That(error, Is.Null);
        }
        else
        {
            Assert.That(value.IsNull, Is.True);
            Assert.That(error, Does.Contain("DataType"));
        }
    }

    [Test]
    public void NumericDataTypeResolutionStillParsesArrays()
    {
        bool parsed = VariantParser.TryParse(
            DataTypeIds.Int32, ValueRanks.OneDimension, "1, 2, 3", out Variant value, out string? error);

        Assert.That(parsed, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(value.TryGetValue(out ArrayOf<int> numbers), Is.True);
        Assert.That(numbers.ToList(), Is.EqualTo(s_expectedNumbers));
    }

    private static readonly int[] s_expectedNumbers = [1, 2, 3];
}
