/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

namespace Opc.Ua
{
    #region ICertificatePasswordProvider Interface
    /// <summary>
    /// An interface for a password provider for certificate private keys.
    /// </summary>
    public interface ICertificatePasswordProvider
    {
        /// <summary>
        /// Return the password for a certificate private key.
        /// </summary>
        /// <param name="certificateIdentifier">The certificate identifier for which the password is needed.</param>
        string GetPassword(CertificateIdentifier certificateIdentifier);
    }
    #endregion

    #region CertificatePasswordProvider
    /// <summary>
    /// The default certificate password provider implementation.
    /// </summary>
    public class CertificatePasswordProvider : ICertificatePasswordProvider
    {
        /// <summary>
        /// Constructor which takes a password string.
        /// </summary>
        /// <param name="password"></param>
        public CertificatePasswordProvider(string password)
        {
            m_password = password;
        }

        /// <summary>
        /// Return the password used for the certificate.
        /// </summary>
        public string GetPassword(CertificateIdentifier certificateIdentifier)
        {
            return m_password;
        }

        private string m_password;
    }
    #endregion
}
