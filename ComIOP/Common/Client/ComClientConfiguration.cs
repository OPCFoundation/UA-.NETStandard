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

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [KnownType(typeof(ComDaClientConfiguration))]
    [KnownType(typeof(ComAeClientConfiguration))]
    [KnownType(typeof(ComHdaClientConfiguration))]
    [DataContract(Namespace = Namespaces.ComInterop)]
    public class ComClientConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ComClientConfiguration()
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
        /// The URL of the COM server which has the form: opc.com://hostname/progid/clsid
        /// </summary>
        [DataMember(Order = 1)]
        public string ServerUrl { get; set; }

        /// <summary>
        /// The name for the server that will used for the root in the UA server address space.
        /// </summary>
        [DataMember(Order = 2)]
        public string ServerName { get; set; }

        /// <summary>
        /// The number of milliseconds to wait between reconnect attempts.
        /// </summary>
        [DataMember(Order = 3)]
        public int MaxReconnectWait { get; set; }

        /// <summary>
        /// A list of characters used in item ids to seperate elements.
        /// </summary>
        /// <remarks>
        /// If specified the wrapper attempts to parse the item ids by looking for the one of these
        /// characters starting from the end of the item id. All text after this character is assumed
        /// to be the name of the item or branch.
        /// </remarks>
        [DataMember(Order = 4)]
        public string SeperatorChars 
        {
            get
            {
                return m_seperatorChars;            
            }

            set
            {
                if (String.IsNullOrEmpty(value))
                {
                    m_seperatorChars = String.Empty;
                    return;
                }

                m_seperatorChars = value.Trim();
            }
        }

        /// <summary>
        /// A list of locales supported by the server that should made visible as locales for the UA server.
        /// </summary>
        [DataMember(Order = 5)]
        public StringCollection AvailableLocales { get; set; }

        /// <summary>
        /// Gets or sets the item id parser.
        /// </summary>
        /// <value>The item id parser.</value>
        public IItemIdParser ItemIdParser { get; set; }
        #endregion

        #region Private Members
        private string m_seperatorChars;
        #endregion
    }
    
    /// <summary>
    /// A collection of COM WrapperConfiguration objects.
    /// </summary>
    [CollectionDataContract(Name="ListOfComClientConfiguration", Namespace=Namespaces.ComInterop, ItemName="ComClientConfiguration")]
    public partial class ComClientConfigurationCollection : List<ComClientConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ComClientConfigurationCollection() { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        public ComClientConfigurationCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of <see cref="ComClientConfiguration"/> used to pre-populate the collection.</param>
        public ComClientConfigurationCollection(IEnumerable<ComClientConfiguration> collection) : base(collection) { }
    }
}
