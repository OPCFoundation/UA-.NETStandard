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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotOpaqueBoundsTests
    {
        [TestCase("1.0", WotConformanceMode.Permissive)]
        [TestCase("1.1", WotConformanceMode.Permissive)]
        [TestCase("1.0", WotConformanceMode.Strict)]
        [TestCase("1.1", WotConformanceMode.Strict)]
        [TestCase("999.0", WotConformanceMode.Permissive)]
        [TestCase("999.0", WotConformanceMode.Strict)]
        public void OpaqueByteBoundIsAnErrorInEveryConsumerMode(string revision, WotConformanceMode mode)
        {
            const string prefix = "{\"urn:vendor:blob\":\"";
            const string suffix = "\"}";
            string opaque = prefix + new string('x', 65537 - prefix.Length - suffix.Length) + suffix;
            Assert.That(Encoding.UTF8.GetByteCount(opaque), Is.EqualTo(65537));

            WotConversionResult<UANodeSet> result = Convert(opaque, revision, mode);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.JsonPointer == "/uav:metadata"), Is.True,
                string.Join("; ", result.Diagnostics));
        }

        [TestCase(false, WotConformanceMode.Permissive)]
        [TestCase(true, WotConformanceMode.Permissive)]
        [TestCase(false, WotConformanceMode.Strict)]
        [TestCase(true, WotConformanceMode.Strict)]
        public void ThirtyTwoOpaqueContainersDoNotCountTheirScalarLeafAsAnotherLevel(
            bool includeArrays, WotConformanceMode mode)
        {
            WotConversionResult<UANodeSet> result = Convert(BuildNestedObject(32, includeArrays), "1.1", mode);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.OpaqueObjectInvalid),
                Is.False);
        }

        [TestCase(WotConformanceMode.Permissive, false)]
        [TestCase(WotConformanceMode.Strict, false)]
        [TestCase(WotConformanceMode.Permissive, true)]
        [TestCase(WotConformanceMode.Strict, true)]
        public void LegacyOpaqueKeyAdmissionDependsOnAuthoringRatherThanConsumerStrictness(
            WotConformanceMode mode, bool authoring)
        {
            WotConversionResult<UANodeSet> result = Convert("""{ "revision": 3 }""", "1.0", mode, authoring);

            Assert.That(result.Success, Is.EqualTo(!authoring), string.Join("; ", result.Diagnostics));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                diagnostic.Severity == (authoring ? WotDiagnosticSeverity.Error : WotDiagnosticSeverity.Warning)),
                Is.True, string.Join("; ", result.Diagnostics));
            if (!authoring)
            {
                using WotDocument roundTrip = WotNodeSetConverter.FromNodeSet(result.Value!);
                Assert.That(roundTrip.RootElement.GetProperty("uav:metadata").GetProperty("revision").GetInt32(),
                    Is.EqualTo(3));
            }
        }

        [TestCase(WotConformanceMode.Permissive, false)]
        [TestCase(WotConformanceMode.Strict, false)]
        [TestCase(WotConformanceMode.Permissive, true)]
        [TestCase(WotConformanceMode.Strict, true)]
        public void ExactOpaqueByteLimitUsesUtf8OctetsAndIgnoresOnlyInsignificantWhitespace(
            WotConformanceMode mode, bool unicode)
        {
            const string prefix = "{\"urn:vendor:blob\":\"";
            const string suffix = "\"}";
            int valueBytes = 65536 - prefix.Length - suffix.Length;
            string payload = unicode
                ? new string('\u00e9', valueBytes / 2) + new string('x', valueBytes % 2)
                : new string('x', valueBytes);
            string compact = prefix + payload + suffix;
            Assert.That(Encoding.UTF8.GetByteCount(compact), Is.EqualTo(65536));
            string received = "{ \n \"urn:vendor:blob\" : \"" + payload + "\" \n }";
            Assert.That(Encoding.UTF8.GetByteCount(received), Is.GreaterThan(65536));

            WotConversionResult<UANodeSet> result = Convert(received, "1.1", mode);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
        }

        [TestCase(256, WotConformanceMode.Permissive)]
        [TestCase(256, WotConformanceMode.Strict)]
        [TestCase(257, WotConformanceMode.Permissive)]
        [TestCase(257, WotConformanceMode.Strict)]
        public void OpaqueTopLevelKeyLimitHasTheSameBoundaryForEveryConsumer(
            int keyCount, WotConformanceMode mode)
        {
            string entries = string.Join(",", Enumerable.Range(0, keyCount).Select(index =>
                "\"urn:vendor:k" + index.ToString(CultureInfo.InvariantCulture) + "\":0"));
            WotConversionResult<UANodeSet> result = Convert("{" + entries + "}", "1.1", mode);

            Assert.That(result.Success, Is.EqualTo(keyCount <= 256), string.Join("; ", result.Diagnostics));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("top-level keys", StringComparison.Ordinal)), Is.EqualTo(keyCount > 256));
        }

        [TestCase(false, WotConformanceMode.Permissive)]
        [TestCase(true, WotConformanceMode.Permissive)]
        [TestCase(false, WotConformanceMode.Strict)]
        [TestCase(true, WotConformanceMode.Strict)]
        public void ThirtyThreeOpaqueContainersExceedTheBoundInEveryConsumer(
            bool includeArrays, WotConformanceMode mode)
        {
            WotConversionResult<UANodeSet> result = Convert(BuildNestedObject(33, includeArrays), "1.0", mode);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.OpaqueObjectInvalid &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Message.Contains("33 levels", StringComparison.Ordinal)), Is.True,
                string.Join("; ", result.Diagnostics));
        }

        private static string BuildNestedObject(int containers, bool includeArrays)
        {
            string value = "0";
            for (int index = 0; index < containers - 1; index++)
            {
                value = includeArrays && index % 2 == 0 ? "[" + value + "]" : "{\"child\":" + value + "}";
            }
            return "{\"urn:vendor:depth\":" + value + "}";
        }

        private static WotConversionResult<UANodeSet> Convert(
            [StringSyntax(StringSyntaxAttribute.Json)] string opaque,
            string revision,
            WotConformanceMode mode,
            bool authoring = false)
        {
            string json = $$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "device": "urn:opaque-bounds#" }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Bounded",
                  "uav:browseName": "device:Bounded",
                  "uav:bindingVersion": "{{revision}}",
                  "uav:metadata": {{opaque}}
                }
                """;
            var options = new WotNodeSetConverterOptions { ConformanceMode = mode, AuthoringValidation = authoring };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(json), options);
            return WotNodeSetConverter.ToNodeSetResult(document, options);
        }
    }
}
