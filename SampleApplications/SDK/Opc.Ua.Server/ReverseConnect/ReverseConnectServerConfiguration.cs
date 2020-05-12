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

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectServerConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
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
        [DataMember(Order = 10)]
        public ReverseConnectClientCollection ReverseConnectClients { get; set; }

        [DataMember(Order = 20)]
        public int ConnectInterval { get; set; }
        #endregion
    }

    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClient
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectClient()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
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
        [DataMember(Order = 10)]
        public string EndpointUrl { get; set; }

        [DataMember(Order = 20)]
        public int MaxConnections { get; set; }

        [DataMember(Order = 30)]
        public int Timeout { get; set; }

        [DataMember(Order = 40)]
        public int RejectedTimeout { get; set; }
        #endregion
    }

    [CollectionDataContract(Name = "ReverseConnectClientCollection", Namespace = Namespaces.OpcUaConfig, ItemName = "ReverseConnectClient")]
    public class ReverseConnectClientCollection : List<ReverseConnectClient>
    {
        #region Constructors
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ReverseConnectClientCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ReverseConnectClientCollection(IEnumerable<ReverseConnectClient> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ReverseConnectClientCollection(int capacity) : base(capacity) { }
        #endregion
    }

}
