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
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Collections.Generic;
using Opc.Ua.Server;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Stores the configuration for UA that wraps a COM server. 
    /// </summary>
    [DataContract(Namespace = Namespaces.ComInterop)]
    public class ComProxyConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ComProxyConfiguration()
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
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the namespace uris known to the server.
        /// </summary>
        /// <value>The namespace uris.</value>
        /// <remarks>
        /// This list starts with index 1.
        /// This list ensures the indexes used to create browse names/node ids do not
        /// change even if the server tables change.
        /// </remarks>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        public StringCollection NamespaceUris { get; set; }

        /// <summary>
        /// Gets or sets the server uris known to the server.
        /// </summary>
        /// <value>The server uris.</value>
        /// <remarks>
        /// This list starts with index 2.
        /// This list ensures the indexes used to create browse names/node ids do not
        /// change even if the server tables change.
        /// </remarks>
        [DataMember(Order = 2, EmitDefaultValue = false)]
        public StringCollection ServerUris { get; set; }

        /// <summary>
        /// Gets or sets the size of the blocks to use when browsing.
        /// </summary>
        /// <value>The size of the block.</value>
        [DataMember(Order=3)]
        public int BrowseBlockSize { get; set; }

        /// <summary>
        /// Gets or sets the sets of mappings for node ids.
        /// </summary>
        /// <value>The sets of mappings for node ids.</value>
        [DataMember(Order = 4, EmitDefaultValue = false)]
        public NodeIdMappingSetCollection MappingSets { get; set; }
        #endregion

        /// <summary>
        /// Returns the mapping set. Creates an empty one if it does not exist.
        /// </summary>
        public NodeIdMappingSet GetMappingSet(string mappingType)
        {
            NodeIdMappingSet mappingSet = new NodeIdMappingSet();
            mappingSet.MappingType = mappingType;

            if (this.MappingSets != null)
            {
                for (int ii = 0; ii < this.MappingSets.Count; ii++)
                {
                    if (this.MappingSets[ii].MappingType == mappingType)
                    {
                        mappingSet.Mappings = this.MappingSets[ii].Mappings;
                        return mappingSet;
                    }
                }
            }

            mappingSet.Mappings = new NodeIdMappingCollection();
            return mappingSet;
        }

        /// <summary>
        /// Replaces the mapping set. Adds it if it does not exist.
        /// </summary>
        public void ReplaceMappingSet(NodeIdMappingSet mappingSet)
        {
            if (this.MappingSets != null)
            {
                for (int ii = 0; ii < this.MappingSets.Count; ii++)
                {
                    if (this.MappingSets[ii].MappingType == mappingSet.MappingType)
                    {
                        this.MappingSets[ii] = mappingSet;
                        return;
                    }
                }
            }

            if (this.MappingSets == null)
            {
                this.MappingSets = new NodeIdMappingSetCollection();
            }

            this.MappingSets.Add(mappingSet);
        }
    }

    #region NodeIdMappingSet Class
    /// <summary>
    /// Stores an integer mapping assigned to a NodeId.
    /// </summary>
    [DataContract(Namespace = Namespaces.ComInterop)]
    public class NodeIdMappingSet
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public NodeIdMappingSet()
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
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the type of the mapping.
        /// </summary>
        /// <value>The type of the mapping.</value>
        [DataMember(Order=1)]
        public string MappingType
        {
            get { return m_mappingType; }
            set { m_mappingType = value; }
        }

        /// <summary>
        /// Gets or sets the mappings.
        /// </summary>
        /// <value>The mappings.</value>
        [DataMember(Order=2)]
        public NodeIdMappingCollection Mappings
        {
            get { return m_mappings; }
            set { m_mappings = value; }
        }
        #endregion

        #region Private Members
        private string m_mappingType;
        private NodeIdMappingCollection m_mappings;
        #endregion
    }
    #endregion

    #region NodeIdMappingSetCollection Class
    /// <summary>
    /// A collection of NodeIdMapping values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of NodeIdMapping values.
    /// </remarks>
    [CollectionDataContract(Name="ListOfNodeIdMappingSet", Namespace=Namespaces.ComInterop, ItemName="NodeIdMappingSet")]
    public class NodeIdMappingSetCollection : List<NodeIdMappingSet>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public NodeIdMappingSetCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of collection</param>
        public NodeIdMappingSetCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of NodeIdMappingSets to add to this collection</param>
        public NodeIdMappingSetCollection(IEnumerable<NodeIdMappingSet> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of NodeIdMappingSets to add to this collection</param>
        public static NodeIdMappingSetCollection ToNodeIdMappingSetCollection(NodeIdMappingSet[] values)
        {
            if (values != null)
            {
                return new NodeIdMappingSetCollection(values);
            }

            return new NodeIdMappingSetCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of NodeIdMappingSets to add to this collection</param>
        public static implicit operator NodeIdMappingSetCollection(NodeIdMappingSet[] values)
        {
            return ToNodeIdMappingSetCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public object Clone()
        {
            return new NodeIdMappingSetCollection(this);
        }
    }
    #endregion

    #region NodeIdMapping Class
    /// <summary>
    /// Stores an integer mapping assigned to a NodeId.
    /// </summary>
    [DataContract(Namespace = Namespaces.ComInterop)]
    public class NodeIdMapping
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public NodeIdMapping()
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
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the integer id.
        /// </summary>
        /// <value>The integer id.</value>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        public uint IntegerId { get; set; }

        /// <summary>
        /// Gets or sets the node id.
        /// </summary>
        /// <value>The node id.</value>
        [DataMember(Order = 2, EmitDefaultValue = false)]
        public string NodeId { get; set; }

        /// <summary>
        /// Gets or sets the browse patj.
        /// </summary>
        /// <value>The node id.</value>
        [DataMember(Order = 3, EmitDefaultValue = false)]
        public string BrowePath { get; set; }
        #endregion
    }
    #endregion

    #region NodeIdMappingCollection Class
    /// <summary>
    /// A collection of NodeIdMapping values.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of NodeIdMapping values.
    /// </remarks>
    [CollectionDataContract(Name="ListOfNodeIdMapping", Namespace=Namespaces.ComInterop, ItemName="NodeIdMapping")]
    public class NodeIdMappingCollection : List<NodeIdMapping>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public NodeIdMappingCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">Max capacity of collection</param>
        public NodeIdMappingCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">A collection of NodeIdMappings to add to this collection</param>
        public NodeIdMappingCollection(IEnumerable<NodeIdMapping> collection) : base(collection) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of NodeIdMappings to add to this collection</param>
        public static NodeIdMappingCollection ToNodeIdMappingCollection(NodeIdMapping[] values)
        {
            if (values != null)
            {
                return new NodeIdMappingCollection(values);
            }

            return new NodeIdMappingCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>        
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">A collection of NodeIdMappings to add to this collection</param>
        public static implicit operator NodeIdMappingCollection(NodeIdMapping[] values)
        {
            return ToNodeIdMappingCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public object Clone()
        {
            return new NodeIdMappingCollection(this);
        }
    }
    #endregion
}
