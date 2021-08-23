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
        private class DataSetState
        {
            public uint MessageCount;
            public DataSet LastDataSet;
            public ConfigurationVersionDataType ConfigurationVersion;
            public DateTime LastMetaDataUpdate;
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public WriterGroupPublishState()
        {
            DataSets = new Dictionary<ushort, DataSetState>();
        }

        /// <summary>
        /// The last DataSet indexed by dataset writer group id.
        /// </summary>
        private Dictionary<ushort, DataSetState> DataSets { get; }

        private DataSetState GetState(DataSetWriterDataType writer)
        {
            DataSetState state;

            if (!DataSets.TryGetValue(writer.DataSetWriterId, out state))
            {
                DataSets[writer.DataSetWriterId] = state = new DataSetState();
            }

            return state;
        }

        /// <summary>
        /// Returns TRUE if the next DataSetMessage is a delta frame.
        /// </summary>
        public bool IsDeltaFrame(DataSetWriterDataType writer, out uint sequenceNumber)
        {
            lock (DataSets)
            {
                DataSetState state = GetState(writer);
                sequenceNumber = state.MessageCount + 1;

                if (state.MessageCount % writer.KeyFrameCount != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns TRUE if the next DataSetMessage is a delta frame.
        /// </summary>
        public bool HasMetaDataChanged(DataSetWriterDataType writer, DataSetMetaDataType metadata, double updateTime)
        {
            if (metadata == null)
            {
                return false;
            }

            lock (DataSets)
            {
                DataSetState state = GetState(writer);

                ConfigurationVersionDataType version = state.ConfigurationVersion;

                if (version == null)
                {
                    state.ConfigurationVersion = metadata.ConfigurationVersion;
                    state.LastMetaDataUpdate = DateTime.UtcNow;
                    return true;
                }

                if (version.MajorVersion != metadata.ConfigurationVersion.MajorVersion ||
                    version.MinorVersion != metadata.ConfigurationVersion.MinorVersion)
                {
                    state.ConfigurationVersion = metadata.ConfigurationVersion;
                    state.LastMetaDataUpdate = DateTime.UtcNow;
                    return true;
                }

                if (updateTime > 0)
                {
                    if (state.LastMetaDataUpdate.AddMilliseconds(updateTime) <= DateTime.UtcNow)
                    {
                        state.LastMetaDataUpdate = DateTime.UtcNow;
                        return true;
                    }
                }
            }

            return false;
        }

        private DataSet Copy(DataSet dataset)
        {
            DataSet lastDataSet = new DataSet() {
                DataSetWriterId = dataset.DataSetWriterId,
                Name = dataset.Name,
                SequenceNumber = dataset.SequenceNumber,
                Fields = new Field[dataset.Fields.Length]
            };

            for (int ii = 0; ii < dataset.Fields.Length && ii < lastDataSet.Fields.Length; ii++)
            {
                var field = dataset.Fields[ii];

                if (field != null)
                {
                    lastDataSet.Fields[ii] = field;
                }
            }

            return lastDataSet;
        }

        /// <summary>
        /// Checks if the DataSet has changed and null
        /// </summary>
        public DataSet ExcludeUnchangedFields(DataSetWriterDataType writer, DataSet dataset)
        {
            lock (DataSets)
            {
                DataSetState state = GetState(writer);

                DataSet lastDataSet = state.LastDataSet;

                if (lastDataSet == null)
                {
                    state.LastDataSet = Copy(dataset);
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
                lock (DataSets)
                {
                    DataSetState state = GetState(writer);
                    state.MessageCount++;
                    state.ConfigurationVersion = dataset.DataSetMetaData.ConfigurationVersion;

                    if (state.LastDataSet == null)
                    {
                        state.LastDataSet = Copy(dataset);
                        return;
                    }

                    for (int ii = 0; ii < dataset.Fields.Length && ii < state.LastDataSet.Fields.Length; ii++)
                    {
                        var field = dataset.Fields[ii];

                        if (field != null)
                        {
                            state.LastDataSet.Fields[ii] = field;
                        }
                    }
                }
            }
        }
    }
}
