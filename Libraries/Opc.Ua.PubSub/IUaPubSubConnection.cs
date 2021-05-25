/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Interface for an UaPubsubConnection
    /// </summary>
    public interface IUaPubSubConnection : IDisposable
    {
        /// <summary>
        /// Get assigned transport protocol for this connection instance
        /// </summary>
        TransportProtocol TransportProtocol { get; }

        /// <summary>
        /// Get the configuration object for this PubSub connection
        /// </summary>
        PubSubConnectionDataType PubSubConnectionConfiguration { get; }

        /// <summary>
        /// Get reference to <see cref="UaPubSubApplication"/>
        /// </summary>
        UaPubSubApplication Application { get; }

        /// <summary>
        /// Get flag that indicates if the Connection is in running state
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        void Start();

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        void Stop();

        /// <summary>
        /// Determine if the connection has anything to publish -> at least one WriterDataSet is configured as enabled for current writer group
        /// </summary>
        bool CanPublish(WriterGroupDataType writerGroupConfiguration);

        /// <summary>
        /// Create the network messages built from the provided writerGroupConfiguration
        /// </summary>
        IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration, WriterGroupPublishState state);

        /// <summary>
        /// Publish the network message
        /// </summary>
        bool PublishNetworkMessage(UaNetworkMessage networkMessage);

        /// <summary>
        /// Get current list of dataset readers available in this UaSubscriber component
        /// </summary>
        List<DataSetReaderDataType> GetOperationalDataSetReaders();
    }

    /// <summary>
    /// The publishing state for a writer group.
    /// </summary>
    public class WriterGroupPublishState
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public WriterGroupPublishState()
        {
            MessageCounts = new Dictionary<ushort, uint>();
            DataSets = new Dictionary<ushort, DataSet>();
        }

        /// <summary>
        /// The message count indexed by dataset writer group id.
        /// </summary>
        public Dictionary<ushort, uint> MessageCounts { get; }

        /// <summary>
        /// The message count indexed by dataset writer group id.
        /// </summary>
        public Dictionary<ushort, DataSet> DataSets { get; }

        /// <summary>
        /// Returns TRUE if the next DataSetMessage is a delta frame.
        /// </summary>
        public bool IsDeltaFrame(DataSetWriterDataType writer)
        {
            if (writer.KeyFrameCount > 1)
            {
                uint count;

                lock (MessageCounts)
                {
                    if (!MessageCounts.TryGetValue(writer.DataSetWriterId, out count))
                    {
                        MessageCounts[writer.DataSetWriterId] = count = 0;
                    }
                }

                if (count % writer.KeyFrameCount != 0)
                {
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// Checks if the DataSet has changed and null
        /// </summary>
        public DataSet ExcludeUnchangedFields(DataSetWriterDataType writer, DataSet dataset)
        {
            lock (MessageCounts)
            {
                DataSet lastDataSet;

                if (!DataSets.TryGetValue(writer.DataSetWriterId, out lastDataSet) || lastDataSet == null)
                {
                    return dataset;
                }

                bool changed = false;

                for (int ii = 0; ii < dataset.Fields.Length && ii < lastDataSet.Fields.Length; ii++)
                {
                    var field1 = dataset.Fields[ii];
                    var field2 = lastDataSet.Fields[ii];

                    if (field1 == null || field2 == null)
                    {
                        changed = true;
                        continue;
                    }

                    if (field1.Value.StatusCode != field2.Value.StatusCode)
                    {
                        changed = true;
                        continue;
                    }

                    if (!Utils.IsEqual(field1.Value.WrappedValue, field2.Value.WrappedValue))
                    {
                        changed = true;
                        continue;
                    }

                    dataset.Fields[ii] = null;
                }

                if (!changed)
                {
                    return null;
                }
            }

            return dataset;
        }

        /// <summary>
        /// Increments the message counter.
        /// </summary>
        public void MessagePublished(DataSetWriterDataType writer, DataSet dataset)
        {
            if (writer.KeyFrameCount > 1)
            {
                uint count;

                lock (MessageCounts)
                {
                    if (MessageCounts.TryGetValue(writer.DataSetWriterId, out count))
                    {
                        MessageCounts[writer.DataSetWriterId] = count + 1;
                    }

                    DataSet lastDataSet = null;

                    if (!DataSets.TryGetValue(writer.DataSetWriterId, out lastDataSet))
                    {
                        DataSets[writer.DataSetWriterId] = lastDataSet = new DataSet()
                        {
                            DataSetWriterId = writer.DataSetWriterId,
                            Name = dataset.Name,
                            SequenceNumber = dataset.SequenceNumber,
                            Fields = new Field[dataset.Fields.Length]
                        };
                    }

                    for (int ii = 0; ii < dataset.Fields.Length && lastDataSet.Fields.Length < 0; ii++)
                    {
                        var field1 = dataset.Fields[ii];

                        if (field1 != null)
                        {
                            lastDataSet.Fields[ii] = field1;
                        }
                    }

                    DataSets[writer.DataSetWriterId] = dataset;
                }
            }
        }
    }
}
