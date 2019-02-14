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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography;


namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Creates UserName.
    /// </summary>
    public class UserNameCreator
    {
        /// <summary>
        /// Triple DES Key
        /// </summary>
        private const string strKey = "h13h6m9F";

        /// <summary>
        /// Triple DES initialization vector
        /// </summary>
        private const string strIV = "Zse5";

        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public UserNameCreator(string applicationName)
        {
            m_UserNameIdentityTokens = LoadUserName(applicationName);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Add a User.
        /// </summary>
        /// <param name="applicationName">The Application Name.</param>
        /// <param name="userName">The UserName.</param>
        /// <param name="password">The Password.</param>
        public void Add(string applicationName, string userName, string password)
        {
            lock (m_lock)
            {
                UserNameIdentityToken newUserNameToken = new UserNameIdentityToken()
                {
                    UserName = userName,
                    DecryptedPassword = password,
                };

                newUserNameToken.Password = new UTF8Encoding().GetBytes(newUserNameToken.DecryptedPassword);

                m_UserNameIdentityTokens.Add(newUserNameToken.UserName, newUserNameToken);

                SaveUserName(applicationName, newUserNameToken);
            }
        }

        /// <summary>
        /// Delete a User.
        /// </summary>
        /// <param name="applicationName">The Application Name.</param>
        /// <param name="userName">The  UserName.</param>
        /// <returns>True if the item deleted from list.</returns>
        public bool Delete(string applicationName, string userName)
        {
            lock (m_lock)
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\Accounts\\{0}\\{1}.xml", applicationName, userName);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, true);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    absolutePath = Utils.GetAbsoluteFilePath(relativePath, true, false, true);
                }

                if (File.Exists(absolutePath))
                {   // delete a file.
                    File.Delete(absolutePath);
                }

                return m_UserNameIdentityTokens.Remove(userName);
            }
        }

        /// <summary>
        /// Load UserNameIdentityToken.
        /// </summary>
        /// <returns>UserNameIdentityToken list.</returns>
        public static Dictionary<string, UserNameIdentityToken> LoadUserName(string applicationName)
        {
            Dictionary<string, UserNameIdentityToken> resultTokens = new Dictionary<string, UserNameIdentityToken>();

            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\Accounts\\{0}", applicationName);
                string absolutePath = Utils.GetAbsoluteDirectoryPath(relativePath, false, false, false);

                if (string.IsNullOrEmpty(absolutePath))
                {
                    return resultTokens;
                }

                foreach (string filePath in Directory.GetFiles(absolutePath))
                {
                    // oops - nothing found.
                    if (filePath == null)
                    {
                        continue;
                    }

                    // open the file.
                    using (FileStream istrm = File.Open(filePath, FileMode.Open, FileAccess.Read))
                    {
                        using (XmlTextReader reader = new XmlTextReader(istrm))
                        {
                            DataContractSerializer serializer = new DataContractSerializer(typeof(UserNameIdentityToken));
                            UserNameIdentityToken userNameToken = (UserNameIdentityToken)serializer.ReadObject(reader, false);

                            if (userNameToken.UserName == null || userNameToken.Password == null)
                            {  // The configuration file has problem.
                                Utils.Trace("Unexpected error saving user configuration for COM Wrapper.");
                                continue;
                            }

                            if (resultTokens.ContainsKey(userNameToken.UserName))
                            {   // When I already exist, I ignore it.
                                Utils.Trace("When I already exist, I ignore it. UserName={0}", userNameToken.UserName);
                                continue;
                            }

                            userNameToken.Password = DecryptPassword(userNameToken.Password);
                            userNameToken.DecryptedPassword = new UTF8Encoding().GetString(userNameToken.Password);

                            resultTokens.Add(userNameToken.UserName, userNameToken);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error saving user configuration for COM Wrapper.");
            }

            return resultTokens;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Save UserNameIdentityToken.
        /// </summary>
        private static void SaveUserName(string applicationName, UserNameIdentityToken userNameToken)
        {
            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\Accounts\\{0}\\{1}.xml", applicationName, userNameToken.UserName);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, true);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    absolutePath = Utils.GetAbsoluteFilePath(relativePath, true, false, true);
                }

                UserNameIdentityToken outputToken = new UserNameIdentityToken()
                {
                    UserName = userNameToken.UserName,
                    Password = EncryptPassword(userNameToken.Password),
                    EncryptionAlgorithm = "Triple DES",
                };

                // open the file.
                FileStream ostrm = File.Open(absolutePath, FileMode.Create, FileAccess.ReadWrite);

                using (XmlTextWriter writer = new XmlTextWriter(ostrm, System.Text.Encoding.UTF8))
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(UserNameIdentityToken));
                    serializer.WriteObject(writer, outputToken);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error saving user configuration for COM Wrapper with UserName={0}.", userNameToken.UserName);
            }
        }

        /// <summary>
        /// Encrypt Password.
        /// </summary>
        /// <param name="srcPassword">The Source Password.</param>
        /// <returns>Encrypted Password.</returns>
        private static byte[] EncryptPassword(byte[] srcPassword)
        {
            byte[] encryptedPassword;
            TripleDESCryptoServiceProvider tdes; // Triple DES service provider
            MemoryStream outStream = null;
            CryptoStream encStream = null;
            string dst = string.Empty;

            // Create Triple DES service provider.
            tdes = new TripleDESCryptoServiceProvider();
            // Get encrypt key and initialization vector.
            byte[] key = Encoding.Unicode.GetBytes(strKey);
            byte[] IV = Encoding.Unicode.GetBytes(strIV);

            // Create result stream and encrypt stream.
            using (outStream = new MemoryStream())
            using (encStream = new CryptoStream(outStream, tdes.CreateEncryptor(key, IV), CryptoStreamMode.Write))
            {
                // Encrypt
                encStream.Write(srcPassword, 0, srcPassword.Length);
                encStream.Close();
                encryptedPassword = outStream.ToArray();
            }

            return encryptedPassword;
        }

        /// <summary>
        /// Decrypt Password.
        /// </summary>
        /// <param name="srcPassword">The Source Password.</param>
        /// <returns>Decrypted Password.</returns>
        private static byte[] DecryptPassword(byte[] srcPassword)
        {
            byte[] decryptedPassword;
            TripleDESCryptoServiceProvider tdes; // Triple DES service provider
            MemoryStream outStream = null;
            CryptoStream decStream = null;
            string dst = string.Empty;

            // Create Triple DES service provider.
            tdes = new TripleDESCryptoServiceProvider();
            // Get encrypt key and initialization vector.
            byte[] key = Encoding.Unicode.GetBytes(strKey);
            byte[] IV = Encoding.Unicode.GetBytes(strIV);

            // Create result stream and decrypt stream.
            using (outStream = new MemoryStream())
            using (decStream = new CryptoStream(outStream, tdes.CreateDecryptor(key, IV), CryptoStreamMode.Write))
            {
                // Decrypt
                decStream.Write(srcPassword, 0, srcPassword.Length);
                decStream.Close();
                decryptedPassword = outStream.ToArray();
            }

            return decryptedPassword;
        }

        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<string, UserNameIdentityToken> m_UserNameIdentityTokens = new Dictionary<string, UserNameIdentityToken>();
        #endregion
    }
}
