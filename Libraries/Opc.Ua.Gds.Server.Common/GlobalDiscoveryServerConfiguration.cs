/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Runtime.Serialization;
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace=Opc.Ua.Gds.Namespaces.OpcUaGds + "Configuration.xsd")]
    public class GlobalDiscoveryServerConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryServerConfiguration()
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
        public string AuthoritiesStorePath { get; set; }

        [DataMember(Order = 2)]
        public string ApplicationCertificatesStorePath { get; set; }

        [DataMember(Order = 3)]
        public string BaseCertificateGroupStorePath { get; set; }
        
        [DataMember(Order = 4)]
        public string DefaultSubjectNameContext { get; set; }

        [DataMember(Order = 5)]
        public CertificateGroupConfigurationCollection CertificateGroups { get; set; }

        [DataMember(Order = 6)]
        public StringCollection KnownHostNames { get; set; }

        [DataMember(Order = 7)]
        public string DatabaseStorePath { get; set; }
        #endregion

        #region Private Members
        #endregion
    }

    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Opc.Ua.Gds.Namespaces.OpcUaGds + "Configuration.xsd")]
    public class CertificateGroupConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateGroupConfiguration()
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
            DefaultCertificateLifetime = CertificateFactory.DefaultLifeTime;
            DefaultCertificateKeySize = CertificateFactory.DefaultKeySize;
            DefaultCertificateHashSize = CertificateFactory.DefaultHashSize;
            CACertificateLifetime = CertificateFactory.DefaultLifeTime;
            CACertificateKeySize = CertificateFactory.DefaultKeySize;
            CACertificateHashSize = CertificateFactory.DefaultHashSize;
        }
        #endregion

        #region Public Properties
        [DataMember(IsRequired = true, Order = 10)]
        public string Id { get; set; }

        [DataMember(IsRequired = true, Order = 20)]
        public string CertificateType { get; set; }

        [DataMember(IsRequired = true, Order = 25)]
        public string SubjectName { get; set; }

        [DataMember(IsRequired = true, Order = 30)]
        public string BaseStorePath { get; set; }

        [DataMember(Order = 40)]
        public ushort DefaultCertificateLifetime { get; set; }

        [DataMember(Order = 50)]
        public ushort DefaultCertificateKeySize { get; set; }

        [DataMember(Order = 60)]
        public ushort DefaultCertificateHashSize { get; set; }

        [DataMember(Order = 70)]
        public ushort CACertificateLifetime { get; set; }

        [DataMember(Order = 80)]
        public ushort CACertificateKeySize { get; set; }

        [DataMember(Order = 90)]
        public ushort CACertificateHashSize { get; set; }

        public string TrustedListPath { get { return BaseStorePath + Path.DirectorySeparatorChar + "trusted"; }}
        public string IssuerListPath { get { return BaseStorePath + Path.DirectorySeparatorChar + "issuer"; } }
        #endregion

        #region Private Members
        #endregion
    }

    [CollectionDataContract(Name = "ListOfCertificateGroupConfiguration", Namespace = Opc.Ua.Gds.Namespaces.OpcUaGds + "Configuration.xsd", ItemName = "CertificateGroupConfiguration")]
    public class CertificateGroupConfigurationCollection : List<CertificateGroupConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public CertificateGroupConfigurationCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public CertificateGroupConfigurationCollection(IEnumerable<CertificateGroupConfiguration> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public CertificateGroupConfigurationCollection(int capacity) : base(capacity) { }
    }
}
