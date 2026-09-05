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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// A projection resolves documents, and every document it resolves counts
    /// against the same bounds the rest of a conversion runs under. These pin
    /// the boundary exactly - at the limit and one past it - and pin that the
    /// resolved view does not depend on the order a source happened to write
    /// its members in.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotProjectionLimitTests
    {
        /// <summary>
        /// A manifest naming exactly as many sources as the budget allows
        /// resolves. The bound is a limit, not a margin.
        /// </summary>
        [Test]
        public async Task ExactlyTheDocumentBudgetResolvesAsync()
        {
            WotConversionResult<WotDocument> result =
                await ResolveChainAsync(sources: 3, maxDocuments: 3).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.ResolverLimitExceeded),
                    Is.False);
            });
            result.Value?.Dispose();
        }

        /// <summary>
        /// One source past the budget is refused, and named as the limit it
        /// broke rather than as a missing document.
        /// </summary>
        [Test]
        public async Task OneSourcePastTheDocumentBudgetIsRefusedAsync()
        {
            WotConversionResult<WotDocument> result =
                await ResolveChainAsync(sources: 4, maxDocuments: 3).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.ResolverLimitExceeded &&
                            d.Message.Contains("maximum document count", StringComparison.Ordinal)),
                    Is.True);
            });
            result.Value?.Dispose();
        }

        /// <summary>
        /// A source larger than the per-document byte limit is refused before
        /// it is parsed.
        /// </summary>
        [Test]
        public async Task ASourcePastThePerDocumentByteLimitIsRefusedAsync()
        {
            WotConversionResult<WotDocument> result = await ResolveChainAsync(
                sources: 1, maxDocuments: 8, maxDocumentBytes: 16).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.ResolverLimitExceeded &&
                            d.Message.Contains("per-document limit", StringComparison.Ordinal)),
                    Is.True);
            });
            result.Value?.Dispose();
        }

        /// <summary>
        /// A source that names itself is a cycle. The shared context sees it,
        /// and it is reported as a projection cycle rather than as a resolver
        /// one, because that is the graph the author drew.
        /// </summary>
        [Test]
        public async Task ASourceNamingItselfIsACycleAsync()
        {
            var documents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["./self.jsonld"] = Projection("./self.jsonld")
            };
            var resolver = new WotProjectionResolver(new MapResolver(documents));
            using WotDocument manifest = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection("./self.jsonld")));

            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(manifest).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionCycle),
                Is.True);
            result.Value?.Dispose();
        }

        /// <summary>
        /// An <c>ua:Organizes</c> traversal that runs out of budget stops on a
        /// partial graph and says so. Returning silently would leave the
        /// acyclicity check answering "no cycle found" for a graph it never
        /// finished reading.
        /// </summary>
        [Test]
        public async Task AnExhaustedOrganizesBudgetIsReportedAsync()
        {
            WotConversionResult<WotDocument> result =
                await ResolveOrganizesChainAsync(depth: 6, maxDocuments: 3)
                    .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(
                    d => d.Code == WotDiagnosticCode.TraversalBudgetExhausted),
                Is.True);
            result.Value?.Dispose();
        }

        /// <summary>
        /// The budget is reported once however many branches run out, so a wide
        /// graph does not bury the conversion in one diagnostic per branch.
        /// </summary>
        [Test]
        public async Task AnExhaustedOrganizesBudgetIsReportedOnceAsync()
        {
            WotConversionResult<WotDocument> result =
                await ResolveOrganizesChainAsync(depth: 12, maxDocuments: 2)
                    .ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Count(
                    d => d.Code == WotDiagnosticCode.TraversalBudgetExhausted),
                Is.EqualTo(1));
            result.Value?.Dispose();
        }

        /// <summary>
        /// An <c>ua:Organizes</c> graph inside the budget is walked to the end
        /// and reports nothing, so the diagnostic above marks exhaustion rather
        /// than traversal.
        /// </summary>
        [Test]
        public async Task AnOrganizesGraphInsideTheBudgetReportsNothingAsync()
        {
            WotConversionResult<WotDocument> result =
                await ResolveOrganizesChainAsync(depth: 3, maxDocuments: 16)
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Diagnostics.Any(
                        d => d.Code == WotDiagnosticCode.TraversalBudgetExhausted),
                    Is.False);
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionCycle),
                    Is.False);
            });
            result.Value?.Dispose();
        }

        /// <summary>
        /// A resolved view is a function of what the sources say, not of the
        /// order they happened to write it in. Permuting members that carry no
        /// order must produce byte-identical canonical output.
        /// </summary>
        [Test]
        public async Task PermutingIrrelevantMembersLeavesTheViewIdenticalAsync()
        {
            string first = await CanonicalViewAsync(
                "\"title\":\"Source\",\"@type\":\"Thing\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"a\":{\"type\":\"number\"},\"b\":{\"type\":\"string\"}}")
                .ConfigureAwait(false);
            string second = await CanonicalViewAsync(
                "\"properties\":{\"b\":{\"type\":\"string\"},\"a\":{\"type\":\"number\"}}," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"security\":\"nosec_sc\"," +
                "\"@type\":\"Thing\",\"title\":\"Source\"")
                .ConfigureAwait(false);

            Assert.That(second, Is.EqualTo(first));
        }

        private static async Task<string> CanonicalViewAsync(string sourceBody)
        {
            var documents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["./source.jsonld"] = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    sourceBody + "}"
            };
            var resolver = new WotProjectionResolver(new MapResolver(documents));
            using WotDocument manifest = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection("./source.jsonld")));

            WotConversionResult<WotDocument> result = await resolver
                .ResolveAsync(manifest).ConfigureAwait(false);
            Assert.That(result.Value, Is.Not.Null, Describe(result));
            using WotDocument view = result.Value!;
            return Encoding.UTF8.GetString(view.ToCanonicalUtf8());
        }

        private static string Describe(WotConversionResult<WotDocument> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.ToString()));
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveChainAsync(
            int sources,
            int maxDocuments,
            int maxDocumentBytes = 16 * 1024 * 1024)
        {
            var documents = new Dictionary<string, string>(StringComparer.Ordinal);
            var hrefs = new List<string>();
            for (int ii = 0; ii < sources; ii++)
            {
                string href = "./source-" +
                    ii.ToString(CultureInfo.InvariantCulture) + ".jsonld";
                hrefs.Add(href);
                documents[href] = Source("Source" + ii.ToString(CultureInfo.InvariantCulture));
            }
            var resolver = new WotProjectionResolver(
                new MapResolver(documents),
                new WotNodeSetConverterOptions
                {
                    MaxResolverDocuments = maxDocuments,
                    MaxResolverDocumentBytes = maxDocumentBytes
                });
            using WotDocument manifest = WotDocument.Parse(
                Encoding.UTF8.GetBytes(Projection([.. hrefs])));
            return await resolver.ResolveAsync(manifest).ConfigureAwait(false);
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveOrganizesChainAsync(
            int depth,
            int maxDocuments)
        {
            var documents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["./source.jsonld"] = Source("Source")
            };
            for (int ii = 0; ii < depth; ii++)
            {
                string href = "./organized-" +
                    ii.ToString(CultureInfo.InvariantCulture) + ".jsonld";
                string? next = ii + 1 < depth
                    ? "./organized-" +
                        (ii + 1).ToString(CultureInfo.InvariantCulture) + ".jsonld"
                    : null;
                documents[href] = Organized(next);
            }
            var resolver = new WotProjectionResolver(
                new MapResolver(documents),
                new WotNodeSetConverterOptions { MaxResolverDocuments = maxDocuments });
            using WotDocument manifest = WotDocument.Parse(
                Encoding.UTF8.GetBytes(
                    Projection(["./source.jsonld"], "./organized-0.jsonld")));
            return await resolver.ResolveAsync(manifest).ConfigureAwait(false);
        }

        private static string Source(string title)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"Thing\",\"title\":\"" + title + "\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"value\":{\"type\":\"number\"}}}";
        }

        private static string Organized(string? next)
        {
            string links = next is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"ua:Organizes\",\"href\":\"" + next + "\"}]";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":\"Thing\",\"title\":\"Organized\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                links + "}";
        }

        private static string Projection(string href)
        {
            return Projection([href]);
        }

        private static string Projection(string[] hrefs, string? organizes = null)
        {
            var sources = new StringBuilder();
            for (int ii = 0; ii < hrefs.Length; ii++)
            {
                if (ii > 0)
                {
                    sources.Append(',');
                }
                sources
                    .Append("{\"uav:sourceName\":\"s")
                    .Append(ii.ToString(CultureInfo.InvariantCulture))
                    .Append("\",\"href\":\"")
                    .Append(hrefs[ii])
                    .Append("\",\"type\":\"application/td+json\",\"uav:selectAll\":true}");
            }
            string links = organizes is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"ua:Organizes\",\"href\":\"" + organizes + "\"}]";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"Thing\",\"uav:projection\"]," +
                "\"title\":\"View\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"uav:scenario\":\"http://example.com/scenario/Limits\"," +
                "\"uav:projects\":[" + sources + "]" + links + "}";
        }

        private sealed class MapResolver(Dictionary<string, string> map) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                return new ValueTask<WotResolverResult>(
                    map.TryGetValue(reference, out string? json)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                        : WotResolverResult.NotFound);
            }
        }
    }
}
