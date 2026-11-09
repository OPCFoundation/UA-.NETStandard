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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Polyfills
{
    [TestFixture]
    public sealed class JsonElementEqualityTests
    {
        [TestCase("null", "null", true)]
        [TestCase("true", "true", true)]
        [TestCase("false", "false", true)]
        [TestCase("true", "false", false)]
        [TestCase("null", "0", false)]
        [TestCase("1", "\"1\"", false)]
        [TestCase("\"Value\"", "\"Value\"", true)]
        [TestCase("\"Value\"", "\"value\"", false)]
        [TestCase("\"\\u0041\"", "\"A\"", true)]
        [TestCase("1", "1.0", true)]
        [TestCase("1", "10e-1", true)]
        [TestCase("0.01", "10e-3", true)]
        [TestCase("-0.000e400", "0", true)]
        [TestCase("-1", "1", false)]
        [TestCase("-1.25e2", "-125.00", true)]
        [TestCase("9007199254740992", "9007199254740993", false)]
        [TestCase("1.000000000000000000000000000001", "1", false)]
        [TestCase("1e400", "10e399", true)]
        [TestCase("1e400", "2e400", false)]
        [TestCase("1e-400", "0", false)]
        [TestCase("1.0e2147483647", "1e2147483647", true)]
        [TestCase("1e-2147483648", "1e-2147483647", false)]
        [TestCase("0.1e-2147483647", "1e-2147483648", true)]
        [TestCase("[]", "[]", true)]
        [TestCase("[1,2]", "[1.0,2.00]", true)]
        [TestCase("[1,2]", "[2,1]", false)]
        [TestCase("[1]", "[1,2]", false)]
        [TestCase("{}", "{}", true)]
        [TestCase(/*lang=json,strict*/ "{\"a\":1,\"b\":2}", /*lang=json,strict*/ "{\"b\":2,\"a\":1.00}", true)]
        [TestCase(/*lang=json,strict*/ "{\"a\":1}", /*lang=json,strict*/ "{\"b\":1}", false)]
        [TestCase(/*lang=json,strict*/ "{\"a\":1}", /*lang=json,strict*/ "{\"a\":1,\"b\":2}", false)]
        [TestCase(
            /*lang=json,strict*/ "{\"a\":1,\"a\":2,\"b\":3}",
            /*lang=json,strict*/ "{\"b\":3,\"a\":1.0,\"a\":2}",
            true)]
        [TestCase(/*lang=json,strict*/ "{\"a\":1,\"a\":2}", /*lang=json,strict*/ "{\"a\":2,\"a\":1}", false)]
        [TestCase(/*lang=json,strict*/ "{\"a\":1,\"a\":1}", /*lang=json,strict*/ "{\"a\":1,\"b\":1}", false)]
        [TestCase(
            /*lang=json,strict*/ "{\"a\":[{\"x\":1},null]}",
            /*lang=json,strict*/ "{\"a\":[{\"x\":1.0},null]}",
            true)]
        public void DeepEqualsUsesExactJsonValueSemantics(
            [StringSyntax(StringSyntaxAttribute.Json)] string first,
            [StringSyntax(StringSyntaxAttribute.Json)] string second,
            bool expected)
        {
            using var left = JsonDocument.Parse(first);
            using var right = JsonDocument.Parse(second);

            Assert.That(JsonElement.DeepEquals(left.RootElement, right.RootElement), Is.EqualTo(expected));
            Assert.That(JsonElement.DeepEquals(right.RootElement, left.RootElement), Is.EqualTo(expected));
        }

        [Test]
        public void DeepEqualsRejectsUndefinedValues()
        {
            Assert.Throws<InvalidOperationException>(() => JsonElement.DeepEquals(default, default));
        }

        [TestCase("10e99999999999999999999")]
        [TestCase("1e100000000000000000000")]
        public void DeepEqualsRejectsOutOfRangeExponents(
            [StringSyntax(StringSyntaxAttribute.Json)] string secondValue)
        {
            using var first = JsonDocument.Parse("1e100000000000000000000");
            using var second = JsonDocument.Parse(secondValue);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => JsonElement.DeepEquals(first.RootElement, second.RootElement));
        }

        [Test]
        public void DeepEqualsRejectsDisposedDocuments()
        {
            JsonElement expired;
            using (var document = JsonDocument.Parse("{}"))
            {
                expired = document.RootElement;
            }
            using var current = JsonDocument.Parse("{}");

            Assert.Throws<ObjectDisposedException>(() => JsonElement.DeepEquals(expired, current.RootElement));
        }
    }
}
