/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the Json encoder and decoder class.
    /// </summary>
    [TestFixture]
    [Category("JsonEncoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderCompactTests
    {
        internal const string NamespaceUri1 = "http://test.org/UA/Data1/";
        internal const string NamespaceUri2 = "tag:test.org,2024-07:schema:data:2";
        internal const string NamespaceUri3 = "urn:test.org:2024-07:schema:data:3";

        internal static readonly string[] NamespaceUris
            = [NamespaceUri1, NamespaceUri2, NamespaceUri3];

        internal const string ServerUri1 = "http://test.org/product/server1/";
        internal const string ServerUri2 = "tag:test.org,2024-07:product:server:2";
        internal const string ServerUri3 = "urn:test.org:2024-07:product:server:3";
        internal static readonly string[] ServerUris = [ServerUri1, ServerUri2, ServerUri3];

        internal const uint NumericId1 = 1234;
        internal const uint NumericId2 = 5678;
        internal const uint NumericId3 = 9876;
        internal static readonly uint[] NumericIds = [NumericId1, NumericId2, NumericId3];

        internal const string StringId1 = /*lang=json,strict*/
            @"{""World"": ""Pandora""}";

        internal const string StringId2 = "<World>Pandora</World>";
        internal const string StringId3 = "http://world.org/Pandora/?alien=blue";
        internal static readonly string[] StringIds = [StringId1, StringId2, StringId3];

        internal static readonly Guid GuidId1 = new("73861B2D-EA9A-4B97-ACE6-9A2943EF641E");
        internal static readonly Guid GuidId2 = new("BCFE58C8-CDC5-444F-B1F8-A12903008BE0");
        internal static readonly Guid GuidId3 = new("C141B9D1-F1FD-4D15-9918-E37FD697EA1D");
        internal static readonly Guid[] GuidIds = [GuidId1, GuidId2, GuidId3];

        internal static readonly ByteString OpaqueId1 = ByteString.FromHexString(
            "138DAA907373409AB6A4A36322063745");

        internal static readonly ByteString OpaqueId2 = ByteString.FromHexString(
            "E41047609A9248318EB907991A66B7BEE6B60CB5114828");

        internal static readonly ByteString OpaqueId3 = ByteString.FromHexString("FBD8F0DE652A479B");
        internal static readonly ByteString[] OpaqueIds = [OpaqueId1, OpaqueId2, OpaqueId3];
        internal static readonly string[] Body = ["opc.tcp://localhost/"];
        internal static readonly string[] BodyArray = ["opc.tcp://localhost/"];

        private static string Escape(string input)
        {
            return input
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string ToString(Array input, int index)
        {
            object element = input.GetValue(index % input.Length);

            if (element is ByteString oid)
            {
                return oid.ToBase64();
            }

            if (element is string sid)
            {
                return Escape(sid);
            }

            return element != null ? element.ToString() : string.Empty;
        }

        private static T Get<T>(IList<T> input, int index)
        {
            return input[index % input.Count];
        }

        private static void CheckDecodedNodeIds(
            ServiceMessageContext context,
            JsonDecoder decoder,
            int index)
        {
            NodeId n0 = decoder.ReadNodeId("D0");
            Assert.That((int)n0.NamespaceIndex, Is.Zero);
            Assert.That(n0.TryGetValue(out uint id0) ? id0 : 0, Is.EqualTo(2263U));

            NodeId n1 = decoder.ReadNodeId("D1");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index)),
                Is.EqualTo((int)n1.NamespaceIndex));
            Assert.That(n1.TryGetValue(out uint id1) ? id1 : 0, Is.EqualTo(Get(NumericIds, index)));

            NodeId n2 = decoder.ReadNodeId("D2");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)),
                Is.EqualTo((int)n2.NamespaceIndex));
            Assert.That(n2.TryGetValue(out string id3) ? id3 : string.Empty, Is.EqualTo(Get(StringIds, index)));

            NodeId n3 = decoder.ReadNodeId("D3");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)),
                Is.EqualTo((int)n3.NamespaceIndex));
            Assert.That(n3.TryGetValue(out Guid id4) ? id4 : Guid.Empty, Is.EqualTo(Get(GuidIds, index)));

            NodeId n4 = decoder.ReadNodeId("D4");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)),
                Is.EqualTo((int)n4.NamespaceIndex));
            Assert.That(
                (n4.TryGetValue(out ByteString id5) ? id5 : ByteString.Empty).ToHexString(),
                Is.EqualTo(Get(OpaqueIds, index).ToHexString()));
        }

        private static void CheckDecodedExpandedNodeIds(
            ServiceMessageContext context,
            JsonDecoder decoder,
            int index)
        {
            ExpandedNodeId n0 = decoder.ReadExpandedNodeId("D0");
            Assert.That((int)n0.ServerIndex, Is.Zero);
            Assert.That((int)n0.NamespaceIndex, Is.Zero);
            Assert.That(n0.TryGetValue(out uint id0) ? id0 : 0, Is.EqualTo(2263U));

            ExpandedNodeId n1 = decoder.ReadExpandedNodeId("D1");
            Assert.That((int)n1.ServerIndex, Is.Zero);

            string uri = Get(NamespaceUris, index);
            int ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n1.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n1.NamespaceIndex));
            }

            Assert.That(n1.TryGetValue(out uint id1) ? id1 : 0, Is.EqualTo(Get(NumericIds, index)));

            ExpandedNodeId n2 = decoder.ReadExpandedNodeId("D2");
            Assert.That((int)n2.ServerIndex, Is.Zero);

            uri = Get(NamespaceUris, index + 1);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n2.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n2.NamespaceIndex));
            }

            Assert.That(
                n2.TryGetValue(out string guid3) ? guid3 : null,
                Is.EqualTo(Get(StringIds, index)));

            ExpandedNodeId n3 = decoder.ReadExpandedNodeId("D3");
            Assert.That((int)n3.ServerIndex, Is.Zero);

            uri = Get(NamespaceUris, index + 2);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n3.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n3.NamespaceIndex));
            }

            Assert.That(
                n3.TryGetValue(out Guid id3) ? id3 : Guid.Empty,
                Is.EqualTo(Get(GuidIds, index)));

            ExpandedNodeId n4 = decoder.ReadExpandedNodeId("D4");
            Assert.That((int)n4.ServerIndex, Is.Zero);

            uri = Get(NamespaceUris, index + 3);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n4.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n4.NamespaceIndex));
            }

            Assert.That(
                (n4.TryGetValue(out ByteString id4) ? id4 : default).ToHexString(),
                Is.EqualTo(Get(OpaqueIds, index).ToHexString()));

            ExpandedNodeId n5 = decoder.ReadExpandedNodeId("D5");
            Assert.That(
                context.ServerUris.GetIndex(Get(ServerUris, index)),
                Is.EqualTo((int)n5.ServerIndex));

            uri = Get(NamespaceUris, index);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n5.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n5.NamespaceIndex));
            }

            Assert.That(
                n5.TryGetValue(out uint id5) ? id5 : 0,
                Is.EqualTo(Get(NumericIds, index)));

            ExpandedNodeId n6 = decoder.ReadExpandedNodeId("D6");
            Assert.That(
                context.ServerUris.GetIndex(Get(ServerUris, index + 1)),
                Is.EqualTo((int)n6.ServerIndex));

            uri = Get(NamespaceUris, index + 1);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n6.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n6.NamespaceIndex));
            }

            Assert.That(
                n6.TryGetValue(out string id6) ? id6 : null,
                Is.EqualTo(Get(StringIds, index)));

            ExpandedNodeId n7 = decoder.ReadExpandedNodeId("D7");
            Assert.That(
                context.ServerUris.GetIndex(Get(ServerUris, index + 2)),
                Is.EqualTo((int)n7.ServerIndex));

            uri = Get(NamespaceUris, index + 2);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n7.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n7.NamespaceIndex));
            }

            Assert.That(
                n7.TryGetValue(out Guid id7) ? id7 : Guid.Empty,
                Is.EqualTo(Get(GuidIds, index)));

            ExpandedNodeId n8 = decoder.ReadExpandedNodeId("D8");
            Assert.That(
                context.ServerUris.GetIndex(Get(ServerUris, index + 3)),
                Is.EqualTo((int)n8.ServerIndex));

            uri = Get(NamespaceUris, index + 3);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0)
            {
                Assert.That(uri, Is.EqualTo(n8.NamespaceUri));
            }
            else
            {
                Assert.That(ns, Is.EqualTo(n8.NamespaceIndex));
            }

            Assert.That(
                (n8.TryGetValue(out ByteString id8) ? id8 : default).ToHexString(),
                Is.EqualTo(Get(OpaqueIds, index).ToHexString()));
        }

        private static void CheckDecodedQualfiiedNames(
            ServiceMessageContext context,
            JsonDecoder decoder,
            int index)
        {
            QualifiedName n0 = decoder.ReadQualifiedName("D0");
            Assert.That((int)n0.NamespaceIndex, Is.Zero);
            Assert.That(n0.Name, Is.EqualTo("ServerStatus"));

            QualifiedName n1 = decoder.ReadQualifiedName("D1");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index)),
                Is.EqualTo((int)n1.NamespaceIndex));
            Assert.That(n1.Name, Is.EqualTo("N1"));

            QualifiedName n2 = decoder.ReadQualifiedName("D2");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)),
                Is.EqualTo((int)n2.NamespaceIndex));
            Assert.That(n2.Name, Is.EqualTo("N2"));

            QualifiedName n3 = decoder.ReadQualifiedName("D3");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)),
                Is.EqualTo((int)n3.NamespaceIndex));
            Assert.That(n3.Name, Is.EqualTo("N3"));

            QualifiedName n4 = decoder.ReadQualifiedName("D4");
            Assert.That(
                context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)),
                Is.EqualTo((int)n4.NamespaceIndex));
            Assert.That(n4.Name, Is.EqualTo("N4"));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeCompactAndVerboseNodeId(int index)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string data = $$"""

                {
                    "D0": "i=2263",
                    "D1": "nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D2": "nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(StringIds, index)}}",
                    "D3": "nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(GuidIds, index)}}",
                    "D4": "nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(OpaqueIds, index)}}"
                }

            """;

            var context = ServiceMessageContext.Create(telemetry);

            using var decoder = new JsonDecoder(data, context, new JsonDecoderOptions
            {
                UpdateNamespaceTable = true
            });
            CheckDecodedNodeIds(context, decoder, index);
        }

        [Test]
        [TestCase(0, JsonEncodingType.Verbose)]
        [TestCase(1, JsonEncodingType.Verbose)]
        [TestCase(2, JsonEncodingType.Verbose)]
        [TestCase(0, JsonEncodingType.Compact)]
        [TestCase(1, JsonEncodingType.Compact)]
        [TestCase(2, JsonEncodingType.Compact)]
        public void EncodeCompactOrVerboseNodeId(int index, JsonEncodingType jsonEncoding)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string data = $$"""

                {
                    "D0": "i=2263",
                    "D1": "nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D2": "nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(StringIds, index)}}",
                    "D3": "nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(GuidIds, index)}}",
                    "D4": "nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(OpaqueIds, index)}}",
                }

            """;

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = EncoderCommon.PrettifyAndValidateJson(
                jsonObj.ToJsonString(), true);

            var context = ServiceMessageContext.Create(telemetry);
            Array.ForEach(NamespaceUris, x => context.NamespaceUris.Append(x));

            using var encoder = new JsonEncoder(context,
                jsonEncoding == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            encoder.WriteNodeId("D0", new NodeId(2263));
            encoder.WriteNodeId(
                "D1",
                new NodeId(
                    Get(NumericIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
            encoder.WriteNodeId(
                "D2",
                new NodeId(
                    Get(StringIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
            encoder.WriteNodeId(
                "D3",
                new NodeId(
                    Get(GuidIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
            encoder.WriteNodeId(
                "D4",
                new NodeId(
                    Get(OpaqueIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));

            string actual = EncoderCommon.PrettifyAndValidateJson(encoder.CloseAndReturnText(), true);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeCompactAndVerboseExpandedNodeId(int index)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string data = $$"""

                {
                    "D0": "i=2263",
                    "D1": "nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D2": "nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(StringIds, index)}}",
                    "D3": "nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(GuidIds, index)}}",
                    "D4": "nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(OpaqueIds, index)}}",
                    "D5": "svu={{ToString(ServerUris, index)}};nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D6": "svu={{ToString(ServerUris, index + 1)}};nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(
                    StringIds,
                    index
                )}}",
                    "D7": "svu={{ToString(ServerUris, index + 2)}};nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(
                    GuidIds,
                    index
                )}}",
                    "D8": "svu={{ToString(ServerUris, index + 3)}};nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(
                    OpaqueIds,
                    index
                )}}"
                }

            """;

            var context = ServiceMessageContext.Create(telemetry);

            using var decoder = new JsonDecoder(data, context, new JsonDecoderOptions
            {
                UpdateNamespaceTable = true
            });
            CheckDecodedExpandedNodeIds(context, decoder, index);
        }

        [Test]
        [TestCase(0, JsonEncodingType.Verbose)]
        [TestCase(1, JsonEncodingType.Verbose)]
        [TestCase(2, JsonEncodingType.Verbose)]
        [TestCase(0, JsonEncodingType.Compact)]
        [TestCase(1, JsonEncodingType.Compact)]
        [TestCase(2, JsonEncodingType.Compact)]
        public void EncodeCompactOrVerboseExpandedNodeId(int index, JsonEncodingType jsonEncoding)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string data = $$"""

                {
                    "D0": "i=2263",
                    "D1": "nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D2": "nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(StringIds, index)}}",
                    "D3": "nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(GuidIds, index)}}",
                    "D4": "nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(OpaqueIds, index)}}",
                    "D5": "svu={{ToString(ServerUris, index)}};nsu={{ToString(NamespaceUris, index)}};i={{ToString(NumericIds, index)}}",
                    "D6": "svu={{ToString(ServerUris, index + 1)}};nsu={{ToString(NamespaceUris, index + 1)}};s={{ToString(
                    StringIds,
                    index
                )}}",
                    "D7": "svu={{ToString(ServerUris, index + 2)}};nsu={{ToString(NamespaceUris, index + 2)}};g={{ToString(
                    GuidIds,
                    index
                )}}",
                    "D8": "svu={{ToString(ServerUris, index + 3)}};nsu={{ToString(NamespaceUris, index + 3)}};b={{ToString(
                    OpaqueIds,
                    index
                )}}"
                }

""";

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = EncoderCommon.PrettifyAndValidateJson(
                jsonObj.ToJsonString(), true);

            var context = ServiceMessageContext.Create(telemetry);
            Array.ForEach(NamespaceUris, x => context.NamespaceUris.Append(x));
            context.ServerUris.Append("http://server-placeholder");
            Array.ForEach(ServerUris, x => context.ServerUris.Append(x));

            using var encoder = new JsonEncoder(context,
                jsonEncoding == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            encoder.WriteExpandedNodeId("D0", new ExpandedNodeId(2263));
            encoder.WriteExpandedNodeId(
                "D1",
                new ExpandedNodeId(
                    Get(NumericIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
            encoder.WriteExpandedNodeId(
                "D2",
                new ExpandedNodeId(
                    Get(StringIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
            encoder.WriteExpandedNodeId(
                "D3",
                new ExpandedNodeId(
                    Get(GuidIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
            encoder.WriteExpandedNodeId(
                "D4",
                new ExpandedNodeId(
                    Get(OpaqueIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));
            encoder.WriteExpandedNodeId(
                "D5",
                new ExpandedNodeId(
                    Get(NumericIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index)),
                    null,
                    (uint)context.ServerUris.GetIndex(Get(ServerUris, index))));
            encoder.WriteExpandedNodeId(
                "D6",
                new ExpandedNodeId(
                    Get(StringIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)),
                    null,
                    (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 1))));
            encoder.WriteExpandedNodeId(
                "D7",
                new ExpandedNodeId(
                    Get(GuidIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)),
                    null,
                    (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 2))));
            encoder.WriteExpandedNodeId(
                "D8",
                new ExpandedNodeId(
                    Get(OpaqueIds, index),
                    (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)),
                    null,
                    (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 3))));

            string actual = EncoderCommon.PrettifyAndValidateJson(
                encoder.CloseAndReturnText(), true);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeCompactAndVerboseQualifiedName()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            string data = $$"""

                {
                    "D0": "ServerStatus",
                    "D1": "nsu={{ToString(NamespaceUris, 0)}};N1",
                    "D2": "nsu={{ToString(NamespaceUris, 1)}};N2",
                    "D3": "nsu={{ToString(NamespaceUris, 2)}};N3",
                    "D4": "nsu={{ToString(NamespaceUris, 3)}};N4"
                }

""";

            var context = ServiceMessageContext.Create(telemetry);

            using var decoder = new JsonDecoder(data, context, new JsonDecoderOptions
            {
                UpdateNamespaceTable = true
            });
            CheckDecodedQualfiiedNames(context, decoder, 0);
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void EncodeCompactAndVerboseQualifiedName(JsonEncodingType jsonEncoding)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var context1 = ServiceMessageContext.Create(telemetry);
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            string data = $$"""

                {
                    "D0": "ServerStatus",
                    "D1": "nsu={{ToString(NamespaceUris, 0)}};N1",
                    "D2": "nsu={{ToString(NamespaceUris, 1)}};N2",
                    "D3": "nsu={{ToString(NamespaceUris, 2)}};N3",
                    "D4": "nsu={{ToString(NamespaceUris, 3)}};N4"
                }

""";

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = jsonObj.ToJsonString();
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = ServiceMessageContext.Create(telemetry);
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using var encoder = new JsonEncoder(context2,
                jsonEncoding == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

            encoder.WriteQualifiedName("D0", QualifiedName.From("ServerStatus"));
            encoder.WriteQualifiedName(
                "D1",
                new QualifiedName(
                    "N1",
                    (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 0))));
            encoder.WriteQualifiedName(
                "D2",
                new QualifiedName(
                    "N2",
                    (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 1))));
            encoder.WriteQualifiedName(
                "D3",
                new QualifiedName(
                    "N3",
                    (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 2))));
            encoder.WriteQualifiedName(
                "D4",
                new QualifiedName(
                    "N4",
                    (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 3))));

            string actual = encoder.CloseAndReturnText();
            EncoderCommon.PrettifyAndValidateJson(actual, true);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeCompactAndVerboseMatrix()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """
                {
                    "D0": { "Dimensions": [ 2, 3 ], "Array": [ "1", "2", "3", "4", "5", "6" ] },
                    "D1": { "Dimensions": [ 1, 2, 3 ], "Array": [ "1", "2", "3", "4", "5", "6" ] }
                }
                """;

            var context = ServiceMessageContext.Create(telemetry);

            using var decoder = new JsonDecoder(data, context);
            Variant v1 = decoder.ReadVariantValue("D0", TypeInfo.Create(BuiltInType.Int64, 3));
            MatrixOf<long> a1 = v1.GetInt64Matrix();
            Assert.That(a1.Dimensions, Has.Length.EqualTo(2));
            Assert.That(a1.Count, Is.EqualTo(6));
            Assert.That(a1.Dimensions[0], Is.EqualTo(2));
            Assert.That(a1.Dimensions[1], Is.EqualTo(3));

            Variant v2 = decoder.ReadVariantValue("D1", TypeInfo.Create(BuiltInType.Int64, 3));
            MatrixOf<long> a2 = v2.GetInt64Matrix();
            Assert.That(a2.Dimensions, Has.Length.EqualTo(3));
            Assert.That(a2.Count, Is.EqualTo(6));
            Assert.That(a2.Dimensions[0], Is.EqualTo(1));
            Assert.That(a2.Dimensions[1], Is.EqualTo(2));
            Assert.That(a2.Dimensions[2], Is.EqualTo(3));
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void EncodeCompactAndVerboseMatrix(JsonEncodingType jsonEncoding)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """

                {
                    "D0": { "Array": [ 1, 2, 3, 4, 5, 6 ], "Dimensions": [ 2, 3 ] },
                    "D1": { "Array": [ 1, 2, 3, 4, 5, 6 ], "Dimensions": [ 1, 2, 3 ] }
                }

            """;

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = jsonObj.ToJsonString();
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = ServiceMessageContext.Create(telemetry);

            using var encoder = new JsonEncoder(context,
                jsonEncoding == JsonEncodingType.Verbose ?
                JsonEncoderOptions.Verbose :
                JsonEncoderOptions.Compact);
            encoder.WriteVariantValue(
                "D0",
                new int[,]
                {
                    { 1, 2, 3 },
                    { 4, 5, 6 }
                }.ToMatrixOf());
            encoder.WriteVariantValue(
                "D1",
                new int[,,]
                {
                    {
                        { 1, 2, 3 },
                        { 4, 5, 6 }
                    }
                }.ToMatrixOf());

            string actual = encoder.CloseAndReturnText();
            EncoderCommon.PrettifyAndValidateJson(actual, true);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeCompactExtensionObject()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """

                {
                    "D0": {
                        "UaTypeId": "i=884",
                        "High": 9876.5432
                    },
                    "D1": {
                        "UaType": 22,
                        "Value": {
                            "UaTypeId": "nsu=http://opcfoundation.org/UA/GDS/;i=1",
                            "ApplicationId": "nsu=urn:localhost:server;s=urn:123456789",
                            "ApplicationUri": "urn:localhost:test.org:client",
                            "ApplicationType": 1,
                            "ApplicationNames": [{ "Text":"Test Client", "Locale":"en" }],
                            "ProductUri": "http://test.org/client",
                            "DiscoveryUrls": ["opc.tcp://localhost/"]
                        }
                    }
                }

            """;

            var context = ServiceMessageContext.Create(telemetry);
            context.Factory.AddEncodeableTypes(typeof(Gds.ApplicationRecordDataType).Assembly);
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Gds.Namespaces.OpcUaGds);

            using var decoder = new JsonDecoder(data, context);
            ExtensionObject eo = decoder.ReadExtensionObject("D0");
            Assert.That(eo.TypeId.ToString(), Is.EqualTo(DataTypeIds.Range.ToString()));
            Assert.That(eo.TryGetValue(out Range range), Is.True);
            Assert.That(range, Is.Not.Null);
            Assert.That(range.Low, Is.Zero);
            Assert.That(range.High, Is.EqualTo(9876.5432));

            Variant v1 = decoder.ReadVariant("D1");
            Assert.That(v1.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));

            eo = v1.GetExtensionObject();
            Assert.That(eo.IsNull, Is.False);
            Assert.That(
                eo.TypeId.ToString(),
                Is.EqualTo(Gds.DataTypeIds.ApplicationRecordDataType.ToString()));

            Assert.That(eo.TryGetValue(out Gds.ApplicationRecordDataType record), Is.True);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.ApplicationType, Is.EqualTo(ApplicationType.Client));
            Assert.That(record.ApplicationNames[0].Text, Is.EqualTo("Test Client"));
        }

        [Test]
        public void EncodeCompactExtensionObject()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """

                {
                    "D0": {
                        "UaTypeId": "i=884",
                        "High": 9876.5432
                    },
                    "D1": {
                        "UaType": 22,
                        "Value": {
                            "UaTypeId": "nsu=http://opcfoundation.org/UA/GDS/;i=1",
                            "ApplicationId": "nsu=urn:localhost:server;s=urn:123456789",
                            "ApplicationUri": "urn:localhost:test.org:client",
                            "ApplicationType": 0,
                            "ApplicationNames": [{ "Text":"Test Client", "Locale":"en" }],
                            "ProductUri": "http://test.org/client",
                            "DiscoveryUrls": ["opc.tcp://localhost/"],
                            "ServerCapabilities": []
                        }
                    }
                }

""";

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = jsonObj.ToJsonString();
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Gds.Namespaces.OpcUaGds);

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact);
            encoder.WriteExtensionObject(
                "D0",
                new ExtensionObject(new Range { High = 9876.5432 }));

            encoder.WriteVariant(
                "D1",
                new Variant(
                    new ExtensionObject(
                        new Gds.ApplicationRecordDataType
                        {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationNames = new LocalizedText[] { new("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = Body
                        })));

            string actual = encoder.CloseAndReturnText();
            EncoderCommon.PrettifyAndValidateJson(actual, true);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecodeVerboseExtensionObject()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """

                {
                    "D0": {
                        "UaTypeId": "i=884",
                        "Low": 0,
                        "High": 9876.5432
                    },
                    "D1": {
                        "UaType": 22,
                        "Value": {
                            "UaTypeId": "nsu=http://opcfoundation.org/UA/GDS/;i=1",
                            "ApplicationId": "nsu=urn:localhost:server;s=urn:123456789",
                            "ApplicationUri": "urn:localhost:test.org:client",
                            "ApplicationType": "Client_1",
                            "ApplicationNames": [{ "Text":"Test Client", "Locale":"en" }],
                            "ProductUri": "http://test.org/client",
                            "DiscoveryUrls": ["opc.tcp://localhost/"],
                            "ServerCapabilities": []
                        }
                    }
                }

            """;

            var context = ServiceMessageContext.Create(telemetry);
            context.Factory.AddEncodeableTypes(typeof(Gds.ApplicationRecordDataType).Assembly);
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Gds.Namespaces.OpcUaGds);

            using var decoder = new JsonDecoder(data, context);
            ExtensionObject eo = decoder.ReadExtensionObject("D0");
            Assert.That(eo.TypeId.ToString(), Is.EqualTo(DataTypeIds.Range.ToString()));
            Assert.That(eo.TryGetValue(out Range range), Is.True);
            Assert.That(range, Is.Not.Null);
            Assert.That(range.Low, Is.Zero);
            Assert.That(range.High, Is.EqualTo(9876.5432));

            Variant v1 = decoder.ReadVariant("D1");
            Assert.That(v1.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));

            eo = v1.GetExtensionObject();
            Assert.That(eo.IsNull, Is.False);
            Assert.That(
                eo.TypeId.ToString(),
                Is.EqualTo(Gds.DataTypeIds.ApplicationRecordDataType.ToString()));

            Assert.That(eo.TryGetValue(out Gds.ApplicationRecordDataType record), Is.True);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.ApplicationType, Is.EqualTo(ApplicationType.Client));
            Assert.That(record.ApplicationNames[0].Text, Is.EqualTo("Test Client"));
        }

        [Test]
        public void EncodeVerboseExtensionObject()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string data = /*lang=json,strict*/
                """

                {
                    "D0": {
                        "UaTypeId": "i=884",
                        "Low": 0,
                        "High": 9876.5432
                    },
                    "D1": {
                        "UaType": 22,
                        "Value": {
                            "UaTypeId": "nsu=http://opcfoundation.org/UA/GDS/;i=1",
                            "ApplicationId": "nsu=urn:localhost:server;s=urn:123456789",
                            "ApplicationUri": "urn:localhost:test.org:client",
                            "ApplicationType": "Client_1",
                            "ApplicationNames": [{ "Text":"Test Client", "Locale":"en" }],
                            "ProductUri": "http://test.org/client",
                            "DiscoveryUrls": ["opc.tcp://localhost/"],
                            "ServerCapabilities": []
                        }
                    }
                }

""";

            var jsonObj = JsonNode.Parse(data, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            string expected = jsonObj.ToJsonString();
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Gds.Namespaces.OpcUaGds);

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            encoder.WriteExtensionObject(
                "D0",
                new ExtensionObject(new Range { Low = 0, High = 9876.5432 }));

            encoder.WriteVariant(
                "D1",
                new Variant(
                    new ExtensionObject(
                        new Gds.ApplicationRecordDataType
                        {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationType = ApplicationType.Client,
                            ApplicationNames = new LocalizedText[] { new("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = Body
                        })));

            string actual = encoder.CloseAndReturnText();
            EncoderCommon.PrettifyAndValidateJson(actual, true);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
