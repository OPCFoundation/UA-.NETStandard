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
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// A stack overflow terminates the process and cannot be caught, so no
    /// nesting a peer can send may overflow the stack of the thread that
    /// decodes it, or of the thread that encodes the decoded value again.
    /// Every test runs the codec on a thread with a small stack and expects
    /// either success or a <see cref="ServiceResultException"/>; an overflow
    /// takes down the test host.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncoderStackDepthTests
    {
        public enum Codec
        {
            Binary,
            Json,
            Xml,
            XmlParser
        }

        public enum Shape
        {
            VariantArray,
            DataValue,
            ExtensionObjectVariant,
            ExtensionObjectChain,
            EncodeableTree
        }

        private const int kOneMegabyte = 1024 * 1024;
        private const int kQuarterMegabyte = 256 * 1024;
        private const int kBuilderStack = 256 * 1024 * 1024;
        private const int kUnboundedLimit = 1_000_000;

        private static readonly Codec[] s_decoderCodecs =
            [Codec.Binary, Codec.Json, Codec.Xml, Codec.XmlParser];
        private static readonly Codec[] s_encoderCodecs = [Codec.Binary, Codec.Json, Codec.Xml];
        private static readonly Shape[] s_shapes =
        [
            Shape.VariantArray,
            Shape.DataValue,
            Shape.ExtensionObjectVariant,
            Shape.ExtensionObjectChain,
            Shape.EncodeableTree
        ];
        private static readonly int[] s_stackSizes = [kOneMegabyte, kQuarterMegabyte];

        private static IEnumerable<TestCaseData> DecoderCases()
        {
            foreach (Codec codec in s_decoderCodecs)
            {
                foreach (Shape shape in s_shapes)
                {
                    yield return new TestCaseData(codec, shape);
                }
            }
        }

        private static IEnumerable<TestCaseData> EncoderCases()
        {
            foreach (Codec codec in s_encoderCodecs)
            {
                foreach (Shape shape in s_shapes)
                {
                    yield return new TestCaseData(codec, shape);
                }
            }
        }

        [TestCaseSource(nameof(DecoderCases))]
        public void DecodeBeyondDefaultNestingLimitDoesNotOverflowTheStack(Codec codec, Shape shape)
        {
            // The input nests deeper than the default limit, so the decoder
            // recurses to the limit before it fails.
            object input = EncodeOnLargeStack(
                codec,
                Build(shape, DefaultEncodingLimits.MaxEncodingNestingLevels + 50));

            foreach (int stackSize in s_stackSizes)
            {
                RunOnThread(stackSize, () => Decode(codec, shape, input, CreateContext(null)));
            }
        }

        [TestCaseSource(nameof(EncoderCases))]
        public void EncodeBeyondDefaultNestingLimitDoesNotOverflowTheStack(Codec codec, Shape shape)
        {
            object graph = Build(shape, DefaultEncodingLimits.MaxEncodingNestingLevels + 50);

            foreach (int stackSize in s_stackSizes)
            {
                RunOnThread(stackSize, () => Encode(codec, graph, CreateContext(null)));
            }
        }

        [TestCaseSource(nameof(DecoderCases))]
        public void DecodeWithUnboundedNestingLimitFailsBeforeTheStackOverflows(Codec codec, Shape shape)
        {
            // Without a usable nesting limit only the stack check stands between
            // a deep message and a crash. The JSON encoder cannot write more than
            // 1000 levels, which still overflowed a small stack before.
            int depth = codec == Codec.Json ? 450 : 2000;
            object input = EncodeOnLargeStack(codec, Build(shape, depth));

            ServiceResultException sre = RunOnThread(
                kQuarterMegabyte,
                () => Decode(codec, shape, input, CreateContext(kUnboundedLimit)));

            Assert.That(sre, Is.Not.Null);
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [TestCaseSource(nameof(EncoderCases))]
        public void EncodeWithUnboundedNestingLimitFailsBeforeTheStackOverflows(Codec codec, Shape shape)
        {
            object graph = Build(shape, 2000);

            ServiceResultException sre = RunOnThread(
                kQuarterMegabyte,
                () => Encode(codec, graph, CreateContext(kUnboundedLimit)));

            Assert.That(sre, Is.Not.Null);
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [TestCase(Codec.Xml)]
        [TestCase(Codec.XmlParser)]
        public void DecodeXmlElementNestedDeeperThanTheLimitThrows(Codec codec)
        {
            // Building the DOM of the value recursed once per element level in
            // System.Xml and overflowed a 1 MB stack at 5800 (XmlParser) and
            // 10700 (XmlDecoder) levels, whatever the nesting limit.
            object input = EncodeOnLargeStack(
                codec,
                new Variant(XmlElement.From(CreateNestedXml(20000))));

            ServiceResultException sre = RunOnThread(
                kOneMegabyte,
                () => Decode(codec, Shape.VariantArray, input, CreateContext(null)));

            Assert.That(sre, Is.Not.Null);
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void XmlParserRejectsExtensionObjectBodyNestedDeeperThanTheLimit()
        {
            // The body of an unknown type is kept as XML, which serialized the
            // DOM element recursively.
            object input = EncodeOnLargeStack(
                Codec.XmlParser,
                new Variant(new ExtensionObject(
                    new ExpandedNodeId(4712, 0),
                    XmlElement.From(CreateNestedXml(20000)))));

            ServiceResultException sre = RunOnThread(
                kOneMegabyte,
                () => Decode(Codec.XmlParser, Shape.VariantArray, input, CreateContext(null)));

            Assert.That(sre, Is.Not.Null);
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [TestCase(Codec.Xml)]
        [TestCase(Codec.XmlParser)]
        public void DecodeXmlElementWithinTheLimitRoundTrips(Codec codec)
        {
            string nested = CreateNestedXml(10);
            object input = EncodeOnLargeStack(codec, new Variant(XmlElement.From(nested)));

            Variant value = default;
            ServiceResultException sre = RunOnThread(
                kOneMegabyte,
                () => value = Decode(codec, Shape.VariantArray, input, CreateContext(null)));

            Assert.That(sre, Is.Null);
            Assert.That(value.TryGetValue(out XmlElement element), Is.True);
            // The XmlParser writes the innermost element as an empty element.
            Assert.That(CountElements(element.OuterXml), Is.EqualTo(10));
        }

        private static int CountElements(string xml)
        {
            int count = 0;
            int index = 0;
            while ((index = xml.IndexOf("<a", index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += 2;
            }
            return count;
        }

        private static string CreateNestedXml(int depth)
        {
            var builder = new StringBuilder();
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("<a>");
            }
            for (int ii = 0; ii < depth; ii++)
            {
                builder.Append("</a>");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Runs the action on a thread with the given stack size and returns the
        /// ServiceResultException it threw, if any. Any other exception fails
        /// the test.
        /// </summary>
        private static ServiceResultException RunOnThread(int stackSize, Action action)
        {
            ServiceResultException sre = null;
            Exception unexpected = null;
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        action();
                    }
                    catch (ServiceResultException e)
                    {
                        sre = e;
                    }
                    catch (Exception e)
                    {
                        unexpected = e;
                    }
                },
                stackSize);
            thread.Start();
            thread.Join();
            Assert.That(unexpected, Is.Null, unexpected?.ToString());
            return sre;
        }

        private static ServiceMessageContext CreateContext(int? maxEncodingNestingLevels)
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.Factory.AddEncodeableType(typeof(Holder));
            context.Factory.AddEncodeableType(typeof(Nested));
            context.Factory.AddEncodeableType(typeof(Tree));
            if (maxEncodingNestingLevels != null)
            {
                context.MaxEncodingNestingLevels = maxEncodingNestingLevels.Value;
            }
            return context;
        }

        private static object Build(Shape shape, int depth)
        {
            switch (shape)
            {
                case Shape.VariantArray:
                {
                    var value = new Variant(1);
                    for (int ii = 0; ii < depth; ii++)
                    {
                        value = new Variant(new Variant[] { value });
                    }
                    return value;
                }
                case Shape.DataValue:
                {
                    var value = new Variant(1);
                    for (int ii = 0; ii < depth; ii++)
                    {
                        value = new Variant(new DataValue(value));
                    }
                    return value;
                }
                case Shape.ExtensionObjectVariant:
                {
                    var value = new Variant(1);
                    for (int ii = 0; ii < depth; ii++)
                    {
                        value = new Variant(new ExtensionObject(new Holder { Value = value }));
                    }
                    return value;
                }
                case Shape.ExtensionObjectChain:
                {
                    var nested = new Nested();
                    for (int ii = 0; ii < depth; ii++)
                    {
                        nested = new Nested { Child = new ExtensionObject(nested) };
                    }
                    return new Variant(new ExtensionObject(nested));
                }
                default:
                {
                    var tree = new Tree();
                    for (int ii = 0; ii < depth; ii++)
                    {
                        tree = new Tree { Children = new Tree[] { tree } };
                    }
                    return tree;
                }
            }
        }

        /// <summary>
        /// Encodes the input for a decoder test on a large stack with a limit
        /// that allows its depth.
        /// </summary>
        private static object EncodeOnLargeStack(Codec codec, object graph)
        {
            object result = null;
            ServiceResultException sre = RunOnThread(
                kBuilderStack,
                () => result = Encode(codec, graph, CreateContext(kUnboundedLimit)));
            Assert.That(sre, Is.Null, sre?.ToString());
            return result;
        }

        private static object Encode(Codec codec, object graph, ServiceMessageContext context)
        {
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var encoder = new BinaryEncoder(context);
                    Write(encoder, graph);
                    return encoder.CloseAndReturnBuffer();
                }
                case Codec.Json:
                {
                    using var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
                    Write(encoder, graph);
                    return encoder.CloseAndReturnText();
                }
                default:
                {
                    // The XmlParser reads the document element as the field, so
                    // the Value element is the root of its input.
                    bool isRoot = codec == Codec.XmlParser;
                    var builder = new StringBuilder();
                    using (var writer = XmlWriter.Create(
                        builder,
                        new XmlWriterSettings { OmitXmlDeclaration = true }))
                    using (var encoder = new XmlEncoder(
                        new XmlQualifiedName(isRoot ? "Value" : "Root", Namespaces.OpcUaXsd),
                        writer,
                        context))
                    {
                        encoder.PushNamespace(Namespaces.OpcUaXsd);
                        Write(encoder, graph, isRoot ? null : "Value");
                        encoder.PopNamespace();
                        encoder.Close();
                    }
                    return builder.ToString();
                }
            }
        }

        private static void Write(IEncoder encoder, object graph, string fieldName = "Value")
        {
            if (graph is Tree tree)
            {
                encoder.WriteEncodeable(fieldName, tree);
            }
            else
            {
                encoder.WriteVariant(fieldName, (Variant)graph);
            }
        }

        private static Variant Decode(Codec codec, Shape shape, object input, ServiceMessageContext context)
        {
            switch (codec)
            {
                case Codec.Binary:
                {
                    using var decoder = new BinaryDecoder((byte[])input, context);
                    return Read(decoder, shape);
                }
                case Codec.Json:
                {
                    using var decoder = new JsonDecoder((string)input, context);
                    return Read(decoder, shape);
                }
                case Codec.Xml:
                {
                    using var decoder = new XmlDecoder(
                        null,
                        XmlReader.Create(new StringReader((string)input)),
                        context);
                    decoder.PushNamespace(Namespaces.OpcUaXsd);
                    return Read(decoder, shape);
                }
                default:
                {
                    using var decoder = new XmlParser((string)input, context);
                    decoder.PushNamespace(Namespaces.OpcUaXsd);
                    return Read(decoder, shape);
                }
            }
        }

        private static Variant Read(IDecoder decoder, Shape shape)
        {
            if (shape == Shape.EncodeableTree)
            {
                decoder.ReadEncodeable<Tree>("Value");
                return default;
            }
            return decoder.ReadVariant("Value");
        }
        /// <summary>
        /// A structure with a Variant field.
        /// </summary>
        public sealed class Holder : IEncodeable
        {
            public Variant Value { get; set; }

            public ExpandedNodeId TypeId => new(88901, 0);
            public ExpandedNodeId BinaryEncodingId => new(88902, 0);
            public ExpandedNodeId XmlEncodingId => new(88903, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteVariant("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadVariant("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new Holder { Value = Value };
            }
        }

        /// <summary>
        /// A structure with an ExtensionObject field.
        /// </summary>
        public sealed class Nested : IEncodeable
        {
            public ExtensionObject Child { get; set; }

            public ExpandedNodeId TypeId => new(88911, 0);
            public ExpandedNodeId BinaryEncodingId => new(88912, 0);
            public ExpandedNodeId XmlEncodingId => new(88913, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteExtensionObject("Child", Child);
            }

            public void Decode(IDecoder decoder)
            {
                Child = decoder.ReadExtensionObject("Child");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new Nested { Child = Child };
            }
        }

        /// <summary>
        /// A structure with an array of itself.
        /// </summary>
        public sealed class Tree : IEncodeable
        {
            public ArrayOf<Tree> Children { get; set; }

            public ExpandedNodeId TypeId => new(88921, 0);
            public ExpandedNodeId BinaryEncodingId => new(88922, 0);
            public ExpandedNodeId XmlEncodingId => new(88923, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteEncodeableArray("Children", Children);
            }

            public void Decode(IDecoder decoder)
            {
                Children = decoder.ReadEncodeableArray<Tree>("Children");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new Tree { Children = Children };
            }
        }
    }
}
