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
    /// Four channels can say what a Variable's DataType is, and they are
    /// ranked: <c>uav:mapToType</c>, then an authored DataType definition, then
    /// <c>uav:dataTypeId</c>, then what the json type implies.
    /// </summary>
    /// <remarks>
    /// The ranking settles which statement is read when several are present.
    /// It does not excuse a document that makes two different definitive
    /// statements: silently taking the higher-ranked one would leave a Variable
    /// typed against a statement its own author contradicted, and the
    /// contradiction would surface only where a value failed to encode.
    /// Inference is excluded from that, because being overridden is exactly
    /// what the lowest rank means.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDataTypeAuthorityTests
    {
        private const string DefinitionId = "ns=1;s=DataTypes/Reading";

        [Test]
        public void MapToTypeOutranksAnAuthoredDefinition()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"},",
                    expectError: true),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void MapToTypeOutranksDataTypeId()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\",",
                    expectError: true),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void AnAuthoredDefinitionOutranksDataTypeId()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"}," +
                    "\"uav:dataTypeId\":\"i=12\",",
                    expectError: true),
                Is.EqualTo(DefinitionId));
        }

        /// <summary>
        /// Inference is the lowest rank, so being overridden by any definitive
        /// statement is what it is for and never a contradiction.
        /// </summary>
        [TestCase("\"uav:mapToType\":\"i=11\",", "i=11")]
        [TestCase("\"uav:dataTypeId\":\"i=11\",", "i=11")]
        [TestCase(
            "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"},",
            DefinitionId)]
        public void ADefinitiveStatementSilentlyOverridesInference(
            string terms, string expected)
        {
            Assert.That(DataTypeOf(terms, expectError: false), Is.EqualTo(expected));
        }

        /// <summary>
        /// Two definitive statements that name the same type agree, so nothing
        /// is reported: the check is about contradiction, not redundancy.
        /// </summary>
        [Test]
        public void TwoDefinitiveStatementsThatAgreeAreAccepted()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=11\",",
                    expectError: false),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void TheDisagreementNamesBothTermsAndBothTypes()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\",");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Message.Contains("uav:mapToType 'i=11'", StringComparison.Ordinal) &&
                    d.Message.Contains("uav:dataTypeId 'i=12'", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// All three at once is one contradiction per disagreeing pair, so a
        /// reader is told about every statement it has to reconcile rather than
        /// only the first.
        /// </summary>
        [Test]
        public void EveryDisagreeingPairIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:mapToType\":\"i=11\"," +
                "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"}," +
                "\"uav:dataTypeId\":\"i=12\",");

            Assert.That(
                result.Diagnostics.Count(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Message.Contains("definitive statements", StringComparison.Ordinal)),
                Is.EqualTo(3),
                Messages(result));
        }

        /// <summary>
        /// A definitive statement stated once cannot disagree with itself, so a
        /// perfectly ordinary document gains no diagnostic from the check.
        /// </summary>
        [Test]
        public void ASingleDefinitiveStatementIsNeverAContradiction()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:mapToType\":\"i=11\",");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                Messages(result));
        }

        /// <summary>
        /// A DataSchema with no BrowseName of its own still names the
        /// contradiction it makes, falling back to its title, so the reader is
        /// not handed a diagnostic that points at nothing.
        /// </summary>
        [Test]
        public void ASchemaWithoutABrowseNameIsStillLocated()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"type\":\"string\"," +
                "\"title\":\"Speed\"," +
                "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Location?.Reference == "Speed"),
                Is.True,
                Messages(result));
        }

        private static string? DataTypeOf(string terms, bool expectError)
        {
            WotConversionResult<UANodeSet> result = Convert(terms);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.EqualTo(expectError),
                Messages(result));
            return result.Value?.Items?
                .OfType<UAVariable>()
                .FirstOrDefault(v => string.Equals(
                    v.BrowseName, "1:Speed", StringComparison.Ordinal))?
                .DataType;
        }

        private static string Messages(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        private static WotConversionResult<UANodeSet> Convert(string terms)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"type\":\"string\"," +
                "\"uav:browseName\":\"pump:Speed\"," +
                terms.TrimEnd(',') + "}}," +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"urn:test:pump#Reading\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Reading\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]}]}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
