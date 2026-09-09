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

#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Covers the ValueRank and ArrayDimensions mapping of WoT Binding Sections
    /// 7 and 9.1 on ordinary Variable affordances, which OPC 10000-3 gives five
    /// distinct meanings a DataSchema's <c>type</c> alone cannot carry.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotValueRankTests
    {
        [TestCase(-3, TestName = "ScalarOrOneDimension")]
        [TestCase(-2, TestName = "Any")]
        [TestCase(0, TestName = "OneOrMoreDimensions")]
        [TestCase(1, TestName = "OneDimension")]
        [TestCase(3, TestName = "ThreeDimensions")]
        public void EveryValueRankSemanticSurvivesTheRoundTrip(int valueRank)
        {
            UANodeSet source = CreateNodeSet(valueRank, null);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Samples"].GetProperty("uav:valueRank").GetInt32(),
                Is.EqualTo(valueRank));

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            UAVariable samples = restored.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName == "1:Samples");
            Assert.That(samples.ValueRank, Is.EqualTo(valueRank));
        }

        [Test]
        public void TheScalarRankIsTheDefaultAndIsNotRestated()
        {
            UANodeSet source = CreateNodeSet(-1, null);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Samples"].TryGetProperty("uav:valueRank", out _),
                Is.False,
                "A NodeSet omits the scalar rank, so restating it would state a " +
                "fact the source only implies.");

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                restored.Items!.OfType<UAVariable>()
                    .Single(v => v.BrowseName == "1:Samples").ValueRank,
                Is.EqualTo(-1));
        }

        [Test]
        public void AnAmbiguousRankIsNeverCollapsedToScalar()
        {
            foreach (int rank in new[] { -3, -2, 0 })
            {
                UANodeSet source = CreateNodeSet(rank, null);
                using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
                UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

                Assert.That(
                    restored.Items!.OfType<UAVariable>()
                        .Single(v => v.BrowseName == "1:Samples").ValueRank,
                    Is.EqualTo(rank),
                    $"A ValueRank of {rank} says something a scalar does not.");
            }
        }

        [Test]
        public void FixedArrayDimensionsSurviveTheRoundTrip()
        {
            UANodeSet source = CreateNodeSet(2, "3,4");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement dimensions = document.Properties["Samples"]
                .GetProperty("uav:arrayDimensions");
            Assert.Multiple(() =>
            {
                Assert.That(dimensions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(dimensions[0].GetInt32(), Is.EqualTo(3));
                Assert.That(dimensions[1].GetInt32(), Is.EqualTo(4));
            });

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                restored.Items!.OfType<UAVariable>()
                    .Single(v => v.BrowseName == "1:Samples").ArrayDimensions,
                Is.EqualTo("3,4"));
        }

        [Test]
        public void AZeroDimensionMeansUnspecifiedAndIsPreserved()
        {
            UANodeSet source = CreateNodeSet(2, "0,4");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Samples"].GetProperty("uav:arrayDimensions")[0]
                    .GetInt32(),
                Is.Zero,
                "OPC 10000-3 uses zero for a dimension whose length is not fixed.");

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                restored.Items!.OfType<UAVariable>()
                    .Single(v => v.BrowseName == "1:Samples").ArrayDimensions,
                Is.EqualTo("0,4"));
        }

        [Test]
        public void UnspecifiedDimensionsAreNotEmitted()
        {
            UANodeSet source = CreateNodeSet(1, null);

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.Properties["Samples"].TryGetProperty("uav:arrayDimensions", out _),
                Is.False);
        }

        [Test]
        public void RankedVariablesNeedNoStructuredFallback()
        {
            UANodeSet source = CreateNodeSet(2, "3,4");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(
                document.RootElement.TryGetProperty("uav:nodes", out _),
                Is.False,
                "The readable mapping now carries the rank and the dimensions.");
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);
            Assert.That(
                NodeSetComparer.CompareEquivalent(
                    source, restored).AreEquivalent,
                Is.True);
        }

        [Test]
        public void ARankBelowMinusThreeIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":-4}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True);
        }

        [Test]
        public void AFractionalRankIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":1.5}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True);
        }

        [Test]
        public void DimensionsThatDisagreeWithAFixedRankAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":2,\"uav:arrayDimensions\":[3]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True,
                "ArrayDimensions carries one bound per dimension, so its length " +
                "is the rank.");
        }

        [Test]
        public void DimensionsAgainstAnUnfixedRankAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":-2,\"uav:arrayDimensions\":[3]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True);
        }

        [Test]
        public void DimensionsWithoutARankAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:arrayDimensions\":[3]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True,
                "The default rank is scalar, which fixes no number of dimensions.");
        }

        [Test]
        public void ANegativeDimensionIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":[-1]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True);
        }

        [Test]
        public void DimensionsThatAreNotAnArrayAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":3}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.True);
        }

        [Test]
        public void MatchingRankAndDimensionsProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"samples\":{\"type\":\"number\"," +
                "\"uav:valueRank\":2,\"uav:arrayDimensions\":[0,4]}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.False,
                WotAnalogTestData.Describe(result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);

            UAVariable samples = result.Value!.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName == "1:samples");
            Assert.Multiple(() =>
            {
                Assert.That(samples.ValueRank, Is.EqualTo(2));
                Assert.That(samples.ArrayDimensions, Is.EqualTo("0,4"));
            });
        }

        [Test]
        public void MethodArgumentsCarryTheirOwnRankAndDimensions()
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"actions\":{\"load\":{\"@type\":\"uav:method\",\"title\":\"Load\"," +
                "\"input\":{\"type\":\"object\",\"uav:fieldOrder\":[\"Samples\"]," +
                "\"properties\":{\"Samples\":{\"type\":\"number\"," +
                "\"uav:mapToType\":\"i=11\"," +
                "\"uav:valueRank\":2,\"uav:arrayDimensions\":[3,4]}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement samples = restored.Actions["load"]
                .GetProperty("input")
                .GetProperty("properties")
                .GetProperty("Samples");
            Assert.Multiple(() =>
            {
                Assert.That(samples.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(2));
                Assert.That(
                    samples.GetProperty("uav:arrayDimensions")[1].GetInt32(),
                    Is.EqualTo(4));
            });
        }

        [Test]
        public void EventFieldsCarryTheirOwnRankAndDimensions()
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"events\":{\"trace\":{\"@type\":\"uav:eventType\"," +
                "\"title\":\"Trace\"," +
                "\"uav:browseName\":\"pump:TraceEventType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{" +
                "\"Samples\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"uav:browseName\":\"pump:Samples\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":[8]}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);

            UAVariable field = nodeSet.Items!.OfType<UAVariable>()
                .Single(v => v.BrowseName!.EndsWith(":Samples", StringComparison.Ordinal));
            Assert.Multiple(() =>
            {
                Assert.That(field.ValueRank, Is.EqualTo(1));
                Assert.That(field.ArrayDimensions, Is.EqualTo("8"));
            });

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);
            JsonElement samples = restored.Events["TraceEventType"]
                .GetProperty("data")
                .GetProperty("properties")
                .GetProperty("Samples");
            Assert.Multiple(() =>
            {
                Assert.That(samples.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(1));
                Assert.That(
                    samples.GetProperty("uav:arrayDimensions")[0].GetInt32(),
                    Is.EqualTo(8));
            });
        }

        [Test]
        public void RankedVariablesImportAsANodeSet()
        {
            UANodeSet source = CreateNodeSet(2, "3,4");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(document);

            using var stream = new System.IO.MemoryStream();
            nodeSet.Write(stream);
            stream.Position = 0;
            var reread = UANodeSet.Read(stream);

            Assert.That(reread, Is.Not.Null);
            Assert.That(
                reread!.Items!.OfType<UAVariable>()
                    .Single(v => v.BrowseName == "1:Samples").ArrayDimensions,
                Is.EqualTo("3,4"));
        }

        [Test]
        public void ThePublishedDataTypeExampleKeepsItsFieldRanksAndDimensions()
        {
            using var document = WotDocument.Parse(
                WotUnitsAndRangesTests.ReadExample("23-datatype-definitions.jsonld"));

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidValueRank),
                Is.False,
                WotAnalogTestData.Describe(result.Diagnostics));
            Assert.That(result.Value, Is.Not.Null);

            DataTypeField matrix = result.Value!.Items!.OfType<UADataType>()
                .Where(d => d.Definition?.Field is not null)
                .SelectMany(d => d.Definition!.Field!)
                .First(f => f.ArrayDimensions == "3,3");
            Assert.Multiple(() =>
            {
                Assert.That(
                    matrix.ValueRank,
                    Is.EqualTo(2),
                    "A fixed rank of two carries two bounds.");
                Assert.That(
                    result.Value!.Items!.OfType<UADataType>()
                        .Where(d => d.Definition?.Field is not null)
                        .SelectMany(d => d.Definition!.Field!)
                        .Any(f => f.ValueRank == -1),
                    Is.True,
                    "The scalar rank stays distinct from the array ranks.");
            });
        }

        private static UANodeSet CreateNodeSet(int valueRank, string? arrayDimensions)
        {
            // A NodeSet2 document may only use a name where a NodeId is
            // expected if it declares that name, so the fixture declares what
            // it uses and is a document a Server could load.
            return NodeSetAliasCompleter.Complete(new UANodeSet
            {
                NamespaceUris = ["urn:test:rank"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:rank" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:RecorderType",
                        DisplayName = WotAnalogTestData.Text("RecorderType"),
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:Samples",
                        DisplayName = WotAnalogTestData.Text("Samples"),
                        ParentNodeId = "ns=1;i=1000",
                        DataType = "i=11",
                        ValueRank = valueRank,
                        ArrayDimensions = arrayDimensions ?? string.Empty,
                        AccessLevel = 1,
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasTypeDefinition",
                                IsForward = true,
                                Value = "i=63"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1000"
                            }
                        ]
                    }
                ]
            }, WotNodeSetAliases.Instance)!;
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            using WotDocument document = WotUnitsAndRangesTests.ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
