using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

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
        public void WhenServerUrisAreLessThanNamespaces_ShouldNotThrowAndMustReturnCorrectServerUris()
        {
            //arrange
            UInt32 uriVersion = 1234;
            var namespaceTable = new NamespaceTable(new List<string> { Namespaces.OpcUa, "http://bar", "http://foo" });
            var expectedServerUri = "http://foobar";
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
                Assert.DoesNotThrow(() => {
                    envelope.Encode(jsonEncoder);
                });

                result = jsonEncoder.CloseAndReturnText();
            }

            var jObject = JObject.Parse(result);
            Assert.IsNotNull(jObject);
            UInt32 version = jObject["UriVersion"].ToObject<UInt32>();
            Assert.AreEqual(uriVersion, version);
            var serverUrisToken = jObject["ServerUris"];
            Assert.IsNotNull(serverUrisToken);
            var serverUrisEncoded = serverUrisToken.ToObject<string[]>();
            Assert.IsNotNull(serverUrisEncoded);
            Assert.AreEqual(1, serverUrisEncoded.Length);
            Assert.Contains(expectedServerUri, serverUrisEncoded);
        }
    }
}
