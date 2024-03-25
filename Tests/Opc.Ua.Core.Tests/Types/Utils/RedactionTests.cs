/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Redaction;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture, Category("Utils")]
    public class RedactionTests
    {
        [Test]
        public void FallbackStrategyIsInvokedWhenNoStrategyWasAdded()
        {
            RedactionStrategies.ResetStrategy();

            string original = "Original test string";

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void FallbackStrategyIsRedactingNullCorrectly()
        {
            RedactionStrategies.ResetStrategy();

            string original = null;

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo("null"));
        }

        [Test]
        public void StrategyIsInvokedWhenItExists()
        {
            RedactionStrategies.ResetStrategy();

            RedactionStrategies.SetStrategy(new TestRedactionStrategy("int_"));

            int original = 123;

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo("int_123"));
        }

        [Test]
        public void TheLastStrategyIsInvokedWhenMultipleWereSet()
        {
            RedactionStrategies.ResetStrategy();

            RedactionStrategies.SetStrategy(new TestRedactionStrategy("first_"));
            RedactionStrategies.SetStrategy(new TestRedactionStrategy("second_"));

            int originalNumber = 456;

            string resultNumber = Redact.Create(originalNumber).ToString();

            Assert.That(resultNumber, Is.EqualTo("second_456"));

            string originalString = "test string 890";

            string resultString = Redact.Create(originalString).ToString();

            Assert.That(resultString, Is.EqualTo("second_test string 890"));
        }

        private class TestRedactionStrategy : IRedactionStrategy
        {
            private readonly string m_prefix;

            public TestRedactionStrategy(string prefix)
            {
                m_prefix = prefix;
            }

            public string Redact(object value)
            {
                return $"{m_prefix}{value}";
            }
        }
    }
}
