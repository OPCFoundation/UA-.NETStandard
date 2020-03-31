/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Provides access to security configuration for windows based .NET application.
    /// </summary>
    public class SecurityConfigurationManager : ISecurityConfigurationManager
    {
        #region ISecurityConfigurationManager Members
        /// <summary>
        /// Exports the security configuration for an application identified by a file or url.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The security configuration.</returns>
        public SecuredApplication ReadConfiguration(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            string configFilePath = filePath;
            string exeFilePath = null;

            // check for valid file.
            if (!File.Exists(filePath))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotReadable,
                    "Cannot find the executable or configuration file: {0}",
                    filePath);
            }

            // find the configuration file for the executable.
            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeFilePath = filePath;

                try
                {
                    FileInfo file = new FileInfo(filePath);
                    string sectionName = file.Name;
                    sectionName = sectionName.Substring(0, sectionName.Length - file.Extension.Length);

                    configFilePath = ApplicationConfiguration.GetFilePathFromAppConfig(sectionName);

                    if (configFilePath == null)
                    {
                        configFilePath = filePath + ".config";
                    }
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotReadable,
                        e,
                        "Cannot find the configuration file for the executable: {0}",
                        filePath);
                }

                if (!File.Exists(configFilePath))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotReadable,
                        "Cannot find the configuration file: {0}",
                        configFilePath);
                }
            }

            SecuredApplication application = null;
            ApplicationConfiguration applicationConfiguration = null;

            try
            {
                FileStream reader = File.Open(configFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);


                try
                {
                    byte[] data = new byte[reader.Length];
                    reader.Read(data, 0, (int)reader.Length);

                    // find the SecuredApplication element in the file.
                    if (data.ToString().Contains("SecuredApplication"))
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(SecuredApplication));
                        application = serializer.ReadObject(reader) as SecuredApplication;

                        application.ConfigurationFile = configFilePath;
                        application.ExecutableFile = exeFilePath;
                    }

                    // load the application configuration.
                    else
                    {
                        reader.Dispose();
                        reader = File.Open(configFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        DataContractSerializer serializer = new DataContractSerializer(typeof(ApplicationConfiguration));
                        applicationConfiguration = serializer.ReadObject(reader) as ApplicationConfiguration;
                    }
                }
                finally
                {
                    reader.Dispose();
                }
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotReadable,
                    e,
                    "Cannot load the configuration file: {0}",
                    filePath);
            }

            // check if security info store on disk.
            if (application != null)
            {
                return application;
            }

            application = new SecuredApplication();

            // copy application info.
            application.ApplicationName = applicationConfiguration.ApplicationName;
            application.ApplicationUri = applicationConfiguration.ApplicationUri;
            application.ProductName = applicationConfiguration.ProductUri;
            application.ApplicationType = (ApplicationType)(int)applicationConfiguration.ApplicationType;
            application.ConfigurationFile = configFilePath;
            application.ExecutableFile = exeFilePath;
            application.ConfigurationMode = "http://opcfoundation.org/UASDK/ConfigurationTool";
            application.LastExportTime = DateTime.UtcNow;

            // copy the security settings.
            if (applicationConfiguration.SecurityConfiguration != null)
            {
                application.ApplicationCertificate = SecuredApplication.ToCertificateIdentifier(applicationConfiguration.SecurityConfiguration.ApplicationCertificate);

                if (applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates != null)
                {
                    application.IssuerCertificateStore = SecuredApplication.ToCertificateStoreIdentifier(applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates);

                    if (applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.TrustedCertificates != null)
                    {
                        application.IssuerCertificates = SecuredApplication.ToCertificateList(applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.TrustedCertificates);
                    }
                }

                if (applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates != null)
                {
                    application.TrustedCertificateStore = SecuredApplication.ToCertificateStoreIdentifier(applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates);

                    if (applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.TrustedCertificates != null)
                    {
                        application.TrustedCertificates = SecuredApplication.ToCertificateList(applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.TrustedCertificates);
                    }
                }

                if (applicationConfiguration.SecurityConfiguration.RejectedCertificateStore != null)
                {
                    application.RejectedCertificatesStore = SecuredApplication.ToCertificateStoreIdentifier(applicationConfiguration.SecurityConfiguration.RejectedCertificateStore);
                }
            }

            ServerBaseConfiguration serverConfiguration = null;

            if (applicationConfiguration.ServerConfiguration != null)
            {
                serverConfiguration = applicationConfiguration.ServerConfiguration;
            }

            else if (applicationConfiguration.DiscoveryServerConfiguration != null)
            {
                serverConfiguration = applicationConfiguration.DiscoveryServerConfiguration;
            }

            if (serverConfiguration != null)
            {
                application.BaseAddresses = SecuredApplication.ToListOfBaseAddresses(serverConfiguration);
                application.SecurityProfiles = SecuredApplication.ToListOfSecurityProfiles(serverConfiguration.SecurityPolicies);
            }

            // return exported setttings.
            return application;
        }

        /// <summary>
        /// Finds the specified parent.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="localName">Name of the local.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <returns></returns>
        private XmlElement Find(XmlNode parent, string localName, string namespaceUri)
        {
            for (XmlNode ii = parent.FirstChild; ii != null; ii = ii.NextSibling)
            {
                if (ii is XmlElement && ii.LocalName == "SecuredApplication" && ii.NamespaceURI == Namespaces.OpcUaSecurity)
                {
                    return (XmlElement)ii;
                }

                XmlElement child = Find(ii, localName, namespaceUri);

                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the security configuration for an application identified by a file or url.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="configuration">The configuration.</param>
        public void WriteConfiguration(string filePath, SecuredApplication configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // check for valid file.
            if (String.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotReadable,
                    "Cannot find the configuration file: {0}",
                    configuration.ConfigurationFile);
            }

            // load from file.
            XmlDocument document = new XmlDocument();
            document.Load(new FileStream(filePath, FileMode.Open));

            XmlElement element = Find(document.DocumentElement, "SecuredApplication", Namespaces.OpcUaSecurity);

            // update secured application.
            if (element != null)
            {
                configuration.LastExportTime = DateTime.UtcNow;
                element.InnerXml = SetObject(typeof(SecuredApplication), configuration);
            }

            // update application configuration.
            else
            {
                UpdateDocument(document.DocumentElement, configuration);
            }

            try
            {
                // update configuration file.
                Stream ostrm = File.Open(filePath, FileMode.Create, FileAccess.Write);
                StreamWriter writer = new StreamWriter(ostrm, System.Text.Encoding.UTF8);

                try
                {
                    document.Save(writer);
                }
                finally
                {
                    writer.Flush();
                    writer.Dispose();
                }
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotWritable,
                    e,
                    "Cannot update the configuration file: {0}",
                    configuration.ConfigurationFile);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the XML document with the new configuration information.
        /// </summary>
        private static void UpdateDocument(XmlElement element, SecuredApplication application)
        {
            for (XmlNode node = element.FirstChild; node != null; node = node.NextSibling)
            {
                if (node.Name == "ApplicationName" && node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    node.InnerText = application.ApplicationName;
                    continue;
                }

                if (node.Name == "ApplicationUri" && node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    node.InnerText = application.ApplicationUri;
                    continue;
                }

                if (node.Name == "SecurityConfiguration" && node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    SecurityConfiguration security = (SecurityConfiguration)GetObject(typeof(SecurityConfiguration), node);

                    if (application.ApplicationCertificate != null)
                    {
                        security.ApplicationCertificate = SecuredApplication.FromCertificateIdentifier(application.ApplicationCertificate);
                    }

                    security.TrustedIssuerCertificates = SecuredApplication.FromCertificateStoreIdentifierToTrustList(application.IssuerCertificateStore);
                    security.TrustedIssuerCertificates.TrustedCertificates = SecuredApplication.FromCertificateList(application.IssuerCertificates);
                    security.TrustedPeerCertificates = SecuredApplication.FromCertificateStoreIdentifierToTrustList(application.TrustedCertificateStore);
                    security.TrustedPeerCertificates.TrustedCertificates = SecuredApplication.FromCertificateList(application.TrustedCertificates);
                    security.RejectedCertificateStore = SecuredApplication.FromCertificateStoreIdentifier(application.RejectedCertificatesStore);

                    node.InnerXml = SetObject(typeof(SecurityConfiguration), security);
                    continue;
                }

                if (node.Name == "ServerConfiguration" && node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    ServerConfiguration configuration = (ServerConfiguration)GetObject(typeof(ServerConfiguration), node);

                    SecuredApplication.FromListOfBaseAddresses(configuration, application.BaseAddresses);
                    configuration.SecurityPolicies = SecuredApplication.FromListOfSecurityProfiles(application.SecurityProfiles);

                    node.InnerXml = SetObject(typeof(ServerConfiguration), configuration);
                    continue;
                }

                else if (node.Name == "DiscoveryServerConfiguration" && node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    DiscoveryServerConfiguration configuration = (DiscoveryServerConfiguration)GetObject(typeof(DiscoveryServerConfiguration), node);

                    SecuredApplication.FromListOfBaseAddresses(configuration, application.BaseAddresses);
                    configuration.SecurityPolicies = SecuredApplication.FromListOfSecurityProfiles(application.SecurityProfiles);

                    node.InnerXml = SetObject(typeof(DiscoveryServerConfiguration), configuration);
                    continue;
                }
            }
        }

        /// <summary>
        /// Reads an object from the body of an XML element.
        /// </summary>
        private static object GetObject(Type type, XmlNode element)
        {
            using (MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(element.InnerXml)))
            {
                XmlDictionaryReader reader = XmlDictionaryReader.CreateTextReader(memoryStream, Encoding.UTF8, new XmlDictionaryReaderQuotas(), null);
                DataContractSerializer serializer = new DataContractSerializer(type);
                return serializer.ReadObject(reader);
            }
        }

        /// <summary>
        /// Reads an object from the body of an XML element.
        /// </summary>
        private static string SetObject(Type type, object value)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                DataContractSerializer serializer = new DataContractSerializer(value.GetType());
                serializer.WriteObject(memoryStream, value);

                // must extract the inner xml.
                XmlDocument document = new XmlDocument();
                document.InnerXml = Encoding.UTF8.GetString(memoryStream.ToArray());
                return document.DocumentElement.InnerXml;
            }
        }
        #endregion
    }
}
