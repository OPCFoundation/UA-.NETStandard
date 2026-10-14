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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Pins discriminator, array, validation and atomic sample invariants independently of transport execution.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionInputTests
    {
        [TestCase(PublisherIdType.Byte, "255", BuiltInType.Byte, "255")]
        [TestCase(PublisherIdType.UInt16, "65535", BuiltInType.UInt16, "65535")]
        [TestCase(PublisherIdType.UInt32, "4294967295", BuiltInType.UInt32, "4294967295")]
        [TestCase(PublisherIdType.UInt64, "\"18446744073709551615\"", BuiltInType.UInt64, "18446744073709551615")]
        [TestCase(PublisherIdType.String, "\"42\"", BuiltInType.String, "42")]
        [TestCase(PublisherIdType.Guid, "\"08a3c7b5-cec2-4d12-a4ab-93dbbb762dd0\"", BuiltInType.Guid,
            "08a3c7b5-cec2-4d12-a4ab-93dbbb762dd0")]
        public void PublisherIdPreservesWireDiscriminator(
            PublisherIdType type, string json, BuiltInType builtIn, string expected)
        {
            var input = new PubSubPublisherIdInput { Type = type, Value = PubSubExpansionTestContext.Element(json) };

            PublisherId result = PubSubInputValidation.Publisher(input, PubSubMessageEncoding.Json);

            Assert.That(result.Type, Is.EqualTo(type));
            Assert.That(result.ToVariant().TypeInfo.BuiltInType, Is.EqualTo(builtIn));
            Assert.That(result.ToString(), Is.EqualTo(expected));
            Assert.That(PublisherId.From(result.ToVariant()), Is.EqualTo(result));
        }

        [Test]
        public void GuidPublisherCannotSilentlyBecomeUadpString()
        {
            var input = new PubSubPublisherIdInput
            {
                Type = PublisherIdType.Guid,
                Value = PubSubExpansionTestContext.Element("\"08a3c7b5-cec2-4d12-a4ab-93dbbb762dd0\"")
            };

            Assert.That(() => PubSubInputValidation.Publisher(input, PubSubMessageEncoding.Uadp),
                Throws.ArgumentException);
        }

        [Test]
        public void BytePublisherOverflowIsRejectedRatherThanNarrowed()
        {
            var input = new PubSubPublisherIdInput
            {
                Type = PublisherIdType.Byte,
                Value = PubSubExpansionTestContext.Element("256")
            };

            Assert.That(() => PubSubInputValidation.Publisher(input, PubSubMessageEncoding.Uadp),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ScalarFalseAndArrayWidthsSurviveConversion()
        {
            var options = new McpPubSubOptions();
            var boolean = new PubSubFieldInput { Name = "flag", Type = BuiltInType.Boolean };
            var array = new PubSubFieldInput { Name = "ids", Type = BuiltInType.UInt64, ValueRank = 1 };

            Variant flag = PubSubInputValidation.Value(boolean, PubSubExpansionTestContext.Element("false"), options);
            Variant ids = PubSubInputValidation.Value(array,
                PubSubExpansionTestContext.Element("[\"0\",\"18446744073709551615\"]"), options);

            Assert.That(flag.TryGetValue(out bool value), Is.True);
            Assert.That(value, Is.False);
            Assert.That(ids.TryGetValue(out ArrayOf<ulong> values), Is.True);
            Assert.That(values.ToArray(), Is.EqualTo([0UL, ulong.MaxValue]));
            Assert.That(ids.TypeInfo.ValueRank, Is.EqualTo(1));
        }

        [TestCase("opc.udp://broker.test:4840")]
        [TestCase("opc.udp://user:password@127.0.0.1:4840")]
        [TestCase("opc.udp://127.0.0.1:4840/ignored")]
        [TestCase("opc.udp://0.0.0.0:4840")]
        [TestCase("opc.udp://127.0.0.1")]
        public void DatagramValidationRefusesDnsCredentialsPathsAndImplicitPorts(string endpoint)
        {
            PubSubApplicationInput baseline = PubSubExpansionTestContext.Input();
            PubSubApplicationInput input = baseline with
            {
                Connection = baseline.Connection with { Endpoint = endpoint }
            };

            Assert.That(() => PubSubInputValidation.Validate(input, new McpPubSubOptions()), Throws.ArgumentException);
        }

        [Test]
        public void EmptyDuplicateUnsupportedAndOverBudgetFieldsAreDistinctInvalidPartitions()
        {
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            var options = new McpPubSubOptions { MaxFields = 2, MaxArrayElements = 2 };

            Assert.That(() => PubSubInputValidation.Validate(input with { Fields = [] }, options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PubSubInputValidation.Validate(
                input with { Fields = [input.Fields[0], input.Fields[0]] }, options), Throws.ArgumentException);
            Assert.That(() => PubSubInputValidation.Validate(
                input with { Fields = [input.Fields[0], input.Fields[1], input.Fields[0] with { Name = "third" }] },
                options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PubSubInputValidation.Validate(
                input with { Fields = [input.Fields[0] with { Type = BuiltInType.ExtensionObject }] }, options),
                Throws.ArgumentException);
            Assert.That(() => PubSubInputValidation.Value(input.Fields[0] with { ValueRank = 1 },
                PubSubExpansionTestContext.Element("[1,2,3]"), options), Throws.ArgumentException);
            Variant boundary = PubSubInputValidation.Value(input.Fields[0] with { ValueRank = 1 },
                PubSubExpansionTestContext.Element("[1,2]"), options);
            Assert.That(boundary.TryGetValue(out ArrayOf<int> values), Is.True);
            Assert.That(values.ToArray(), Is.EqualTo(s_boundaryValues));
        }

        [Test]
        public async Task InvalidSecondFieldDoesNotPartiallyCommitFirstFieldAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            var source = new McpPubSubDataSetSource(PubSubExpansionTestContext.Input(), new McpPubSubOptions(),
                fixture.Remote.Context, fixture.Clock);
            var input = new PubSubPublishInput
            {
                Fields =
                [
                    new PubSubFieldValueInput { Name = "Count", Value = PubSubExpansionTestContext.Element("99") },
                    new PubSubFieldValueInput
                    {
                        Name = "Enabled", Value = PubSubExpansionTestContext.Element("\"bad\"")
                    }
                ]
            };

            Assert.That(() => source.Update(input), Throws.TypeOf<InvalidOperationException>());
            PublishedDataSetSnapshot result = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);

            Assert.That(result.Fields[0].Value.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(7));
            Assert.That(result.Fields[1].Value.TryGetValue(out bool enabled), Is.True);
            Assert.That(enabled, Is.False);
            Assert.That(result.MetaDataVersion.MajorVersion, Is.EqualTo(1));
            Assert.That(result.MetaDataVersion.MinorVersion, Is.Zero);
        }

        [Test]
        public async Task FieldMetadataIdentityIsStableAndOrderSensitiveOnlyThroughDeclaredFieldsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            var source = new McpPubSubDataSetSource(
                input, new McpPubSubOptions(), fixture.Remote.Context, fixture.Clock);

            DataSetMetaDataType first = source.BuildMetaData();
            DataSetMetaDataType second = source.BuildMetaData();
            var independent = new McpPubSubDataSetSource(input with { Name = "another-runtime" },
                new McpPubSubOptions(), fixture.Remote.Context, fixture.Clock);
            DataSetMetaDataType third = independent.BuildMetaData();

            Assert.That(first.Fields.ToList().Select(field => field.DataSetFieldId).ToArray(),
                Is.EqualTo(second.Fields.ToList().Select(field => field.DataSetFieldId).ToArray()));
            Assert.That(first.Fields.ToList().Select(field => field.DataSetFieldId), Is.Unique);
            Assert.That(first.Fields.ToList().Select(field => field.DataSetFieldId).ToArray(),
                Is.EqualTo(third.Fields.ToList().Select(field => field.DataSetFieldId).ToArray()));
            Assert.That(first.Fields.ToList().Select(field => field.Name).ToArray(), Is.EqualTo(s_fieldNames));
            Assert.That(first.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Int32));
            Assert.That(first.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
        }

        private static readonly int[] s_boundaryValues = [1, 2];
        private static readonly string[] s_fieldNames = ["Count", "Enabled"];
    }
}
#endif
