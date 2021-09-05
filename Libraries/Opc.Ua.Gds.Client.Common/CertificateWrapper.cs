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

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class CertificateWrapper : IFormattable, IEncodeable
    {
        public X509Certificate2 Certificate { get; set; }

        [DataMember(Order = 1)]
        public string SubjectName
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.Subject;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 2)]
        public string IssuerName
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.Issuer;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 3)]
        public DateTime ValidFrom
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.NotBefore;
                }

                return DateTime.MinValue;
            }

            private set { }
        }

        [DataMember(Order = 4)]
        public DateTime ValidTo
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.NotAfter;
                }

                return DateTime.MinValue;
            }

            private set { }
        }

        [DataMember(Order = 5)]
        public string SerialNumber
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.SerialNumber;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 6)]
        public string Thumbprint
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.Thumbprint;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 7)]
        public string SignatureAlgorithm
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.SignatureAlgorithm.FriendlyName;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 8)]
        public string PublicKeyAlgorithm
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.PublicKey.Oid.FriendlyName;
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 9)]
        public byte[] PublicKey
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.PublicKey.EncodedKeyValue.RawData;
                }

                return null;
            }

            private set { }
        }


        [DataMember(Order = 10)]
        public int KeySize
        {
            get
            {
                if (Certificate != null)
                {
                    return Certificate.PublicKey.Key.KeySize;
                }

                return 0;
            }

            private set { }
        }

        [DataMember(Order = 11)]
        public string ApplicationUri
        {
            get
            {
                if (Certificate != null)
                {
                    try
                    {
                        return X509Utils.GetApplicationUriFromCertificate(Certificate);
                    }
                    catch (Exception e)
                    {
                        return e.Message;
                    }
                }

                return null;
            }

            private set { }
        }

        [DataMember(Order = 12)]
        public IList<string> Domains
        {
            get
            {
                if (Certificate != null)
                {
                    try
                    {
                        return X509Utils.GetDomainsFromCertficate(Certificate);
                    }
                    catch (Exception e)
                    {
                        return new string[] { e.Message };
                    }
                }

                return null;
            }

            private set { }
        }

        public override string ToString()
        {
            return ToString(null, null);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return SubjectName;
        }

        #region IEncodeable Members
        public ExpandedNodeId TypeId
        {
            get { return NodeId.Null; }
        }

        public ExpandedNodeId BinaryEncodingId
        {
            get { return NodeId.Null; }
        }

        public ExpandedNodeId XmlEncodingId
        {
            get { return NodeId.Null; }
        }

        public void Encode(IEncoder encoder)
        {
            throw new NotImplementedException();
        }

        public void Decode(IDecoder decoder)
        {
            throw new NotImplementedException();
        }

        public bool IsEqual(IEncodeable encodeable)
        {
            throw new NotImplementedException();
        }

        public object Clone()
        {
            return new CertificateWrapper() { Certificate = this.Certificate };
        }
        #endregion
    }
}
