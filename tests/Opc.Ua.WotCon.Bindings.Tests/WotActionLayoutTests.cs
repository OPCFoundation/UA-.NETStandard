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

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Contains tests for WoT action layout.
    /// </summary>
    [TestFixture]
    public sealed class WotActionLayoutTests
    {
        /// <summary>
        /// Verifies that compiled action layouts retain complete ordered schemas.
        /// </summary>
        /// <param name="opcUa">The OPC-UA flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void CompiledActionLayoutsRetainCompleteOrderedSchemas(bool opcUa)
        {
            WotBindingCompilation compilation = Compile(
                opcUa,
                """
                "input":{
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["High","Low"],
                  "properties":{
                    "Low":{"type":"number","minimum":1},
                    "High":{"type":"number","maximum":9}
                  },
                  "required":["High","Low"]
                },
                "output":{
                  "type":"object","uav:argumentLayout":"single","uav:mapToType":"i=22",
                  "properties":{"Code":{"type":"integer"},"Text":{"type":"string"}}
                },
                """);
            Assert.That(compilation.HasErrors, Is.False);
            WotCompiledForm form = compilation.Entries.Single()
                .WithExecutable(false).WithExecutable(true)
                .WithTargetMapping(new WotTargetMappingDescriptor())
                .WithOpcUaSecurityRequirements([])
                .WithConditionInvocation(null);

            WotMethodArgumentLayout input = form.Payload.InputLayout!;
            Assert.That(input, Is.Not.Null);
            Assert.That(input.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.Named));
            Assert.That(input.ArgumentCount, Is.EqualTo(2));
            Assert.That(input.FieldOrder.ToArray(), Is.EqualTo(s_order));
            Assert.That(input.GetArgumentSchema(0).GetProperty("maximum").GetInt32(), Is.EqualTo(9));
            Assert.That(input.GetArgumentSchema(1).GetProperty("minimum").GetInt32(), Is.EqualTo(1));
            Assert.That(input.Schema.GetProperty("required").GetArrayLength(), Is.EqualTo(2));
            WotMethodArgumentLayout output = form.Payload.OutputLayout!;
            Assert.That(output.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.Single));
            Assert.That(output.ArgumentCount, Is.EqualTo(1));
            Assert.That(output.FieldOrder.IsEmpty, Is.True);
            Assert.That(output.GetArgumentSchema(0).GetProperty("properties").EnumerateObject().Count(), Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that absent action schemas declare no native arguments.
        /// </summary>
        /// <param name="opcUa">The OPC-UA flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AbsentActionSchemasDeclareNoNativeArguments(bool opcUa)
        {
            WotCompiledForm form = Compile(opcUa, string.Empty).Entries.Single();

            Assert.That(form.Payload.InputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.None));
            Assert.That(form.Payload.InputLayout.ArgumentCount, Is.Zero);
            Assert.That(form.Payload.InputLayout.Schema.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(form.Payload.OutputLayout!.ArgumentCount, Is.Zero);
            Assert.That(() => form.Payload.InputLayout.GetArgumentSchema(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Verifies that invalid named layouts fail during compilation at the schema pointer.
        /// </summary>
        [TestCase("input")]
        [TestCase("output")]
        public void InvalidNamedLayoutsFailDuringCompilationAtTheSchemaPointer(string member)
        {
            WotBindingCompilation result = Compile(
                opcUa: true,
                "\"" +
                member +
                "\":{\"type\":\"object\",\"uav:argumentLayout\":\"named\"," +
                "\"properties\":{\"Value\":{\"type\":\"integer\"}}},");

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.IsError && diagnostic.JsonPointer == "/actions/act/" + member), Is.True);
        }

        /// <summary>
        /// Verifies that layout argument indexes are bounded.
        /// </summary>
        [TestCase(-1)]
        [TestCase(1)]
        public void LayoutArgumentIndexesAreBounded(int index)
        {
            WotCompiledForm form = Compile(opcUa: true, "\"input\":{\"type\":\"number\"},").Entries.Single();

            Assert.That(() => form.Payload.InputLayout!.GetArgumentSchema(index),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static WotBindingCompilation Compile(bool opcUa, string schemas)
        {
            string endpoint = opcUa ? "opc.tcp://source:4840" : "https://source.example/action";
            string addressing = opcUa
                ? """
                  "uav:id":"nsu=urn:source;s=Run","uav:callObjectId":"nsu=urn:source;s=Owner",
                  """
                : string.Empty;
            string json = $$$"""
                {
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "security":["none"],
                  "actions":{
                    "act":{
                      {{{schemas}}}
                      "forms":[{
                        {{{addressing}}}
                        "href":"{{{endpoint}}}","op":"invokeaction","contentType":"application/json"
                      }]
                    }
                  }
                }
                """;
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "resource", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json));
            IWotBindingPlanner planner = opcUa ? new OpcUaBindingPlanner() : new HttpBindingPlanner();
            return planner.Compile(
                request.Forms.Single(), request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));
        }

        private static readonly string[] s_order = ["High", "Low"];

        /// <summary>
        /// Verifies that published object-shaped action schemas need no 1.1 layout annotations and
        /// that native Method metadata, rather than JSON member order, resolves their argument order.
        /// </summary>
        [Test]
        public void PublishedObjectSchemasResolveInNativeInputAndOutputOrder()
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{
                  "type":"object",
                  "required":["First","Second"],
                  "properties":{
                    "Second":{"type":"integer","uav:dataTypeId":"i=6"},
                    "First":{"type":"integer","uav:dataTypeId":"i=6"}
                  }
                },
                "output":{
                  "type":"object",
                  "properties":{
                    "ResultB":{"type":"integer","uav:dataTypeId":"i=6"},
                    "ResultA":{"type":"integer","uav:dataTypeId":"i=6"}
                  }
                },
                """);

            WotPayloadDescriptor resolved = form.Payload.WithNativeArgumentOrder(
                [
                    new Argument { Name = "First", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "Second", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ],
                [
                    new Argument { Name = "ResultA", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "ResultB", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ],
                new NamespaceTable());

            Assert.Multiple(() =>
            {
                Assert.That(form.Payload.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(form.Payload.InputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.NamedUnresolved));
                Assert.That(form.Payload.OutputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.NamedUnresolved));
                Assert.That(resolved.InputLayout!.FieldOrder.ToArray(), Is.EqualTo(s_expectedInputArgumentOrder));
                Assert.That(resolved.InputLayout.GetArgumentName(0), Is.EqualTo("First"));
                Assert.That(resolved.InputLayout.GetArgumentName(1), Is.EqualTo("Second"));
                Assert.That(resolved.InputLayout.GetArgumentSchema(0).GetProperty("uav:dataTypeId").GetString(),
                    Is.EqualTo("i=6"));
                Assert.That(resolved.OutputLayout!.FieldOrder.ToArray(), Is.EqualTo(s_expectedOutputArgumentOrder));
                Assert.That(resolved.OutputLayout.GetArgumentSchema(0).GetProperty("uav:dataTypeId").GetString(),
                    Is.EqualTo("i=6"));
            });
        }

        /// <summary>
        /// Verifies that a published schema cannot claim a native signature with different arity, name,
        /// DataType, or ValueRank.
        /// </summary>
        /// <param name="mismatch">The signature mismatch to exercise.</param>
        [TestCase("arity")]
        [TestCase("name")]
        [TestCase("type")]
        [TestCase("rank")]
        public void PublishedObjectSchemaRejectsNativeSignatureMismatch(string mismatch)
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{
                  "type":"object",
                  "properties":{
                    "Second":{"type":"integer","uav:dataTypeId":"i=6"},
                    "First":{"type":"integer","uav:dataTypeId":"i=6"}
                  }
                },
                """);
            ArrayOf<Argument> nativeInputs = mismatch switch
            {
                "arity" =>
                [
                    new Argument { Name = "First", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ],
                "name" =>
                [
                    new Argument { Name = "Missing", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "Second", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ],
                "type" =>
                [
                    new Argument { Name = "First", DataType = new NodeId(12), ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "Second", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ],
                _ =>
                [
                    new Argument { Name = "First", DataType = new NodeId(6), ValueRank = ValueRanks.OneDimension },
                    new Argument { Name = "Second", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }
                ]
            };

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                form.Payload.WithNativeArgumentOrder(nativeInputs, [], new NamespaceTable()));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Verifies that a published action with no input or output schemas has a zero-position signature.
        /// </summary>
        [Test]
        public void PublishedActionWithoutSchemasDeclaresZeroNativeArguments()
        {
            WotCompiledForm form = CompilePublished(string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(form.Payload.InputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.None));
                Assert.That(form.Payload.InputLayout.ArgumentCount, Is.Zero);
                Assert.That(form.Payload.OutputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.None));
                Assert.That(form.Payload.OutputLayout.ArgumentCount, Is.Zero);
            });
        }

        /// <summary>
        /// Verifies that a typed object-shaped structure is one native argument without 1.1 layout terms.
        /// </summary>
        [Test]
        public void PublishedStructuredSchemaWithoutLayoutAnnotationIsOneArgument()
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{
                  "type":"object",
                  "uav:dataTypeId":"i=296",
                  "properties":{
                    "Name":{"type":"string","uav:dataTypeId":"i=12"},
                    "DataType":{"type":"string","uav:dataTypeId":"i=17"}
                  }
                },
                """);

            Assert.Multiple(() =>
            {
                Assert.That(form.Payload.InputLayout!.Kind, Is.EqualTo(WotMethodArgumentLayoutKind.Single));
                Assert.That(form.Payload.InputLayout.ArgumentCount, Is.EqualTo(1));
                Assert.That(form.Payload.InputLayout.Schema.TryGetProperty("uav:argumentLayout", out _), Is.False);
                Assert.That(form.Payload.InputLayout.Schema.TryGetProperty("uav:fieldOrder", out _), Is.False);
                Assert.That(form.Payload.InputLayout.Schema.GetProperty("properties").EnumerateObject().Count(),
                    Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Verifies that JSON optional properties do not shorten a published native Method signature.
        /// </summary>
        [Test]
        public void PublishedOptionalSchemaMemberDoesNotShortenNativeArity()
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{
                  "type":"object",
                  "properties":{
                    "OptionalNote":{"type":"string","uav:dataTypeId":"i=12"},
                    "Value":{"type":"integer","uav:dataTypeId":"i=6"}
                  }
                },
                """);
            WotPayloadDescriptor resolved = form.Payload.WithNativeArgumentOrder(
                [
                    new Argument { Name = "Value", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "OptionalNote", DataType = new NodeId(12), ValueRank = ValueRanks.Scalar }
                ],
                [],
                new NamespaceTable());
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                resolved.ValidateInputs([new Variant(7)], context));

            Assert.That(resolved.InputLayout!.ArgumentCount, Is.EqualTo(2));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        /// <summary>
        /// Verifies that an abstract integer schema does not authorize a different native integer width.
        /// </summary>
        [Test]
        public void PublishedNativeIntegerWidthConstrainsInputAndOutputValues()
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{"type":"object","properties":{"Value":{"type":"integer"}}},
                "output":{"type":"object","properties":{"Result":{"type":"integer"}}},
                """);
            WotPayloadDescriptor resolved = form.Payload.WithNativeArgumentOrder(
                [new Argument { Name = "Value", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }],
                [new Argument { Name = "Result", DataType = new NodeId(6), ValueRank = ValueRanks.Scalar }],
                new NamespaceTable());
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());

            Assert.DoesNotThrow(() => resolved.ValidateInputs([new Variant(7)], context));
            Assert.DoesNotThrow(() => resolved.ValidateOutputs([new Variant(8)], context));
            ServiceResultException? inputError = Assert.Throws<ServiceResultException>(() =>
                resolved.ValidateInputs([new Variant(7L)], context));
            ServiceResultException? outputError = Assert.Throws<ServiceResultException>(() =>
                resolved.ValidateOutputs([new Variant(8L)], context));

            Assert.That(inputError!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(outputError!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Verifies that an ordinary action name and schema do not create a Condition occurrence.
        /// </summary>
        [Test]
        public void PublishedActionWithoutConditionTermHasNoConditionInvocation()
        {
            WotCompiledForm form = CompilePublished(
                """
                "input":{"type":"integer","uav:dataTypeId":"i=6"},
                """,
                actionName: "ConditionMaintenance");

            Assert.That(form.ConditionInvocation, Is.Null);
            Assert.That(form.Addressing.Metadata.ContainsKey("callObjectId"), Is.False);
        }

        private static WotCompiledForm CompilePublished(string schemas, string actionName = "run")
        {
            string document = $$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],
                  "securityDefinitions":{"none":{"scheme":"nosec"}},
                  "security":["none"],
                  "actions":{
                    "{{{actionName}}}":{
                      "uav:componentOf":["nsu=urn:published;s=Owner"],
                      {{{schemas}}}
                      "forms":[{
                        "href":"opc.tcp://published.example:4840",
                        "uav:id":"nsu=urn:published;s=Run",
                        "op":"invokeaction",
                        "contentType":"application/octet-stream"
                      }]
                    }
                  }
                }
                """;
            var selection = new WotBindingSelectionContext(
                [], [], WotBindingProcessingMode.PublishedOneZero);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "published-action-layout",
                WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(document),
                selection: selection);
            var registry = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]);
            WotBindingPlan plan = registry.Prepare(request);
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.False,
                string.Join("; ", plan.Diagnostics.Select(diagnostic => diagnostic.Message)));
            return plan.CompiledForms.Single();
        }

        /// <summary>
        /// The expected native input argument order for the published object schema test.
        /// </summary>
        private static readonly string[] s_expectedInputArgumentOrder = ["First", "Second"];

        /// <summary>
        /// The expected native output argument order for the published object schema test.
        /// </summary>
        private static readonly string[] s_expectedOutputArgumentOrder = ["ResultA", "ResultB"];
    }
}
