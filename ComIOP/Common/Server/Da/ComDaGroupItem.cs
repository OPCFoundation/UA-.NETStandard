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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A class that stores the state of an item in a COM DA group.
    /// </summary>
    public class ComDaGroupItem
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaGroupItem"/> class.
        /// </summary>
        public ComDaGroupItem(ComDaGroup group, string itemId)
		{
            m_group = group;
            m_itemId = itemId;
            m_serverHandle = 0;
            m_active = true;
            m_euType = -1;
            m_samplingRate = -1;
            m_bufferEnabled = false;
            m_deadband = -1;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets the group that the item belongs to.
        /// </summary>
        /// <value>The group.</value>
        public ComDaGroup Group
        {
            get { return m_group; }
        }

        /// <summary>
        /// Gets the remote node id.
        /// </summary>
        /// <value>The remote node id.</value>
        public NodeId NodeId
        {
            get { return m_nodeId; }
            set { m_nodeId = value; }
        }

        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

        /// <summary>
        /// Gets or sets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
            set { m_serverHandle = value; }
        }

        /// <summary>
        /// Gets or sets the client handle.
        /// </summary>
        /// <value>The client handle.</value>
        public int ClientHandle
        {
            get { return m_clientHandle; }
            set { m_clientHandle = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="ComDaGroupItem"/> is active.
        /// </summary>
        /// <value><c>true</c> if active; otherwise, <c>false</c>.</value>
        public bool Active
        {
            get { return m_active; }
            set { m_active = value; }
        }

        /// <summary>
        /// Gets or sets the the requested data type.
        /// </summary>
        /// <value>The type of the requested data type.</value>
        public short RequestedDataType
        {
            get { return m_requestedDataType; }
            set { m_requestedDataType = value; }
        }

        /// <summary>
        /// Gets or sets the the canonical data type.
        /// </summary>
        /// <value>The type of the canonical data type.</value>
        public short CanonicalDataType
        {
            get { return m_canonicalDataType; }
            set { m_canonicalDataType = value; }
        }

        /// <summary>
        /// Gets or sets the remote data type for the item.
        /// </summary>
        /// <value>The remote data type.</value>
        public TypeInfo RemoteDataType
        {
            get { return m_remoteDataType; }
            set { m_remoteDataType = value; }
        }

        /// <summary>
        /// Gets or sets the access rights.
        /// </summary>
        /// <value>The access rights.</value>
        public int AccessRights
        {
            get { return m_accessRights; }
            set { m_accessRights = value; }
        }

        /// <summary>
        /// Gets or sets the EU type.
        /// </summary>
        /// <value>The EU type.</value>
        public int EuType
        {
            get { return m_euType; }
            set { m_euType = value; }
        }

        /// <summary>
        /// Gets or sets the EU info (HighEU/LowEU for Analog, EnumStrings for Enumerated).
        /// </summary>
        /// <value>The EU info.</value>
        public object EuInfo
        {
            get { return m_euInfo; }
            set { m_euInfo = value; }
        }

        /// <summary>
        /// Gets or sets the sampling interval.
        /// </summary>
        /// <value>The sampling interval.</value>
        public int SamplingRate
        {
            get { return m_samplingRate; }
            set { m_samplingRate = value; }
        }

        /// <summary>
        /// Gets or sets the actual sampling interval.
        /// </summary>
        /// <value>The actual sampling interval.</value>
        public int ActualSamplingRate
        {
            get { return m_actualSamplingRate; }
            set { m_actualSamplingRate = value; }
        }

        /// <summary>
        /// Gets or sets whether buffering is enabled.
        /// </summary>
        /// <value>Whether buffering is enabled.</value>
        public bool BufferEnabled
        {
            get { return m_bufferEnabled; }
            set { m_bufferEnabled = value; }
        }

        /// <summary>
        /// Gets or sets the deadband.
        /// </summary>
        /// <value>The deadband.</value>
        public float Deadband
        {
            get { return m_deadband; }
            set { m_deadband = value; }
        }

        /// <summary>
        /// Gets or sets the monitored item.
        /// </summary>
        /// <value>The monitored item.</value>
        public MonitoredItem MonitoredItem
        {
            get { return m_monitoredItem; }
            set { m_monitoredItem = value; }
        }

        /// <summary>
        /// Gets or sets the cache entry for the item.
        /// </summary>
        /// <value>The cache entry.</value>
        public DaCacheValue CacheEntry
        {
            get { return m_cacheEntry; }
            set { m_cacheEntry = value; }
        }

        /// <summary>
        /// Gets or sets the last sent value.
        /// </summary>
        /// <value>The last sent value.</value>
        public DaValue LastSentValue
        {
            get { return m_lastSentValue; }
            set { m_lastSentValue = value; }
        }

        /// <summary>
        /// Gets or sets the next update time.
        /// </summary>
        /// <value>The next update time.</value>
        public long NextUpdateTime
        {
            get { return m_nextUpdateTime; }
            set { m_nextUpdateTime = value; }
        }

        /// <summary>
        /// Clones the item.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <returns>The new item.</returns>
        public ComDaGroupItem CloneItem(ComDaGroup group)
        {
            ComDaGroupItem copy = new ComDaGroupItem(group, this.ItemId);

            copy.m_nodeId = this.m_nodeId;
            copy.m_serverHandle = this.m_serverHandle;
            copy.m_clientHandle = this.m_clientHandle;
            copy.m_active = this.m_active;
            copy.m_requestedDataType = this.m_requestedDataType;
            copy.m_canonicalDataType = this.m_canonicalDataType;
            copy.m_accessRights = this.m_accessRights;
            copy.m_euType = this.m_euType;
            copy.m_euInfo = this.m_euInfo;
            copy.m_samplingRate = this.m_samplingRate;
            copy.m_bufferEnabled = this.m_bufferEnabled;
            copy.m_actualSamplingRate = this.m_actualSamplingRate;
            copy.m_deadband = this.m_deadband;

            // create new monitored item and use its client handle as the server handle.
            copy.m_monitoredItem = new MonitoredItem(this.MonitoredItem);

            return copy;
        }
        #endregion

        #region Private Fields
        private ComDaGroup m_group;
        private string m_itemId;
        private NodeId m_nodeId;
        private int m_serverHandle;
        private int m_clientHandle;
        private bool m_active;
        private short m_requestedDataType;
        private short m_canonicalDataType;
        private int m_accessRights;
        private int m_euType;
        private object m_euInfo;
        private int m_samplingRate;
        private bool m_bufferEnabled;
        private int m_actualSamplingRate;
        private float m_deadband;
        private MonitoredItem m_monitoredItem;
        private DaCacheValue m_cacheEntry;
        private DaValue m_lastSentValue;
        private TypeInfo m_remoteDataType;
        private long m_nextUpdateTime;
        #endregion
	}
}
