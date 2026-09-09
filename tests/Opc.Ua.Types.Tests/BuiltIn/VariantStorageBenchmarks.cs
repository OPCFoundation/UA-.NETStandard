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
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Types.Tests.State;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Measures scalar storage and consumption with identical, shared inner payloads.
    /// </summary>
    [MemoryDiagnoser]
    [Config(typeof(NodeStateStorageBenchmarks.StorageBenchmarkConfig))]
    public class VariantStorageBenchmarks
    {
        [Params("QualifiedName", "NodeId", "ByteString", "LocalizedText", "RichText", "Guid")]
        public string Kind { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_input = Kind switch
            {
                "QualifiedName" => m_name,
                "NodeId" => m_id,
                "ByteString" => m_bytes,
                "LocalizedText" => m_text,
                "RichText" => m_richText,
                "Guid" => m_guid,
                _ => throw new ArgumentException("Unknown benchmark kind.")
            };
            m_variant = ConstructTyped();
            m_context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.CreateForBenchmarks());
        }

        [Benchmark]
        public Variant ConstructTyped()
        {
            return Kind switch
            {
                "QualifiedName" => new Variant(m_name),
                "NodeId" => new Variant(m_id),
                "ByteString" => new Variant(m_bytes),
                "LocalizedText" => new Variant(m_text),
                "RichText" => new Variant(m_richText),
                "Guid" => new Variant(m_guid),
                _ => throw new ArgumentException("Unknown benchmark kind.")
            };
        }

        [Benchmark]
        public Variant ConstructGeneric()
        {
            return VariantHelper.CastFrom(m_input);
        }

        [Benchmark]
        public BaseDataVariableState ConstructNode()
        {
            return new BaseDataVariableState(null) { Value = ConstructTyped() };
        }

        [Benchmark]
        public BaseDataVariableState ConstructGenericNode()
        {
            return new BaseDataVariableState(null) { Value = VariantHelper.CastFrom(m_input) };
        }

        [Benchmark]
        public int ExtractAndHash()
        {
            return Kind switch
            {
                "QualifiedName" => m_variant.GetQualifiedName().GetHashCode(),
                "NodeId" => m_variant.GetNodeId().GetHashCode(),
                "ByteString" => m_variant.GetByteString().GetHashCode(),
                "LocalizedText" or "RichText" => m_variant.GetLocalizedText().GetHashCode(),
                "Guid" => m_variant.GetGuid().GetHashCode(),
                _ => throw new ArgumentException("Unknown benchmark kind.")
            };
        }

        [Benchmark]
        public ByteString BinaryRoundTrip()
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteVariant(null, m_variant);
            byte[] buffer = encoder.CloseAndReturnBuffer();
            using var decoder = new BinaryDecoder(buffer, m_context);
            Variant decoded = decoder.ReadVariant(null);
            if (!decoded.Equals(m_variant))
            {
                throw new InvalidOperationException("Codec changed the value.");
            }
            return new ByteString(buffer);
        }

        [TestCase("QualifiedName")]
        [TestCase("NodeId")]
        [TestCase("ByteString")]
        [TestCase("LocalizedText")]
        [TestCase("RichText")]
        [TestCase("Guid")]
        public void BenchmarkInputsRetainTheirKindsAndConsumeValues(string kind)
        {
            Kind = kind;
            Setup();
            Assert.That(ConstructGeneric(), Is.EqualTo(ConstructTyped()));
            Assert.That(ConstructNode().Value, Is.EqualTo(m_variant));
            Assert.That(ConstructGenericNode().Value, Is.EqualTo(m_variant));
            Assert.That(ExtractAndHash(), Is.EqualTo(m_input.GetHashCode()));
            Assert.That(BinaryRoundTrip().Length, Is.GreaterThan(0));
        }

        private readonly QualifiedName m_name = new("cached-payload", 7);
        private readonly NodeId m_id = new(new Guid("00112233-4455-6677-8899-aabbccddeeff"), 7);
        private readonly ByteString m_bytes = new(new byte[64]);
        private readonly LocalizedText m_text = new("cached-payload");
        private readonly LocalizedText m_richText = new("en", "cached-payload");
        private readonly Uuid m_guid = new(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        private object m_input;
        private Variant m_variant;
        private ServiceMessageContext m_context;
    }
}
