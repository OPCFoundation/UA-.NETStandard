using System;
using NUnit.Framework;
using Opc.Ua.Redaction;
using Opc.Ua.Types.Redaction;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture, Category("Utils")]
    internal class SimpleRedactionStrategyTests
    {
        [SetUp]
        public void Setup()
        {
            RedactionStrategies.SetStrategy(new SimpleRedactionStrategy());
        }

        [TearDown]
        public void Teardown()
        {
            RedactionStrategies.ResetStrategy();
        }

        [Test]
        public void ZeroMinimumLengthIsAllowed()
        {
            _ = new SimpleRedactionStrategy(0, 12);
        }

        [Test]
        public void ZeroMaximumLengthIsAllowed()
        {
            _ = new SimpleRedactionStrategy(12, 0);
        }

        [Test]
        public void NegativeMinimumLengthThrows()
        {
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _ = new SimpleRedactionStrategy(-1, 0))
                    .ParamName, Is.EqualTo("minLength"));
        }

        [Test]
        public void MaximumLengthLowerThanNegativeOneThrows()
        {
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _ = new SimpleRedactionStrategy(12, -2))
                    .ParamName, Is.EqualTo("maxLength"));
        }

        [Test]
        public void NegativeOneMaximumLengthMeansNoLimit()
        {
            var strategy = new SimpleRedactionStrategy(0, -1);

            string original = new string('a', 200);
            string expected = new string('*', 200);

            string result = strategy.Redact(original);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void RedactString()
        {
            var redacted = Redact.Create("my long test string");

            Assert.That(redacted.ToString(), Is.EqualTo("****************"));
        }

        [Test]
        public void RedactNullString()
        {
            string original = null;

            string result = Redact.Create(original).ToString();

            Assert.That(result, Is.EqualTo("null"));
        }

        [Test]
        public void RedactUri()
        {
            var redacted = Redact.Create(new Uri("http://example.com:8080"));

            Assert.That(redacted.ToString(), Is.EqualTo("http://***********:8080"));
        }

        [Test]
        public void RedactUriBuilder()
        {
            var redacted = Redact.Create(new UriBuilder("test.com/index.html"));

            Assert.That(redacted.ToString(), Is.EqualTo("http://********/index.html"));
        }

        [Test]
        public void RedactNullUri()
        {
            Uri uri = null;

            string result = Redact.Create(uri).ToString();

            Assert.That(result, Is.EqualTo("null"));
        }

        [Test]
        public void RedactException()
        {
            var original = new Exception("Test message with $ecret");

            string result = Redact.Create(original).ToString();

            Assert.That(result, Does.Not.Contain('$'));
        }

        [Test]
        public void RedactNullException()
        {
            Exception exception = null;

            string result = Redact.Create(exception).ToString();

            Assert.That(result, Is.EqualTo("null"));
        }
    }
}
