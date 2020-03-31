/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

namespace Quickstarts.DataAccessServer
{
    /// <summary>
    /// This class simulates a block in the system.
    /// </summary>
    public class UnderlyingSystemBlock
    {
        #region Public Members
        /// <summary>
        /// Initializes a new instance of the <see cref="UnderlyingSystemBlock"/> class.
        /// </summary>
        public UnderlyingSystemBlock()
        {
            m_tags = new List<UnderlyingSystemTag>();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the unique identifier for the block.
        /// </summary>
        /// <value>The unique identifier for the block.</value>
        public string Id
        {
            get { return m_id; }
            set { m_id = value; }
        }

        /// <summary>
        /// Gets or sets the name of the block.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or sets the type of the block.
        /// </summary>
        /// <value>The type of the block.</value>
        public string BlockType
        {
            get { return m_blockType; }
            set { m_blockType = value; }
        }

        /// <summary>
        /// Gets or sets the time when the block structure last changed.
        /// </summary>
        /// <value>When the block structure last changed.</value>
        public DateTime Timestamp
        {
            get { return m_timestamp; }
            set { m_timestamp = value; }
        }

        /// <summary>
        /// Creates the tag.
        /// </summary>
        /// <param name="tagName">Name of the tag.</param>
        /// <param name="dataType">Type of the data.</param>
        /// <param name="tagType">Type of the tag.</param>
        /// <param name="engineeringUnits">The engineering units.</param>
        /// <param name="writeable">if set to <c>true</c> the tag is writeable.</param>
        public void CreateTag(
            string tagName, 
            UnderlyingSystemDataType dataType, 
            UnderlyingSystemTagType tagType, 
            string engineeringUnits,
            bool writeable)
        {
            // create tag.
            UnderlyingSystemTag tag = new UnderlyingSystemTag();

            tag.Block = this;
            tag.Name = tagName;
            tag.Description = null;
            tag.EngineeringUnits = engineeringUnits;
            tag.DataType = dataType;
            tag.TagType = tagType;
            tag.IsWriteable = writeable;
            tag.Labels = null;
            tag.EuRange = null;

            switch (tagType)
            {
                case UnderlyingSystemTagType.Analog:
                {
                    tag.Description = "An analog value.";
                    tag.TagType = UnderlyingSystemTagType.Analog;
                    tag.EuRange = new double[] { 100, 0 };
                    break;
                }

                case UnderlyingSystemTagType.Digital:
                {
                    tag.Description = "A digital value.";
                    tag.TagType = UnderlyingSystemTagType.Digital;
                    tag.Labels = new string[] { "Online", "Offline" };
                    break;
                }

                case UnderlyingSystemTagType.Enumerated:
                {
                    tag.Description = "An enumerated value.";
                    tag.TagType = UnderlyingSystemTagType.Enumerated;
                    tag.Labels = new string[] { "Red", "Yellow", "Green" };
                    break;
                }

                default:
                {
                    tag.Description = "A generic value.";
                    break;
                }
            }

            // set an initial value.
            switch (tag.DataType)
            {
                case UnderlyingSystemDataType.Integer1: { tag.Value = (sbyte)0; break; }
                case UnderlyingSystemDataType.Integer2: { tag.Value = (short)0; break; }
                case UnderlyingSystemDataType.Integer4: { tag.Value = (int)0; break; }
                case UnderlyingSystemDataType.Real4: { tag.Value = (float)0; break; }
                case UnderlyingSystemDataType.String: { tag.Value = String.Empty; break; }
            }

            lock (m_tags)
            {
                m_tags.Add(tag);
                m_timestamp = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Returns a snapshot of the tags belonging to the block.
        /// </summary>
        /// <returns>The list of tags. Null if the block does not exist.</returns>
        public IList<UnderlyingSystemTag> GetTags()
        {
            lock (m_tags)
            {
                // create snapshots of the tags.
                UnderlyingSystemTag[] tags = new UnderlyingSystemTag[m_tags.Count];

                for (int ii = 0; ii < m_tags.Count; ii++)
                {
                    tags[ii] = m_tags[ii].CreateSnapshot();
                }

                return tags;
            }
        }
                    
        /// <summary>
        /// Starts the monitoring.
        /// </summary>
        /// <param name="callback">The callback to use when reporting changes.</param>
        public void StartMonitoring(TagsChangedEventHandler callback)
        {
            lock (m_tags)
            {
                OnTagsChanged = callback;
            }
        }

        /// <summary>
        /// Writes the tag value.
        /// </summary>
        /// <param name="tagName">Name of the tag.</param>
        /// <param name="value">The value.</param>
        /// <returns>The status code for the operation.</returns>
        public uint WriteTagValue(string tagName, object value)
        {
            UnderlyingSystemTag tag = null;
            TagsChangedEventHandler onTagsChanged = null;

            lock (m_tags)
            {
                onTagsChanged = OnTagsChanged;

                // find the tag.
                tag = FindTag(tagName);
                
                if (tag == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                // cast value to correct type.
                try
                {
                    switch (tag.DataType)
                    {
                        case UnderlyingSystemDataType.Integer1:
                        {
                            tag.Value = (sbyte)value;
                            break;
                        }

                        case UnderlyingSystemDataType.Integer2:
                        {
                            tag.Value = (short)value;
                            break;
                        }

                        case UnderlyingSystemDataType.Integer4:
                        {
                            tag.Value = (int)value;
                            break;
                        }

                        case UnderlyingSystemDataType.Real4:
                        {
                            tag.Value = (float)value;
                            break;
                        }

                        case UnderlyingSystemDataType.String:
                        {
                            tag.Value = (string)value;
                            break;
                        }
                    }
                }
                catch
                {
                    return StatusCodes.BadTypeMismatch;
                }

                // updated the timestamp.
                tag.Timestamp = DateTime.UtcNow;
            }

            // raise notification.
            if (tag != null && onTagsChanged != null)
            {
                onTagsChanged(new UnderlyingSystemTag[] { tag });
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Stops monitoring.
        /// </summary>
        public void StopMonitoring()
        {
            lock (m_tags)
            {
                OnTagsChanged = null;
            }
        }

        /// <summary>
        /// Simulates a block by updating the state of the tags belonging to the condition.
        /// </summary>
        /// <param name="counter">The number of simulation cycles that have elapsed.</param>
        /// <param name="index">The index of the block within the system.</param>
        /// <param name="generator">An object which generates random data.</param>
        public void DoSimulation(long counter, int index, Opc.Ua.Test.DataGenerator generator)
        {
            try
            {
                TagsChangedEventHandler onTagsChanged = null;
                List<UnderlyingSystemTag> snapshots = new List<UnderlyingSystemTag>();

                // update the tags.
                lock (m_tags)
                {
                    onTagsChanged = OnTagsChanged;

                    // do nothing if not monitored.
                    if (onTagsChanged == null)
                    {
                        return;
                    }

                    for (int ii = 0; ii < m_tags.Count; ii++)
                    {
                        UnderlyingSystemTag tag = m_tags[ii];
                        UpdateTagValue(tag, generator);

                        DataValue value = new DataValue();

                        value.Value = tag.Value;
                        value.StatusCode = StatusCodes.Good;
                        value.SourceTimestamp = tag.Timestamp;
                 
                        if (counter % (8 + (index%4)) == 0)
                        {
                            UpdateTagMetadata(tag, generator);
                        }

                        snapshots.Add(tag.CreateSnapshot());
                    }
                }

                // report any tag changes after releasing the lock.
                if (onTagsChanged != null)
                {
                    onTagsChanged(snapshots);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error running simulation for block {0}", m_name);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Finds the tag identified by the name.
        /// </summary>
        /// <param name="tagName">Name of the tag.</param>
        /// <returns>The tag if null; otherwise null.</returns>
        private UnderlyingSystemTag FindTag(string tagName)
        {
            lock (m_tags)
            {
                // look up tag.
                for (int ii = 0; ii < m_tags.Count; ii++)
                {
                    UnderlyingSystemTag tag = m_tags[ii];

                    if (tag.Name == tagName)
                    {
                        return tag;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Updates the value of an tag.
        /// </summary>
        private bool UpdateTagValue(
            UnderlyingSystemTag tag,
            Opc.Ua.Test.DataGenerator generator)
        {
            // don't update writeable tags.
            if (tag.IsWriteable)
            {
                return false;
            }

            // check if a range applies to the value.
            int high = 0;
            int low = 0;

            switch (tag.TagType)
            {
                case UnderlyingSystemTagType.Analog:
                {
                    if (tag.EuRange != null && tag.EuRange.Length >= 2)
                    {
                        high = (int)tag.EuRange[0];
                        low = (int)tag.EuRange[1];
                    }

                    break;
                }

                case UnderlyingSystemTagType.Digital:
                {
                    high = 1;
                    low = 0;
                    break;
                }

                case UnderlyingSystemTagType.Enumerated:
                {
                    if (tag.Labels != null && tag.Labels.Length > 0)
                    {
                        high = tag.Labels.Length-1;
                        low = 0;
                    }

                    break;
                }
            }

            // select a value in the range.
            int value = -1;

            if (high > low)
            {
                value = (generator.GetRandomUInt16()%(high - low + 1)) + low;
            }

            // cast value to correct type or generate a random value.
            switch (tag.DataType)
            {
                case UnderlyingSystemDataType.Integer1:
                {
                    if (value == -1)
                    {
                        tag.Value = generator.GetRandomSByte();
                    }
                    else
                    {
                        tag.Value = (sbyte)value;
                    }

                    break;
                }

                case UnderlyingSystemDataType.Integer2:
                {
                    if (value == -1)
                    {
                        tag.Value = generator.GetRandomInt16();
                    }
                    else
                    {
                        tag.Value = (short)value;
                    }

                    break;
                }

                case UnderlyingSystemDataType.Integer4:
                {
                    if (value == -1)
                    {
                        tag.Value = generator.GetRandomInt32();
                    }
                    else
                    {
                        tag.Value = (int)value;
                    }

                    break;
                }

                case UnderlyingSystemDataType.Real4:
                {
                    if (value == -1)
                    {
                        tag.Value = generator.GetRandomFloat();
                    }
                    else
                    {
                        tag.Value = (float)value;
                    }

                    break;
                }

                case UnderlyingSystemDataType.String:
                {
                    tag.Value = generator.GetRandomString();
                    break;
                }
            }

            tag.Timestamp = DateTime.UtcNow;
            return true;
        }
        
        /// <summary>
        /// Updates the metadata for a tag.
        /// </summary>
        private bool UpdateTagMetadata(
            UnderlyingSystemTag tag,
            Opc.Ua.Test.DataGenerator generator)
        {
            switch (tag.TagType)
            {
                case UnderlyingSystemTagType.Analog:
                {
                    if (tag.EuRange != null)
                    {
                        double[] range = new double[tag.EuRange.Length];

                        for (int ii = 0; ii < tag.EuRange.Length; ii++)
                        {
                            range[ii] = tag.EuRange[ii]+1;
                        }

                        tag.EuRange = range;
                    }

                    break;
                }

                case UnderlyingSystemTagType.Digital:
                case UnderlyingSystemTagType.Enumerated:
                {
                    if (tag.Labels != null)
                    {
                        string[] labels = new string[tag.Labels.Length];

                        for (int ii = 0; ii < tag.Labels.Length; ii++)
                        {
                            labels[ii] = generator.GetRandomString();
                        }

                        tag.Labels = labels;
                    }

                    break;
                }

                default:
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private string m_id;
        private string m_name;
        private string m_blockType;
        private DateTime m_timestamp;
        private List<UnderlyingSystemTag> m_tags;
        private TagsChangedEventHandler OnTagsChanged;
        #endregion
    }

    /// <summary>
    /// Used to receive events when the state of an tag changes.
    /// </summary>
    public delegate void TagsChangedEventHandler(IList<UnderlyingSystemTag> tags);
}
