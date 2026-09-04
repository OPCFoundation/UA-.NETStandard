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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises the Method argument mapping of WoT Binding Section 9.1: a UA
    /// Method's <c>InputArguments</c> and <c>OutputArguments</c> are the WoT
    /// action's <c>input</c> and <c>output</c> DataSchemas, in both directions.
    /// </summary>
    /// <remarks>
    /// Arguments are positional in OPC 10000-4 and JSON object members are
    /// unordered, so every test here is as much about the order surviving as
    /// about the values doing so.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotMethodArgumentTests
    {
        [Test]
        public void MethodArgumentsBecomeTheActionsOrderedInputAndOutputSchemas()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateMethodNodeSet());

            JsonElement action = document.Actions["Reset"];
            JsonElement input = action.GetProperty("input");

            Assert.That(input.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(
                Order(input),
                Is.EqualTo(s_resetInputOrder),
                "The declaration order of the arguments is what a Call is positional over.");

            JsonElement reason = input.GetProperty("properties").GetProperty("Reason");
            Assert.That(reason.GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(reason.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=12"));
            Assert.That(reason.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(-1));
            Assert.That(
                reason.GetProperty("description").GetString(),
                Is.EqualTo("Why the machine was reset."));

            JsonElement level = input.GetProperty("properties").GetProperty("Level");
            Assert.That(level.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=7"));
            Assert.That(level.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(1));
            Assert.That(
                level.GetProperty("uav:arrayDimensions").EnumerateArray()
                    .Select(d => d.GetUInt32()),
                Is.EqualTo(s_levelDimensions));

            JsonElement output = action.GetProperty("output");
            Assert.That(Order(output), Is.EqualTo(s_resetOutputOrder));
            Assert.That(
                output.GetProperty("properties").GetProperty("Accepted")
                    .GetProperty("type").GetString(),
                Is.EqualTo("boolean"));
        }

        /// <summary>
        /// Once the action schemas represent the argument Variables, emitting
        /// them again as properties of the Thing would state the same two Nodes
        /// twice, in two different languages.
        /// </summary>
        [Test]
        public void RepresentedArgumentVariablesAreNotAlsoSiblingProperties()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateMethodNodeSet());

            Assert.That(document.Properties.Keys, Does.Not.Contain("InputArguments"));
            Assert.That(document.Properties.Keys, Does.Not.Contain("OutputArguments"));
            Assert.That(document.Properties.Keys, Does.Contain("Speed"));
        }

        /// <summary>
        /// A value this direction cannot read is not re-stated as an argument
        /// list it is not: the Variable stays a property naming its Method, so
        /// the Node is still carried.
        /// </summary>
        [Test]
        public void AnUndecodableArgumentListStaysAProperty()
        {
            UANodeSet source = CreateMethodNodeSet();
            UAVariable arguments = source.Items.OfType<UAVariable>()
                .Single(v => v.BrowseName == "InputArguments");
            arguments.Value = WotTestData.ParseValue(
                "<uax:ListOfExtensionObject xmlns:uax=\"" + UaXsd + "\">" +
                "<uax:ExtensionObject><uax:TypeId><uax:Identifier>i=999</uax:Identifier>" +
                "</uax:TypeId><uax:Body /></uax:ExtensionObject>" +
                "</uax:ListOfExtensionObject>");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(document.Actions["Reset"].TryGetProperty("input", out _), Is.False);
            Assert.That(document.Properties.Keys, Does.Contain("InputArguments"));
        }

        [Test]
        public void ArgumentsSurviveTheRoundTripBackToTheNodeSet()
        {
            UANodeSet source = CreateMethodNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(WithoutNativeProjection(document));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            List<DecodedArgument> input =
                ArgumentsOf(result.Value!, "InputArguments");
            Assert.That(input.Select(a => a.Name), Is.EqualTo(s_resetInputOrder));
            Assert.That(input[0].DataType, Is.EqualTo("i=12"));
            Assert.That(input[0].ValueRank, Is.EqualTo("-1"));
            Assert.That(input[0].Description, Is.EqualTo("Why the machine was reset."));
            Assert.That(input[1].DataType, Is.EqualTo("i=7"));
            Assert.That(input[1].ValueRank, Is.EqualTo("1"));
            Assert.That(input[1].ArrayDimensions, Is.EqualTo(s_levelDimensionText));

            List<DecodedArgument> output =
                ArgumentsOf(result.Value!, "OutputArguments");
            Assert.That(output.Select(a => a.Name), Is.EqualTo(s_resetOutputOrder));
            Assert.That(output[0].DataType, Is.EqualTo("i=1"));

            UAVariable arguments = result.Value!.Items.OfType<UAVariable>()
                .Single(v => v.BrowseName == "InputArguments");
            Assert.That(arguments.DataType, Is.EqualTo("i=296"));
            Assert.That(arguments.ValueRank, Is.EqualTo(1));
            Assert.That(arguments.ArrayDimensions, Is.EqualTo("2"));

            UAMethod method = result.Value.Items.OfType<UAMethod>().Single();
            Assert.That(
                method.References.Count(r =>
                    string.Equals(r.ReferenceType, "HasProperty", StringComparison.Ordinal) &&
                    r.IsForward),
                Is.EqualTo(2),
                "The Method holds both argument Properties.");
            Assert.That(arguments.ParentNodeId, Is.EqualTo(method.NodeId));
        }

        [Test]
        public void ARoundTrippedMethodStaysImportable()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateMethodNodeSet());
            UANodeSet restored =
                WotNodeSetConverter.ToNodeSet(WithoutNativeProjection(document));

            WotNodeSetImportTests.AssertImportable(restored, "method arguments");
        }

        /// <summary>
        /// The readable schemas state what the arguments are, not which Nodes
        /// hold them, so the exact identity and attributes of the argument
        /// Variables travel in the preservation projection - which is what
        /// keeps the mapping lossless rather than merely readable.
        /// </summary>
        [Test]
        public void TheArgumentVariablesKeepTheirIdentityThroughPreservation()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateMethodNodeSet());
            Assert.That(
                document.TryGetNativeProjection(out _),
                Is.True,
                "The readable mapping does not reproduce the argument Nodes exactly.");

            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            UAVariable input = restored.Items.OfType<UAVariable>()
                .Single(v => string.Equals(
                    v.BrowseName, "InputArguments", StringComparison.Ordinal));
            Assert.That(input.NodeId, Is.EqualTo("ns=1;i=6002"));
            Assert.That(
                restored.Items.OfType<UAVariable>().Single(v => string.Equals(
                    v.BrowseName, "OutputArguments", StringComparison.Ordinal)).NodeId,
                Is.EqualTo("ns=1;i=6003"));
        }

        /// <summary>
        /// The half of the mapping that never existed: a document authoring
        /// arguments used to produce a Method with no argument Property at all
        /// and no diagnostic saying so.
        /// </summary>
        [Test]
        public void AnAuthoredInputSchemaBecomesTheInputArgumentsProperty()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
                "\"uav:browseName\":\"pump:Reset\"," +
                "\"input\":{\"type\":\"object\"," +
                "\"uav:fieldOrder\":[\"Reason\",\"Level\"]," +
                "\"properties\":{" +
                "\"Reason\":{\"type\":\"string\",\"description\":\"Why.\"}," +
                "\"Level\":{\"type\":\"integer\",\"uav:mapToType\":\"i=7\"," +
                "\"uav:valueRank\":1,\"uav:arrayDimensions\":[4]}}}," +
                "\"output\":{\"type\":\"object\",\"uav:fieldOrder\":[\"Accepted\"]," +
                "\"properties\":{\"Accepted\":{\"type\":\"boolean\"}}}}}");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            List<DecodedArgument> input =
                ArgumentsOf(result.Value!, "InputArguments");
            Assert.That(input.Select(a => a.Name), Is.EqualTo(s_resetInputOrder));
            Assert.That(input[0].DataType, Is.EqualTo("i=12"));
            Assert.That(input[0].Description, Is.EqualTo("Why."));
            Assert.That(input[1].DataType, Is.EqualTo("i=7"));
            Assert.That(input[1].ValueRank, Is.EqualTo("1"));
            Assert.That(input[1].ArrayDimensions, Is.EqualTo(s_levelDimensionText));

            Assert.That(
                ArgumentsOf(result.Value!, "OutputArguments").Select(a => a.Name),
                Is.EqualTo(s_resetOutputOrder));
        }

        /// <summary>
        /// An argument whose DataType is a type the document defines resolves
        /// through the same identity machinery a property affordance uses,
        /// rather than being guessed from the json type.
        /// </summary>
        [Test]
        public void AnArgumentNamingACustomDataTypeResolvesThatIdentity()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"configure\":{\"@type\":\"uav:method\"," +
                "\"input\":{\"type\":\"object\",\"uav:fieldOrder\":[\"Mode\"]," +
                "\"properties\":{\"Mode\":{\"type\":\"integer\"," +
                "\"uav:mapToType\":\"nsu=urn:test:pump;i=3002\"}}}}}");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            DecodedArgument mode = ArgumentsOf(result.Value!, "InputArguments").Single();
            var resolved = NodeId.Parse(mode.DataType!);
            Assert.That(resolved.IdentifierAsString, Is.EqualTo("3002"));
            Assert.That(
                result.Value!.NamespaceUris[resolved.NamespaceIndex - 1],
                Is.EqualTo("urn:test:pump"));
        }

        /// <summary>
        /// A schema that names one DataType is one value and therefore one
        /// argument, whatever members that type has - the shape a Union-typed
        /// input takes in Section 6.11.4.
        /// </summary>
        [Test]
        public void ASchemaNamingOneDataTypeIsOneArgument()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"setCommand\":{\"@type\":\"uav:method\"," +
                "\"input\":{\"type\":\"object\",\"uav:mapToType\":\"i=22\"," +
                "\"uav:fieldOrder\":[\"Setpoint\",\"Stop\"]," +
                "\"properties\":{\"Setpoint\":{\"type\":\"number\"}," +
                "\"Stop\":{\"type\":\"boolean\"}}}}}");

            List<DecodedArgument> input =
                ArgumentsOf(result.Value!, "InputArguments");
            Assert.That(input, Has.Count.EqualTo(1));
            Assert.That(input[0].DataType, Is.EqualTo("i=22"));
        }

        /// <summary>
        /// JSON object member order carries no meaning, so a two-argument
        /// schema that states none is reported rather than silently ordered by
        /// however the document happened to be serialized.
        /// </summary>
        [Test]
        public void AnUnorderedMultiArgumentSchemaIsReportedAndPreserved()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
                "\"input\":{\"type\":\"object\",\"properties\":{" +
                "\"Reason\":{\"type\":\"string\"},\"Level\":{\"type\":\"integer\"}}}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.MethodArgumentOrderAmbiguous &&
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
            Assert.That(
                result.Value!.Items.OfType<UAVariable>()
                    .Any(v => v.BrowseName == "InputArguments"),
                Is.False,
                "Nothing is materialized from an order that cannot be known.");

            // Reported is not the same as dropped: the schema is still carried,
            // so converting back returns exactly what was authored.
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value);
            Assert.That(
                restored.Actions["reset"].GetProperty("input")
                    .GetProperty("properties").EnumerateObject().Select(p => p.Name),
                Is.EquivalentTo(s_resetInputOrder));
        }

        /// <summary>
        /// An order that names something the schema does not define, or that
        /// leaves a member out, disagrees with the schema it orders.
        /// </summary>
        [TestCase("[\"Reason\"]")]
        [TestCase("[\"Reason\",\"Reason\"]")]
        [TestCase("[\"Reason\",\"Missing\"]")]
        [TestCase("\"Reason\"")]
        public void AFieldOrderThatDisagreesWithTheMembersIsReported(string order)
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
                "\"input\":{\"type\":\"object\",\"uav:fieldOrder\":" + order + "," +
                "\"properties\":{" +
                "\"Reason\":{\"type\":\"string\"},\"Level\":{\"type\":\"integer\"}}}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.MethodArgumentSchemaInvalid),
                Is.True);
        }

        [Test]
        public void AnInputThatIsNotADataSchemaIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\",\"input\":\"Reason\"}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.MethodArgumentSchemaInvalid),
                Is.True);
        }

        /// <summary>
        /// Section 13.4 requires an Acknowledge, Confirm or AddComment action to
        /// declare EventId as an input, and OPC 10000-9 fixes the signature of
        /// all three, so the order follows from the Method rather than needing
        /// to be restated.
        /// </summary>
        [TestCase("Acknowledge")]
        [TestCase("Confirm")]
        [TestCase("AddComment")]
        public void AConditionActionMaterializesEventIdFirst(string conditionAction)
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"highTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:conditionType\":\"ua:LimitAlarmType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":" +
                "{\"EventId\":{\"type\":\"string\",\"contentEncoding\":\"base64\"}}}}}," +
                "\"actions\":{\"act\":{\"@type\":\"uav:method\"," +
                "\"uav:conditionAction\":\"" + conditionAction + "\"," +
                "\"uav:actsOn\":\"highTemperature\"," +
                "\"input\":{\"type\":\"object\",\"required\":[\"EventId\"]," +
                "\"properties\":{" +
                "\"Comment\":{\"type\":\"string\"}," +
                "\"EventId\":{\"type\":\"string\",\"contentEncoding\":\"base64\"}}}}}");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            List<DecodedArgument> input =
                ArgumentsOf(result.Value!, "InputArguments");
            Assert.That(
                input.Select(a => a.Name),
                Is.EqualTo(s_conditionOrder),
                "OPC 10000-9 puts EventId first however the members are written.");
            Assert.That(input[0].DataType, Is.EqualTo("i=15"));
            Assert.That(input[1].DataType, Is.EqualTo("i=12"));
        }

        /// <summary>
        /// A term the converter materializes must not also be carried as
        /// residue, or the same argument list would be stated twice.
        /// </summary>
        [Test]
        public void MaterializedArgumentSchemasAreNotAlsoResidue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"actions\":{\"reset\":{\"@type\":\"uav:method\"," +
                "\"input\":{\"type\":\"object\",\"uav:fieldOrder\":[\"Reason\"]," +
                "\"properties\":{\"Reason\":{\"type\":\"string\"}}}}}");

            string extensions = result.Value!.Extensions is null
                ? string.Empty
                : string.Concat(result.Value.Extensions.Select(e => e.OuterXml));
            Assert.That(extensions, Does.Not.Contain("/input"));
        }

        private static IReadOnlyList<string> Order(JsonElement schema)
        {
            return [.. schema.GetProperty("uav:fieldOrder").EnumerateArray()
                .Select(e => e.GetString()!)];
        }

        /// <summary>
        /// Decodes the Argument list a NodeSet's argument Property holds,
        /// independently of the converter's own reader.
        /// </summary>
        private static List<DecodedArgument> ArgumentsOf(
            UANodeSet nodeSet,
            string browseName)
        {
            UAVariable variable = nodeSet.Items.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, browseName, StringComparison.Ordinal));
            var decoded = new List<DecodedArgument>();
            foreach (System.Xml.XmlNode node in variable.Value.ChildNodes)
            {
                System.Xml.XmlElement argument = ((System.Xml.XmlElement)node)
                    .GetElementsByTagName("Argument", UaXsd)
                    .OfType<System.Xml.XmlElement>()
                    .Single();
                decoded.Add(new DecodedArgument(
                    Text(Child(argument, "Name")),
                    Text(Child(Child(argument, "DataType"), "Identifier")),
                    Text(Child(argument, "ValueRank")),
                    [.. Child(argument, "ArrayDimensions").ChildNodes
                        .OfType<System.Xml.XmlElement>().Select(e => e.InnerText)],
                    Text(Child(Child(argument, "Description"), "Text"))));
            }
            return decoded;
        }

        private static System.Xml.XmlElement Child(
            System.Xml.XmlElement parent,
            string localName)
        {
            return parent.ChildNodes.OfType<System.Xml.XmlElement>().FirstOrDefault(e =>
                string.Equals(e.LocalName, localName, StringComparison.Ordinal));
        }

        private static string Text(System.Xml.XmlElement element)
        {
            return element is null || element.InnerText.Length == 0 ? null : element.InnerText;
        }

        private sealed record DecodedArgument(
            string Name,
            string DataType,
            string ValueRank,
            IReadOnlyList<string> ArrayDimensions,
            string Description);

        /// <summary>
        /// Builds a type with one Method whose two argument Properties hold the
        /// canonical <c>Argument</c> value shape a NodeSet2 document carries.
        /// </summary>
        private static UANodeSet CreateMethodNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:MachineType",
                        DisplayName = [new Export.LocalizedText { Value = "MachineType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype", IsForward = false, Value = "i=58"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=6001"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=7001"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=6001",
                        BrowseName = "1:Speed",
                        DisplayName = [new Export.LocalizedText { Value = "Speed" }],
                        DataType = "i=11",
                        AccessLevel = 1,
                        ParentNodeId = "ns=1;i=1001",
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
                                Value = "ns=1;i=1001"
                            }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;i=7001",
                        BrowseName = "1:Reset",
                        DisplayName = [new Export.LocalizedText { Value = "Reset" }],
                        ParentNodeId = "ns=1;i=1001",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=6002"
                            },
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=6003"
                            },
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=1001"
                            }
                        ]
                    },
                    ArgumentProperty(
                        "ns=1;i=6002",
                        "InputArguments",
                        "<uax:Argument><uax:Name>Reason</uax:Name>" +
                        "<uax:DataType><uax:Identifier>i=12</uax:Identifier></uax:DataType>" +
                        "<uax:ValueRank>-1</uax:ValueRank><uax:ArrayDimensions />" +
                        "<uax:Description><uax:Locale>en</uax:Locale>" +
                        "<uax:Text>Why the machine was reset.</uax:Text>" +
                        "</uax:Description></uax:Argument>",
                        "<uax:Argument><uax:Name>Level</uax:Name>" +
                        "<uax:DataType><uax:Identifier>i=7</uax:Identifier></uax:DataType>" +
                        "<uax:ValueRank>1</uax:ValueRank>" +
                        "<uax:ArrayDimensions><uax:UInt32>4</uax:UInt32></uax:ArrayDimensions>" +
                        "<uax:Description /></uax:Argument>"),
                    ArgumentProperty(
                        "ns=1;i=6003",
                        "OutputArguments",
                        "<uax:Argument><uax:Name>Accepted</uax:Name>" +
                        "<uax:DataType><uax:Identifier>i=1</uax:Identifier></uax:DataType>" +
                        "<uax:ValueRank>-1</uax:ValueRank><uax:ArrayDimensions />" +
                        "<uax:Description /></uax:Argument>")
                ]
            };
        }

        private static UAVariable ArgumentProperty(
            string nodeId,
            string browseName,
            params string[] arguments)
        {
            var value = WotTestData.ParseValue(
                "<uax:ListOfExtensionObject xmlns:uax=\"" + UaXsd + "\">" +
                string.Concat(arguments.Select(a =>
                    "<uax:ExtensionObject><uax:TypeId>" +
                    "<uax:Identifier>i=297</uax:Identifier></uax:TypeId>" +
                    "<uax:Body>" + a + "</uax:Body></uax:ExtensionObject>")) +
                "</uax:ListOfExtensionObject>");

            return new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = [new Export.LocalizedText { Value = browseName }],
                ParentNodeId = "ns=1;i=7001",
                DataType = "i=296",
                ValueRank = 1,
                ArrayDimensions = arguments.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                AccessLevel = 1,
                Value = value,
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty", IsForward = false, Value = "ns=1;i=7001"
                    }
                ]
            };
        }

        /// <summary>
        /// Strips the native projection so a round trip exercises the readable
        /// mapping; left in place it is preferred on the way back and the
        /// readable terms are never read at all.
        /// </summary>
        private static WotDocument WithoutNativeProjection(WotDocument document)
        {
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return WotDocument.Parse(buffer.ToArray());
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:Pump\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                members + "}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static readonly string[] s_resetInputOrder = ["Reason", "Level"];
        private static readonly string[] s_resetOutputOrder = ["Accepted"];
        private static readonly uint[] s_levelDimensions = [4];
        private static readonly string[] s_levelDimensionText = ["4"];
        private static readonly string[] s_conditionOrder = ["EventId", "Comment"];

        private const string UaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
