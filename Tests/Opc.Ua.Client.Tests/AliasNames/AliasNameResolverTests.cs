/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2007: NUnit tests do not run in a synchronization context, so the
// ConfigureAwait recommendations on `await using` do not change behaviour
// — and AliasNameResolver.DisposeAsync returns synchronously anyway.
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;

namespace Opc.Ua.Client.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameResolver"/>: ensures the
    /// cache is populated lazily, that
    /// <see cref="AliasNameResolver.Invalidate"/> forces a refresh on
    /// the next call, and that reverse lookups round-trip.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameResolverTests
    {
        private static CallMethodResult AliasesResult(
            params (string name, ExpandedNodeId target)[] aliases)
        {
            var entries = new AliasNameDataType[aliases.Length];
            for (int i = 0; i < aliases.Length; i++)
            {
                entries[i] = new AliasNameDataType
                {
                    AliasName = new QualifiedName(aliases[i].name),
                    ReferencedNodes = new[] { aliases[i].target }.ToArrayOf()
                };
            }
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new[]
                {
                    Variant.FromStructure(entries.ToArrayOf())
                }.ToArrayOf()
            };
        }

        [Test]
        public async Task ResolveLoadsCacheOnDemandAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            int callCount = 0;
            harness.CallHandler = _ =>
            {
                callCount++;
                return AliasesResult(
                    ("Alpha", new ExpandedNodeId("T1", 2)),
                    ("Beta", new ExpandedNodeId("T2", 2)));
            };

            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(client);

            IReadOnlyList<ExpandedNodeId> alpha =
                await resolver.ResolveAsync("Alpha").ConfigureAwait(false);
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(alpha, Has.Count.EqualTo(1));
            Assert.That(alpha[0], Is.EqualTo(new ExpandedNodeId("T1", 2)));

            // Second resolve hits the cache; the underlying server is
            // NOT contacted again.
            IReadOnlyList<ExpandedNodeId> beta =
                await resolver.ResolveAsync("Beta").ConfigureAwait(false);
            Assert.That(callCount, Is.EqualTo(1),
                "Second resolve must reuse the cache.");
            Assert.That(beta[0], Is.EqualTo(new ExpandedNodeId("T2", 2)));
        }

        [Test]
        public async Task ResolveAliasNameAsyncReverseLookupAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ => AliasesResult(
                ("Speed", new ExpandedNodeId("V1", 2)));
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(client);

            string name = await resolver
                .ResolveAliasNameAsync(new ExpandedNodeId("V1", 2))
                .ConfigureAwait(false);
            Assert.That(name, Is.EqualTo("Speed"));
        }

        [Test]
        public async Task InvalidateForcesRefreshOnNextResolveAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            int callCount = 0;
            harness.CallHandler = _ =>
            {
                callCount++;
                return AliasesResult(
                    ("Sigma", new ExpandedNodeId($"T-{callCount}", 2)));
            };
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(client);

            await resolver.ResolveAsync("Sigma").ConfigureAwait(false);
            resolver.Invalidate();
            IReadOnlyList<ExpandedNodeId> refreshed =
                await resolver.ResolveAsync("Sigma").ConfigureAwait(false);

            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(refreshed[0],
                Is.EqualTo(new ExpandedNodeId("T-2", 2)));
        }

        [Test]
        public async Task UnknownNameReturnsEmptyListAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ => AliasesResult(
                ("A", new ExpandedNodeId("T1", 2)));
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(client);
            IReadOnlyList<ExpandedNodeId> result = await resolver
                .ResolveAsync("Unknown").ConfigureAwait(false);
            Assert.That(result, Is.Empty);
        }

        // ----------------------------------------------------------------
        // Verbose mode coverage
        // ----------------------------------------------------------------

        private static CallMethodResult VerboseAliasesResult(
            params (string name, ExpandedNodeId target, string? serverUri)[] aliases)
        {
            var entries = new AliasNameVerboseDataType[aliases.Length];
            for (int i = 0; i < aliases.Length; i++)
            {
                entries[i] = new AliasNameVerboseDataType
                {
                    AliasName = new QualifiedName(aliases[i].name),
                    ReferencedNodes = new[] { aliases[i].target }.ToArrayOf(),
                    ServerUris = new[] { aliases[i].serverUri ?? string.Empty }.ToArrayOf(),
                    AliasNameCategoryId = ObjectIds.Aliases,
                };
            }
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new[]
                {
                    Variant.FromStructure(entries.ToArrayOf())
                }.ToArrayOf()
            };
        }

        [Test]
        public async Task VerboseResolverPopulatesServerUrisAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = req =>
            {
                if (req.MethodId == MethodIds.AliasNameCategoryType_FindAliasVerbose)
                {
                    return VerboseAliasesResult(
                        ("Tag1", new ExpandedNodeId("T1", 2), "urn:remote-server"));
                }
                // Non-verbose path should not be hit when UseVerbose succeeds.
                return AliasesResult(("Tag1", new ExpandedNodeId("T1", 2)));
            };
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(
                client,
                new AliasNameResolverOptions { UseVerbose = true });

            IReadOnlyList<ExpandedNodeId> targets =
                await resolver.ResolveAsync("Tag1").ConfigureAwait(false);
            IReadOnlyList<string?> uris =
                await resolver.ResolveServerUrisAsync("Tag1").ConfigureAwait(false);

            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.EqualTo(new ExpandedNodeId("T1", 2)));
            Assert.That(uris, Has.Count.EqualTo(1));
            Assert.That(uris[0], Is.EqualTo("urn:remote-server"));
        }

        [Test]
        public async Task NonVerboseResolverReturnsEmptyServerUrisAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            harness.CallHandler = _ => AliasesResult(
                ("Tag1", new ExpandedNodeId("T1", 2)));
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(client);

            await resolver.ResolveAsync("Tag1").ConfigureAwait(false);
            IReadOnlyList<string?> uris =
                await resolver.ResolveServerUrisAsync("Tag1").ConfigureAwait(false);

            Assert.That(uris, Is.Empty,
                "Non-verbose mode must surface an empty list for ServerUris (no decoration).");
        }

        [Test]
        public async Task VerboseResolverFallsBackToNonVerboseOnNotSupportedAsync()
        {
            // Server returns BadNotImplemented for the verbose method —
            // the resolver must transparently fall back to FindAlias and
            // populate the cache from the non-verbose response.
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            int verboseCalls = 0;
            int nonVerboseCalls = 0;
            harness.CallHandler = req =>
            {
                if (req.MethodId == MethodIds.AliasNameCategoryType_FindAliasVerbose)
                {
                    verboseCalls++;
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.BadNotImplemented
                    };
                }
                nonVerboseCalls++;
                return AliasesResult(("Tag1", new ExpandedNodeId("T1", 2)));
            };
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            await using var resolver = new AliasNameResolver(
                client,
                new AliasNameResolverOptions { UseVerbose = true });

            IReadOnlyList<ExpandedNodeId> targets =
                await resolver.ResolveAsync("Tag1").ConfigureAwait(false);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0], Is.EqualTo(new ExpandedNodeId("T1", 2)));
            Assert.That(verboseCalls, Is.EqualTo(1),
                "Verbose call must have been attempted once.");
            Assert.That(nonVerboseCalls, Is.EqualTo(1),
                "Non-verbose fallback must have been issued after the verbose failure.");

            // A second resolve should NOT re-attempt the verbose call —
            // the resolver flips UseVerbose=false once it falls back.
            resolver.Invalidate();
            await resolver.ResolveAsync("Tag1").ConfigureAwait(false);
            Assert.That(verboseCalls, Is.EqualTo(1),
                "Once fallen back, the resolver must not re-attempt the verbose method.");
            Assert.That(nonVerboseCalls, Is.EqualTo(2));
        }
    }
}
