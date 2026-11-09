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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Bounds generated sets before document accumulation and recursive graph traversal.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotDocumentSetBudgetTests
    {
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void GeneratedDocumentCountHasAnInclusiveBoundary(int limit, bool success)
        {
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                Nodes(2), "nodes", options: new WotNodeSetConverterOptions { MaxDocumentSetDocuments = limit });
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), Describe(result));
            if (success)
            {
                Assert.That(set!.Entries.Count, Is.EqualTo(2));
            }
            else
            {
                Assert.That(set, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ResolverLimitExceeded), Is.True);
            }
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void SourceNodeCountIsCheckedBeforeGeneratingDocuments(int limit, bool success)
        {
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                Nodes(2), "nodes", options: new WotNodeSetConverterOptions { MaxNodeCount = limit });
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), Describe(result));
            if (!success)
            {
                Assert.That(set, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NodeCountExceeded), Is.True);
            }
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public void LinkedDocumentDepthHasAnInclusiveBoundary(int limit, bool success)
        {
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                Nodes(3, linked: true), "nodes",
                options: new WotNodeSetConverterOptions { MaxDocumentSetDepth = limit });
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), Describe(result));
            if (success)
            {
                Assert.That(set!.Entries.Count, Is.EqualTo(3));
            }
            else
            {
                Assert.That(set, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.DepthExceeded), Is.True);
            }
        }

        [Test]
        public void DeepObjectGraphIsRefusedBeforeRecursiveEmission()
        {
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                Nodes(1000, linked: true), "nodes",
                options: new WotNodeSetConverterOptions { MaxDocumentSetDepth = 2 });
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(set, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DepthExceeded), Is.True);
        }

        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void GeneratedDocumentBytesHaveAnInclusiveCumulativeBoundary(int delta, bool success)
        {
            UANodeSet source = Nodes(2);
            WotConversionResult<WotDocumentSet> baseline = WotNodeSetConverter.FromNodeSetDocuments(source, "nodes");
            Assert.That(baseline.Success, Is.True, Describe(baseline));
            using WotDocumentSet expected = baseline.Value!;
            long bytes = expected.Entries.ToArray()!.Sum(entry => (long)entry.Document.Utf8Json.Length);
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(source, "nodes",
                options: new WotNodeSetConverterOptions { MaxDocumentSetBytes = bytes + delta });
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), Describe(result));
            if (success)
            {
                Assert.That(set!.Entries.ToArray()!.Sum(entry => (long)entry.Document.Utf8Json.Length),
                    Is.EqualTo(bytes));
            }
            else
            {
                Assert.That(set, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.JsonDocumentTooLarge), Is.True);
            }
        }

        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public async Task PreservedPartitionsShareTheGeneratedByteBudgetAsync(int delta, bool success)
        {
            UANodeSet source = Nodes(2);
            var options = new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always };
            WotConversionResult<WotDocumentSet> baseline = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                source, "nodes", options: options).ConfigureAwait(false);
            Assert.That(baseline.Success, Is.True, Describe(baseline));
            using WotDocumentSet expected = baseline.Value!;
            long bytes = expected.Entries.ToArray()!.Sum(entry => (long)entry.Document.Utf8Json.Length);
            options.MaxDocumentSetBytes = bytes + delta;
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                source, "nodes", options: options).ConfigureAwait(false);
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), Describe(result));
            if (success)
            {
                Assert.That(set!.Entries.ToArray()!.Sum(entry => (long)entry.Document.Utf8Json.Length),
                    Is.EqualTo(bytes));
            }
            else
            {
                Assert.That(set, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.JsonDocumentTooLarge), Is.True);
            }
        }

        [Test]
        public async Task PreCancellationStopsDocumentSetConversion()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    Nodes(2), "nodes", cancellationToken: cancellation.Token).ConfigureAwait(false));
        }

        [TestCase("MaxDocumentSetDocuments", 0)]
        [TestCase("MaxDocumentSetDocuments", -1)]
        [TestCase("MaxDocumentSetDepth", 0)]
        [TestCase("MaxDocumentSetDepth", -1)]
        [TestCase("MaxDocumentSetBytes", 0)]
        [TestCase("MaxDocumentSetBytes", -1)]
        public void InvalidGeneratedSetLimitsAreExplicitErrors(string property, int limit)
        {
            var options = new WotNodeSetConverterOptions();
            switch (property)
            {
                case nameof(options.MaxDocumentSetDocuments):
                    options.MaxDocumentSetDocuments = limit;
                    break;
                case nameof(options.MaxDocumentSetDepth):
                    options.MaxDocumentSetDepth = limit;
                    break;
                case nameof(options.MaxDocumentSetBytes):
                    options.MaxDocumentSetBytes = limit;
                    break;
                default:
                    Assert.Fail("Unknown test option.");
                    break;
            }
            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo(property));
        }

        [Test]
        public void GeneratedSetLimitsDoNotRepurposeExternalResolverPolicy()
        {
            var options = new WotNodeSetConverterOptions
            {
                MaxResolverDocuments = 1,
                MaxResolverDepth = 1,
                MaxResolverTotalBytes = 1
            };
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                Nodes(3, linked: true), "nodes", options: options);
            using WotDocumentSet? set = result.Value;

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(set!.Entries.Count, Is.EqualTo(3));
        }

        private static UANodeSet Nodes(int count, bool linked = false)
        {
            var nodes = new UANode[count];
            for (int index = 0; index < count; index++)
            {
                string name = "Node" + index.ToString(CultureInfo.InvariantCulture);
                string parent = "ns=1;s=Node" + (index - 1).ToString(CultureInfo.InvariantCulture);
                nodes[index] = new UAObject
                {
                    NodeId = "ns=1;s=" + name,
                    BrowseName = "1:" + name,
                    DisplayName = [new Export.LocalizedText { Value = name }],
                    ParentNodeId = linked && index > 0 ? parent : null,
                    References = linked && index > 0
                        ? [
                            new Reference { ReferenceType = "i=40", Value = "i=58" },
                            new Reference { ReferenceType = "i=47", IsForward = false, Value = parent }
                        ]
                        : [new Reference { ReferenceType = "i=40", Value = "i=58" }]
                };
            }
            return new UANodeSet
            {
                NamespaceUris = ["urn:wot:document-budgets"],
                Models = [new ModelTableEntry { ModelUri = "urn:wot:document-budgets" }],
                Items = nodes
            };
        }

        private static string Describe(WotConversionResult<WotDocumentSet> result)
        {
            return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }
    }
}
