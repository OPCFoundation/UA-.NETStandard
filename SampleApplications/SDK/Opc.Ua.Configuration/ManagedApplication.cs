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
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// An application that is managed by the configuration tool.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ManagedApplication 
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedApplication"/> class.
        /// </summary>
        public ManagedApplication()
        {
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            if (!String.IsNullOrEmpty(m_displayName))
            {
                return m_displayName;
            }

            if (!String.IsNullOrEmpty(m_executablePath))
            {
                return new FileInfo(m_executablePath).Name;
            }

            if (!String.IsNullOrEmpty(m_configurationPath))
            {
                return new FileInfo(m_configurationPath).Name;
            }

            return String.Empty;
        }

        /// <summary>
        /// Gets the source file.
        /// </summary>
        /// <value>The source file.</value>
        public FileInfo SourceFile
        {
            get { return m_sourceFile; }
        }

        /// <summary>
        /// Gets a value indicating whether this application is SDK compatible.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this application is SDK compatible; otherwise, <c>false</c>.
        /// </value>
        public bool IsSdkCompatible
        {
            get { return m_isSdkCompatible; }
        }

        /// <summary>
        /// Gets the application.
        /// </summary>
        /// <value>The application.</value>
        public Opc.Ua.Security.SecuredApplication Application
        {
            get { return m_application; }
        }

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        /// <value>The display name.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 0)]  
        public string DisplayName
        {
            get { return m_displayName; }
            set { m_displayName = value; }
        }

        /// <summary>
        /// Gets or sets the executable path.
        /// </summary>
        /// <value>The executable path.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]  
        public string ExecutablePath
        {
            get { return m_executablePath; }
            set { m_executablePath = value; }
        }

        /// <summary>
        /// Gets or sets the configuration path.
        /// </summary>
        /// <value>The configuration path.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]  
        public string ConfigurationPath
        {
            get { return m_configurationPath; }
            set { m_configurationPath = value; }
        }

        /// <summary>
        /// Gets or sets the certificate.
        /// </summary>
        /// <value>The certificate.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public CertificateIdentifier Certificate
        {
            get { return m_certificate; }
            set { m_certificate = value; }
        }

        /// <summary>
        /// Gets or sets the trust list.
        /// </summary>
        /// <value>The trust list.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public CertificateStoreIdentifier TrustList
        {
            get { return m_trustList; }
            set { m_trustList = value; }
        }

        /// <summary>
        /// Gets or sets the trust list.
        /// </summary>
        /// <value>The trust list.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 5)]
        public StringCollection BaseAddresses
        {
            get { return m_baseAddresses; }
            set { m_baseAddresses = value; }
        }

        /// <summary>
        /// Loads the specified file path.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns></returns>
        public static ManagedApplication Load(string filePath)
        {
            using (Stream istrm = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(ManagedApplication));
                ManagedApplication application = (ManagedApplication)serializer.ReadObject(istrm);
                application.m_sourceFile = new FileInfo(filePath);

                if (String.IsNullOrEmpty(application.DisplayName))
                {
                    string name = application.m_sourceFile.Name;
                    int index = name.LastIndexOf('.');

                    if (index > 0)
                    {
                        name = name.Substring(0, index);
                    }

                    application.DisplayName = name;
                }

                application.LoadSdkConfigFile();
                return application;
            }
        }

        /// <summary>
        /// Sets the executable file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        public void SetExecutableFile(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                m_executablePath = null;
                return;
            }

            m_executablePath = filePath;
            m_configurationPath = null;
            m_isSdkCompatible = false;
            m_application = null;

            FileInfo executableFile = new FileInfo(m_executablePath);
            m_displayName = executableFile.Name.Substring(0, executableFile.Name.Length-4);

            FileInfo configFile = new FileInfo(executableFile.FullName + ".config");
            Utils.Trace(1, "APPCONFIG={0}", configFile);

            if (configFile.Exists)
            {
                // save the .NET config file.
                m_configurationPath = configFile.FullName;

                // look for the UA SDK config file.
                string configurationPath = GetConfigFileFromAppConfig(configFile);
                Utils.Trace(1, "UACONFIG={0}", configurationPath);

                if (configurationPath != null)
                {
                    m_configurationPath = configurationPath;
                }
                else
                {
                    m_configurationPath = configFile.FullName;
                }

                LoadSdkConfigFile();
            }

            // set display name.
            if (m_sourceFile == null || String.IsNullOrEmpty(m_displayName))
            {
                string name = executableFile.Name;
                int index = name.LastIndexOf('.');

                if (index > 0)
                {
                    name = name.Substring(0, index);
                }

                m_displayName = name;
            }
        }

        /// <summary>
        /// Tries to loads the SDK config file.
        /// </summary>
        private void LoadSdkConfigFile()
        {
            m_isSdkCompatible = false;
            m_application = null;

            if (String.IsNullOrEmpty(m_configurationPath))
            {
                return;
            }

            FileInfo executablePath = new FileInfo(m_executablePath);
            string currentDirectory = Directory.GetCurrentDirectory();
            
            try
            {
                m_application = GetApplicationSettings(m_configurationPath);

                if (m_application != null)
                {
                    m_isSdkCompatible = true;
                    m_certificate = Opc.Ua.Security.SecuredApplication.FromCertificateIdentifier(m_application.ApplicationCertificate);
                    m_trustList = Opc.Ua.Security.SecuredApplication.FromCertificateStoreIdentifier(m_application.TrustedCertificateStore);
                    m_application.ExecutableFile = m_executablePath;
                    m_configurationPath = m_application.ConfigurationFile;
                }
            }

            // ignore errors.
            catch (Exception)
            {
                m_application = null;
            }
        }

        /// <summary>
        /// Sets the configuration file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        public void SetConfigurationFile(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                m_configurationPath = null;
                return;
            }
            
            m_configurationPath = filePath;

            FileInfo configFile = new FileInfo(filePath);
            m_isSdkCompatible = false;
            m_application = null;
            m_configurationPath = configFile.FullName;

            if (configFile.Exists)
            {
                LoadSdkConfigFile();
            }
        }

        /// <summary>
        /// Reloads the configuration from disk.
        /// </summary>
        public void Reload()
        {
            LoadSdkConfigFile();
        }

        /// <summary>
        /// Gets the application secuirty settings from a file.
        /// </summary>
        private Opc.Ua.Security.SecuredApplication GetApplicationSettings(string filePath)
        {
            string absolutePath = Utils.GetAbsoluteFilePath(filePath, true, false, false);

            if (absolutePath == null)
            {
                return null;
            }

            return new Opc.Ua.Security.SecurityConfigurationManager().ReadConfiguration(absolutePath);
        }

        /// <summary>
        /// Gets the config file location from app config.
        /// </summary>
        private string GetConfigFileFromAppConfig(FileInfo appConfigFile)
        {
            try
            {
                StreamReader reader = new StreamReader(appConfigFile.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                XmlDocument doc = new XmlDocument();
                doc.Load(reader);
                
                try
                {
                    foreach( XmlNode node1 in doc.ChildNodes)
                    {
                        if (node1.Name == "ConfigurationLocation")
                        {
                            foreach (XmlNode node2 in node1.ChildNodes)
                            {
                                if (node2.Name == "FilePath")
                                {
                                    return node2.InnerXml;
                                }
                            }
                        }
                    }

                    return null;
                }
                finally
                {
                    reader.Dispose();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        #region Private Fields
        private FileInfo m_sourceFile;
        private bool m_isSdkCompatible;
        private Opc.Ua.Security.SecuredApplication m_application;
        private string m_displayName;
        private string m_executablePath;
        private string m_configurationPath;
        private CertificateIdentifier m_certificate;
        private CertificateStoreIdentifier m_trustList;
        private StringCollection m_baseAddresses;
        #endregion 
    }
}
