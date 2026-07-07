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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Tests for <see cref="SharedStorePubSubRuntimeStateStore"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class SharedStorePubSubRuntimeStateStoreTests
    {
        [Test]
        public async Task GetStateReturnsNullWhenAbsentAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            PubSubState? state = await store.GetStateAsync("pubsub:connection:Connection1");

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task SetThenGetRoundTripsStateAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            await store.SetStateAsync("pubsub:connection:Connection1", PubSubState.Operational);
            PubSubState? state = await store.GetStateAsync("pubsub:connection:Connection1");

            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task SetOverwritesPreviousStateAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            await store.SetStateAsync("pubsub:writergroup:WriterGroup1", PubSubState.Paused);
            await store.SetStateAsync("pubsub:writergroup:WriterGroup1", PubSubState.Error);
            PubSubState? state = await store.GetStateAsync("pubsub:writergroup:WriterGroup1");

            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public async Task SharedBackendIsVisibleToAnotherStoreInstanceAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var writer = new SharedStorePubSubRuntimeStateStore(backend);
            var reader = new SharedStorePubSubRuntimeStateStore(backend);

            await writer.SetStateAsync("pubsub:datasetwriter:Writer1", PubSubState.Operational);
            PubSubState? state = await reader.GetStateAsync("pubsub:datasetwriter:Writer1");

            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void SetStateRejectsEmptyComponentId()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            Assert.That(
                async () => await store.SetStateAsync(string.Empty, PubSubState.Operational),
                Throws.ArgumentException);
        }
    }
}
