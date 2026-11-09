/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

using System;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Covers source-only, portable Call receiver capture without server I/O.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotCallReceiverTests
    {
        private const string SourceObjectId = "nsu=urn:source;s=Owner";
        private const string SourceEndpoint = "opc.tcp://source.example:4840";

        /// <summary>
        /// Verifies that the original source Object and its normalized OPC endpoint are captured.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void CapturesPortableRootObjectAndResolvesRelativeFormEndpoint(bool explicitType)
        {
            string source = """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "tm": "https://www.w3.org/2019/wot/tm#"
                    }
                  ],
                  "@type": ["Thing", "uav:object"],
                  "uav:id": "nsu=urn:source;s=Owner",
                  "base": "opc.tcp://source.example:4840/",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "?target=one",
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;
            if (!explicitType)
            {
                source = source.Replace("\"@type\": [\"Thing\", \"uav:object\"],", string.Empty,
                    StringComparison.Ordinal);
            }
            bool captured = Capture(
                source,
                out string receiver,
                out string endpoint,
                out string error);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.True, error);
                Assert.That(receiver, Is.EqualTo(SourceObjectId));
                Assert.That(endpoint, Is.EqualTo(SourceEndpoint));
                Assert.That(error, Is.Empty);
            });
        }

        /// <summary>
        /// Verifies that an absent, null, empty or non-portable root identity cannot supply a receiver.
        /// </summary>
        [TestCase(null)]
        [TestCase("null")]
        [TestCase("\"\"")]
        [TestCase("\"ns=1;s=Owner\"")]
        [TestCase("\"i=0\"")]
        public void MissingOrInvalidRootIdentityIsRejected(string? identity)
        {
            string identityMember = identity is null ? string.Empty : "\"uav:id\":" + identity + ",";
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
                  ],
                  "@type": ["Thing", "uav:object"],
                  {{identityMember}}
                  "base": "{{SourceEndpoint}}",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "{{SourceEndpoint}}",
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;

            bool captured = Capture(json, out string receiver, out string endpoint, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.False);
                Assert.That(receiver, Is.Empty);
                Assert.That(endpoint, Is.Empty);
                Assert.That(error, Does.Contain("explicitly identified"));
            });
        }

        /// <summary>
        /// Verifies that a non-Object source discriminator is not promoted to a Call receiver.
        /// </summary>
        [TestCase("uav:variable")]
        [TestCase("uav:method")]
        [TestCase("uav:objectType")]
        [TestCase("tm:ThingModel")]
        public void NonObjectRootTypesAreRejected(string type)
        {
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "tm": "https://www.w3.org/2019/wot/tm#"
                    }
                  ],
                  "@type": ["Thing", "{{type}}"],
                  "uav:id": "{{SourceObjectId}}",
                  "base": "{{SourceEndpoint}}",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "{{SourceEndpoint}}",
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;

            bool captured = Capture(json, out string receiver, out string endpoint, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.False);
                Assert.That(receiver, Is.Empty);
                Assert.That(endpoint, Is.Empty);
                Assert.That(error, Does.Contain("explicitly identified"));
            });
        }

        /// <summary>
        /// Verifies that a source Thing with distinct OPC endpoints is rejected as ambiguous.
        /// </summary>
        [Test]
        public void MultipleSourceOpcEndpointsAreRejectedAsAmbiguous()
        {
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
                  ],
                  "@type": ["Thing", "uav:object"],
                  "uav:id": "{{SourceObjectId}}",
                  "base": "{{SourceEndpoint}}",
                  "actions": {
                    "run": {
                      "forms": [{
                        "href": "opc.tcp://other.example:4840",
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;

            bool captured = Capture(json, out string receiver, out string endpoint, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.False);
                Assert.That(receiver, Is.Empty);
                Assert.That(endpoint, Is.Empty);
                Assert.That(error, Does.Contain("exactly one OPC UA endpoint"));
            });
        }

        /// <summary>
        /// Verifies that projection, scenario, and relocated affordances do not create source authority.
        /// </summary>
        [TestCase("projection")]
        [TestCase("scenario")]
        [TestCase("relocated")]
        public void ProjectionScenarioAndRelocatedActionCannotSupplyReceiver(string boundary)
        {
            string rootMarker = boundary switch
            {
                "projection" => "\"@type\":[\"Thing\",\"uav:projection\"],",
                "scenario" => "\"@type\":[\"Thing\",\"uav:object\"],\"uav:scenario\":\"urn:scenario\",",
                _ => "\"@type\":[\"Thing\",\"uav:object\"],"
            };
            string actionMarker = boundary == "relocated"
                ? "\"uav:resolvedFrom\":\"urn:source#/actions/run\","
                : string.Empty;
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "tm": "https://www.w3.org/2019/wot/tm#"
                    }
                  ],
                  {{rootMarker}}
                  "uav:id": "nsu=urn:projection;s=View",
                  "base": "{{SourceEndpoint}}",
                  "actions": {
                    "run": {
                      {{actionMarker}}
                      "forms": [{
                        "href": "{{SourceEndpoint}}",
                        "uav:id": "nsu=urn:source;s=Run",
                        "op": "invokeaction"
                      }]
                    }
                  }
                }
                """;

            bool captured = Capture(json, out string receiver, out string endpoint, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.False);
                Assert.That(receiver, Is.Empty);
                Assert.That(endpoint, Is.Empty);
                Assert.That(error, Does.Contain("explicitly identified"));
            });
        }

        private static bool Capture(
            string json,
            out string receiver,
            out string endpoint,
            out string error)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            JsonElement action = document.RootElement.GetProperty("actions").GetProperty("run");
            return WotCallReceiver.TryCaptureSourceObject(
                document, action, out receiver, out endpoint, out error);
        }
    }
}
