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
    /// The publishing state for a writer group.
    /// </summary>
    public class WriterGroupPublishState
    {
        /// <summary>
        /// Hold the DataSet State
        /// </summary>
        private class DataSetState
        {
            public uint MessageCount;
            public DataSet LastDataSet;

            public ConfigurationVersionDataType ConfigurationVersion;
            public DateTime LastMetaDataUpdate;
        }

        /// <summary>
        /// The DataSetStates indexed by dataset writer group id.
        /// </summary>
        private Dictionary<ushort, DataSetState> m_dataSetStates;

        #region Constructor
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public WriterGroupPublishState()
        {
            m_dataSetStates = new Dictionary<ushort, DataSetState>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns TRUE if the next DataSetMessage is a delta frame.
        /// </summary>
        public bool IsDeltaFrame(DataSetWriterDataType writer, out uint sequenceNumber)
        {
            lock (m_dataSetStates)
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
        public bool HasMetaDataChanged(DataSetWriterDataType writer, DataSetMetaDataType metadata)
        {
            if (metadata == null)
            {
                return false;
            }

            lock (m_dataSetStates)
            {
                DataSetState state = GetState(writer);

                ConfigurationVersionDataType version = state.ConfigurationVersion;
                // no matter what the TransportSettings.MetaDataUpdateTime is the ConfigurationVersion is checked
                if (version == null)
                {
                    // keep a copy of ConfigurationVersion
                    state.ConfigurationVersion = metadata.ConfigurationVersion.Clone() as ConfigurationVersionDataType;
                    state.LastMetaDataUpdate = DateTime.UtcNow;
                    return true;
                }

                if (version.MajorVersion != metadata.ConfigurationVersion.MajorVersion ||
                    version.MinorVersion != metadata.ConfigurationVersion.MinorVersion)
                {
                    // keep a copy of ConfigurationVersion
                    state.ConfigurationVersion = metadata.ConfigurationVersion.Clone() as ConfigurationVersionDataType;
                    state.LastMetaDataUpdate = DateTime.UtcNow;
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
            lock (m_dataSetStates)
            {
                DataSetState state = GetState(writer);

                DataSet lastDataSet = state.LastDataSet;

                if (lastDataSet == null)
                {
                    state.LastDataSet = Utils.Clone(dataset) as DataSet;
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
        public void OnMessagePublished(DataSetWriterDataType writer, DataSet dataset)
        {
            lock (m_dataSetStates)
            {
                DataSetState state = GetState(writer);
                state.MessageCount++;

                if (writer.KeyFrameCount > 1)
                {

                    state.ConfigurationVersion =
                        dataset.DataSetMetaData.ConfigurationVersion.Clone() as ConfigurationVersionDataType;

                    if (state.LastDataSet == null)
                    {
                        state.LastDataSet = Utils.Clone(dataset) as DataSet;
                        return;
                    }

                    for (int ii = 0; ii < dataset.Fields.Length && ii < state.LastDataSet.Fields.Length; ii++)
                    {
                        var field = dataset.Fields[ii];

                        if (field != null)
                        {
                            state.LastDataSet.Fields[ii] = Utils.Clone(field) as Field;
                        }
                    }

                }
            }
        }
        #endregion

        #region Private Methods


        private DataSetState GetState(DataSetWriterDataType writer)
        {
            DataSetState state;

            if (!m_dataSetStates.TryGetValue(writer.DataSetWriterId, out state))
            {
                m_dataSetStates[writer.DataSetWriterId] = state = new DataSetState();
            }

            return state;
        }
        #endregion
    }
}
