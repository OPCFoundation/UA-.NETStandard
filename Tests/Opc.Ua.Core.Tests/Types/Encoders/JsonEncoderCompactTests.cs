/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Opc.Ua.Bindings;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the Json encoder and decoder class.
    /// </summary>
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderCompactTests
    {
        const string NamespaceUri1 = "http://test.org/UA/Data1/";
        const string NamespaceUri2 = "tag:test.org,2024-07:schema:data:2";
        const string NamespaceUri3 = "urn:test.org:2024-07:schema:data:3";
        static readonly string[] NamespaceUris = new string[] { NamespaceUri1, NamespaceUri2, NamespaceUri3 };

        const string ServerUri1 = "http://test.org/product/server1/";
        const string ServerUri2 = "tag:test.org,2024-07:product:server:2";
        const string ServerUri3 = "urn:test.org:2024-07:product:server:3";
        static readonly string[] ServerUris = new string[] { ServerUri1, ServerUri2, ServerUri3 };

        const uint NumericId1 = 1234;
        const uint NumericId2 = 5678;
        const uint NumericId3 = 9876;
        static readonly uint[] NumericIds = new uint[] { NumericId1, NumericId2, NumericId3 };

        const string StringId1 = @"{""World"": ""Pandora""}";
        const string StringId2 = @"<World>Pandora</World>";
        const string StringId3 = @"http://world.org/Pandora/?alien=blue";
        static readonly string[] StringIds = new string[] { StringId1, StringId2, StringId3 };

        static readonly Guid GuidId1 = new Guid("73861B2D-EA9A-4B97-ACE6-9A2943EF641E");
        static readonly Guid GuidId2 = new Guid("BCFE58C8-CDC5-444F-B1F8-A12903008BE0");
        static readonly Guid GuidId3 = new Guid("C141B9D1-F1FD-4D15-9918-E37FD697EA1D");
        static readonly Guid[] GuidIds = new Guid[] { GuidId1, GuidId2, GuidId3 };

        static readonly byte[] OpaqueId1 = Utils.FromHexString("138DAA907373409AB6A4A36322063745");
        static readonly byte[] OpaqueId2 = Utils.FromHexString("E41047609A9248318EB907991A66B7BEE6B60CB5114828");
        static readonly byte[] OpaqueId3 = Utils.FromHexString("FBD8F0DE652A479B");
        static readonly byte[][] OpaqueIds = new byte[][] { OpaqueId1, OpaqueId2, OpaqueId3 };

        private string Escape(string input)
        {
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string ToString(Array input, int index)
        {
            var element = input.GetValue(index % input.Length);

            if (element is byte[] oid)
            {
                return Convert.ToBase64String(oid);
            }

            if (element is string sid)
            {
                return Escape(sid);
            }

            return (element != null) ? element.ToString() : String.Empty;
        }

        private T Get<T>(IList<T> input, int index)
        {
            return input[index % input.Count];
        }

        private void CheckDecodedNodeIds(ServiceMessageContext context, JsonDecoder decoder, int index)
        {
            var n0 = decoder.ReadNodeId("D0");
            Assert.AreEqual((int)n0.NamespaceIndex, 0);
            Assert.AreEqual(2263U, (uint)n0.Identifier);

            var n1 = decoder.ReadNodeId("D1");
            Assert.AreEqual((int)n1.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index)));
            Assert.AreEqual(Get(NumericIds, index), (uint)n1.Identifier);

            var n2 = decoder.ReadNodeId("D2");
            Assert.AreEqual((int)n2.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)));
            Assert.AreEqual(Get(StringIds, index), (string)n2.Identifier);

            var n3 = decoder.ReadNodeId("D3");
            Assert.AreEqual((int)n3.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)));
            Assert.AreEqual(Get(GuidIds, index), (Guid)n3.Identifier);

            var n4 = decoder.ReadNodeId("D4");
            Assert.AreEqual((int)n4.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)));
            Assert.AreEqual(Utils.ToHexString(Get(OpaqueIds, index)), Utils.ToHexString((byte[])n4.Identifier));
        }

        private void CheckDecodedExpandedNodeIds(ServiceMessageContext context, JsonDecoder decoder, int index)
        {
            var n0 = decoder.ReadExpandedNodeId("D0");
            Assert.AreEqual((int)n0.ServerIndex, 0);
            Assert.AreEqual((int)n0.NamespaceIndex, 0);
            Assert.AreEqual(2263U, (uint)n0.Identifier);

            var n1 = decoder.ReadExpandedNodeId("D1");
            Assert.AreEqual((int)n1.ServerIndex, 0);

            var uri = Get(NamespaceUris, index);
            var ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n1.NamespaceUri, uri);
            else Assert.AreEqual(n1.NamespaceIndex, ns);

            Assert.AreEqual(Get(NumericIds, index), (uint)n1.Identifier);

            var n2 = decoder.ReadExpandedNodeId("D2");
            Assert.AreEqual((int)n2.ServerIndex, 0);

            uri = Get(NamespaceUris, index + 1);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n2.NamespaceUri, uri);
            else Assert.AreEqual(n2.NamespaceIndex, ns);

            Assert.AreEqual(Get(StringIds, index), (string)n2.Identifier);

            var n3 = decoder.ReadExpandedNodeId("D3");
            Assert.AreEqual((int)n3.ServerIndex, 0);

            uri = Get(NamespaceUris, index + 2);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n3.NamespaceUri, uri);
            else Assert.AreEqual(n3.NamespaceIndex, ns);

            Assert.AreEqual(Get(GuidIds, index), (Guid)n3.Identifier);

            var n4 = decoder.ReadExpandedNodeId("D4");
            Assert.AreEqual((int)n4.ServerIndex, 0);

            uri = Get(NamespaceUris, index + 3);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n4.NamespaceUri, uri);
            else Assert.AreEqual(n4.NamespaceIndex, ns);

            Assert.AreEqual(Utils.ToHexString(Get(OpaqueIds, index)), Utils.ToHexString((byte[])n4.Identifier));

            var n5 = decoder.ReadExpandedNodeId("D5");
            Assert.AreEqual((int)n5.ServerIndex, context.ServerUris.GetIndex(Get(ServerUris, index)));

            uri = Get(NamespaceUris, index);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n5.NamespaceUri, uri);
            else Assert.AreEqual(n5.NamespaceIndex, ns);

            Assert.AreEqual(Get(NumericIds, index), (uint)n5.Identifier);

            var n6 = decoder.ReadExpandedNodeId("D6");
            Assert.AreEqual((int)n6.ServerIndex, context.ServerUris.GetIndex(Get(ServerUris, index + 1)));

            uri = Get(NamespaceUris, index + 1);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n6.NamespaceUri, uri);
            else Assert.AreEqual(n6.NamespaceIndex, ns);

            Assert.AreEqual(Get(StringIds, index), (string)n6.Identifier);

            var n7 = decoder.ReadExpandedNodeId("D7");
            Assert.AreEqual((int)n7.ServerIndex, context.ServerUris.GetIndex(Get(ServerUris, index + 2)));

            uri = Get(NamespaceUris, index + 2);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n7.NamespaceUri, uri);
            else Assert.AreEqual(n7.NamespaceIndex, ns);

            Assert.AreEqual(Get(GuidIds, index), (Guid)n7.Identifier);

            var n8 = decoder.ReadExpandedNodeId("D8");
            Assert.AreEqual((int)n8.ServerIndex, context.ServerUris.GetIndex(Get(ServerUris, index + 3)));

            uri = Get(NamespaceUris, index + 3);
            ns = context.NamespaceUris.GetIndex(uri);
            if (ns < 0) Assert.AreEqual(n8.NamespaceUri, uri);
            else Assert.AreEqual(n8.NamespaceIndex, ns);

            Assert.AreEqual(Utils.ToHexString(Get(OpaqueIds, index)), Utils.ToHexString((byte[])n8.Identifier));
        }

        private void CheckDecodedQualfiiedNames(ServiceMessageContext context, JsonDecoder decoder, int index)
        {
            var n0 = decoder.ReadQualifiedName("D0");
            Assert.AreEqual((int)n0.NamespaceIndex, 0);
            Assert.AreEqual("ServerStatus", n0.Name);

            var n1 = decoder.ReadQualifiedName("D1");
            Assert.AreEqual((int)n1.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index)));
            Assert.AreEqual("N1", n1.Name);

            var n2 = decoder.ReadQualifiedName("D2");
            Assert.AreEqual((int)n2.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)));
            Assert.AreEqual("N2", n2.Name);

            var n3 = decoder.ReadQualifiedName("D3");
            Assert.AreEqual((int)n3.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)));
            Assert.AreEqual("N3", n3.Name);

            var n4 = decoder.ReadQualifiedName("D4");
            Assert.AreEqual((int)n4.NamespaceIndex, context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)));
            Assert.AreEqual("N4", n4.Name);
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeCompactAndVerboseNodeId(int index)
        {
            var data = $@"
                {{
                    ""D0"": ""i=2263"",
                    ""D1"": ""nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D2"": ""nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D3"": ""nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D4"": ""nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}""
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                decoder.UpdateNamespaceTable = true;
                CheckDecodedNodeIds(context, decoder, index);
            }
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
            var data = $@"
                {{
                    ""D0"": ""i=2263"",
                    ""D1"": ""nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D2"": ""nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D3"": ""nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D4"": ""nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}"",
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            Array.ForEach(NamespaceUris, x => context.NamespaceUris.Append(x));

            using (var encoder = new JsonEncoder(context, jsonEncoding))
            {
                encoder.WriteNodeId("D0", new NodeId(2263));
                encoder.WriteNodeId("D1", new NodeId(Get(NumericIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteNodeId("D2", new NodeId(Get(StringIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteNodeId("D3", new NodeId(Get(GuidIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteNodeId("D4", new NodeId(Get(OpaqueIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeCompactAndVerboseExpandedNodeId(int index)
        {
            var data = $@"
                {{
                    ""D0"": ""i=2263"",
                    ""D1"": ""nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D2"": ""nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D3"": ""nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D4"": ""nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}"",
                    ""D5"": ""svu={ToString(ServerUris, index)};nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D6"": ""svu={ToString(ServerUris, index + 1)};nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D7"": ""svu={ToString(ServerUris, index + 2)};nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D8"": ""svu={ToString(ServerUris, index + 3)};nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}""
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                decoder.UpdateNamespaceTable = true;
                CheckDecodedExpandedNodeIds(context, decoder, index);
            }
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
            var data = $@"
                {{
                    ""D0"": ""i=2263"",
                    ""D1"": ""nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D2"": ""nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D3"": ""nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D4"": ""nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}"",
                    ""D5"": ""svu={ToString(ServerUris, index)};nsu={ToString(NamespaceUris, index)};i={ToString(NumericIds, index)}"",
                    ""D6"": ""svu={ToString(ServerUris, index + 1)};nsu={ToString(NamespaceUris, index + 1)};s={ToString(StringIds, index)}"",
                    ""D7"": ""svu={ToString(ServerUris, index + 2)};nsu={ToString(NamespaceUris, index + 2)};g={ToString(GuidIds, index)}"",
                    ""D8"": ""svu={ToString(ServerUris, index + 3)};nsu={ToString(NamespaceUris, index + 3)};b={ToString(OpaqueIds, index)}""
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            Array.ForEach(NamespaceUris, x => context.NamespaceUris.Append(x));
            context.ServerUris.Append("http://server-placeholder");
            Array.ForEach(ServerUris, x => context.ServerUris.Append(x));

            using (var encoder = new JsonEncoder(context, jsonEncoding))
            {
                encoder.WriteExpandedNodeId("D0", new ExpandedNodeId(2263));
                encoder.WriteExpandedNodeId("D1", new ExpandedNodeId(Get(NumericIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteExpandedNodeId("D2", new ExpandedNodeId(Get(StringIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteExpandedNodeId("D3", new ExpandedNodeId(Get(GuidIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteExpandedNodeId("D4", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));
                encoder.WriteExpandedNodeId("D5", new ExpandedNodeId(Get(NumericIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index)), null, (uint)context.ServerUris.GetIndex(Get(ServerUris, index))));
                encoder.WriteExpandedNodeId("D6", new ExpandedNodeId(Get(StringIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)), null, (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 1))));
                encoder.WriteExpandedNodeId("D7", new ExpandedNodeId(Get(GuidIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)), null, (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 2))));
                encoder.WriteExpandedNodeId("D8", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)), null, (uint)context.ServerUris.GetIndex(Get(ServerUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeReversibleNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index))} }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))} }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))} }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))} }}
                }}
            ";

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var decoder = new JsonDecoder(data, context2))
            {
                decoder.UpdateNamespaceTable = false;
                decoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);
                CheckDecodedNodeIds(context2, decoder, index);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EncodeReversibleNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index))} }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))} }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))} }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))} }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.Reversible))
            {
                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteNodeId("D0", new NodeId(2263));
                encoder.WriteNodeId("D1", new NodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteNodeId("D2", new NodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteNodeId("D3", new NodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteNodeId("D4", new NodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeReversibleExpandedNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);
            context1.ServerUris.Append("http://server-placeholder");
            context1.ServerUris.Append(ServerUris[0]);
            context1.ServerUris.Append(ServerUris[1]);
            context1.ServerUris.Append(ServerUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index))} }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))} }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))} }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))} }},
                    ""D5"": {{ ""Id"": {ToString(NumericIds, index)}, ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index))}, ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D6"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 1))}, ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D7"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 2))}, ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D8"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 3))}, ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }}
                }}
            ";

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);
            context2.ServerUris.Append("http://server-placeholder");
            context2.ServerUris.Append(ServerUris[2]);
            context2.ServerUris.Append(ServerUris[0]);
            context2.ServerUris.Append(ServerUris[1]);

            using (var decoder = new JsonDecoder(data, context2))
            {
                decoder.UpdateNamespaceTable = false;
                decoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);
                CheckDecodedExpandedNodeIds(context2, decoder, index);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EncodeReversibleExpandedNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);
            context1.ServerUris.Append("http://server-placeholder");
            context1.ServerUris.Append(ServerUris[0]);
            context1.ServerUris.Append(ServerUris[1]);
            context1.ServerUris.Append(ServerUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index))} }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))} }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))} }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))} }},
                    ""D5"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index))}, ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index))} }},
                    ""D6"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))}, ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 1))} }},
                    ""D7"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))}, ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 2))} }},
                    ""D8"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))}, ""ServerUri"": {context1.ServerUris.GetIndex(Get(ServerUris, index + 3))} }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);
            context2.ServerUris.Append("http://server-placeholder");
            context2.ServerUris.Append(ServerUris[2]);
            context2.ServerUris.Append(ServerUris[0]);
            context2.ServerUris.Append(ServerUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.Reversible))
            {
                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteExpandedNodeId("D0", new ExpandedNodeId(2263));
                encoder.WriteExpandedNodeId("D1", new ExpandedNodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteExpandedNodeId("D2", new ExpandedNodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteExpandedNodeId("D3", new ExpandedNodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteExpandedNodeId("D4", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));
                encoder.WriteExpandedNodeId("D5", new ExpandedNodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index))));
                encoder.WriteExpandedNodeId("D6", new ExpandedNodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 1))));
                encoder.WriteExpandedNodeId("D7", new ExpandedNodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 2))));
                encoder.WriteExpandedNodeId("D8", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeNonReversibleNodeId(int index)
        {
            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }}
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                decoder.UpdateNamespaceTable = true;
                CheckDecodedNodeIds(context, decoder, index);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EncodeNonReversibleNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.NonReversible))
            {
                //encoder.ForceNamespaceUri = true;
                //encoder.ForceNamespaceUriForIndex1 = true;
                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteNodeId("D0", new NodeId(2263));
                encoder.WriteNodeId("D1", new NodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteNodeId("D2", new NodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteNodeId("D3", new NodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteNodeId("D4", new NodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DecodeNonReversibleExpandedNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);
            context1.ServerUris.Append("http://server-placeholder");
            context1.ServerUris.Append(ServerUris[0]);
            context1.ServerUris.Append(ServerUris[1]);
            context1.ServerUris.Append(ServerUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }},
                    ""D5"": {{ ""Id"": {ToString(NumericIds, index)}, ""ServerUri"": ""{Get(ServerUris, index)}"", ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D6"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""ServerUri"": ""{Get(ServerUris, index + 1)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D7"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""ServerUri"": ""{Get(ServerUris, index + 2)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D8"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""ServerUri"": ""{Get(ServerUris, index + 3)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }}
                }}
            ";

            var context2 = new ServiceMessageContext();
            context2.ServerUris.Append("http://server-placeholder");

            using (var decoder = new JsonDecoder(data, context2))
            {
                decoder.UpdateNamespaceTable = true;
                CheckDecodedExpandedNodeIds(context2, decoder, index);
            }
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EncodeNonReversibleExpandedNodeId(int index)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);
            context1.ServerUris.Append("http://server-placeholder");
            context1.ServerUris.Append(ServerUris[0]);
            context1.ServerUris.Append(ServerUris[1]);
            context1.ServerUris.Append(ServerUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Id"": 2263 }},
                    ""D1"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":""{Get(NamespaceUris, index)}"" }},
                    ""D2"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"" }},
                    ""D3"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"" }},
                    ""D4"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"" }},
                    ""D5"": {{ ""Id"": {ToString(NumericIds, index)}, ""Namespace"":""{Get(NamespaceUris, index)}"", ""ServerUri"": ""{Get(ServerUris, index)}"" }},
                    ""D6"": {{ ""IdType"": 1, ""Id"": ""{ToString(StringIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 1)}"", ""ServerUri"": ""{Get(ServerUris, index + 1)}"" }},
                    ""D7"": {{ ""IdType"": 2, ""Id"": ""{ToString(GuidIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 2)}"", ""ServerUri"": ""{Get(ServerUris, index + 2)}"" }},
                    ""D8"": {{ ""IdType"": 3, ""Id"": ""{ToString(OpaqueIds, index)}"", ""Namespace"":""{Get(NamespaceUris, index + 3)}"", ""ServerUri"": ""{Get(ServerUris, index + 3)}"" }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);
            context2.ServerUris.Append("http://server-placeholder");
            context2.ServerUris.Append(ServerUris[2]);
            context2.ServerUris.Append(ServerUris[0]);
            context2.ServerUris.Append(ServerUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.NonReversible))
            {
                // encoder.ForceNamespaceUri = true;
                // encoder.ForceNamespaceUriForIndex1 = true;

                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteExpandedNodeId("D0", new ExpandedNodeId(2263));
                encoder.WriteExpandedNodeId("D1", new ExpandedNodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index))));
                encoder.WriteExpandedNodeId("D2", new ExpandedNodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1))));
                encoder.WriteExpandedNodeId("D3", new ExpandedNodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2))));
                encoder.WriteExpandedNodeId("D4", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3))));
                encoder.WriteExpandedNodeId("D5", new ExpandedNodeId(Get(NumericIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index))));
                encoder.WriteExpandedNodeId("D6", new ExpandedNodeId(Get(StringIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 1)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 1))));
                encoder.WriteExpandedNodeId("D7", new ExpandedNodeId(Get(GuidIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 2)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 2))));
                encoder.WriteExpandedNodeId("D8", new ExpandedNodeId(Get(OpaqueIds, index), (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, index + 3)), null, (uint)context2.ServerUris.GetIndex(Get(ServerUris, index + 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeCompactAndVerboseQualifiedName()
        {
            var data = $@"
                {{
                    ""D0"": ""ServerStatus"",
                    ""D1"": ""nsu={ToString(NamespaceUris, 0)};N1"",
                    ""D2"": ""nsu={ToString(NamespaceUris, 1)};N2"",
                    ""D3"": ""nsu={ToString(NamespaceUris, 2)};N3"",
                    ""D4"": ""nsu={ToString(NamespaceUris, 3)};N4""
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                decoder.UpdateNamespaceTable = true;
                CheckDecodedQualfiiedNames(context, decoder, 0);
            }
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void EncodeCompactAndVerboseQualifiedName(JsonEncodingType jsonEncoding)
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": ""ServerStatus"",
                    ""D1"": ""nsu={ToString(NamespaceUris, 0)};N1"",
                    ""D2"": ""nsu={ToString(NamespaceUris, 1)};N2"",
                    ""D3"": ""nsu={ToString(NamespaceUris, 2)};N3"",
                    ""D4"": ""nsu={ToString(NamespaceUris, 3)};N4""
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var encoder = new JsonEncoder(context2, jsonEncoding))
            {
                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteQualifiedName("D0", new QualifiedName("ServerStatus"));
                encoder.WriteQualifiedName("D1", new QualifiedName("N1", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 0))));
                encoder.WriteQualifiedName("D2", new QualifiedName("N2", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 1))));
                encoder.WriteQualifiedName("D3", new QualifiedName("N3", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 2))));
                encoder.WriteQualifiedName("D4", new QualifiedName("N4", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeReversibleQualifiedName()
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Name"": ""ServerStatus"" }},
                    ""D1"": {{ ""Name"": ""N1"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 0))} }},
                    ""D2"": {{ ""Name"": ""N2"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 1))} }},
                    ""D3"": {{ ""Name"": ""N3"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 2))} }},
                    ""D4"": {{ ""Name"": ""N4"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 3))} }}
                }}
            ";

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var decoder = new JsonDecoder(data, context2))
            {
                decoder.UpdateNamespaceTable = false;
                decoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);
                CheckDecodedQualfiiedNames(context2, decoder, 0);
            }
        }

        [Test]
        public void EncodeReversibleQualifiedName()
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Name"": ""ServerStatus"" }},
                    ""D1"": {{ ""Name"": ""N1"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 0))} }},
                    ""D2"": {{ ""Name"": ""N2"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 1))} }},
                    ""D3"": {{ ""Name"": ""N3"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 2))} }},
                    ""D4"": {{ ""Name"": ""N4"", ""Uri"":{context1.NamespaceUris.GetIndex(Get(NamespaceUris, 3))} }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.Reversible))
            {
                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteQualifiedName("D0", new QualifiedName("ServerStatus"));
                encoder.WriteQualifiedName("D1", new QualifiedName("N1", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 0))));
                encoder.WriteQualifiedName("D2", new QualifiedName("N2", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 1))));
                encoder.WriteQualifiedName("D3", new QualifiedName("N3", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 2))));
                encoder.WriteQualifiedName("D4", new QualifiedName("N4", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeNonReversibleQualifiedName()
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Name"":""ServerStatus"" }},
                    ""D1"": {{ ""Name"": ""N1"", ""Uri"":""{Get(NamespaceUris, 0)}"" }},
                    ""D2"": {{ ""Name"": ""N2"", ""Uri"":""{Get(NamespaceUris, 1)}"" }},
                    ""D3"": {{ ""Name"": ""N3"", ""Uri"":""{Get(NamespaceUris, 2)}"" }},
                    ""D4"": {{ ""Name"": ""N4"", ""Uri"":""{Get(NamespaceUris, 3)}"" }}
                }}
            ";

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var decoder = new JsonDecoder(data, context2))
            {
                decoder.UpdateNamespaceTable = false;
                decoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);
                CheckDecodedQualfiiedNames(context2, decoder, 0);
            }
        }

        [Test]
        public void EncodeNonReversibleQualifiedName()
        {
            var context1 = new ServiceMessageContext();
            context1.NamespaceUris.Append(NamespaceUris[0]);
            context1.NamespaceUris.Append(NamespaceUris[1]);
            context1.NamespaceUris.Append(NamespaceUris[2]);

            var data = $@"
                {{
                    ""D0"": {{ ""Name"": ""ServerStatus"" }},
                    ""D1"": {{ ""Name"": ""N1"", ""Uri"":""{Get(NamespaceUris, 0)}"" }},
                    ""D2"": {{ ""Name"": ""N2"", ""Uri"":""{Get(NamespaceUris, 1)}"" }},
                    ""D3"": {{ ""Name"": ""N3"", ""Uri"":""{Get(NamespaceUris, 2)}"" }},
                    ""D4"": {{ ""Name"": ""N4"", ""Uri"":""{Get(NamespaceUris, 3)}"" }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context2 = new ServiceMessageContext();
            context2.NamespaceUris.Append(NamespaceUris[2]);
            context2.NamespaceUris.Append(NamespaceUris[0]);
            context2.NamespaceUris.Append(NamespaceUris[1]);

            using (var encoder = new JsonEncoder(context2, JsonEncodingType.NonReversible))
            {
                // encoder.ForceNamespaceUri = true;
                // encoder.ForceNamespaceUriForIndex1 = true;

                encoder.SetMappingTables(context1.NamespaceUris, context1.ServerUris);

                encoder.WriteQualifiedName("D0", new QualifiedName("ServerStatus"));
                encoder.WriteQualifiedName("D1", new QualifiedName("N1", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 0))));
                encoder.WriteQualifiedName("D2", new QualifiedName("N2", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 1))));
                encoder.WriteQualifiedName("D3", new QualifiedName("N3", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 2))));
                encoder.WriteQualifiedName("D4", new QualifiedName("N4", (ushort)context2.NamespaceUris.GetIndex(Get(NamespaceUris, 3))));

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeCompactAndVerboseMatrix()
        {
            var data = $@"
                {{
                    ""D0"": {{ ""Dimensions"": [ 2, 3 ], ""Array"": [ 1, 2, 3, 4, 5, 6 ] }},
                    ""D1"": {{ ""Dimensions"": [ 1, 2, 3 ], ""Array"": [ 1, 2, 3, 4, 5, 6 ] }}
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                Array a1 = decoder.ReadArray("D0", 2, BuiltInType.Int64);
                Assert.AreEqual(2, a1.Rank);
                Assert.AreEqual(6, a1.Length);
                Assert.AreEqual(2, a1.GetLength(0));
                Assert.AreEqual(3, a1.GetLength(1));

                Array a2 = decoder.ReadArray("D1", 2, BuiltInType.Int64);
                Assert.AreEqual(3, a2.Rank);
                Assert.AreEqual(6, a2.Length);
                Assert.AreEqual(1, a2.GetLength(0));
                Assert.AreEqual(2, a2.GetLength(1));
                Assert.AreEqual(3, a2.GetLength(2));
            }
        }

        [Test]
        [TestCase(JsonEncodingType.Compact)]
        [TestCase(JsonEncodingType.Verbose)]
        public void EncodeCompactAndVerboseMatrix(JsonEncodingType jsonEncoding)
        {
            var data = $@"
                {{
                    ""D0"": {{ ""Dimensions"": [ 2, 3 ], ""Array"": [ 1, 2, 3, 4, 5, 6 ] }},
                    ""D1"": {{ ""Dimensions"": [ 1, 2, 3 ], ""Array"": [ 1, 2, 3, 4, 5, 6 ] }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();

            using (var encoder = new JsonEncoder(context, jsonEncoding))
            {
                encoder.WriteArray("D0", new int[,] { { 1, 2, 3 }, { 4, 5, 6 } }, 2, BuiltInType.Int32);
                encoder.WriteArray("D1", new int[,,] { { { 1, 2, 3 }, { 4, 5, 6 } } }, 3, BuiltInType.Int32);

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeReversibleMatrix()
        {
            var data = $@"
                {{
                    ""D0"": [[1, 2, 3], [4, 5, 6]],
                    ""D1"": [[[1, 2, 3], [4, 5, 6]]]
                }}
            ";

            var context = new ServiceMessageContext();

            using (var decoder = new JsonDecoder(data, context))
            {
                Array a1 = decoder.ReadArray("D0", 2, BuiltInType.Int64);
                Assert.AreEqual(2, a1.Rank);
                Assert.AreEqual(6, a1.Length);
                Assert.AreEqual(2, a1.GetLength(0));
                Assert.AreEqual(3, a1.GetLength(1));

                Array a2 = decoder.ReadArray("D1", 2, BuiltInType.Int64);
                Assert.AreEqual(3, a2.Rank);
                Assert.AreEqual(6, a2.Length);
                Assert.AreEqual(1, a2.GetLength(0));
                Assert.AreEqual(2, a2.GetLength(1));
                Assert.AreEqual(3, a2.GetLength(2));
            }
        }

        [Test]
        [TestCase(JsonEncodingType.Reversible)]
        [TestCase(JsonEncodingType.NonReversible)]
        public void EncodeReversibleAndNonReversibleMatrix(JsonEncodingType jsonEncoding)
        {
            var data = $@"
                {{
                    ""D0"": [[1, 2, 3], [4, 5, 6]],
                    ""D1"": [[[1, 2, 3], [4, 5, 6]]]
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();

            using (var encoder = new JsonEncoder(context, jsonEncoding))
            {
                encoder.WriteArray("D0", new int[,] { { 1, 2, 3 }, { 4, 5, 6 } }, 2, BuiltInType.Int32);
                encoder.WriteArray("D1", new int[,,] { { { 1, 2, 3 }, { 4, 5, 6 } } }, 3, BuiltInType.Int32);

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeCompactExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""UaTypeId"": ""i=884"",
                        ""High"": 9876.5432
                    }},
                    ""D1"": {{ 
                        ""UaType"": 22,
                        ""Body"": {{ 
                            ""UaTypeId"": ""nsu=http://opcfoundation.org/UA/GDS/;i=1"", 
                            ""ApplicationId"": ""nsu=urn:localhost:server;s=urn:123456789"",  
                            ""ApplicationUri"": ""urn:localhost:test.org:client"",
                            ""ApplicationType"": 1,
                            ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                            ""ProductUri"": ""http://test.org/client"",
                            ""DiscoveryUrls"": [""opc.tcp://localhost/""]
                        }}
                    }}
                }}
            ";

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var decoder = new JsonDecoder(data, context))
            {
                var eo = decoder.ReadExtensionObject("D0");
                Assert.AreEqual(Opc.Ua.DataTypeIds.Range.ToString(), eo.TypeId.ToString());
                var range = eo.Body as Opc.Ua.Range;
                Assert.IsNotNull(range);
                Assert.AreEqual(0, range.Low);
                Assert.AreEqual(9876.5432, range.High);

                var v1 = decoder.ReadVariant("D1");
                Assert.AreEqual(v1.TypeInfo.BuiltInType, BuiltInType.ExtensionObject);

                eo = v1.Value as ExtensionObject;
                Assert.IsNotNull(eo);
                Assert.AreEqual(Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType.ToString(), eo.TypeId.ToString());

                var record = eo.Body as Opc.Ua.Gds.ApplicationRecordDataType;
                Assert.IsNotNull(record);
                Assert.AreEqual(Opc.Ua.ApplicationType.Client, record.ApplicationType);
                Assert.AreEqual("Test Client", record.ApplicationNames[0].Text);
            }
        }

        [Test]
        public void EncodeCompactExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""UaTypeId"": ""i=884"",
                        ""High"": 9876.5432
                    }},
                    ""D1"": {{ 
                        ""UaType"": 22,
                        ""Value"": {{ 
                            ""UaTypeId"": ""nsu=http://opcfoundation.org/UA/GDS/;i=1"", 
                            ""ApplicationId"": ""nsu=urn:localhost:server;s=urn:123456789"",  
                            ""ApplicationUri"": ""urn:localhost:test.org:client"",
                            ""ApplicationType"": 0,
                            ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                            ""ProductUri"": ""http://test.org/client"",
                            ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                            ""ServerCapabilities"": []
                        }}
                    }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var encoder = new JsonEncoder(context, JsonEncodingType.Compact))
            {
                encoder.WriteExtensionObject(
                    "D0",
                    new ExtensionObject(
                        Opc.Ua.DataTypeIds.Range,
                        new Opc.Ua.Range() { High = 9876.5432 })
                );

                encoder.WriteVariant(
                    "D1",
                    new Variant(new ExtensionObject(
                        Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType,
                        new Opc.Ua.Gds.ApplicationRecordDataType() {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationNames = new LocalizedText[] { new LocalizedText("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = new string[] { "opc.tcp://localhost/" },
                        }))
                );

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeVerboseExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""TypeId"": ""i=884"",
                        ""Body"": {{ ""Low"": 0, ""High"": 9876.5432 }}
                    }},
                    ""D1"": {{ 
                        ""Type"": 22,
                        ""Body"": {{ 
                            ""TypeId"": ""nsu=http://opcfoundation.org/UA/GDS/;i=1"", 
                            ""Body"": {{ 
                                ""ApplicationId"": ""nsu=urn:localhost:server;s=urn:123456789"",  
                                ""ApplicationUri"": ""urn:localhost:test.org:client"",
                                ""ApplicationType"": ""Client_1"",
                                ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                                ""ProductUri"": ""http://test.org/client"",
                                ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                                ""ServerCapabilities"": []
                            }}
                        }}
                    }}
                }}
            ";

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var decoder = new JsonDecoder(data, context))
            {
                var eo = decoder.ReadExtensionObject("D0");
                Assert.AreEqual(Opc.Ua.DataTypeIds.Range.ToString(), eo.TypeId.ToString());
                var range = eo.Body as Opc.Ua.Range;
                Assert.IsNotNull(range);
                Assert.AreEqual(0, range.Low);
                Assert.AreEqual(9876.5432, range.High);

                var v1 = decoder.ReadVariant("D1");
                Assert.AreEqual(v1.TypeInfo.BuiltInType, BuiltInType.ExtensionObject);

                eo = v1.Value as ExtensionObject;
                Assert.IsNotNull(eo);
                Assert.AreEqual(Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType.ToString(), eo.TypeId.ToString());

                var record = eo.Body as Opc.Ua.Gds.ApplicationRecordDataType;
                Assert.IsNotNull(record);
                Assert.AreEqual(Opc.Ua.ApplicationType.Client, record.ApplicationType);
                Assert.AreEqual("Test Client", record.ApplicationNames[0].Text);
            }
        }

        [Test]
        public void EncodeVerboseExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""UaTypeId"": ""i=884"",
                        ""Low"": 0,
                        ""High"": 9876.5432
                    }},
                    ""D1"": {{ 
                        ""UaType"": 22,
                        ""Value"": {{ 
                            ""UaTypeId"": ""nsu=http://opcfoundation.org/UA/GDS/;i=1"", 
                            ""ApplicationId"": ""nsu=urn:localhost:server;s=urn:123456789"",  
                            ""ApplicationUri"": ""urn:localhost:test.org:client"",
                            ""ApplicationType"": ""Client_1"",
                            ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                            ""ProductUri"": ""http://test.org/client"",
                            ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                            ""ServerCapabilities"": []
                        }}
                    }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var encoder = new JsonEncoder(context, JsonEncodingType.Verbose))
            {
                encoder.WriteExtensionObject(
                    "D0",
                    new ExtensionObject(
                        Opc.Ua.DataTypeIds.Range,
                        new Opc.Ua.Range() { Low = 0, High = 9876.5432 })
                );

                encoder.WriteVariant(
                    "D1",
                    new Variant(new ExtensionObject(
                        Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType,
                        new Opc.Ua.Gds.ApplicationRecordDataType() {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationType = Opc.Ua.ApplicationType.Client,
                            ApplicationNames = new LocalizedText[] { new LocalizedText("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = new string[] { "opc.tcp://localhost/" },
                        }))
                );

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeReversibleExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""TypeId"": {{ ""Id"": 884 }},
                        ""Body"": {{ ""High"": 9876.5432 }}
                    }},
                    ""D1"": {{ 
                        ""Type"": 22,
                        ""Body"": {{ 
                            ""TypeId"": {{ ""Id"": 1, ""Namespace"": 2 }},
                            ""Body"": {{ 
                                ""ApplicationId"": {{ ""IdType"":1, ""Id"":""urn:123456789"",""Namespace"":1 }},  
                                ""ApplicationUri"": ""urn:localhost:test.org:client"",
                                ""ApplicationType"": 1,
                                ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                                ""ProductUri"": ""http://test.org/client"",
                                ""DiscoveryUrls"": [""opc.tcp://localhost/""]
                            }}
                        }}
                    }}
                }}
            ";

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var decoder = new JsonDecoder(data, context))
            {
                var eo = decoder.ReadExtensionObject("D0");
                Assert.AreEqual(Opc.Ua.DataTypeIds.Range.ToString(), eo.TypeId.ToString());
                var range = eo.Body as Opc.Ua.Range;
                Assert.IsNotNull(range);
                Assert.AreEqual(0, range.Low);
                Assert.AreEqual(9876.5432, range.High);

                var v1 = decoder.ReadVariant("D1");
                Assert.AreEqual(v1.TypeInfo.BuiltInType, BuiltInType.ExtensionObject);

                eo = v1.Value as ExtensionObject;
                Assert.IsNotNull(eo);
                Assert.AreEqual(Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType.ToString(), eo.TypeId.ToString());

                var record = eo.Body as Opc.Ua.Gds.ApplicationRecordDataType;
                Assert.IsNotNull(record);
                Assert.AreEqual(Opc.Ua.ApplicationType.Client, record.ApplicationType);
                Assert.AreEqual("Test Client", record.ApplicationNames[0].Text);
            }
        }

        [Test]
        public void EncodeReversibleExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ 
                        ""TypeId"": {{ ""Id"": 884 }},
                        ""Body"": {{ ""Low"":0, ""High"": 9876.5432 }}
                    }},
                    ""D1"": {{ 
                        ""Type"": 22,
                        ""Body"": {{ 
                            ""TypeId"": {{ ""Id"": 1, ""Namespace"": 2 }},
                            ""Body"": {{ 
                                ""ApplicationId"": {{ ""IdType"":1, ""Id"":""urn:123456789"",""Namespace"":1 }},  
                                ""ApplicationUri"": ""urn:localhost:test.org:client"",
                                ""ApplicationType"": 1,
                                ""ApplicationNames"": [{{ ""Text"":""Test Client"", ""Locale"":""en"" }}],
                                ""ProductUri"": ""http://test.org/client"",
                                ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                                ""ServerCapabilities"": []
                            }}
                        }}
                    }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var encoder = new JsonEncoder(context, JsonEncodingType.Reversible))
            {
                encoder.WriteExtensionObject(
                    "D0",
                    new ExtensionObject(
                        Opc.Ua.DataTypeIds.Range,
                        new Opc.Ua.Range() { High = 9876.5432 })
                );

                encoder.WriteVariant(
                    "D1",
                    new Variant(new ExtensionObject(
                        Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType,
                        new Opc.Ua.Gds.ApplicationRecordDataType() {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationType = Opc.Ua.ApplicationType.Client,
                            ApplicationNames = new LocalizedText[] { new LocalizedText("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = new string[] { "opc.tcp://localhost/" },
                        }))
                );

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void DecodeNonReversibleExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ ""Low"": 0, ""High"": 9876.5432 }},
                    ""D1"": {{
                        ""ApplicationId"": {{ ""IdType"":1, ""Id"":""urn:123456789"",""Namespace"":""urn:localhost:server"" }},  
                        ""ApplicationUri"": ""urn:localhost:test.org:client"",
                        ""ApplicationType"": ""Client_1"",
                        ""ApplicationNames"": [""Test Client""],
                        ""ProductUri"": ""http://test.org/client"",
                        ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                        ""ServerCapabilities"": []
                    }}
                }}
            ";

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");
            context.NamespaceUris.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);

            using (var decoder = new JsonDecoder(data, context))
            {
                var range = decoder.ReadEncodeable("D0", typeof(Opc.Ua.Range)) as Opc.Ua.Range;
                Assert.IsNotNull(range);
                Assert.AreEqual(0, range.Low);
                Assert.AreEqual(9876.5432, range.High);

                var record = decoder.ReadEncodeable("D1", typeof(Opc.Ua.Gds.ApplicationRecordDataType)) as Opc.Ua.Gds.ApplicationRecordDataType;
                Assert.IsNotNull(record);
                Assert.AreEqual(Opc.Ua.ApplicationType.Client, record.ApplicationType);
                Assert.AreEqual("Test Client", record.ApplicationNames[0].Text);
            }
        }

        [Test]
        public void EncodeNonReversibleExtensionObject()
        {
            var data = $@"
                {{
                    ""D0"": {{ ""Low"": 0, ""High"": 9876.5432 }},
                    ""D1"": {{
                        ""ApplicationId"": {{ ""IdType"":1, ""Id"":""urn:123456789"",""Namespace"":""urn:localhost:server"" }},  
                        ""ApplicationUri"": ""urn:localhost:test.org:client"",
                        ""ApplicationType"": ""Client_1"",
                        ""ApplicationNames"": [""Test Client""],
                        ""ProductUri"": ""http://test.org/client"",
                        ""DiscoveryUrls"": [""opc.tcp://localhost/""],
                        ""ServerCapabilities"": []
                    }}
                }}
            ";

            JObject jsonObj = JObject.Parse(data);
            string expected = JsonConvert.SerializeObject(jsonObj, Formatting.None);
            EncoderCommon.PrettifyAndValidateJson(expected, true);

            var context = new ServiceMessageContext();
            context.NamespaceUris.Append("urn:localhost:server");

            using (var encoder = new JsonEncoder(context, JsonEncodingType.NonReversible))
            {
                encoder.WriteExtensionObject(
                    "D0",
                    new ExtensionObject(
                        Opc.Ua.DataTypeIds.Range,
                        new Opc.Ua.Range() { Low = 0, High = 9876.5432 })
                );

                encoder.WriteVariant(
                    "D1",
                    new Variant(new ExtensionObject(
                        Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType,
                        new Opc.Ua.Gds.ApplicationRecordDataType() {
                            ApplicationId = new NodeId("urn:123456789", 1),
                            ApplicationUri = "urn:localhost:test.org:client",
                            ApplicationType = Opc.Ua.ApplicationType.Client,
                            ApplicationNames = new LocalizedText[] { new LocalizedText("en", "Test Client") },
                            ProductUri = "http://test.org/client",
                            DiscoveryUrls = new string[] { "opc.tcp://localhost/" },
                        }))
                );

                string actual = encoder.CloseAndReturnText();
                EncoderCommon.PrettifyAndValidateJson(actual, true);
                Assert.AreEqual(expected, actual);
            }
        }
    }
}
