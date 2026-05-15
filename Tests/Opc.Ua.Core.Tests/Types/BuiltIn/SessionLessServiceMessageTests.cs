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
    }
}
