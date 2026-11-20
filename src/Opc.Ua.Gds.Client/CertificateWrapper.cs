// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Client
{
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class CertificateWrapper : IFormattable
    {
        public Certificate? Certificate { get; set; }

        [DataMember(Order = 1)]
        public string? SubjectName
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
        public string? IssuerName
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
        public string? SerialNumber
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
        public string? Thumbprint
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
        public string? SignatureAlgorithm
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
        public string? PublicKeyAlgorithm
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
        public byte[]? PublicKey
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
                    // TODO  use X509Utils.GetPublicKeySize(Certificate); everywhere
                    return X509Utils.GetRSAPublicKeySize(Certificate);
                }

                return 0;
            }
            private set { }
        }

        [DataMember(Order = 11)]
        public string? ApplicationUri
        {
            get
            {
                if (Certificate != null)
                {
                    try
                    {
                        IReadOnlyList<string> applicationUris
                            = X509Utils.GetApplicationUrisFromCertificate(Certificate);
                        return applicationUris.Count > 0 ? applicationUris[0] : null;
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
        public IList<string>? Domains
        {
            get
            {
                if (Certificate != null)
                {
                    try
                    {
                        return X509Utils.GetDomainsFromCertificate(Certificate).ToList();
                    }
                    catch (Exception e)
                    {
                        return [e.Message];
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

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            // SubjectName is null when Certificate has not been assigned; preserves
            // pre-nullable runtime behavior of returning null from ToString in that case.
            return SubjectName!;
        }

        public CertificateWrapper Clone()
        {
            return new CertificateWrapper { Certificate = Certificate };
        }
    }
}
