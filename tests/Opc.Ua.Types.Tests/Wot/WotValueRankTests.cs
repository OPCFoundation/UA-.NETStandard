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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
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
        /// <summary>
        /// Checks that readable scalar type refinements are retained inside arrays and matrices.
        /// </summary>
        [TestCase("i=20", null, -1)]
        [TestCase("i=21", null, -1)]
        [TestCase("i=13", "date-time", 1)]
        [TestCase("i=294", "date-time", -1)]
        [TestCase("i=294", "date-time", 2)]
        [TestCase("i=14", "uuid", -1)]
        public void StandardStringTypesKeepReadableSchemas(string dataType, string? format, int rank)
        {
            UANodeSet source = CreateNodeSet(rank, null);
            source.Items!.OfType<UAVariable>().Single().DataType = dataType;

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            JsonElement schema = document.Properties["Samples"];
            for (int dimension = 0; dimension < rank; dimension++)
            {
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
                schema = schema.GetProperty("items");
            }
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("string"));
            if (format is not null)
            {
                Assert.That(schema.GetProperty("format").GetString(), Is.EqualTo(format));
            }
            Assert.That(document.Properties["Samples"].GetProperty("uav:mapToType").GetString(), Is.EqualTo(dataType));
        }

        /// <summary>
        /// Checks that an unknown element type does not produce an empty items schema.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public void UnknownArrayElementTypesOmitEmptyItems(int rank)
        {
            UANodeSet source = CreateNodeSet(rank, null);
            source.Items!.OfType<UAVariable>().Single().DataType = "ns=1;i=9001";

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            JsonElement schema = document.Properties["Samples"];
            for (int dimension = 1; dimension < rank; dimension++)
            {
                schema = schema.GetProperty("items");
            }
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(schema.TryGetProperty("items", out _), Is.False);
            Assert.That(document.Properties["Samples"].GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(rank));
        }

        /// <summary>
        /// Resolves local scalar inheritance for scalar, array and matrix schemas without losing native identities.
        /// </summary>
        [TestCase(-1, "i=12", null, false)]
        [TestCase(1, "i=12", null, false)]
        [TestCase(2, "i=13", "date-time", false)]
        [TestCase(-1, "i=12", null, true)]
        [TestCase(1, "i=12", null, true)]
        [TestCase(2, "i=13", "date-time", true)]
        public async Task LocalScalarSubtypesKeepTheirReadableSchemasAsync(
            int rank, string terminal, string? format, bool forwardOnly)
        {
            UANodeSet source = CreateNodeSet(rank, null);
            source.Items!.OfType<UAVariable>().Single().DataType = "ns=1;i=2000";
            source.Items =
            [
                .. source.Items!,
                new UADataType
                {
                    NodeId = "ns=1;i=2000",
                    BrowseName = "1:MeasurementType",
                    References = forwardOnly
                        ? []
                        : [new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "ns=1;i=2001" }]
                },
                new UADataType
                {
                    NodeId = "ns=1;i=2001",
                    BrowseName = "1:ScalarBase",
                    References = forwardOnly
                        ? [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = terminal },
                            new Reference { ReferenceType = "HasSubtype", Value = "ns=1;i=2000" }
                        ]
                        : [new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = terminal }]
                }
            ];
            byte[] original = WotTestData.Serialize(source);
            using (WotDocument document = WotNodeSetConverter.FromNodeSet(source))
            {
                JsonElement schema = document.Properties["Samples"];
                string? nativeIdentity = schema.GetProperty("uav:mapToType").GetString();
                for (int dimension = 0; dimension < rank; dimension++)
                {
                    schema = schema.GetProperty("items");
                }
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("string"));
                Assert.That(nativeIdentity, Does.EndWith(";i=2000"));
                if (format is not null)
                {
                    Assert.That(schema.GetProperty("format").GetString(), Is.EqualTo(format));
                }
            }
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            using WotDocumentSet? documents = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(original));
        }

        /// <summary>
        /// Keeps declared datatype aliases from becoming built-ins when fresh or cached resolution fails.
        /// </summary>
        [TestCase(-1, false)]
        [TestCase(1, false)]
        [TestCase(-1, true)]
        [TestCase(1, true)]
        public void UnresolvedScalarAliasesDoNotFallBackToBuiltInSpellings(int rank, bool structure)
        {
            UANodeSet source = CreateNodeSet(rank, null);
            source.Aliases = [.. source.Aliases!, new NodeIdAlias { Alias = "String", Value = "ns=1;i=2000" }];
            UAVariable variable = source.Items!.OfType<UAVariable>().Single();
            variable.DataType = "String";
            source.Items![0].References =
            [
                .. source.Items[0].References!,
                new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=1002" }
            ];
            source.Items =
            [
                .. source.Items,
                new UAVariable
                {
                    NodeId = "ns=1;i=1002",
                    BrowseName = "1:RepeatedSamples",
                    ParentNodeId = variable.ParentNodeId,
                    DataType = variable.DataType,
                    ValueRank = rank,
                    References = variable.References
                },
                new UADataType
                {
                    NodeId = "ns=1;i=2000",
                    BrowseName = "1:DeclaredString",
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype",
                            IsForward = false,
                            Value = structure ? "i=22" : "ns=1;i=9999"
                        }
                    ],
                    Definition = structure
                        ? new Export.DataTypeDefinition
                        {
                            Name = "1:DeclaredString",
                            Field = [new DataTypeField { Name = "Label", DataType = "i=12" }]
                        }
                        : null
                }
            ];

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            foreach (string key in new[] { "Samples", "RepeatedSamples" })
            {
                JsonElement schema = document.Properties[key];
                Assert.That(schema.GetProperty("uav:mapToType").GetString(), Does.EndWith(";i=2000"));
                if (rank == ValueRanks.Scalar)
                {
                    Assert.That(schema.TryGetProperty("type", out _), Is.False);
                }
                else
                {
                    Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
                    Assert.That(schema.TryGetProperty("items", out _), Is.False);
                }
            }
        }

        /// <summary>
        /// Deduplicates equivalent forward and inverse parents without hiding conflicting subtype edges.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ForwardSubtypeEdgesRetainParentAmbiguity(bool conflicting)
        {
            UANodeSet source = CreateNodeSet(1, null);
            source.Items!.OfType<UAVariable>().Single().DataType = "ns=1;i=2000";
            source.Items =
            [
                .. source.Items!,
                new UADataType
                {
                    NodeId = "ns=1;i=2000",
                    BrowseName = "1:Label",
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype",
                            IsForward = false,
                            Value = conflicting ? "i=6" : "nsu=urn:test:rank;i=2001"
                        }
                    ]
                },
                new UADataType
                {
                    NodeId = "ns=1;i=2001",
                    BrowseName = "1:LabelBase",
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=12" },
                        new Reference { ReferenceType = "HasSubtype", Value = "ns=1;i=2000" }
                    ]
                }
            ];

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            JsonElement schema = document.Properties["Samples"];
            Assert.That(schema.TryGetProperty("items", out JsonElement items), Is.EqualTo(!conflicting));
            if (!conflicting)
            {
                Assert.That(items.GetProperty("type").GetString(), Is.EqualTo("string"));
            }
        }

        /// <summary>
        /// Shares scalar resolution within one export without retaining stale terminals across conversions.
        /// </summary>
        [Test]
        public void ScalarSubtypeLookupIsScopedToEachConversion()
        {
            UANodeSet source = CreateNodeSet(1, null);
            UAVariable variable = source.Items!.OfType<UAVariable>().Single();
            variable.DataType = "ns=1;i=2000";
            var type = new UADataType
            {
                NodeId = "ns=1;i=2000",
                BrowseName = "1:Scalar",
                References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=12" }]
            };
            source.Items = [.. source.Items!, type];
            using (WotDocument first = WotNodeSetConverter.FromNodeSet(source))
            {
                Assert.That(first.Properties["Samples"].GetProperty("items").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            }

            type.References![0].Value = "i=6";
            using WotDocument second = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(second.Properties["Samples"].GetProperty("items").GetProperty("type").GetString(),
                Is.EqualTo("integer"));
        }

        /// <summary>
        /// Verifies that arrays and matrices export ordinary nested array schemas.
        /// </summary>
        [TestCase(1, "4")]
        [TestCase(2, "2,3")]
        [TestCase(3, "0,2,0")]
        public void ArraysAndMatricesExportOrdinaryNestedArraySchemas(int rank, string dimensions)
        {
            UANodeSet source = CreateNodeSet(rank, dimensions);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            JsonElement schema = document.Properties["Samples"];
            for (int dimension = 0; dimension < rank; dimension++)
            {
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
                schema = schema.GetProperty("items");
            }
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("number"));

            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using var readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(readableDocument);

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            UAVariable variable = restored.Value!.Items!.OfType<UAVariable>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(variable.ValueRank, Is.EqualTo(rank));
                Assert.That(variable.ArrayDimensions, Is.EqualTo(dimensions));
                Assert.That(variable.DataType, Is.EqualTo("i=11"));
            });
        }

        /// <summary>
        /// Verifies that ranked item constraints survive canonical projection.
        /// </summary>
        /// <param name="nestedItems">The nested-items flag for this test case.</param>
        /// <param name="archive">The archive flag for this test case.</param>
        [TestCase(1, false, false)]
        [TestCase(2, false, false)]
        [TestCase(2, true, false)]
        [TestCase(2, false, true)]
        public void RankedItemConstraintsSurviveCanonicalProjection(int rank, bool nestedItems, bool archive)
        {
            const string itemJson =
                """
                {
                  "type": "number",
                  "uav:dataTypeName": "ua:Double",
                  "uav:dataTypeId": "i=11",
                  "minimum": -10.5,
                  "exclusiveMaximum": 30,
                  "multipleOf": 0.5,
                  "enum": [1.5, 2],
                  "title": "Calibrated sample",
                  "vendor:annotation": { "scale": 100 }
                }
                """;
            JsonObject root = RankedItemsDocument(rank, JsonNode.Parse(itemJson)!);
            if (nestedItems)
            {
                root["properties"]!["Matrix"]!["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 3,
                    ["maxItems"] = 3,
                    ["items"] = JsonNode.Parse(itemJson)
                };
            }
            using var authored = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            using var expectedItems = JsonDocument.Parse(itemJson);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(authored);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));

            for (int roundTrip = 0; roundTrip < 2; roundTrip++)
            {
                UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
                Assert.That(variable.DataType, Is.EqualTo("i=11"));
                Assert.That(variable.ValueRank, Is.EqualTo(rank));
                Assert.That(variable.ArrayDimensions, Is.EqualTo(rank == 1 ? "3" : "2,3"));
                byte[] beforeExport = WotTestData.Serialize(result.Value);
                WotConversionResult<WotDocument> converted = WotNodeSetConverter.FromNodeSetResult(
                    result.Value,
                    options: new WotNodeSetConverterOptions
                    {
                        PreservationMode = archive
                            ? WotNodeSetPreservationMode.Always
                            : WotNodeSetPreservationMode.Never
                    });
                using WotDocument exported = converted.Value!;
                Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics));
                Assert.That(WotTestData.Serialize(result.Value), Is.EqualTo(beforeExport));
                JsonElement item = exported.Properties["Matrix"];
                for (int dimension = 0; dimension < rank; dimension++)
                {
                    Assert.That(item.GetProperty("type").GetString(), Is.EqualTo("array"));
                    item = item.GetProperty("items");
                }
                Assert.That(JsonElement.DeepEquals(item, expectedItems.RootElement), Is.True, item.GetRawText());
                if (nestedItems)
                {
                    JsonElement innerArray = exported.Properties["Matrix"].GetProperty("items");
                    Assert.That(innerArray.GetProperty("minItems").GetInt32(), Is.EqualTo(3));
                    Assert.That(innerArray.GetProperty("maxItems").GetInt32(), Is.EqualTo(3));
                }
                JsonObject projected = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
                if (!archive)
                {
                    projected.Remove("uav:nodes");
                    projected.Remove("uav:nodeSet");
                }
                else
                {
                    Assert.That(exported.TryGetEnvelope(out _), Is.True);
                }
                using var imported = WotDocument.Parse(WotTestData.Utf8(projected.ToJsonString()));
                result = WotNodeSetConverter.ToNodeSetResult(imported);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            }
        }

        /// <summary>
        /// Verifies that contradictory or opaque items are not discarded.
        /// </summary>
        /// <param name="itemJson">The item JSON used by this test case.</param>
        [TestCase(/*lang=json,strict*/ "{\"type\":\"string\",\"minLength\":2}")]
        [TestCase("false")]
        public void ContradictoryOrOpaqueItemsAreNotDiscarded(string itemJson)
        {
            JsonObject root = RankedItemsDocument(2, JsonNode.Parse(itemJson)!);
            using var authored = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            WotConversionResult<UANodeSet> imported = WotNodeSetConverter.ToNodeSetResult(authored);
            bool contradictoryType = itemJson != "false";
            Assert.That(imported.Success, Is.EqualTo(!contradictoryType), string.Join("; ", imported.Diagnostics));
            Assert.That(imported.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Location?.JsonPointer == "/properties/Matrix/items/type"), Is.EqualTo(contradictoryType));
            byte[] beforeExport = WotTestData.Serialize(imported.Value!);

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(imported.Value!);
            using WotDocument exported = result.Value!;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.ResidueConflict),
                Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(WotTestData.Serialize(imported.Value!), Is.EqualTo(beforeExport));
        }

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

        /// <summary>
        /// Retains ranked method argument schemas, including scalar types declared by forward-only subtype edges.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MethodArgumentsCarryTheirOwnRankAndDimensions(bool forwardSubtype)
        {
            using WotDocument original = WotUnitsAndRangesTests.ParseThingModel(
                "\"actions\":{\"load\":{\"@type\":\"uav:method\",\"title\":\"Load\"," +
                "\"input\":{\"type\":\"object\",\"uav:fieldOrder\":[\"Samples\"]," +
                "\"properties\":{\"Samples\":{\"type\":\"number\"," +
                "\"uav:mapToType\":\"i=11\"," +
                "\"uav:valueRank\":2,\"uav:arrayDimensions\":[3,4]}}}}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            if (forwardSubtype)
            {
                UAVariable arguments = nodeSet.Items!.OfType<UAVariable>()
                    .Single(variable => variable.BrowseName == "InputArguments");
                arguments.Value!.GetElementsByTagName(
                    "DataType", "http://opcfoundation.org/UA/2008/02/Types.xsd")
                    .OfType<System.Xml.XmlElement>().Single()
                    .GetElementsByTagName("Identifier", "http://opcfoundation.org/UA/2008/02/Types.xsd")
                    .OfType<System.Xml.XmlElement>().Single().InnerText = "ns=1;i=2000";
                AddForwardStringSubtype(nodeSet);
            }
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
                Assert.That(samples.GetProperty("items").GetProperty("items").GetProperty("type").GetString(),
                    Is.EqualTo(forwardSubtype ? "string" : "number"));
            });
        }

        /// <summary>
        /// Retains ranked event field schemas, including scalar types declared by forward-only subtype edges.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EventFieldsCarryTheirOwnRankAndDimensions(bool forwardSubtype)
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
            if (forwardSubtype)
            {
                field.DataType = "ns=1;i=2000";
                AddForwardStringSubtype(nodeSet);
            }

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
                Assert.That(samples.GetProperty("items").GetProperty("type").GetString(),
                    Is.EqualTo(forwardSubtype ? "string" : "number"));
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

        /// <summary>
        /// Adds a local String subtype whose immediate base alone records their subtype relationship.
        /// </summary>
        private static void AddForwardStringSubtype(UANodeSet nodeSet)
        {
            nodeSet.Items =
            [
                .. nodeSet.Items!,
                new UADataType { NodeId = "ns=1;i=2000", BrowseName = "1:Label" },
                new UADataType
                {
                    NodeId = "ns=1;i=2001",
                    BrowseName = "1:LabelBase",
                    References =
                    [
                        new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=12" },
                        new Reference { ReferenceType = "i=45", Value = "ns=1;i=2000" }
                    ]
                }
            ];
        }

        private static JsonObject RankedItemsDocument(int rank, JsonNode items)
        {
            return new JsonObject
            {
                ["@type"] = new JsonArray("tm:ThingModel", "uav:objectType"),
                ["uav:id"] = "nsu=urn:test:ranked-items;i=1",
                ["uav:browseName"] = "nsu=urn:test:ranked-items;Root",
                ["properties"] = new JsonObject
                {
                    ["Matrix"] = new JsonObject
                    {
                        ["uav:id"] = "nsu=urn:test:ranked-items;i=2",
                        ["uav:browseName"] = "nsu=urn:test:ranked-items;Matrix",
                        ["uav:mapToType"] = "i=11",
                        ["type"] = "array",
                        ["uav:valueRank"] = rank,
                        ["uav:arrayDimensions"] = rank == 1 ? new JsonArray(3) : new JsonArray(2, 3),
                        ["items"] = items
                    }
                }
            };
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
