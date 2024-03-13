
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
