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

using System.IO;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Stores the configuration for the Global Discovery Server.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public partial class GlobalDiscoveryServerConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryServerConfiguration()
        {
        }

        [DataTypeField(Order = 0)]
        public string AuthoritiesStorePath { get; set; }

        [DataTypeField(Order = 1)]
        public string ApplicationCertificatesStorePath { get; set; }

        [DataTypeField(Order = 2)]
        public string BaseCertificateGroupStorePath { get; set; }

        [DataTypeField(Order = 3)]
        public string DefaultSubjectNameContext { get; set; }

        [DataTypeField(Order = 4)]
        public ArrayOf<CertificateGroupConfiguration> CertificateGroups { get; set; }

        [DataTypeField(Order = 5)]
        public ArrayOf<string> KnownHostNames { get; set; }

        [DataTypeField(Order = 6)]
        public string DatabaseStorePath { get; set; }

        [DataTypeField(Order = 7)]
        public string UsersDatabaseStorePath { get; set; }
    }

    /// <summary>
    /// Stores the configuration for a certificate group.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public sealed partial class CertificateGroupConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateGroupConfiguration()
        {
            Initialize();
        }

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

        [DataTypeField(Order = 0, IsRequired = true)]
        public string Id { get; set; }

        [DataTypeField(Order = 1)]
        public ArrayOf<string> CertificateTypes { get; set; }

        [DataTypeField(Order = 2, IsRequired = true)]
        public string SubjectName { get; set; }

        [DataTypeField(Order = 3, IsRequired = true)]
        public string BaseStorePath { get; set; }

        [DataTypeField(Order = 4)]
        public ushort DefaultCertificateLifetime { get; set; }

        [DataTypeField(Order = 5)]
        public ushort DefaultCertificateKeySize { get; set; }

        [DataTypeField(Order = 6)]
        public ushort DefaultCertificateHashSize { get; set; }

        [DataTypeField(Order = 7)]
        public ushort CACertificateLifetime { get; set; }

        [DataTypeField(Order = 8)]
        public ushort CACertificateKeySize { get; set; }

        [DataTypeField(Order = 9)]
        public ushort CACertificateHashSize { get; set; }

        public string TrustedListPath
            => BaseStorePath + Path.DirectorySeparatorChar + "trusted";

        public string IssuerListPath
            => BaseStorePath + Path.DirectorySeparatorChar + "issuer";
    }
}
