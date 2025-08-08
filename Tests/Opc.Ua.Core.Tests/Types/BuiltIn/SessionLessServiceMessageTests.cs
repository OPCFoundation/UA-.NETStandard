using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the SessionLessServiceMessage Tests.
    /// </summary>
    [TestFixture, Category("BuiltIn")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class SessionLessServiceMessageTests
    {
        [Test]
        public void WhenServerUrisAreLessThanNamespacesShouldNotThrowAndMustReturnCorrectServerUris()
        {
            //arrange
            const uint uriVersion = 1234;
            var namespaceTable = new NamespaceTable([Namespaces.OpcUa, "http://bar", "http://foo"]);
            const string expectedServerUri = "http://foobar";
            var serverUris = new StringTable(new[] { Namespaces.OpcUa, expectedServerUri });
            var context = new ServiceMessageContext { NamespaceUris = namespaceTable, ServerUris = serverUris };
            string result;
            using (var jsonEncoder = new JsonEncoder(context, true))
            {
                var envelope = new SessionLessServiceMessage {
                    UriVersion = uriVersion,
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Message = null
                };

                //act and validate it does not throw
                NUnit.Framework.Assert.DoesNotThrow(() => envelope.Encode(jsonEncoder));

                result = jsonEncoder.CloseAndReturnText();
            }

            var jObject = JObject.Parse(result);
            Assert.IsNotNull(jObject);
            uint version = jObject["UriVersion"].ToObject<uint>();
            Assert.AreEqual(uriVersion, version);
            JToken serverUrisToken = jObject["ServerUris"];
            Assert.IsNotNull(serverUrisToken);
            string[] serverUrisEncoded = serverUrisToken.ToObject<string[]>();
            Assert.IsNotNull(serverUrisEncoded);
            Assert.AreEqual(1, serverUrisEncoded.Length);
            Assert.Contains(expectedServerUri, serverUrisEncoded);
        }
    }
}
