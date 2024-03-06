
using System;
using System.Collections.Generic;
using System.Reflection;
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
            ResetStrategies();

            string original = "Original test string";

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void FallbackStrategyIsRedactingNullCorrectly()
        {
            ResetStrategies();

            string original = null;

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo("null"));
        }

        [Test]
        public void FallbackStrategyIsInvokedWhenNoStrategyFoundForTheGivenType()
        {
            ResetStrategies();

            RedactionStrategies.AddStrategy(new TestRedactionStrategy(typeof(int), "int_"));

            string original = "Original test string";

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void StrategyIsInvokedWhenItExists()
        {
            ResetStrategies();

            RedactionStrategies.AddStrategy(new TestRedactionStrategy(typeof(int), "int_"));

            int original = 123;

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo("int_123"));
        }

        [Test]
        public void TheRightStrategyIsInvokedWhenMultipleStrategiesExist()
        {
            ResetStrategies();

            RedactionStrategies.AddStrategy(new TestRedactionStrategy(typeof(int), "int_"));
            RedactionStrategies.AddStrategy(new TestRedactionStrategy(typeof(string), "string_"));

            int originalNumber = 456;

            string resultNumber = Redact.Create(originalNumber).ToString();

            Assert.That(resultNumber, Is.EqualTo("int_456"));

            string originalString = "test string 890";

            string resultString = Redact.Create(originalString).ToString();

            Assert.That(resultString, Is.EqualTo("string_test string 890"));
        }

        private static void ResetStrategies()
        {
            Type t = typeof(RedactionStrategies);
            FieldInfo field = t.GetField("m_strategies", BindingFlags.Static | BindingFlags.NonPublic);
            List<IRedactionStrategy> strategies = field.GetValue(null) as List<IRedactionStrategy>;

            strategies.Clear();
        }

        private class TestRedactionStrategy : IRedactionStrategy
        {
            private readonly Type m_type;
            private readonly string m_prefix;

            public TestRedactionStrategy(Type type , string prefix)
            {

                m_type = type;
                m_prefix = prefix;
            }
            public bool CanRedact(Type type)
            {
                return type == m_type;
            }

            public string Redact(object value)
            {
                return $"{m_prefix}{value}";
            }
        }
    }
}
