
using System;
using CommandLine.Text;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture, Category("Utils")]
    public class RedactionTests
    {
        private static readonly Func<string, RedactionWrapper<string>>[] _basicGenerators = new Func<string, RedactionWrapper<string>>[] {
            Redact.Username,
            Redact.Password,
            Redact.Endpoint,
        };

        [Test]
        [TestCaseSource(nameof(_basicGenerators))]
        public void DisabledRedactionResultsInOriginalValue(Func<string, RedactionWrapper<string>> generator)
        {
            string original = "Original test string";

            Redact.IsEnabled = false;
            string result = generator(original).ToString();

            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        [TestCase("user1234", "us******", Description = "Username is redacted")]
        [TestCase("User1", "Us******", Description = "Minimum length should be 8")]
        [TestCase("a", "********", Description = "Shorter names should be fully concealed")]
        [TestCase(null, "null", Description = "Null value gives 'null' string")]
        public void UserNameRedactionTest(string original, string expected)
        {
            Redact.IsEnabled = true;
            string result = Redact.Username(original).ToString();

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase("pass1234", "********", Description = "Password is fully concealed")]
        [TestCase("a", "********", Description = "Minimum length should be 8")]
        [TestCase("abcdefghijklmnopqrstuvwxyz", "********", Description = "Long passwords should be 8 characters long")]
        [TestCase(null, "null", Description = "Null value gives 'null' string")]
        public void PasswordRedationTest(string original, string expected)
        {
            Redact.IsEnabled = true;
            string result = Redact.Password(original).ToString();

            Assert.That(result, Is.EqualTo(expected));
        }


        [Test]
        [TestCase("opc", "********", Description = "Short address should be fully concealed")]
        [TestCase("opcplc:50000", "opc*******", Description = "Address without scheme should have starting characters")]
        [TestCase("127.0.1.2:1234", "127*******", Description = "IP without scheme should have starting digits")]
        [TestCase("opc.tcp://opc", "opc.tcp://********", Description = "Short address without port should have scheme visible")]
        [TestCase("opc.tcp://opcplc:50000", "opc.tcp://opc*****:50000", Description = "Full endpoint address should have scheme, port number, and starting characters visible")]
        [TestCase("opc.tcp://127.0.1.2:1234", "opc.tcp://127******:1234", Description = "Full endpoint IP should have scheme, port number, and starting digits visible")]
        [TestCase(null, "null", Description = "Null value gives 'null' string")]
        public void EndpointRedactionTest(string original, string expected)
        {
            Redact.IsEnabled = true;
            string result = Redact.Endpoint(original).ToString();

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
