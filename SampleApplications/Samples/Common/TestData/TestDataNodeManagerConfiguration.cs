/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Collections.Generic;

using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Stores the configuration the test node manager
    /// </summary>
    [DataContract(Namespace = Namespaces.TestData)]
    public class TestDataNodeManagerConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public TestDataNodeManagerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_saveFilePath = null;
            m_maxQueueSize = 100;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The path to the file that stores state of the node manager.
        /// </summary>
        [DataMember(Order = 1)]
        public string SaveFilePath
        {
            get { return m_saveFilePath; }
            set { m_saveFilePath = value; }
        }

        /// <summary>
        /// The maximum length for a monitored item sampling queue.
        /// </summary>
        [DataMember(Order = 2)]
        public uint MaxQueueSize
        {
            get { return m_maxQueueSize; }
            set { m_maxQueueSize = value; }
        }

        /// <summary>
        /// The next unused value that can be assigned to new nodes.
        /// </summary>
        [DataMember(Order = 3)]
        public uint NextUnusedId
        {
            get { return m_nextUnusedId; }
            set { m_nextUnusedId = value; }
        }
        #endregion

        #region Private Members
        private string m_saveFilePath;
        private uint m_maxQueueSize;
        private uint m_nextUnusedId;
        #endregion
    }
}
