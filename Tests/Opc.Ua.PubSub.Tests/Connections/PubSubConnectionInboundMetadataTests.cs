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

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.Connections
{
    /// <summary>
    /// Regression coverage for inbound DataSetMetaData routing through
    /// <see cref="PubSubConnection"/>'s receive loop: confirms JSON
    /// <c>ua-metadata</c> envelopes and UADP DataSetMetaData discovery
    /// responses are forwarded to the connection-scoped
    /// <see cref="IDataSetMetaDataRegistry"/>, that
    /// <see cref="IDataSetMetaDataRegistry.MetaDataChanged"/> fires, and
    /// that strictly older MajorVersions are rejected per
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.2.9.4">
    /// Part 14 §6.2.9.4</see> and
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.3.4.8">
    /// §7.3.4.8</see>.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PubSubConnectionInboundMetadataTests
    {
        private static DataSetMetaDataType NewMeta(uint major = 1, uint minor = 0, string name = "DS1")
        {
            return new DataSetMetaDataType
            {
                Name = name,
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = major,
                    MinorVersion = minor
                }
            };
        }

        [Test]
        [TestSpec("7.3.4.8",
            Summary = "JSON ua-metadata updates registry on inbound receive")]
        public void OnInbound_JsonMetaData_UpdatesRegistry()
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(major: 3, minor: 7, name: "JsonRouted");
            var message = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromUInt16(42),
                DataSetWriterId = 17,
                DataSetClassId = Uuid.Empty,
                MetaDataPayload = meta
            };

            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry, message, NullLogger.Instance);

            Assert.That(routed, Is.True);
            var key = new DataSetMetaDataKey(
                PublisherId.FromUInt16(42), 0, 17, Uuid.Empty, 3);
            MetaDataMatchResult result = registry.TryGet(in key, out DataSetMetaDataType? stored);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(stored, Is.SameAs(meta));
        }

        [Test]
        [TestSpec("7.2.4.6.4",
            Summary = "UADP DataSetMetaData response updates registry")]
        public void OnInbound_UadpDataSetMetaData_UpdatesRegistry()
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(major: 2, minor: 9, name: "UadpRouted");
            var message = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt32(7),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 99,
                DataSetClassId = Uuid.Empty,
                DataSetMetaData = meta
            };

            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry, message, NullLogger.Instance);

            Assert.That(routed, Is.True);
            var key = new DataSetMetaDataKey(
                PublisherId.FromUInt32(7), 0, 99, Uuid.Empty, 2);
            MetaDataMatchResult result = registry.TryGet(in key, out DataSetMetaDataType? stored);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(stored, Is.SameAs(meta));
        }

        [Test]
        [TestSpec("6.2.9.4",
            Summary = "MetaDataChanged event fires after inbound routing")]
        public void OnInbound_MetaData_RaisesMetaDataChanged()
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType meta = NewMeta(major: 5);
            DataSetMetaDataChangedEventArgs? captured = null;
            registry.MetaDataChanged += (_, e) => captured = e;

            var message = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromString("Plant1"),
                DataSetWriterId = 4,
                MetaDataPayload = meta
            };
            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry, message, NullLogger.Instance);

            Assert.That(routed, Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Current, Is.SameAs(meta));
            Assert.That(captured.Key.DataSetWriterId, Is.EqualTo((ushort)4));
            Assert.That(captured.Key.MajorVersion, Is.EqualTo(5u));
        }

        [Test]
        [TestSpec("6.2.9.4",
            Summary = "Inbound metadata older than registered MajorVersion is dropped")]
        public void OnInbound_StaleMajorVersion_Rejects()
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType newer = NewMeta(major: 5, minor: 0, name: "Newer");
            DataSetMetaDataType older = NewMeta(major: 2, minor: 0, name: "Older");

            var existingKey = new DataSetMetaDataKey(
                PublisherId.FromUInt16(11), 0, 33, Uuid.Empty, 5);
            registry.Register(in existingKey, newer);

            int changeEvents = 0;
            registry.MetaDataChanged += (_, _) => changeEvents++;

            var staleMessage = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(11),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 33,
                DataSetClassId = Uuid.Empty,
                DataSetMetaData = older
            };

            bool routed = PubSubConnection.TryRouteInboundMetaData(
                registry, staleMessage, NullLogger.Instance);

            Assert.That(routed, Is.True, "Routing helper still claims ownership of the frame.");
            Assert.That(changeEvents, Is.Zero, "Stale metadata must not trigger MetaDataChanged.");
            MetaDataMatchResult check = registry.TryGet(in existingKey, out DataSetMetaDataType? stored);
            Assert.That(check, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(stored, Is.SameAs(newer), "Registry retains the newer description.");
        }
    }
}
