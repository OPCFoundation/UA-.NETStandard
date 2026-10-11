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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the JSON codec and EncodeableFactory defects
    /// reported by the read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonAuditRegressionTests
    {
        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static readonly string[] s_switches = ["First", "Second"];
        private static readonly int[] s_oneTwoThree = [1, 2, 3];

        [Test]
        public void AbsentSwitchFieldIsResolvedFromTheMemberName()
        {
            // An absent SwitchField was read as selector 0 and then indexed
            // switches[-1], which threw IndexOutOfRangeException. The name based
            // Verbose fallback was unreachable.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"Second\":42}", context);

            uint selector = decoder.ReadSwitchField(s_switches, out string? fieldName);

            Assert.Multiple(() =>
            {
                Assert.That(selector, Is.EqualTo(2u));
                Assert.That(fieldName, Is.EqualTo("Second"));
            });
        }

        [Test]
        public void SwitchFieldZeroSelectsNoField()
        {
            // A Compact union with selector 0 also indexed switches[-1].
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"SwitchField\":0}", context);

            uint selector = decoder.ReadSwitchField(s_switches, out string? fieldName);

            Assert.Multiple(() =>
            {
                Assert.That(selector, Is.Zero);
                Assert.That(fieldName, Is.Null);
            });
        }

        [Test]
        public void LastSwitchFieldSelectorIsResolved()
        {
            // The bounds check used >= instead of >, so the selector of the last
            // union field was reported as unknown.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"SwitchField\":2,\"Second\":42}", context);

            uint selector = decoder.ReadSwitchField(s_switches, out string? fieldName);

            Assert.Multiple(() =>
            {
                Assert.That(selector, Is.EqualTo(2u));
                Assert.That(fieldName, Is.EqualTo("Second"));
            });
        }

        [Test]
        public void SwitchFieldWithoutFieldNamesStillReportsTheSelector()
        {
            // ReadSwitchField(null) always returned 0, so a dynamically decoded
            // union lost its selector.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"SwitchField\":2,\"Second\":42}", context);

            uint selector = decoder.ReadSwitchField(null!, out _);

            Assert.That(selector, Is.EqualTo(2u));
        }

        [Test]
        public void ArrayLengthIsCheckedAgainstMaxArrayLength()
        {
            // The JSON decoder enforced no array limit at all.
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 2;
            using var decoder = new JsonDecoder("{\"Value\":[1,2,3]}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32Array("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void StringLengthIsCheckedAgainstMaxStringLength()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 3;
            using var decoder = new JsonDecoder("{\"Value\":\"abcdef\"}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadString("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void StringLengthIsMeasuredInBytesNotCharactersWhenDecoding()
        {
            // Part 3 5.6.4 and Part 5 6.3.2 define MaxStringLength as a number
            // of bytes, which is also what the binary codec enforces. Counting
            // UTF-16 code units here would accept a string the binary decoder
            // rejects.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 4;

            // Three characters, six UTF-8 bytes.
            using var decoder = new JsonDecoder("{\"Value\":\"äöü\"}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadString("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void StringLengthIsMeasuredInBytesNotCharactersWhenEncoding()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 4;

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteString("Value", "äöü"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void StringAtExactlyTheByteLimitIsAccepted()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 6;

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact);
            Assert.DoesNotThrow(() => encoder.WriteString("Value", "äöü"));

            using var decoder = new JsonDecoder("{\"Value\":\"äöü\"}", context);
            Assert.That(decoder.ReadString("Value"), Is.EqualTo("äöü"));
        }

        [Test]
        public void ByteStringLengthIsCheckedAgainstMaxByteStringLength()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 2;
            using var decoder = new JsonDecoder("{\"Value\":\"AQIDBA==\"}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteString("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void EveryByteArraySpellingIsCheckedAgainstMaxArrayLength()
        {
            // TryGetArrayElements is the choke point for MaxArrayLength, and the
            // base64 fast paths never went through it, so the same value was
            // bounded as a JSON array and unbounded as a base64 string.
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 2;
            string base64 = Convert.ToBase64String(new byte[8]);

            using var bytes = new JsonDecoder($"{{\"Value\":\"{base64}\"}}", context);
            using var sbytes = new JsonDecoder($"{{\"Value\":\"{base64}\"}}", context);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => bytes.ReadByteArray("Value"))!
                        .StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => sbytes.ReadSByteArray("Value"))!
                        .StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            });
        }

        [Test]
        public void EveryByteStringSpellingIsCheckedAgainstMaxByteStringLength()
        {
            // The check was added only to the base64 branch, so the array
            // spelling of the same byte string stayed unbounded.
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 2;
            context.MaxArrayLength = 0;
            using var decoder = new JsonDecoder("{\"Value\":[1,2,3,4,5]}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteString("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void LocalizedTextShorthandIsCheckedAgainstMaxStringLength()
        {
            // The object form was checked through TryGetStringFromElement while
            // the shorthand string form was not, so whether MaxStringLength
            // applied depended on which legal spelling the peer chose.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 4;
            using var shorthand = new JsonDecoder("{\"Value\":\"abcdefgh\"}", context);
            using var verbose = new JsonDecoder("{\"Value\":{\"Text\":\"abcdefgh\"}}", context);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => shorthand.ReadLocalizedText("Value"))!
                        .StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(
                    Assert.Throws<ServiceResultException>(() => verbose.ReadLocalizedText("Value"))!
                        .StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            });
        }

        [Test]
        public void EnumerationSymbolIsCheckedAgainstMaxStringLength()
        {
            // The symbolic name is kept verbatim from the wire, so an unbounded
            // one let a peer park an arbitrarily long string in an EnumValue.
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 4;
            using var decoder = new JsonDecoder("{\"Value\":\"AAAAAAAAAAAA_5\"}", context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumerated("Value"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ZeroLimitsStillMeanUnlimited()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 0;
            context.MaxStringLength = 0;
            context.MaxByteStringLength = 0;
            using var decoder = new JsonDecoder(
                "{\"Array\":[1,2,3],\"Text\":\"abcdef\",\"Bytes\":\"AQIDBA==\"}",
                context);

            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadInt32Array("Array").Count, Is.EqualTo(3));
                Assert.That(decoder.ReadString("Text"), Is.EqualTo("abcdef"));
                Assert.That(decoder.ReadByteString("Bytes").Length, Is.EqualTo(4));
            });
        }

        [Test]
        public void JsonEncoderTreatsZeroLimitsAsUnlimited()
        {
            // The encoder's limit checks lacked the "0 = unlimited" convention
            // that every other codec uses, so an empty context rejected
            // everything.
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 0;
            context.MaxStringLength = 0;
            context.MaxByteStringLength = 0;

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact);

            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => encoder.WriteString("Text", "abcdef"));
                Assert.DoesNotThrow(
                    () => encoder.WriteByteString("Bytes", new ByteString(new byte[] { 1, 2 })));
                Assert.DoesNotThrow(
                    () => encoder.WriteInt32Array("Array", s_oneTwoThree.ToArrayOf()));
            });
        }

        [Test]
        public void OmittedMatrixFieldDecodesAsNullVariant()
        {
            // A null or omitted matrix field was rejected by the dimension
            // check instead of decoding as a null variant.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"Value\":null}", context);

            Variant value = decoder.ReadVariantValue(
                "Value",
                TypeInfo.Create(BuiltInType.Int32, 2));

            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        public void MatrixFieldWithBadDimensionsDoesNotDesynchronizeLaterFields()
        {
            // The matrix branch pushed a stack entry and returned early on the
            // dimension check, leaking it, so every following field decoded
            // from the wrong element.
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"Matrix\":{\"Array\":[1,2],\"Dimensions\":[2]},\"After\":7}",
                context);

            // The bad matrix is rejected ...
            Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue("Matrix", TypeInfo.Create(BuiltInType.Int32, 2)));

            // ... and the element stack is balanced again, so the next field
            // still decodes from the right element.
            Assert.That(decoder.ReadInt32("After"), Is.EqualTo(7));
        }

        [Test]
        public void BareNumericEnumTextRoundTrips()
        {
            // A bare "5" was decoded with the symbol "5", so re-encoding it
            // produced "5_5".
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder("{\"Value\":\"5\"}", context);

            EnumValue value = decoder.ReadEnumerated("Value");

            Assert.Multiple(() =>
            {
                Assert.That(value.Value, Is.EqualTo(5));
                Assert.That(value.ToString(), Is.EqualTo("5"));
            });
        }

        [Test]
        public void NonObjectRawJsonBodyIsRejectedInsteadOfDropped()
        {
            // The body was silently dropped, producing an ExtensionObject whose
            // body was gone without any error.
            ServiceMessageContext context = CreateContext();
            var extension = new ExtensionObject(new NodeId(1234u), "[1,2,3]");

            using var encoder = new JsonEncoder(context, JsonEncoderOptions.Compact);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteExtensionObject("Body", extension));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingError));
        }

        [Test]
        public void ExplicitIdRegistrationNormalizesTheNamespaceZeroUri()
        {
            // Registering with an explicit id skipped the namespace zero
            // normalization that the reflection based path applies, so the type
            // could not be found by its relative id.
            IEncodeableFactory factory = EncodeableFactory.Create();
            var absoluteId = new ExpandedNodeId(99001, Namespaces.OpcUa);

            factory.Builder.AddType(absoluteId, typeof(Sample)).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(
                    factory.TryGetEncodeableType(absoluteId, out IEncodeableType? byAbsolute),
                    Is.True);
                Assert.That(byAbsolute!.Type, Is.EqualTo(typeof(Sample)));
                Assert.That(
                    factory.TryGetEncodeableType(
                        new ExpandedNodeId(99001),
                        out IEncodeableType? byRelative),
                    Is.True);
                Assert.That(byRelative!.Type, Is.EqualTo(typeof(Sample)));
            });
        }

        [Test]
        [TestCase("[-1,2]")]
        [TestCase("[3,1]")]
        [TestCase("[]")]
        [TestCase("[65536,65537]")]
        public void EncodeableMatrixWithInvalidDimensionsIsADecodingError(string dimensions)
        {
            // MatrixOf throws ArgumentException for invalid wire dimensions; the
            // JSON decoder let it escape instead of reporting BadDecodingError.
            ServiceMessageContext context = CreateContext();
            string json =
                "{\"Samples\":{\"Dimensions\":" + dimensions +
                ",\"Array\":[{\"Value\":1},{\"Value\":2}]}}";
            using var decoder = new JsonDecoder(json, context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableMatrix<Sample>("Samples"));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
        }

        [Test]
        public void EncodeableMatrixWithValidDimensionsDecodes()
        {
            ServiceMessageContext context = CreateContext();
            const string json =
                "{\"Samples\":{\"Dimensions\":[1,2],\"Array\":[{\"Value\":1},{\"Value\":2}]}}";
            using var decoder = new JsonDecoder(json, context);

            MatrixOf<Sample> matrix = decoder.ReadEncodeableMatrix<Sample>("Samples");

            Assert.Multiple(() =>
            {
                Assert.That(matrix.Count, Is.EqualTo(2));
                Assert.That(matrix.Span[1].Value, Is.EqualTo(2));
            });
        }

        [Test]
        public void JsonEncoderNeverEmitsDeeperJsonThanTheDecoderAccepts()
        {
            // The encoder allowed one more open object than the decoder's
            // MaxDepth and did not check arrays at all, so it produced output
            // that a peer with the same limits rejects.
            for (int depth = 0; depth < 12; depth++)
            {
                ServiceMessageContext context = CreateContext();
                context.MaxEncodingNestingLevels = 5;

                var diagnosticInfo = new DiagnosticInfo { AdditionalInfo = "0" };
                DiagnosticInfo current = diagnosticInfo;
                for (int ii = 0; ii < depth; ii++)
                {
                    current.InnerDiagnosticInfo = new DiagnosticInfo();
                    current = current.InnerDiagnosticInfo;
                }
                var variant = new Variant(
                    new Variant[] { new Variant(new Variant[] { new Variant(depth) }) });

                foreach (Action<JsonEncoder> write in new Action<JsonEncoder>[]
                {
                    e => e.WriteDiagnosticInfo("Value", diagnosticInfo),
                    e => e.WriteVariant("Value", variant),
                    e => e.WriteVariantArray("Value", Enumerable.Repeat(variant, depth).ToArrayOf())
                })
                {
                    string json;
                    using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
                    {
                        try
                        {
                            write(encoder);
                        }
                        catch (ServiceResultException sre)
                        {
                            Assert.That(
                                sre.StatusCode,
                                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                            continue;
                        }
                        json = encoder.CloseAndReturnText();
                    }

                    Assert.DoesNotThrow(
                        () =>
                        {
                            using var decoder = new JsonDecoder(json, context);
                        },
                        json);
                }
            }
        }

        [Test]
        public void JsonEncoderWithUnsetNestingLimitUsesTheDecoderDefaultDepth()
        {
            // A limit of zero made the encoder fail on its own root object; the
            // decoder treats zero as the System.Text.Json default depth (64).
            ServiceMessageContext context = CreateContext();
            context.MaxEncodingNestingLevels = 0;

            string json;
            using (var encoder = new JsonEncoder(context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteInt32("Value", 42);
                json = encoder.CloseAndReturnText();
            }
            using (var decoder = new JsonDecoder(json, context))
            {
                Assert.That(decoder.ReadInt32("Value"), Is.EqualTo(42));
            }

            var nested = new Variant(1);
            for (int ii = 0; ii < 100; ii++)
            {
                nested = new Variant(new Variant[] { nested });
            }
            using var deepEncoder = new JsonEncoder(context, JsonEncoderOptions.Verbose);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => deepEncoder.WriteVariant("Value", nested));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void EmbeddedBinaryBodyWithTrailingBytesIsRejected()
        {
            // The binary UaBody was decoded without checking that it was fully
            // consumed, so trailing garbage was silently accepted.
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(Sample));

            using (var decoder = new JsonDecoder(
                "{\"Inner\":{\"UaTypeId\":\"i=99001\",\"UaEncoding\":1,\"UaBody\":\"KgAAAA==\"}}",
                context))
            {
                Sample sample = decoder.ReadEncodeableAsExtensionObject<Sample>("Inner");
                Assert.That(sample.Value, Is.EqualTo(42));
            }

            using (var decoder = new JsonDecoder(
                "{\"Inner\":{\"UaTypeId\":\"i=99001\",\"UaEncoding\":1,\"UaBody\":\"KgAAAAE=\"}}",
                context))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadEncodeableAsExtensionObject<Sample>("Inner"));
                Assert.That(
                    ex.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadDecodingError));
            }
        }

        [Test]
        public void EmbeddedBinaryBodyContinuesTheNestingBudget()
        {
            // The embedded body restarted the nesting level at 0.
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(Wrapper));

            // three ExtensionObject levels below the root body.
            var body = new Wrapper
            {
                Child = new ExtensionObject(new Wrapper
                {
                    Child = new ExtensionObject(new Wrapper
                    {
                        Child = new ExtensionObject(new Wrapper())
                    })
                })
            };
            string base64;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteEncodeable(null, body);
                base64 = Convert.ToBase64String(encoder.CloseAndReturnBuffer()!);
            }

            // The JSON part is two levels deep; the body adds four more.
            context.MaxEncodingNestingLevels = 4;
            using var decoder = new JsonDecoder(
                "{\"Inner\":{\"UaTypeId\":\"i=99021\",\"UaEncoding\":1,\"UaBody\":\"" +
                base64 + "\"}}",
                context);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableAsExtensionObject<Wrapper>("Inner"));
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReflectionBasedTypeEqualsIsReflexiveAndConsistent()
        {
            // Equals compared against (obj as IEncodeableType)?.Type, so an
            // enumerated type was not even equal to itself.
            IType enumType = ReflectionBasedType.From(typeof(NodeClass))!;
            IType enumType2 = ReflectionBasedType.From(typeof(NodeClass))!;
            IType encodeableType = ReflectionBasedType.From(typeof(Sample))!;
            IType encodeableType2 = ReflectionBasedType.From(typeof(Sample))!;

            Assert.Multiple(() =>
            {
                Assert.That(enumType, Is.EqualTo(enumType2));
                Assert.That(enumType!.GetHashCode(), Is.EqualTo(enumType2!.GetHashCode()));
                Assert.That(encodeableType, Is.EqualTo(encodeableType2));
                Assert.That(
                    encodeableType!.GetHashCode(),
                    Is.EqualTo(encodeableType2!.GetHashCode()));
                Assert.That(encodeableType, Is.Not.EqualTo(enumType));
                var set = new HashSet<IType> { enumType };
                Assert.That(set, Does.Contain(enumType2));
            });
        }

        [Test]
        [NonParallelizable]
        public void DateTimeWithoutOffsetIsDecodedAsUtc()
        {
            // An offset-less value was converted with ToUniversalTime, which
            // treats it as host-local time, so the result depended on the
            // time zone of the decoding host (a non-UTC zone is forced here).
            using BuiltIn.LocalTimeZoneScope scope = BuiltIn.LocalTimeZoneScope.Create();
            ServiceMessageContext context = CreateContext();
            using var decoder = new JsonDecoder(
                "{\"Value\":\"2024-01-01T00:00:00\",\"Utc\":\"2024-01-01T00:00:00Z\"}",
                context);

            DateTimeUtc expected = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadDateTime("Value"), Is.EqualTo(expected));
                Assert.That(decoder.ReadDateTime("Utc"), Is.EqualTo(expected));
            });
        }

        [Test]
        public void NonStrictExtensionObjectBodyThatThrowsKeepsTheElementStackBalanced()
        {
            // The legacy object form of UaBody pushed the body element without a
            // try/finally, so a throwing Decode left it on the stack and every
            // following field was read from the wrong JSON object.
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(ThrowingSample));
            const string json =
                "{\"Inner\":{\"UaTypeId\":\"i=99011\",\"UaEncoding\":0,\"UaBody\":{\"Value\":1}}," +
                "\"After\":5}";
            using var decoder = new JsonDecoder(
                json,
                context,
                new JsonDecoderOptions { ParseStrict = false });

            Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableAsExtensionObject<ThrowingSample>("Inner"));
            Assert.That(decoder.ReadInt32("After"), Is.EqualTo(5));
        }

        /// <summary>
        /// An encodeable with an ExtensionObject field used to nest bodies.
        /// </summary>
        public sealed class Wrapper : IEncodeable
        {
            public ExtensionObject Child { get; set; }

            public ExpandedNodeId TypeId => new(99021, 0);
            public ExpandedNodeId BinaryEncodingId => new(99022, 0);
            public ExpandedNodeId XmlEncodingId => new(99023, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteExtensionObject("Child", Child);
            }

            public void Decode(IDecoder decoder)
            {
                Child = decoder.ReadExtensionObject("Child");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new Wrapper { Child = Child };
            }
        }

        /// <summary>
        /// An encodeable whose Decode always fails.
        /// </summary>
        public sealed class ThrowingSample : IEncodeable
        {
            public ExpandedNodeId TypeId => new(99011, 0);
            public ExpandedNodeId BinaryEncodingId => new(99012, 0);
            public ExpandedNodeId XmlEncodingId => new(99013, 0);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
                decoder.ReadInt32("Value");
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Decode failed.");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new ThrowingSample();
            }
        }

        /// <summary>
        /// A minimal encodeable used for factory registration.
        /// </summary>
        public sealed class Sample : IEncodeable
        {
            public int Value { get; set; }

            public ExpandedNodeId TypeId => new(99001, 0);
            public ExpandedNodeId BinaryEncodingId => new(99002, 0);
            public ExpandedNodeId XmlEncodingId => new(99003, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is Sample other && other.Value == Value;
            }

            public object Clone()
            {
                return new Sample { Value = Value };
            }
        }
    }
}
