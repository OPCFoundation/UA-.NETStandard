/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
