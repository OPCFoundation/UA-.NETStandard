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

#if NET10_0
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
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();
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
