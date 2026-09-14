using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the SessionLessServiceMessage Tests.
    /// </summary>
    [TestFixture]
    [Category("BuiltIn")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SessionLessServiceMessageTests
    {
        [Test]
        public void WhenServerUrisAreLessThanNamespacesShouldNotThrowAndMustReturnCorrectServerUris()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //arrange
            const uint uriVersion = 1234;
            var namespaceTable = new NamespaceTable([Namespaces.OpcUa, "http://bar", "http://foo"]);
            const string expectedServerUri = "http://foobar";
            var serverUris = new StringTable([Namespaces.OpcUa, expectedServerUri]);
            var context = new ServiceMessageContext(telemetry, EncodeableFactory.Create())
            {
                NamespaceUris = namespaceTable,
                ServerUris = serverUris
            };
            string result;
            using (var jsonEncoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                var envelope = new SessionLessServiceMessage
                {
                    UriVersion = uriVersion,
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Message = null
                };

                //act and validate it does not throw
                Assert.DoesNotThrow(() => envelope.Encode(jsonEncoder));

                result = jsonEncoder.CloseAndReturnText();
            }

            var jObject = JsonNode.Parse(result);
            Assert.That(jObject, Is.Not.Null);
            uint version = (uint)jObject["UriVersion"];
            Assert.That(version, Is.EqualTo(uriVersion));
            JsonNode serverUrisToken = jObject["ServerUris"];
            Assert.That(serverUrisToken, Is.Not.Null);
            string[] serverUrisEncoded = JsonSerializer.Deserialize<string[]>(serverUrisToken.ToJsonString());
            Assert.That(serverUrisEncoded, Is.Not.Null);
            Assert.That(serverUrisEncoded, Has.Length.EqualTo(1));
            Assert.Contains(expectedServerUri, serverUrisEncoded);
        }

        /// <summary>
        /// Every configured locale id is encoded. The list used to be written
        /// only when it held more than one entry - the reserved-first-entry rule
        /// the namespace and server tables follow, which a locale table does not
        /// - so a client that asked for a single locale had it silently dropped.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public void EncodeWritesEveryConfiguredLocaleId(int localeCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string[] locales = localeCount == 1
                ? ["en-US"]
                : ["en-US", "de-DE"];

            var context = new ServiceMessageContext(telemetry, EncodeableFactory.Create());
            string result;

            using (var jsonEncoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                var envelope = new SessionLessServiceMessage
                {
                    UriVersion = 1,
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    LocaleIds = new StringTable(locales),
                    Message = null
                };

                envelope.Encode(jsonEncoder);
                result = jsonEncoder.CloseAndReturnText();
            }

            JsonNode jObject = JsonNode.Parse(result);
            Assert.That(jObject, Is.Not.Null);

            JsonNode localeIdsToken = jObject["LocaleIds"];
            Assert.That(localeIdsToken, Is.Not.Null);

            string[] localeIdsEncoded = JsonSerializer.Deserialize<string[]>(
                localeIdsToken.ToJsonString());
            Assert.That(localeIdsEncoded, Is.EqualTo(locales));
        }
    }
}
