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
    [TestFixture]
    public sealed class WotActionLayoutTests
    {
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
    }
}
