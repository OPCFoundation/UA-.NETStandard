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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates that <see cref="HistorianConformanceContributor"/> claims
    /// exactly the Server profiles <see cref="HistorianProfileCatalog"/>
    /// confirms are supported by the providers registered in an
    /// <see cref="IHistorianProviderRegistry"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianConformanceContributorTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public void ConstructorRejectsNullRegistry()
        {
            Assert.That(
                () => new HistorianConformanceContributor(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void PropertiesAreEmptyBeforeTheFirstRefresh()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var contributor = new HistorianConformanceContributor(registry);

            Assert.That(contributor.ConformanceUnits.Count, Is.Zero);
            Assert.That(contributor.ServerProfiles.Count, Is.Zero);
        }

        [Test]
        public async Task RefreshWithNoProvidersLeavesBothCollectionsEmptyAsync()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            var contributor = new HistorianConformanceContributor(registry);

            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(contributor.ConformanceUnits.Count, Is.Zero);
            Assert.That(contributor.ServerProfiles.Count, Is.Zero);
        }

        [Test]
        public async Task RefreshClaimsOnlyTheTenNonEventProfilesForAGenericReadWriteProviderAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            provider.Register(new NodeId("fully-capable", NamespaceIndex), HistorianNodeCapabilities.ReadWrite);

            var registry = new HistorianProviderRegistry(new NamespaceTable());
            registry.RegisterDefault(provider);
            var contributor = new HistorianConformanceContributor(registry);

            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(contributor.ServerProfiles.Count, Is.EqualTo(10));
            Assert.That(contributor.ConformanceUnits.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task RefreshNeverClaimsAnyEventsFamilyProfileAsync()
        {
            // Even though EventReadWrite sets every Event read/update
            // flag, the provider-wide NodeId.Null rollup never carries
            // notifier-specific EventTypes/MandatoryEventFields, so no
            // Events-family Server profile URI must ever be claimed
            // through this contributor.
            using var provider = new InMemoryHistorianProvider();
            provider.Register(new NodeId("event-notifier", NamespaceIndex), HistorianNodeCapabilities.EventReadWrite);

            var registry = new HistorianProviderRegistry(new NamespaceTable());
            registry.RegisterDefault(provider);
            var contributor = new HistorianConformanceContributor(registry);

            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

            string[] eventProfileUris = [.. HistoricalAccessProfileCatalog
                .GetProfiles(HistoricalAccessProfileFamily.Events)
                .ToArray()!
                .Where(profile => profile.Side == HistoricalAccessProfileSide.Server)
                .Select(profile => profile.ProfileUri)];

            foreach (string profileUri in eventProfileUris)
            {
                Assert.That(
                    contributor.ServerProfiles.ToArray()!,
                    Does.Not.Contain(profileUri),
                    $"Events-family profile must not be claimed: {profileUri}");
            }
        }

        [Test]
        public async Task RefreshUnionsClaimsAcrossMultipleProvidersAsync()
        {
            using var dataProvider = new InMemoryHistorianProvider();
            dataProvider.Register(new NodeId("data", NamespaceIndex), HistorianNodeCapabilities.DataReadWrite);

            using var structuredProvider = new InMemoryHistorianProvider();
            structuredProvider.RegisterStructured(
                new NodeId("structured", NamespaceIndex),
                TimestampStructuredDataKeySelector.Instance,
                HistorianNodeCapabilities.StructuredReadWrite);

            var registry = new HistorianProviderRegistry(new NamespaceTable());
            registry.RegisterForNamespace("urn:data", dataProvider);
            registry.RegisterForNamespace("urn:structured", structuredProvider);
            var contributor = new HistorianConformanceContributor(registry);

            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

            bool ClaimsUri(string uri) => contributor.ServerProfiles.ToArray()!.Contains(uri);

            Assert.That(
                ClaimsUri("http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022"),
                Is.True,
                "Data-capable provider should claim the Data Insert profile.");
            Assert.That(
                ClaimsUri("http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022"),
                Is.True,
                "Structured-capable provider should claim the Structured Data profile.");
        }

        [Test]
        public Task RefreshSkipsProvidersThatThrowNotSupportedOnNullNodeAsync()
        {
            var registry = new HistorianProviderRegistry(new NamespaceTable());
            registry.RegisterDefault(new ThrowingProvider());
            var contributor = new HistorianConformanceContributor(registry);

            Assert.That(
                async () => await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false),
                Throws.Nothing);
            Assert.That(contributor.ServerProfiles.Count, Is.Zero);
            return Task.CompletedTask;
        }

        [Test]
        public async Task RefreshReflectsTheMostRecentProviderStateAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("changing", NamespaceIndex);
            provider.Register(nodeId, HistorianNodeCapabilities.ReadOnly);

            var registry = new HistorianProviderRegistry(new NamespaceTable());
            registry.RegisterDefault(provider);
            var contributor = new HistorianConformanceContributor(registry);

            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                contributor.ServerProfiles.ToArray()!,
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022"),
                "Read-only provider must not claim the Insert profile before capabilities change.");

            provider.SetCapabilities(nodeId, HistorianNodeCapabilities.DataReadWrite);
            await contributor.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                contributor.ServerProfiles.ToArray()!,
                Does.Contain(
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022"),
                "Refreshing after the node becomes writable should claim the Insert profile.");
        }

        /// <summary>
        /// Provider whose <see cref="GetCapabilitiesAsync"/> always throws
        /// <see cref="NotSupportedException"/> for <see cref="NodeId.Null"/>,
        /// mirroring a provider that cannot answer a provider-wide rollup.
        /// </summary>
        private sealed class ThrowingProvider : IHistorianProvider
        {
            public ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
            {
                return new(false);
            }

            public ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
