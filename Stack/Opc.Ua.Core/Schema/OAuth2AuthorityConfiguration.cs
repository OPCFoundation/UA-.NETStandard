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

namespace Opc.Ua
{
    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaConfig)]
    public class OAuth2AuthorityConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public OAuth2AuthorityConfiguration()
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
        [DataMember(Order = 1)]
        public OAuth2AuthorityCollection KnownAuthorities { get; set; }
        #endregion
    }

    [CollectionDataContract(Name = "ListOfOAuth2Authority", Namespace = Namespaces.OpcUaConfig, ItemName = "OAuth2Authority")]
    public class OAuth2AuthorityCollection : List<OAuth2Authority>
    {
        public OAuth2AuthorityCollection()
        {
        }
    }

    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaConfig)]
    public enum OAuth2AuthorityType
    {
        [EnumMember()]
        ClientCredentials,

        [EnumMember()]
        AzureAD
    }

    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaConfig)]
    public class OAuth2Authority
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public OAuth2Authority()
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
        [DataMember(Order = 1)]
        public string AuthorityUrl { get; set; }

        [DataMember(Order = 2)]
        public OAuth2AuthorityType AuthorityType { get; set; }

        [DataMember(Order = 3)]
        public string ClientId { get; set; }

        [DataMember(Order = 4)]
        public string ClientSecret { get; set; }

        [DataMember(Order = 5)]
        public string RedirectUrl { get; set; }
        #endregion
    }
}
