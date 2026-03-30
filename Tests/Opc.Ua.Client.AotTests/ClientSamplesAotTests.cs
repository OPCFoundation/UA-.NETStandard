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

namespace Opc.Ua.Client.AotTests
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
        public async Task ReadNodes()
        {
            await AotClientSamples
                .ReadNodesAsync(fixture.Session!)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task WriteNodes()
        {
            await AotClientSamples
                .WriteNodesAsync(fixture.Session!)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task Browse()
        {
            await AotClientSamples
                .BrowseAsync(fixture.Session!)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CallMethod()
        {
            await AotClientSamples
                .CallMethodAsync(fixture.Session!)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SubscribeToDataChanges()
        {
            await AotClientSamples
                .SubscribeToDataChangesAsync(fixture.Session!)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task BrowseFullAddressSpace()
        {
            ArrayOf<ReferenceDescription> refs = await AotClientSamples
                .BrowseFullAddressSpaceAsync(
                    fixture.Session!, ObjectIds.RootFolder)
                .ConfigureAwait(false);
            await Assert.That(refs.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FetchAllNodesNodeCache()
        {
            IList<INode> nodes = await AotClientSamples
                .FetchAllNodesNodeCacheAsync(
                    fixture.Session!, ObjectIds.ObjectsFolder)
                .ConfigureAwait(false);
            await Assert.That(nodes.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FormatValueAsJson()
        {
            var dataValue = new DataValue(new Variant(42));
            string json = AotClientSamples.FormatValueAsJson(
                fixture.Session!.MessageContext,
                "TestValue",
                dataValue);
            await Assert.That(json).IsNotNull();
            await Assert.That(json).Contains("42");
        }
    }
}
