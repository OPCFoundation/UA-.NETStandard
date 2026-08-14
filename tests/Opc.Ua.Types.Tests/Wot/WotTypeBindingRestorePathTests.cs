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
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotTypeBindingRestorePathTests
    {
        private const string BindingNamespace = "urn:test:binding";

        [Test]
        public async Task EnvelopeWithAmbiguousBindingRestoresWithoutBindingErrorAsync()
        {
            byte[] json = BuildRestoreDocument(WotNodeSetPreservationMode.Always);

            WotConversionResult<UANodeSet> result = await ConvertWithHeldNamespaceAsync(json)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics.Any(IsTypeBindingError), Is.False);
        }

        [Test]
        public async Task NativeProjectionWithAmbiguousBindingRestoresWithoutBindingErrorAsync()
        {
            byte[] json = BuildRestoreDocument(WotNodeSetPreservationMode.Never);

            WotConversionResult<UANodeSet> result = await ConvertWithHeldNamespaceAsync(json)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics.Any(IsTypeBindingError), Is.False);
        }

        [Test]
        public async Task SynthesisWithUnresolvedBindingReportsBindingErrorAsync()
        {
            byte[] json = BuildSynthesisDocument();

            WotConversionResult<UANodeSet> result = await ConvertWithHeldNamespaceAsync(json)
                .ConfigureAwait(false);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.True);
            Assert.That(result.Diagnostics.Any(IsTypeBindingError), Is.True);
        }

        private static byte[] BuildRestoreDocument(WotNodeSetPreservationMode preservationMode)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                WotTestData.CreateReconstructableNodeSet(),
                options: new WotNodeSetConverterOptions { PreservationMode = preservationMode });

            string json = Encoding.UTF8.GetString(document.Utf8Json.ToArray());
            JsonObject root = JsonNode.Parse(json)!.AsObject();
            AddAmbiguousTypeBinding(root);
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        }

        private static byte[] BuildSynthesisDocument()
        {
            return WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + BindingNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\",\"pump:MissingType\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:binding;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}}");
        }

        private static void AddAmbiguousTypeBinding(JsonObject root)
        {
            root["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/",
                    ["ua"] = "http://opcfoundation.org/UA/",
                    ["pump"] = BindingNamespace
                });
            root["@type"] = new JsonArray("Thing", "uav:object", "pump:MissingType");
            root["links"] = new JsonArray(
                new JsonObject
                {
                    ["rel"] = "ua:HasTypeDefinition",
                    ["href"] = "nsu=urn:test:binding;i=1"
                },
                new JsonObject
                {
                    ["rel"] = "ua:HasTypeDefinition",
                    ["href"] = "nsu=urn:test:binding;i=2"
                });
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertWithHeldNamespaceAsync(
            byte[] json)
        {
            using WotDocument document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document,
                null,
                null,
                null,
                new HeldNamespaceResolver()).ConfigureAwait(false);
        }

        private static bool IsTypeBindingError(WotDiagnostic diagnostic)
        {
            return diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Code is WotDiagnosticCode.UnresolvedTypeBinding
                    or WotDiagnosticCode.AmbiguousTypeBinding
                    or WotDiagnosticCode.InvalidTypeBinding;
        }

        private sealed class HeldNamespaceResolver : IWotNodeResolver
        {
            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, BindingNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
            }
        }
    }
}
