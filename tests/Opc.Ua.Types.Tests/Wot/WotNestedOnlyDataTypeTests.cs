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
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// A nested-only DataType may be a field of another Structure and nothing
    /// else, so selecting it directly is refused while the document is in hand.
    /// </summary>
    /// <remarks>
    /// A concrete Structure or Union that states
    /// <c>uav:hasDefaultEncoding: false</c> has a null
    /// <c>DefaultEncodingId</c> and no encoding Objects. A Variable, Method
    /// argument or Event field that selects it looks perfectly well formed and
    /// browses correctly, and then fails the first time a client tries to read
    /// or write it, because there is no encoding to name in the ExtensionObject
    /// that would carry the value. Closure validation is the last point at
    /// which the document that caused that is still available to blame.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotNestedOnlyDataTypeTests
    {
        private const string NestedOnlyId = "ns=1;s=DataTypes/Reading";

        /// <summary>
        /// The type itself is still materialized: it is legitimate as a field,
        /// so refusing the document outright would refuse a valid model.
        /// </summary>
        [Test]
        public void ANestedOnlyTypeIsStillMaterializedForUseAsAField()
        {
            WotConversionResult<UANodeSet> result = Convert(
                properties: "\"batch\":{\"type\":\"object\"," +
                    "\"uav:browseName\":\"pump:Batch\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + BatchGraphId + "\"}}");

            Assert.Multiple(() =>
            {
                Assert.That(Errors(result), Is.Empty, Messages(result));
                Assert.That(
                    result.Value!.Items!.OfType<UADataType>()
                        .Any(t => string.Equals(
                            t.NodeId, NestedOnlyId, StringComparison.Ordinal)),
                    Is.True,
                    "The nested-only type is a real DataType Node; only selecting " +
                    "it directly is refused.");
            });
        }

        [Test]
        public void AVariableSelectingANestedOnlyTypeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                properties: "\"reading\":{\"type\":\"object\"," +
                    "\"uav:browseName\":\"pump:Reading\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + ReadingGraphId + "\"}}");

            Assert.That(
                Errors(result).Any(d =>
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains("1:Reading", StringComparison.Ordinal) &&
                    d.Message.Contains(
                        "uav:hasDefaultEncoding false", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// An Event field is a Property of the event type, so it reaches the
        /// same check by the same route a Variable does.
        /// </summary>
        [Test]
        public void AnEventFieldSelectingANestedOnlyTypeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                events: "\"alarm\":{\"@type\":\"uav:eventType\"," +
                    "\"uav:browseName\":\"pump:AlarmType\"," +
                    "\"data\":{\"type\":\"object\"," +
                    "\"properties\":{\"reading\":{\"type\":\"object\"," +
                    "\"uav:browseName\":\"pump:Reading\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + ReadingGraphId + "\"}}}}}");

            Assert.That(
                Errors(result).Any(d =>
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains("'1:Reading'", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// An argument's DataType is not an attribute of any Node - it lives
        /// inside the encoded Argument the InputArguments Property carries - so
        /// it needs a check of its own or it escapes entirely.
        /// </summary>
        [Test]
        public void AMethodArgumentSelectingANestedOnlyTypeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                actions: "\"record\":{\"uav:browseName\":\"pump:Record\"," +
                    "\"input\":{\"type\":\"object\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + ReadingGraphId + "\"}}}");

            Assert.That(
                Errors(result).Any(d =>
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains("InputArguments", StringComparison.Ordinal) &&
                    d.Message.Contains("argument 'Input'", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        [Test]
        public void AMethodOutputArgumentSelectingANestedOnlyTypeIsRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                actions: "\"record\":{\"uav:browseName\":\"pump:Record\"," +
                    "\"output\":{\"type\":\"object\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + ReadingGraphId + "\"}}}");

            Assert.That(
                Errors(result).Any(d =>
                    d.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                    d.Message.Contains("OutputArguments", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// An encodable type selected the same way is accepted, so the check
        /// discriminates on the declaration and not on the shape of the
        /// selection.
        /// </summary>
        [Test]
        public void AnEncodableTypeSelectedTheSameWayIsAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert(
                properties: "\"batch\":{\"type\":\"object\"," +
                    "\"uav:browseName\":\"pump:Batch\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + BatchGraphId + "\"}}",
                actions: "\"record\":{\"uav:browseName\":\"pump:Record\"," +
                    "\"input\":{\"type\":\"object\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"" + BatchGraphId + "\"}}}");

            Assert.That(Errors(result), Is.Empty, Messages(result));
        }

        /// <summary>
        /// Nothing is refused when no type declares itself nested-only, so the
        /// check costs an ordinary document nothing and cannot misfire on one.
        /// </summary>
        [Test]
        public void ADocumentWithoutNestedOnlyTypesIsUnaffected()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"pump:Speed\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(Errors(result), Is.Empty, Messages(result));
        }

        private const string ReadingGraphId = "urn:test:pump#Reading";
        private const string BatchGraphId = "urn:test:pump#Batch";

        private static WotDiagnostic[] Errors(WotConversionResult<UANodeSet> result)
        {
            return [.. result.Diagnostics
                .Where(d => d.Severity == WotDiagnosticSeverity.Error)];
        }

        private static string Messages(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        private static WotConversionResult<UANodeSet> Convert(
            string? properties = null,
            string? actions = null,
            string? events = null)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                (properties is null
                    ? string.Empty
                    : "\"properties\":{" + properties + "},") +
                (actions is null ? string.Empty : "\"actions\":{" + actions + "},") +
                (events is null ? string.Empty : "\"events\":{" + events + "},") +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"" + ReadingGraphId + "\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Reading\"," +
                "\"uav:hasDefaultEncoding\":false," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]},{" +
                "\"@id\":\"" + BatchGraphId + "\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Batch\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Reading\"," +
                "\"uav:fieldDataTypeName\":\"pump:Reading\"," +
                "\"uav:fieldDataTypeDefinition\":{\"@id\":\"" + ReadingGraphId + "\"}}]}]}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
