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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Verifies concrete scalar/array widths, UTC normalization and independent configuration-validation operands.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionValueTests
    {
        private static IEnumerable<TestCaseData> ScalarCases
        {
            get
            {
                yield return Case(BuiltInType.Boolean, "false", Variant.From(false));
                yield return Case(BuiltInType.SByte, "-128", Variant.From(sbyte.MinValue));
                yield return Case(BuiltInType.Byte, "255", Variant.From(byte.MaxValue));
                yield return Case(BuiltInType.Int16, "-32768", Variant.From(short.MinValue));
                yield return Case(BuiltInType.UInt16, "65535", Variant.From(ushort.MaxValue));
                yield return Case(BuiltInType.Int32, "-2147483648", Variant.From(int.MinValue));
                yield return Case(BuiltInType.UInt32, "4294967295", Variant.From(uint.MaxValue));
                yield return Case(BuiltInType.Int64, "\"-9223372036854775808\"", Variant.From(long.MinValue));
                yield return Case(BuiltInType.Int64, "9223372036854775807", Variant.From(long.MaxValue));
                yield return Case(BuiltInType.UInt64, "\"18446744073709551615\"", Variant.From(ulong.MaxValue));
                yield return Case(BuiltInType.UInt64, "18446744073709551615", Variant.From(ulong.MaxValue));
                yield return Case(BuiltInType.Float, "1.25", Variant.From(1.25f));
                yield return Case(BuiltInType.Double, "-2.5", Variant.From(-2.5));
                yield return Case(BuiltInType.String, "\"typed value\"", Variant.From("typed value"));
                yield return Case(BuiltInType.DateTime, "\"2026-10-12T13:00:00+02:00\"", Variant.From(Timestamp));
                yield return Case(BuiltInType.Guid, "\"00112233-4455-6677-8899-aabbccddeeff\"",
                    Variant.From(Identifier));
                yield return Case(BuiltInType.ByteString, "\"AAF//w==\"", Variant.From(Bytes));
            }
        }

        private static IEnumerable<TestCaseData> ArrayCases
        {
            get
            {
                yield return Case(BuiltInType.Boolean, "[false,true]", Variant.From([false, true]));
                yield return Case(BuiltInType.SByte, "[-128,127]", Variant.From((ArrayOf<sbyte>)[-128, 127]));
                yield return Case(BuiltInType.Byte, "[0,255]", Variant.From((ArrayOf<byte>)[0, 255]));
                yield return Case(BuiltInType.Int16, "[-32768,32767]", Variant.From((ArrayOf<short>)[-32768, 32767]));
                yield return Case(BuiltInType.UInt16, "[0,65535]", Variant.From((ArrayOf<ushort>)[0, 65535]));
                yield return Case(BuiltInType.Int32, "[-1,2147483647]", Variant.From([-1, int.MaxValue]));
                yield return Case(BuiltInType.UInt32, "[0,4294967295]",
                    Variant.From([0, uint.MaxValue]));
                yield return Case(BuiltInType.Int64, "[\"-9223372036854775808\",7]",
                    Variant.From([long.MinValue, 7]));
                yield return Case(BuiltInType.UInt64, "[\"18446744073709551615\",0]",
                    Variant.From([ulong.MaxValue, 0]));
                yield return Case(BuiltInType.Float, "[1.25,-2.5]", Variant.From([1.25f, -2.5f]));
                yield return Case(BuiltInType.Double, "[1.25,-2.5]", Variant.From([1.25, -2.5]));
                yield return Case(BuiltInType.String, "[\"value\",null]",
                    Variant.From(["value", string.Empty]));
                yield return Case(BuiltInType.DateTime, "[\"2026-10-12T13:00:00+02:00\"]",
                    Variant.From([Timestamp]));
                yield return Case(BuiltInType.Guid, "[\"00112233-4455-6677-8899-aabbccddeeff\"]",
                    Variant.From([Identifier]));
                yield return Case(BuiltInType.ByteString, "[\"AAF//w==\",\"\"]",
                    Variant.From([Bytes, ByteString.Empty]));
            }
        }

        [TestCaseSource(nameof(ScalarCases))]
        public void ScalarConversionPreservesConcreteTypeAndLosslessValue(
            BuiltInType type, string json, Variant expected)
        {
            Variant actual = PubSubInputValidation.Value(new PubSubFieldInput { Name = "value", Type = type },
                PubSubExpansionTestContext.Element(json), new McpPubSubOptions());

            Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(type));
            Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(ArrayCases))]
        public void ArrayConversionPreservesElementWidthOrderAndNullStringConvention(
            BuiltInType type, string json, Variant expected)
        {
            Variant actual = PubSubInputValidation.Value(
                new PubSubFieldInput { Name = "values", Type = type, ValueRank = 1 },
                PubSubExpansionTestContext.Element(json), new McpPubSubOptions());

            Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(type));
            Assert.That(actual.TypeInfo.ValueRank, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.DateTime)]
        public void DefaultValuesRemainTypedAndArraysRemainEmpty(BuiltInType type)
        {
            var field = new PubSubFieldInput { Name = "default", Type = type };
            Variant scalar = PubSubInputValidation.DefaultValue(field, new McpPubSubOptions());
            Variant array = PubSubInputValidation.DefaultValue(field with { ValueRank = 1 }, new McpPubSubOptions());
            Variant expected = type switch
            {
                BuiltInType.Boolean => Variant.From(false),
                BuiltInType.Int32 => Variant.From(0),
                BuiltInType.String => Variant.From(string.Empty),
                BuiltInType.ByteString => Variant.From(ByteString.Empty),
                BuiltInType.Guid => Variant.From(Uuid.Empty),
                _ => Variant.From(DateTimeUtc.From(
                    new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            };

            Assert.That(scalar, Is.EqualTo(expected));
            Assert.That(array.TypeInfo.BuiltInType, Is.EqualTo(type));
            Assert.That(array.TypeInfo.ValueRank, Is.EqualTo(1));
            Variant expectedArray = type switch
            {
                BuiltInType.Boolean => Variant.From((ArrayOf<bool>)[]),
                BuiltInType.Int32 => Variant.From((ArrayOf<int>)[]),
                BuiltInType.String => Variant.From((ArrayOf<string>)[]),
                BuiltInType.ByteString => Variant.From((ArrayOf<ByteString>)[]),
                BuiltInType.Guid => Variant.From((ArrayOf<Uuid>)[]),
                _ => Variant.From((ArrayOf<DateTimeUtc>)[])
            };
            Assert.That(array, Is.EqualTo(expectedArray));
        }

        [TestCase(BuiltInType.Float, "1e40")]
        [TestCase(BuiltInType.Double, "1e400")]
        [TestCase(BuiltInType.DateTime, "\"2026-10-12T10:00:00\"")]
        [TestCase(BuiltInType.DateTime, "\"2026-10-12\"")]
        [TestCase(BuiltInType.Int32, "{}")]
        [TestCase(BuiltInType.Int32, "[]")]
        public void NonfiniteUnzonedAndStructuredScalarInputsAreRejected(BuiltInType type, string json)
        {
            var field = new PubSubFieldInput { Name = "value", Type = type };

            Assert.That(() => PubSubInputValidation.Value(field,
                PubSubExpansionTestContext.Element(json), new McpPubSubOptions()),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void Utf8BytesRatherThanCharacterCountEnforceTheExactMessageBoundary()
        {
            var field = new PubSubFieldInput { Name = "text", Type = BuiltInType.String };
            var options = new McpPubSubOptions { MaxMessageBytes = 1024 };
            string boundary = "\"aa" + new string('€', 340) + "\"";
            string oversized = "\"aa" + new string('€', 341) + "\"";
            Assert.That(Encoding.UTF8.GetByteCount(boundary), Is.EqualTo(1024));

            Variant accepted = PubSubInputValidation.Value(
                field, PubSubExpansionTestContext.Element(boundary), options);
            ServiceResultException? rejected = Assert.Throws<ServiceResultException>(() => PubSubInputValidation.Value(
                field, PubSubExpansionTestContext.Element(oversized), options));

            Assert.That(accepted.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo("aa" + new string('€', 340)));
            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.ByteString)]
        public void NullTextAndBytesAreNullUaValuesRatherThanTheLiteralTextNull(BuiltInType type)
        {
            Variant value = PubSubInputValidation.Value(new PubSubFieldInput { Name = "nullable", Type = type },
                PubSubExpansionTestContext.Element("null"), new McpPubSubOptions());

            if (type == BuiltInType.ByteString)
            {
                Assert.That(value.IsNull, Is.False, "A typed null ByteString retains its Variant discriminator.");
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
                Assert.That(value.TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.IsNull, Is.True);
            }
            else
            {
                Assert.That(value.IsNull, Is.True);
            }
            Assert.That(value.TryGetValue(out string? text) && text == "null", Is.False);
        }

        [TestCase("mode")]
        [TestCase("rank")]
        [TestCase("duplicate-id")]
        [TestCase("empty-id")]
        [TestCase("invalid-id")]
        [TestCase("security-mode")]
        [TestCase("security-policy")]
        public void IndependentSchemaOperandsRejectDormantInvalidConfiguration(string invalid)
        {
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            var security = new PubSubSecurityInput
            {
                GroupId = "line",
                KeyReference = "issued:opaque",
                Resource = "urn:test:line",
                TokenId = 7,
                ExpiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                PolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr,
                Mode = MessageSecurityMode.SignAndEncrypt
            };
            input = invalid switch
            {
                "mode" => input with { Mode = "Both" },
                "rank" => input with { Fields = [input.Fields[0] with { ValueRank = 2 }] },
                "duplicate-id" => input with
                {
                    Fields =
                    [
                        input.Fields[0] with { Id = "00112233-4455-6677-8899-aabbccddeeff" },
                        input.Fields[1] with { Id = "00112233-4455-6677-8899-aabbccddeeff" }
                    ]
                },
                "empty-id" => input with { Fields = [input.Fields[0] with { Id = Guid.Empty.ToString() }] },
                "invalid-id" => input with { Fields = [input.Fields[0] with { Id = "not-a-guid" }] },
                "security-mode" => input with { Security = security with { Mode = MessageSecurityMode.None } },
                _ => input with { Security = security with { PolicyUri = SecurityPolicies.Basic256Sha256 } }
            };

            Assert.That(() => PubSubInputValidation.Validate(input, new McpPubSubOptions()), Throws.ArgumentException);
        }

        [Test]
        public void ExplicitClassAndFieldIdentifiersRemainValidWithSignOnlyMessageSecurity()
        {
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            input = input with
            {
                DataSetClassId = "00112233-4455-6677-8899-aabbccddeeff",
                Fields = [input.Fields[0] with { Id = "10112233-4455-6677-8899-aabbccddeeff" }],
                Security = new PubSubSecurityInput
                {
                    GroupId = "line",
                    KeyReference = "issued:opaque",
                    Resource = "urn:test:line",
                    TokenId = 7,
                    ExpiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    PolicyUri = PubSubSecurityPolicyUri.PubSubAes256Ctr,
                    Mode = MessageSecurityMode.Sign
                }
            };

            Assert.That(() => PubSubInputValidation.Validate(input, new McpPubSubOptions()), Throws.Nothing);
        }

        [TestCase("mqtt://broker.test:1883", false)]
        [TestCase("mqtts://broker.test:8883", true)]
        public void ProtectedMqttCredentialsRequireTlsEvenForStoppedConfigurations(string endpoint, bool accepted)
        {
            var connection = new PubSubConnectionInput
            {
                Transport = PubSubTransportKind.Mqtt,
                Endpoint = endpoint,
                Topic = "data",
                MetadataTopic = "metadata",
                CredentialReference = "mqtt-password",
                UserName = "test-user",
                AuxiliaryTopics = ["status"]
            };

            if (accepted)
            {
                Assert.That(() => PubSubInputValidation.ValidateConnection(connection), Throws.Nothing);
            }
            else
            {
                Assert.That(() => PubSubInputValidation.ValidateConnection(connection), Throws.ArgumentException);
            }
            Assert.That(() => PubSubInputValidation.ValidateConnection(connection with { CredentialReference = null }),
                Throws.ArgumentException);
        }

        [TestCase("data/#")]
        [TestCase("data/+")]
        [TestCase("$share/group/data")]
        [TestCase("data\nline")]
        public void AuxiliaryTopicsCannotHideWildcardSharedOrControlCharacterSubscriptions(string topic)
        {
            var connection = new PubSubConnectionInput
            {
                Transport = PubSubTransportKind.Mqtt,
                Endpoint = "mqtt://broker.test:1883",
                Topic = "data",
                MetadataTopic = "metadata",
                AuxiliaryTopics = [topic]
            };

            Assert.That(() => PubSubInputValidation.ValidateConnection(connection), Throws.ArgumentException);
        }

        private static TestCaseData Case(BuiltInType type, string json, Variant expected)
        {
            return new TestCaseData(type, json, expected);
        }

        private static readonly DateTimeUtc Timestamp =
            DateTimeUtc.From(new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero));

        private static readonly Uuid Identifier = new(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        private static readonly ByteString Bytes = ByteString.From(Convert.FromHexString("00017FFF"));
    }
}
#endif
