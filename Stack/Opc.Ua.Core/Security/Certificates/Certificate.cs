using System.Collections;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;

using Bc = Org.BouncyCastle.X509;
using Ms = System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// A X509Certificate2 extended with Bouncy castle enabled functionality
    /// </summary>
    public class MsBcCertificate : Ms.X509Certificate2
    {

        #region Public Properies
        /// <summary>
        /// The certificate
        /// </summary>
        public Bc.X509Certificate BouncyCastleCertificate { get => m_bouncyCastleCertificate; set => m_bouncyCastleCertificate = value; }

        /// <summary>
        /// The Private key
        /// </summary>
        public AsymmetricKeyParameter BcPrivateKey { get => m_bcPrivateKey; set => m_bcPrivateKey = value; }
        #endregion

        #region Constructors
        public MsBcCertificate(byte[] data) : base(data)
        {
            BouncyCastleCertificate = new Bc.X509Certificate(X509CertificateStructure.GetInstance(data));
        }

        public MsBcCertificate(byte[] data, string password, Ms.X509KeyStorageFlags flags) : base(data, password, flags)
        {
        }

        public MsBcCertificate(string filePath) : base(filePath)
        {
            BouncyCastleCertificate = new Bc.X509Certificate(X509CertificateStructure.GetInstance(File.ReadAllBytes(filePath)));
        }

        public MsBcCertificate(string fullName, string password, Ms.X509KeyStorageFlags flags) : base(fullName, password, flags)
        {
        }

        public MsBcCertificate(Ms.X509Certificate2 certificate) : base(certificate)
        {
            BouncyCastleCertificate = new Bc.X509Certificate(X509CertificateStructure.GetInstance(certificate.RawData));
        }
        #endregion

        #region Private Members
        private Bc.X509Certificate m_bouncyCastleCertificate;
        private AsymmetricKeyParameter m_bcPrivateKey;
        #endregion


    }

    /// <summary>
    /// A collection of MsBcCertificate
    /// </summary>
    public class MsBcCertificateCollection : Ms.X509Certificate2Collection, IEnumerable<MsBcCertificate>
    {
        public MsBcCertificateCollection()
        {
        }

        public MsBcCertificateCollection(MsBcCertificate certificate) : base(certificate)
        {
        }

        public MsBcCertificateCollection(IEnumerable<MsBcCertificate> certificates)
        {
            if (certificates != null)
            {
                foreach (var certificate in certificates)
                {
                    Add(certificate);
                }
            }
        }

        public MsBcCertificateCollection(Ms.X509Certificate2Collection certificates) : base(certificates)
        {
        }

        public new MsBcCertificate this[int index]
        {
            get
            {
                var certificate = base[index];

                if (certificate is MsBcCertificate)
                {
                    return (MsBcCertificate)certificate;
                }

                return new MsBcCertificate(certificate);
            }

            set
            {
                base[index] = value;
            }
        }

        public new MsBcCertificateCollection Find(Ms.X509FindType findType, object findValue, bool validOnly)
        {
            return new MsBcCertificateCollection(base.Find(findType, findValue, validOnly));
        }

        private class Enumerator : IEnumerator<MsBcCertificate>
        {
            private Ms.X509Certificate2Enumerator m_enumerator;

            public Enumerator(Ms.X509Certificate2Enumerator enumerator)
            {
                m_enumerator = enumerator;
            }

            public MsBcCertificate Current
            {
                get
                {
                    return (MsBcCertificate)m_enumerator.Current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return (MsBcCertificate)m_enumerator.Current;
                }
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                return m_enumerator.MoveNext();
            }

            public void Reset()
            {
                m_enumerator.Reset();
            }
        }

        IEnumerator<MsBcCertificate> IEnumerable<MsBcCertificate>.GetEnumerator()
        {
            return new Enumerator(base.GetEnumerator());
        }
    }
}
