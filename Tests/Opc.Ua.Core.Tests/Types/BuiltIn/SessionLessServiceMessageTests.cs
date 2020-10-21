using System.Collections.Generic;
using System.IO;
using System.Text;
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
            var namespaceTable = new NamespaceTable(new List<string> { Namespaces.OpcUa, "http://bar", "http://foo" });
            var expectedServerUri = "http://foobar";
            var serverUris = new StringTable(new[] { Namespaces.OpcUa, expectedServerUri });
            var memoryStream = new MemoryStream();
            var context = new ServiceMessageContext { NamespaceUris = namespaceTable, ServerUris = serverUris };
            Encoding encoding = Encoding.UTF7; // setting to UTF7 because the BOM marker in UTF8 causes reading error
            // using jsonEncoder, this could have been any IEncoder
            var jsonEncoder = new JsonEncoder(context, true, new StreamWriter(memoryStream, encoding));

            var envelope = new SessionLessServiceMessage {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris,
                Message = null
            };

            //act and validate it does not throw
            Assert.DoesNotThrow(() => {
                envelope.Encode(jsonEncoder);
            });
            jsonEncoder.Close();
            jsonEncoder.Dispose();

            //assert
            var buffer = memoryStream.ToArray();
            var result = encoding.GetString(buffer);

            var jObject = JObject.Parse(result);
            Assert.IsNotNull(jObject);
            var serverUrisToken = jObject["ServerUris"];
            Assert.IsNotNull(serverUrisToken);
            var serverUrisEncoded = serverUrisToken.ToObject<string[]>();
            Assert.IsNotNull(serverUrisEncoded);
            Assert.AreEqual(1, serverUrisEncoded.Length);
            Assert.Contains(expectedServerUri, serverUrisEncoded);
        }
    }
}
