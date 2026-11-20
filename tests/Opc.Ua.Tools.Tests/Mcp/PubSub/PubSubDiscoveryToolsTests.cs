/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class PubSubDiscoveryToolsTests
    {
        [Test]
        public async Task DiscoveryToolsRequireActiveRuntimeAsync()
        {
            await using var scope = new PubSubMcpTestScope();
            using IDisposable caller = scope.Enter();
            await using PubSubRuntimeManager manager =
                scope.NewManager();
            ushort[] writerIds = [1, 2];

            Assert.That(
                () => PubSubDiscoveryTools.DiscoverMetaDataAsync(
                    manager,
                    writerIds,
                    1),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => PubSubDiscoveryTools.DiscoverWriterConfigurationAsync(
                    manager,
                    writerIds,
                    1),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => PubSubDiscoveryTools.DiscoverPublisherEndpointsAsync(
                    manager,
                    1),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void DiscoveryToolsRejectNullManager()
        {
            Assert.That(
                () => PubSubDiscoveryTools.DiscoverMetaDataAsync(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SummarizeMapsAllDiscoveryResultTypes()
        {
            var result = new PubSubDiscoveryResult
            {
                DataSetMetaDataEntries =
                [
                    new PubSubDataSetMetaDataDiscoveryResult
                    {
                        PublisherId = PublisherId.FromUInt16(10),
                        WriterGroupId = 20,
                        DataSetWriterId = 30,
                        StatusCode = StatusCodes.Good,
                        DataSetMetaData = new DataSetMetaDataType
                        {
                            Name = "Data",
                            Fields = [new FieldMetaData()]
                        }
                    }
                ],
                WriterConfigurations =
                [
                    new PubSubDataSetWriterConfigurationDiscoveryResult
                    {
                        PublisherId = PublisherId.FromUInt16(11),
                        WriterGroupId = 21,
                        DataSetWriterIds = [31, 32],
                        StatusCode = StatusCodes.BadNotFound
                    }
                ],
                PublisherEndpoints =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.udp://127.0.0.1:4840"
                    },
                    new EndpointDescription()
                ]
            };

            PubSubDiscoverySummary summary =
                PubSubDiscoveryTools.Summarize(result);

            Assert.That(summary.MetaData, Has.Count.EqualTo(1));
            Assert.That(summary.MetaData[0].Name, Is.EqualTo("Data"));
            Assert.That(summary.MetaData[0].FieldCount, Is.EqualTo(1));
            Assert.That(summary.WriterConfigurations, Has.Count.EqualTo(1));
            Assert.That(
                summary.WriterConfigurations[0].StatusCode,
                Is.EqualTo("BadNotFound"));
            Assert.That(summary.PublisherEndpointUrls, Has.Count.EqualTo(2));
            Assert.That(summary.PublisherEndpointUrls[1], Is.Empty);
        }
    }
}
#endif
