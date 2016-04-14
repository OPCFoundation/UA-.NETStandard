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

namespace MemoryBuffer
{
    /// <summary>
    /// Stores the configuration the test node manager
    /// </summary>
    [DataContract(Namespace = Namespaces.MemoryBuffer)]
    public class MemoryBufferConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public MemoryBufferConfiguration()
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
            m_buffers = null;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The buffers exposed by the memory 
        /// </summary>
        [DataMember(Order = 1)]
        public MemoryBufferInstanceCollection Buffers
        {
            get { return m_buffers; }
            set { m_buffers = value; }
        }
        #endregion

        #region Private Members
        private MemoryBufferInstanceCollection m_buffers;
        #endregion
    }

    /// <summary>
    /// Stores the configuration for a memory buffer instance.
    /// </summary>
    [DataContract(Namespace = Namespaces.MemoryBuffer)]
    public class MemoryBufferInstance
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public MemoryBufferInstance()
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
            m_name = null;
            m_tagCount = 0;
            m_dataType = null;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The browse name for the instance.
        /// </summary>
        [DataMember(Order = 1)]
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// The number of tags in the buffer.
        /// </summary>
        [DataMember(Order = 2)]
        public int TagCount
        {
            get { return m_tagCount; }
            set { m_tagCount = value; }
        }

        /// <summary>
        /// The data type of the tags in the buffer.
        /// </summary>
        [DataMember(Order = 3)]
        public string DataType
        {
            get { return m_dataType; }
            set { m_dataType = value; }
        }
        #endregion

        #region Private Members
        private string m_name;
        private int m_tagCount;
        private string m_dataType;
        #endregion
    }
    
    #region MemoryBufferInstanceCollection Class
    /// <summary>
    /// A collection of MemoryBufferInstances.
    /// </summary>
    [CollectionDataContract(Name = "ListOfMemoryBufferInstance", Namespace = Namespaces.MemoryBuffer, ItemName = "MemoryBufferInstance")]
    public partial class MemoryBufferInstanceCollection : List<MemoryBufferInstance>
    {
    }
    #endregion
}
