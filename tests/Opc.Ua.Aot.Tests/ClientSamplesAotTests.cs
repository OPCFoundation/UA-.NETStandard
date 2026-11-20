// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that exercise <see cref="AotClientSamples"/>
    /// against a ReferenceServer running in-process.
    /// These tests validate that all client-server functionality works
    /// when the test project is published as NativeAOT.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class ClientSamplesAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ReadNodesAsync()
        {
            await AotClientSamples
                .ReadNodesAsync(fixture.Session)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task WriteNodesAsync()
        {
            await AotClientSamples
                .WriteNodesAsync(fixture.Session)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BrowseAsync()
        {
            await AotClientSamples
                .BrowseAsync(fixture.Session)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CallMethodAsync()
        {
            await AotClientSamples
                .CallMethodAsync(fixture.Session)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SubscribeToDataChangesAsync()
        {
            await AotClientSamples
                .SubscribeToDataChangesAsync(fixture.Session)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BrowseFullAddressSpaceAsync()
        {
            ArrayOf<ReferenceDescription> refs = await AotClientSamples
                .BrowseFullAddressSpaceAsync(
                    fixture.Session, ObjectIds.RootFolder)
                .ConfigureAwait(false);
            await Assert.That(refs.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FetchAllNodesNodeCacheAsync()
        {
            IList<INode> nodes = await AotClientSamples
                .FetchAllNodesNodeCacheAsync(
                    fixture.Session, ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);
            await Assert.That(nodes.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FormatValueAsJsonAsync()
        {
            var dataValue = new DataValue(new Variant(42));
            string json = AotClientSamples.FormatValueAsJson(
                fixture.Session.MessageContext,
                "TestValue",
                dataValue);
            await Assert.That(json).IsNotNull();
            await Assert.That(json).Contains("42");
        }
    }
}
