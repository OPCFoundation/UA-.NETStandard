/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Runtime.Serialization;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public partial class GlobalDiscoveryServerConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryServerConfiguration()
        {
        }

        [DataMember(Order = 1)]
        [DataTypeField(Order = 0)]
        public string AuthoritiesStorePath { get; set; }

        [DataMember(Order = 2)]
        [DataTypeField(Order = 1)]
        public string ApplicationCertificatesStorePath { get; set; }

        [DataMember(Order = 3)]
        [DataTypeField(Order = 2)]
        public string BaseCertificateGroupStorePath { get; set; }

        [DataMember(Order = 4)]
        [DataTypeField(Order = 3)]
        public string DefaultSubjectNameContext { get; set; }

        [DataMember(Order = 5)]
        [DataTypeField(Order = 4)]
        public ArrayOf<CertificateGroupConfiguration> CertificateGroups { get; set; }

        [DataMember(Order = 6)]
        [DataTypeField(Order = 5)]
        public ArrayOf<string> KnownHostNames { get; set; }

        [DataMember(Order = 7)]
        [DataTypeField(Order = 6)]
        public string DatabaseStorePath { get; set; }

        [DataMember(Order = 8)]
        [DataTypeField(Order = 7)]
        public string UsersDatabaseStorePath { get; set; }
    }

    /// <summary>
    /// Stores the configuration the data access node manager.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public partial class CertificateGroupConfiguration
    {
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
            DefaultCertificateLifetime = CertificateFactory.DefaultLifeTime;
            DefaultCertificateKeySize = CertificateFactory.DefaultKeySize;
            DefaultCertificateHashSize = CertificateFactory.DefaultHashSize;
            CACertificateLifetime = CertificateFactory.DefaultLifeTime;
            CACertificateKeySize = CertificateFactory.DefaultKeySize;
            CACertificateHashSize = CertificateFactory.DefaultHashSize;
            CertificateTypes = [];
        }

        [DataMember(IsRequired = true, Order = 10)]
        [DataTypeField(Order = 0)]
        public string Id { get; set; }

        [DataMember(IsRequired = false, Order = 20)]
        public string CertificateType
        {
            get
            {
                if (!CertificateTypes.IsEmpty)
                {
                    return CertificateTypes[0];
                }
                return null;
            }
            set
            {
                if (!CertificateTypes.IsEmpty)
                {
                    // This is broken - we remove potentially everything by
                    // assigning null multiple times. This should not be settable
                    // at all, but for backward compatibility with existing
                    // configuration we keep it like this.
                    if (value == null)
                    {
                        CertificateTypes = CertificateTypes[1..];
                    }
                    else
                    {
                        CertificateTypes = CertificateTypes.AddItem(value, 0);
                    }
                }
                else
                {
                    CertificateTypes = [value];
                }
            }
        }

        [DataMember(IsRequired = false, Order = 21)]
        [DataTypeField(Order = 1)]
        public ArrayOf<string> CertificateTypes { get; set; }

        [DataMember(IsRequired = true, Order = 25)]
        [DataTypeField(Order = 2)]
        public string SubjectName { get; set; }

        [DataMember(IsRequired = true, Order = 30)]
        [DataTypeField(Order = 3)]
        public string BaseStorePath { get; set; }

        [DataMember(Order = 40)]
        [DataTypeField(Order = 4)]
        public ushort DefaultCertificateLifetime { get; set; }

        [DataMember(Order = 50)]
        [DataTypeField(Order = 5)]
        public ushort DefaultCertificateKeySize { get; set; }

        [DataMember(Order = 60)]
        [DataTypeField(Order = 6)]
        public ushort DefaultCertificateHashSize { get; set; }

        [DataMember(Order = 70)]
        [DataTypeField(Order = 7)]
        public ushort CACertificateLifetime { get; set; }

        [DataMember(Order = 80)]
        [DataTypeField(Order = 8)]
        public ushort CACertificateKeySize { get; set; }

        [DataMember(Order = 90)]
        [DataTypeField(Order = 9)]
        public ushort CACertificateHashSize { get; set; }

        public string TrustedListPath => BaseStorePath + Path.DirectorySeparatorChar + "trusted";
        public string IssuerListPath => BaseStorePath + Path.DirectorySeparatorChar + "issuer";
    }

    [CollectionDataContract(
        Name = "ListOfCertificateGroupConfiguration",
        Namespace = Namespaces.OpcUaGds + "Configuration.xsd",
        ItemName = "CertificateGroupConfiguration"
    )]
    public class CertificateGroupConfigurationCollection : List<CertificateGroupConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public CertificateGroupConfigurationCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public CertificateGroupConfigurationCollection(
            IEnumerable<CertificateGroupConfiguration> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public CertificateGroupConfigurationCollection(int capacity)
            : base(capacity)
        {
        }
    }
}
