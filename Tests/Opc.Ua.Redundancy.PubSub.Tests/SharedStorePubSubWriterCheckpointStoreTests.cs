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
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Tests for <see cref="SharedStorePubSubWriterCheckpointStore"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class SharedStorePubSubWriterCheckpointStoreTests
    {
        [Test]
        public async Task GetReturnsNullWhenAbsentAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1);

            Assert.That(sequence, Is.Null);
        }

        [Test]
        public async Task SetThenGetRoundTripsSequenceNumberAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 7, 4200u);
            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 7);

            Assert.That(sequence, Is.EqualTo(4200u));
        }

        [Test]
        public async Task WritersAreCheckpointedIndependentlyAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1, 100u);
            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 2, 200u);

            Assert.That(
                await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1),
                Is.EqualTo(100u));
            Assert.That(
                await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 2),
                Is.EqualTo(200u));
        }

        [Test]
        public async Task GetReturnsNullWhenStoredValueIsTruncatedAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);
            string key = PubSubRedundancyStoreKeys.CheckpointPrefix + "pubsub:writergroup:WriterGroup1/1";
            await backend.SetAsync(key, new ByteString(new byte[] { 0x01 }));

            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1);

            Assert.That(sequence, Is.Null);
        }

        [Test]
        public void SetSequenceNumberThrowsWhenComponentIdIsEmpty()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            Assert.That(
                async () => await store.SetSequenceNumberAsync(string.Empty, 1, 5u),
                Throws.ArgumentException);
        }

        [Test]
        public async Task PromotedStandbyReadsTheActivesCheckpointAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var active = new SharedStorePubSubWriterCheckpointStore(backend);
            var standby = new SharedStorePubSubWriterCheckpointStore(backend);

            await active.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1, 999u);
            uint? seen = await standby.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1);

            Assert.That(seen, Is.EqualTo(999u));
        }
    }
}
