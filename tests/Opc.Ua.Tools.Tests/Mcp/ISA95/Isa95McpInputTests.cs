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
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tests;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins typed JSON contracts, optional masks and independent state-machine scopes.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class Isa95McpInputTests
    {
        [Test]
        public void OmittedOptionalsHaveNoBitsButExplicitZeroAndEmptyArraysSurviveEncoding()
        {
            IServiceMessageContext context = Context();
            V2.ISA95JobOrderDataType absent = Isa95InputMapper.OrderV2(
                new Isa95JobOrderInput { JobOrderId = "absent" }, context);
            Isa95JobOrderInput input = JsonSerializer.Deserialize(
                """
                {
                  "jobOrderId":"job-1","priority":0,"description":[],"workMasters":[],
                  "parameters":[
                    {"id":"flag","value":{"dataType":"Boolean","value":false},"description":[],"children":[]},
                    {"id":"count","value":{"dataType":"UInt32","value":0}}
                  ]
                }
                """, Isa95InputJsonContext.Default.Isa95JobOrderInput)!;
            V2.ISA95JobOrderDataType encoded = Isa95InputMapper.OrderV2(input, context);
            using var encoder = new BinaryEncoder(context);
            encoded.Encode(encoder);
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);
            var decoded = new V2.ISA95JobOrderDataType();
            decoded.Decode(decoder);

            Assert.That(absent.EncodingMask, Is.Zero);
            Assert.That(decoded.EncodingMask, Is.EqualTo((uint)(
                V2.ISA95JobOrderDataTypeFields.Priority |
                V2.ISA95JobOrderDataTypeFields.Description |
                V2.ISA95JobOrderDataTypeFields.WorkMasterID |
                V2.ISA95JobOrderDataTypeFields.JobOrderParameters)));
            Assert.That(decoded.Priority, Is.Zero);
            Assert.That(decoded.Description.Count, Is.Zero);
            Assert.That(decoded.WorkMasterID.Count, Is.Zero);
            Assert.That(decoded.JobOrderParameters.Count, Is.EqualTo(2));
            Assert.That(decoded.JobOrderParameters[0].Value.TryGetValue(out bool flag), Is.True);
            Assert.That(flag, Is.False);
            Assert.That(decoded.JobOrderParameters[0].EncodingMask, Is.EqualTo((uint)(
                V2.ISA95ParameterDataTypeFields.Description | V2.ISA95ParameterDataTypeFields.Subparameters)));
            Assert.That(decoded.JobOrderParameters[1].Value.TryGetValue(out uint count), Is.True);
            Assert.That(count, Is.Zero);
            Assert.That(decoded.JobOrderParameters[1].EncodingMask, Is.Zero);
        }

        [Test]
        public void StructuredInputsRetainResourcesUnitsPropertiesAndExactQuantities()
        {
            Isa95JobOrderInput input = JsonSerializer.Deserialize(
                """
                {
                  "jobOrderId":"job-2",
                  "personnel":[{"id":"operator","use":"setup","quantity":"0.000000000000000000000000001",
                    "description":[{"text":"Operator","locale":"en"}],
                    "engineeringUnits":{"namespaceUri":"urn:units","unitId":0},
                    "properties":[{"id":"approved","value":{"dataType":"Boolean","value":false},
                      "children":[{"id":"level","value":{"dataType":"Int16","value":0}}]}]}],
                  "equipment":[{"id":"press","use":"production","quantity":"2","properties":[]}],
                  "physicalAssets":[{"id":"asset","description":[]}],
                  "materials":[{"materialClassId":"","materialLotId":"lot-1","quantity":"100000000000000000000",
                    "properties":[]}],
                  "workMasters":[{"id":"recipe","description":{"text":"Recipe","locale":"de"},"parameters":[]}]
                }
                """, Isa95InputJsonContext.Default.Isa95JobOrderInput)!;

            V2.ISA95JobOrderDataType order = Isa95InputMapper.OrderV2(input, Context());

            Assert.That(order.PersonnelRequirements[0].Quantity, Is.EqualTo("0.000000000000000000000000001"));
            Assert.That(order.PersonnelRequirements[0].Description[0].Locale, Is.EqualTo("en"));
            Assert.That(order.PersonnelRequirements[0].EngineeringUnits.UnitId, Is.Zero);
            Assert.That(order.PersonnelRequirements[0].Properties[0].Value.TryGetValue(out bool approved), Is.True);
            Assert.That(approved, Is.False);
            Assert.That(order.PersonnelRequirements[0].Properties[0].Subproperties[0].ID, Is.EqualTo("level"));
            Assert.That(order.EquipmentRequirements[0].EncodingMask, Is.EqualTo((uint)(
                V2.ISA95EquipmentDataTypeFields.EquipmentUse |
                V2.ISA95EquipmentDataTypeFields.Quantity |
                V2.ISA95EquipmentDataTypeFields.Properties)));
            Assert.That(order.PhysicalAssetRequirements[0].EncodingMask,
                Is.EqualTo((uint)V2.ISA95PhysicalAssetDataTypeFields.Description));
            Assert.That(order.MaterialRequirements[0].Quantity, Is.EqualTo("100000000000000000000"));
            Assert.That(order.MaterialRequirements[0].EncodingMask, Is.EqualTo((uint)(
                V2.ISA95MaterialDataTypeFields.MaterialClassID |
                V2.ISA95MaterialDataTypeFields.MaterialLotID |
                V2.ISA95MaterialDataTypeFields.Quantity |
                V2.ISA95MaterialDataTypeFields.Properties)));
            Assert.That(order.WorkMasterID[0].Description.Locale, Is.EqualTo("de"));
            Assert.That(order.WorkMasterID[0].EncodingMask, Is.EqualTo((uint)(
                V2.ISA95WorkMasterDataTypeFields.Description | V2.ISA95WorkMasterDataTypeFields.Parameters)));
        }

        [Test]
        public void StateNumbersRemainScopedToTheirOwnSubstateMachines()
        {
            Isa95JobResponseInput input = JsonSerializer.Deserialize(
                """
                {"jobResponseId":"r","jobOrderId":"j","states":[
                  {"stateNumber":1,"browsePath":[]},
                  {"stateNumber":1,"browsePath":["3:InterruptedSubstates"]},
                  {"stateNumber":1,"browsePath":["3:EndedSubstates"]}
                ]}
                """, Isa95InputJsonContext.Default.Isa95JobResponseInput)!;

            V2.ISA95JobResponseDataType response = Isa95InputMapper.ResponseV2(input, Context());

            Assert.That(response.EncodingMask, Is.Zero);
            Assert.That(response.JobState.Count, Is.EqualTo(3));
            for (int index = 0; index < response.JobState.Count; index++)
            {
                Assert.That(response.JobState[index].StateNumber, Is.EqualTo(1));
            }
            Assert.That(response.JobState[0].BrowsePath.Elements.Count, Is.Zero);
            Assert.That(response.JobState[1].BrowsePath.Elements[0].TargetName,
                Is.EqualTo(new QualifiedName("InterruptedSubstates", 3)));
            Assert.That(response.JobState[2].BrowsePath.Elements[0].TargetName,
                Is.EqualTo(new QualifiedName("EndedSubstates", 3)));
            Assert.That(response.JobState[1].BrowsePath.Elements[0].ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasSubStateMachine));
        }

        [Test]
        public void TypedValuesPreserveUInt64ArraysAndNonFiniteDoubles()
        {
            IServiceMessageContext context = Context();
            Variant wide = Isa95InputMapper.Value(Typed(BuiltInType.UInt64, "\"18446744073709551615\""), context);
            Variant array = Isa95InputMapper.Value(Typed(BuiltInType.String, "[\"a\",\"b\"]", isArray: true), context);
            Variant nan = Isa95InputMapper.Value(Typed(BuiltInType.Double, "\"NaN\""), context);

            Assert.That(wide.TryGetValue(out ulong number), Is.True);
            Assert.That(number, Is.EqualTo(ulong.MaxValue));
            Assert.That(array.TryGetValue(out ArrayOf<string> strings), Is.True);
            Assert.That(strings.ToArray(), Is.EqualTo(s_arrayValues));
            Assert.That(nan.TryGetValue(out double value), Is.True);
            Assert.That(double.IsNaN(value), Is.True);
        }

        [Test]
        public void V1KeepsParametersAndRejectsUnrepresentableV2Metadata()
        {
            var input = new Isa95JobOrderInput
            {
                JobOrderId = "v1",
                Description = [new Isa95TextInput { Text = "Order" }],
                Parameters =
                [
                    new Isa95ParameterInput
                    {
                        Id = "mass",
                        Value = Typed(BuiltInType.Double, "1.5"),
                        UnitOfMeasure = "kg"
                    }
                ]
            };
            V1.ISA95JobOrderDataType order = Isa95InputMapper.OrderV1(input, Context());

            Assert.That(order.ID, Is.EqualTo("v1"));
            Assert.That(order.Description, Is.EqualTo("Order"));
            Assert.That(order.JobOrderParameters[0].UoM, Is.EqualTo("kg"));
            Assert.That(order.JobOrderParameters[0].Value.TryGetValue(out double mass), Is.True);
            Assert.That(mass, Is.EqualTo(1.5));
            input.Description = [new Isa95TextInput { Text = "Order", Locale = "en" }];
            Assert.That(() => Isa95InputMapper.OrderV1(input, Context()), Throws.ArgumentException);
            input.Description = [];
            Assert.That(() => Isa95InputMapper.OrderV2(input, Context()), Throws.ArgumentException);
        }

        [Test]
        public void InvalidTypedDiscriminatorsAndArrayShapesAreRejected()
        {
            Assert.That(() => Isa95InputMapper.Value(Typed(BuiltInType.Int32, "1", isArray: true), Context()),
                Throws.ArgumentException);
            Assert.That(() => Isa95InputMapper.Value(Typed((BuiltInType)999, "1"), Context()),
                Throws.ArgumentException);
            Assert.That(() => Isa95InputMapper.Value(Typed(BuiltInType.Null, "1"), Context()),
                Throws.ArgumentException);
            Assert.That(() => JsonSerializer.Deserialize(
                """{"jobOrderId":"j","parameters":[null]}""", Isa95InputJsonContext.Default.Isa95JobOrderInput),
                Throws.TypeOf<JsonException>());
            Assert.That(() => JsonSerializer.Deserialize(
                """{"jobOrderId":"j","parameters":{"memory":[]}}""", Isa95InputJsonContext.Default.Isa95JobOrderInput),
                Throws.TypeOf<JsonException>());
        }

        [TestCase(500)]
        [TestCase(501)]
        public void ParameterArrayLimitAcceptsLastAllowedAndRejectsFirstDisallowed(int count)
        {
            var input = new Isa95JobOrderInput
            {
                JobOrderId = "bounded",
                Parameters = Enumerable.Range(0, count).Select(index => new Isa95ParameterInput
                {
                    Id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Value = Typed(BuiltInType.UInt32, "0")
                }).ToArrayOf()
            };
            if (count == 501)
            {
                Assert.That(() => Isa95InputMapper.OrderV2(input, Context()),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            }
            else
            {
                V2.ISA95JobOrderDataType order = Isa95InputMapper.OrderV2(input, Context());
                Assert.That(order.JobOrderParameters.Count, Is.EqualTo(500));
                Assert.That(order.JobOrderParameters[499].ID, Is.EqualTo("499"));
            }
        }

        [TestCase(8)]
        [TestCase(9)]
        public void ParameterDepthLimitAcceptsLastAllowedAndRejectsFirstDisallowed(int depth)
        {
            var parameter = new Isa95ParameterInput { Id = "leaf", Value = Typed(BuiltInType.Boolean, "false") };
            for (int index = 0; index < depth; index++)
            {
                parameter = new Isa95ParameterInput
                {
                    Id = $"level-{index}",
                    Value = Typed(BuiltInType.Boolean, "false"),
                    Children = [parameter]
                };
            }
            var input = new Isa95JobOrderInput { JobOrderId = "bounded", Parameters = [parameter] };
            if (depth == 9)
            {
                Assert.That(() => Isa95InputMapper.OrderV2(input, Context()),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            }
            else
            {
                V2.ISA95ParameterDataType leaf = Isa95InputMapper.OrderV2(input, Context()).JobOrderParameters[0];
                for (int index = 0; index < 8; index++)
                {
                    Assert.That(leaf.Subparameters.Count, Is.EqualTo(1));
                    leaf = leaf.Subparameters[0];
                }
                Assert.That(leaf.ID, Is.EqualTo("leaf"));
                Assert.That(leaf.Value.TryGetValue(out bool value), Is.True);
                Assert.That(value, Is.False);
            }
        }

        [Test]
        public void ResponseVersionValidationAndExplicitOptionalMasksArePreserved()
        {
            var input = new Isa95JobResponseInput { JobOrderId = "j", JobResponseId = "r" };
            Assert.That(() => Isa95InputMapper.ResponseV2(input, Context()), Throws.ArgumentException);
            input.States = [];
            Assert.That(() => Isa95InputMapper.ResponseV1(input, Context()), Throws.ArgumentException);
            input.V1State = Isa95V1State.Running;
            Assert.That(() => Isa95InputMapper.ResponseV2(input, Context()), Throws.ArgumentException);
            input.V1State = Isa95V1State.Undefined;
            input.Description = new Isa95TextInput { Text = string.Empty };
            input.StartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            input.Parameters = [];

            V2.ISA95JobResponseDataType response = Isa95InputMapper.ResponseV2(input, Context());

            Assert.That(response.JobState.Count, Is.Zero);
            Assert.That(response.EncodingMask, Is.EqualTo((uint)(
                V2.ISA95JobResponseDataTypeFields.Description |
                V2.ISA95JobResponseDataTypeFields.StartTime |
                V2.ISA95JobResponseDataTypeFields.JobResponseData)));
            Assert.That(response.StartTime, Is.EqualTo(new DateTimeUtc(input.StartTime.Value)));
        }

        [Test]
        public void SchemasAdvertiseConcreteBoundedArraysAndTypedStateScopes()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaIsa95Tools(McpToolProfile.Isa95);
            using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool tool = provider.GetServices<McpServerTool>()
                .Single(value => value.ProtocolTool.Name == "isa95_v2_store");

            JsonElement schema = Isa95McpFilters.Normalize(tool.ProtocolTool.InputSchema);
            JsonElement parameters = schema.GetProperty("properties").GetProperty("jobOrder")
                .GetProperty("properties").GetProperty("parameters");
            JsonElement definitions = schema.GetProperty("$defs");
            JsonElement parameter = definitions.GetProperty("isa95Parameter");
            Assert.That(definitions.TryGetProperty("isa95State", out _), Is.False,
                "An order schema must not include unused response-state definitions.");
            Assert.That(schema.GetProperty("properties").GetProperty("jobOrder").GetProperty("description").GetString(),
                Does.Contain("typed job order"));
            McpServerTool responseTool = provider.GetServices<McpServerTool>()
                .Single(value => value.ProtocolTool.Name == "isa95_v2_receive_response");
            JsonElement responseSchema = Isa95McpFilters.Normalize(responseTool.ProtocolTool.InputSchema);
            JsonElement path = responseSchema.GetProperty("$defs").GetProperty("isa95State")
                .GetProperty("properties").GetProperty("browsePath");

            Assert.That(parameters.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(parameters.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
            Assert.That(parameters.GetProperty("items").GetProperty("$ref").GetString(),
                Is.EqualTo("#/$defs/isa95Parameter"));
            Assert.That(parameter.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(s_parameterFields));
            Assert.That(parameter.GetProperty("properties").GetProperty("children").GetProperty("items")
                .GetProperty("$ref").GetString(), Is.EqualTo("#/$defs/isa95Parameter"));
            Assert.That(path.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(path.GetProperty("minItems").GetInt32(), Is.Zero);
            Assert.That(path.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
        }

        private static ServiceMessageContext Context()
        {
            return ServiceMessageContext.Create(NUnitTelemetryContext.Create());
        }

        private static Isa95TypedValueInput Typed(BuiltInType type, string json, bool isArray = false)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return new Isa95TypedValueInput
            {
                DataType = type,
                Value = document.RootElement.Clone(),
                IsArray = isArray
            };
        }

        private static readonly string[] s_arrayValues = ["a", "b"];
        private static readonly string[] s_parameterFields = ["id", "value"];
    }
}
#endif
