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

using System.Collections.Generic;
using System.Threading;
using Moq;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Shared builders for the external-server PubSub adapter unit tests: a
    /// telemetry context, mocked sessions, and small PubSub configuration
    /// fragments (PublishedDataSets, WriterGroups) used to drive the adapter
    /// components without a real OPC UA server.
    /// </summary>
    internal static class AdapterTestHelpers
    {
        /// <summary>
        /// Creates a telemetry context suitable for unit tests.
        /// </summary>
        public static ITelemetryContext Telemetry()
        {
            return NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// A single published variable description used to build PublishedDataSets.
        /// </summary>
        public readonly record struct Variable(NodeId Node, uint Attribute, double SamplingHint)
        {
            public static Variable Value(NodeId node, double samplingHint = 0)
            {
                return new Variable(node, Attributes.Value, samplingHint);
            }
        }

        /// <summary>
        /// Builds a PublishedDataSet whose DataSetSource carries the supplied
        /// published variables.
        /// </summary>
        public static PublishedDataSetDataType PublishedDataSet(
            string name,
            params Variable[] variables)
        {
            var published = new PublishedVariableDataType[variables.Length];
            for (int i = 0; i < variables.Length; i++)
            {
                published[i] = new PublishedVariableDataType
                {
                    PublishedVariable = variables[i].Node,
                    AttributeId = variables[i].Attribute,
                    SamplingIntervalHint = variables[i].SamplingHint
                };
            }

            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = published.ToArrayOf()
                })
            };
        }

        /// <summary>
        /// Builds a PubSub configuration with one connection holding a single
        /// WriterGroup that references the supplied datasets through DataSetWriters.
        /// </summary>
        public static PubSubConfigurationDataType Configuration(
            double publishingIntervalMs,
            IList<PublishedDataSetDataType> publishedDataSets)
        {
            var writers = new DataSetWriterDataType[publishedDataSets.Count];
            for (int i = 0; i < publishedDataSets.Count; i++)
            {
                writers[i] = new DataSetWriterDataType
                {
                    Name = "Writer" + publishedDataSets[i].Name,
                    DataSetWriterId = (ushort)(i + 1),
                    DataSetName = publishedDataSets[i].Name
                };
            }

            var writerGroup = new WriterGroupDataType
            {
                Name = "Group1",
                WriterGroupId = 1,
                PublishingInterval = publishingIntervalMs,
                DataSetWriters = writers.ToArrayOf()
            };

            var connection = new PubSubConnectionDataType
            {
                Name = "Connection1",
                WriterGroups = new[] { writerGroup }.ToArrayOf()
            };

            return new PubSubConfigurationDataType
            {
                PublishedDataSets = publishedDataSets.ToArrayOf(),
                Connections = new[] { connection }.ToArrayOf()
            };
        }

        /// <summary>
        /// Creates a connected mocked external-server session that ignores
        /// connect calls and reports itself connected.
        /// </summary>
        public static Mock<IExternalServerSession> ConnectedSession()
        {
            var mock = new Mock<IExternalServerSession>();
            mock.SetupGet(s => s.IsConnected).Returns(true);
            mock.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
                .Returns(default(System.Threading.Tasks.ValueTask));
            return mock;
        }
    }
}
