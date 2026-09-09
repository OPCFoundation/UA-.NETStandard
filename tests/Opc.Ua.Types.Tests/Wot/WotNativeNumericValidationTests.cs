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

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotNativeNumericValidationTests
    {
        [Test]
        public void FractionalNativeValueRankIsRejectedInsteadOfRestoredAsScalar()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "nodeClass": "Variable", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "dataType": "i=11", "valueRank": 1.5
                }
                """);

            AssertInvalid(result, "/uav:nodes/nodes/0/valueRank");
        }

        [TestCase("Variable", "accessLevel", "-1")]
        [TestCase("Variable", "accessLevel", "4294967296")]
        [TestCase("Variable", "userAccessLevel", "1.5")]
        [TestCase("Variable", "writeMask", "-1")]
        [TestCase("Variable", "userWriteMask", "4294967296")]
        [TestCase("Variable", "accessRestrictions", "65536")]
        [TestCase("Variable", "accessRestrictions", "0.5")]
        [TestCase("Object", "eventNotifier", "256")]
        [TestCase("View", "eventNotifier", "-1")]
        [TestCase("View", "eventNotifier", "1.5")]
        [TestCase("VariableType", "valueRank", "2147483648")]
        public void NativeIntegerWidthsRejectInvalidPresentNumbers(string nodeClass, string member, string number)
        {
            WotConversionResult<UANodeSet> result = Convert($$"""
                {
                  "nodeClass": "{{nodeClass}}", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "{{member}}": {{number}}
                }
                """);

            AssertInvalid(result, "/uav:nodes/nodes/0/" + member);
        }

        [TestCase("1e400")]
        [TestCase("-1e400")]
        public void NativeSamplingIntervalMustRemainFinite(string number)
        {
            WotConversionResult<UANodeSet> result = Convert($$"""
                {
                  "nodeClass": "Variable", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "minimumSamplingInterval": {{number}}
                }
                """);

            AssertInvalid(result, "/uav:nodes/nodes/0/minimumSamplingInterval");
        }

        [TestCase("valueRank", "1.5")]
        [TestCase("maxStringLength", "-1")]
        [TestCase("maxStringLength", "4294967296")]
        [TestCase("value", "2147483648")]
        [TestCase("value", "-2147483649")]
        public void NestedDataTypeNumbersReportTheFieldPointer(string member, string number)
        {
            WotConversionResult<UANodeSet> result = Convert($$"""
                {
                  "nodeClass": "DataType", "nodeId": "ns=1;s=Type", "browseName": "1:Type",
                  "definition": {
                    "name": "Type",
                    "fields": [{ "name": "Field", "dataType": "i=11", "{{member}}": {{number}} }]
                  }
                }
                """);

            AssertInvalid(result, "/uav:nodes/nodes/0/definition/fields/0/" + member);
        }

        [Test]
        public void NativePermissionNumbersReportTheirOwningRecord()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "nodeClass": "Object", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "rolePermissions": [{ "roleId": "i=15644", "permissions": -1 }]
                }
                """);

            AssertInvalid(result, "/uav:nodes/nodes/0/rolePermissions/0/permissions");
        }

        [Test]
        public void NativeModelNumbersReportEveryInvalidNestedOwner()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """{ "nodeClass": "Object", "nodeId": "ns=1;s=Value", "browseName": "1:Value" }""",
                """
                [{
                  "modelUri": "urn:native-numbers", "accessRestrictions": 65536,
                  "requiredModels": [{
                    "modelUri": "urn:dependency",
                    "rolePermissions": [{ "roleId": "i=15644", "permissions": 4294967296 }]
                  }]
                }]
                """);

            AssertInvalid(result, "/uav:nodes/models/0/accessRestrictions");
            AssertInvalid(result, "/uav:nodes/models/0/requiredModels/0/rolePermissions/0/permissions");
        }

        [Test]
        public void SupportedNativeNumericLimitsAndMissingDefaultsArePreserved()
        {
            WotConversionResult<UANodeSet> bounded = Convert(
                """
                {
                  "nodeClass": "Variable", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "writeMask": 4294967295, "userWriteMask": 4294967295, "accessRestrictions": 65535,
                  "accessLevel": 4294967295, "userAccessLevel": 4294967295,
                  "valueRank": 2, "minimumSamplingInterval": 0.125
                }
                """);
            Assert.That(bounded.Success, Is.True, string.Join("; ", bounded.Diagnostics));
            var variable = (UAVariable)bounded.Value!.Items!.Single();
            Assert.Multiple(() =>
            {
                Assert.That(variable.WriteMask, Is.EqualTo(uint.MaxValue));
                Assert.That(variable.UserWriteMask, Is.EqualTo(uint.MaxValue));
                Assert.That(variable.AccessRestrictions, Is.EqualTo(ushort.MaxValue));
                Assert.That(variable.AccessRestrictionsSpecified, Is.True);
                Assert.That(variable.AccessLevel, Is.EqualTo(uint.MaxValue));
                Assert.That(variable.UserAccessLevel, Is.EqualTo(uint.MaxValue));
                Assert.That(variable.ValueRank, Is.EqualTo(2));
                Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(0.125));
            });

            WotConversionResult<UANodeSet> defaults = Convert(
                """{ "nodeClass": "Variable", "nodeId": "ns=1;s=Value", "browseName": "1:Value" }""");
            Assert.That(defaults.Success, Is.True, string.Join("; ", defaults.Diagnostics));
            var defaultVariable = (UAVariable)defaults.Value!.Items!.Single();
            Assert.That(defaultVariable.ValueRank, Is.EqualTo(-1));
            Assert.That(defaultVariable.AccessLevel, Is.EqualTo(1));
            Assert.That(defaultVariable.MinimumSamplingInterval, Is.Zero);
            Assert.That(defaultVariable.AccessRestrictionsSpecified, Is.False);
        }

        [TestCase("-1")]
        [TestCase("0")]
        [TestCase("1.7976931348623157e308")]
        public void NativeSamplingIntervalRetainsSupportedFiniteValues(string number)
        {
            WotConversionResult<UANodeSet> result = Convert($$"""
                {
                  "nodeClass": "Variable", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "minimumSamplingInterval": {{number}}
                }
                """);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            double expected = number switch
            {
                "-1" => -1,
                "0" => 0,
                _ => double.MaxValue
            };
            Assert.That(((UAVariable)result.Value!.Items!.Single()).MinimumSamplingInterval, Is.EqualTo(expected));
        }

        [Test]
        public void UnknownNativeRecordNumbersAreNotTreatedAsKnownAttributes()
        {
            WotConversionResult<UANodeSet> result = Convert(
                """
                {
                  "nodeClass": "Object", "nodeId": "ns=1;s=Value", "browseName": "1:Value",
                  "vendorFuture": { "valueRank": 1.5, "eventNotifier": 1000 }
                }
                """);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
        }

        private static void AssertInvalid(WotConversionResult<UANodeSet> result, string pointer)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Code == WotDiagnosticCode.NativeProjectionInvalid &&
                diagnostic.Location?.JsonPointer == pointer), Is.True,
                string.Join("; ", result.Diagnostics));
        }

        private static WotConversionResult<UANodeSet> Convert(
            [StringSyntax(StringSyntaxAttribute.Json)] string node,
            [StringSyntax(StringSyntaxAttribute.Json)] string? models = null)
        {
            string modelMember = models is null ? string.Empty : "\"models\":" + models + ",";
            string document = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
                  ],
                  "@type": "tm:ThingModel",
                  "title": "Native number validation",
                  "uav:nodes": {
                    "@type": "uav:NodeModel",
                    "profileVersion": "1.0",
                    "namespaceUris": ["urn:native-numbers"],
                    {{modelMember}}
                    "nodes": [{{node}}]
                  }
                }
                """;
            using var parsed = WotDocument.Parse(Encoding.UTF8.GetBytes(document));
            return WotNodeSetConverter.ToNodeSetResult(parsed);
        }
    }
}
