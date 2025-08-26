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
        /// <summary>
        /// Exports the security configuration for an application identified by a file or url.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The security configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public SecuredApplication ReadConfiguration(string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

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
                    var file = new FileInfo(filePath);
                    string sectionName = file.Name;
                    sectionName = sectionName[..^file.Extension.Length];

                    configFilePath =
                        ApplicationConfiguration.GetFilePathFromAppConfig(sectionName) ??
                        filePath + ".config";
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
                FileStream reader = File.Open(
                    configFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                try
                {
                    byte[] data = new byte[reader.Length];
                    int bytesRead = reader.Read(data, 0, (int)reader.Length);
                    if (reader.Length != bytesRead)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotReadable,
                            "Cannot read all bytes of the configuration file: {0}<{1}",
                            bytesRead,
                            reader.Length);
                    }

                    // find the SecuredApplication element in the file.
                    if (data.ToString().Contains("SecuredApplication", StringComparison.Ordinal))
                    {
                        var serializer = new DataContractSerializer(typeof(SecuredApplication));
                        application = serializer.ReadObject(reader) as SecuredApplication;

                        application.ConfigurationFile = configFilePath;
                        application.ExecutableFile = exeFilePath;
                    }
                    // load the application configuration.
                    else
                    {
                        reader.Dispose();
                        reader = File.Open(
                            configFilePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read);
                        var serializer = new DataContractSerializer(
                            typeof(ApplicationConfiguration));
                        applicationConfiguration = serializer.ReadObject(
                            reader) as ApplicationConfiguration;
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

            application = new SecuredApplication
            {
                // copy application info.
                ApplicationName = applicationConfiguration.ApplicationName,
                ApplicationUri = applicationConfiguration.ApplicationUri,
                ProductName = applicationConfiguration.ProductUri,
                ApplicationType = (ApplicationType)(int)applicationConfiguration.ApplicationType,
                ConfigurationFile = configFilePath,
                ExecutableFile = exeFilePath,
                ConfigurationMode = "http://opcfoundation.org/UASDK/ConfigurationTool",
                LastExportTime = DateTime.UtcNow
            };

            // copy the security settings.
            if (applicationConfiguration.SecurityConfiguration != null)
            {
                if (applicationConfiguration.SecurityConfiguration.IsDeprecatedConfiguration)
                {
                    application.ApplicationCertificate = SecuredApplication.ToCertificateIdentifier(
                        applicationConfiguration.SecurityConfiguration.ApplicationCertificate);
                }
                else
                {
                    application.ApplicationCertificates = SecuredApplication.ToCertificateList(
                        applicationConfiguration.SecurityConfiguration.ApplicationCertificates);
                }

                if (applicationConfiguration.SecurityConfiguration
                    .TrustedIssuerCertificates != null)
                {
                    application.IssuerCertificateStore = SecuredApplication
                        .ToCertificateStoreIdentifier(
                            applicationConfiguration.SecurityConfiguration
                                .TrustedIssuerCertificates);

                    if (applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                            .TrustedCertificates !=
                        null)
                    {
                        application.IssuerCertificates = SecuredApplication.ToCertificateList(
                            applicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates
                                .TrustedCertificates);
                    }
                }

                if (applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates != null)
                {
                    application.TrustedCertificateStore = SecuredApplication
                        .ToCertificateStoreIdentifier(
                            applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates);

                    if (applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates
                            .TrustedCertificates !=
                        null)
                    {
                        application.TrustedCertificates = SecuredApplication.ToCertificateList(
                            applicationConfiguration.SecurityConfiguration.TrustedPeerCertificates
                                .TrustedCertificates);
                    }
                }

                if (applicationConfiguration.SecurityConfiguration.RejectedCertificateStore != null)
                {
                    application.RejectedCertificatesStore = SecuredApplication
                        .ToCertificateStoreIdentifier(
                            applicationConfiguration.SecurityConfiguration
                                .RejectedCertificateStore);
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
                application.BaseAddresses = SecuredApplication.ToListOfBaseAddresses(
                    serverConfiguration);
                application.SecurityProfiles = SecuredApplication.ToListOfSecurityProfiles(
                    serverConfiguration.SecurityPolicies);
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
        private static XmlElement Find(XmlNode parent, string localName, string namespaceUri)
        {
            for (XmlNode ii = parent.FirstChild; ii != null; ii = ii.NextSibling)
            {
                if (ii is XmlElement xml &&
                    ii.LocalName == "SecuredApplication" &&
                    ii.NamespaceURI == Namespaces.OpcUaSecurity)
                {
                    return xml;
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
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteConfiguration(string filePath, SecuredApplication configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // check for valid file.
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotReadable,
                    "Cannot find the configuration file: {0}",
                    configuration.ConfigurationFile);
            }

            // load from file.
            var document = new XmlDocument();
            using (var stream = new FileStream(filePath, FileMode.Open))
            using (var xmlReader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
            {
                document.Load(xmlReader);
            }
            XmlElement element = Find(
                document.DocumentElement,
                "SecuredApplication",
                Namespaces.OpcUaSecurity);

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
                var writer = new StreamWriter(ostrm, Encoding.UTF8);

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

                if (node.Name == "SecurityConfiguration" &&
                    node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    var security = (SecurityConfiguration)GetObject(
                        typeof(SecurityConfiguration),
                        node);

                    if (application.ApplicationCertificate != null)
                    {
                        security.ApplicationCertificate = SecuredApplication
                            .FromCertificateIdentifier(
                                application.ApplicationCertificate);
                        security.IsDeprecatedConfiguration = true;
                    }

                    if (application.ApplicationCertificates != null)
                    {
                        security.ApplicationCertificates = SecuredApplication.FromCertificateList(
                            application.ApplicationCertificates);
                    }

                    security.TrustedIssuerCertificates = SecuredApplication
                        .FromCertificateStoreIdentifierToTrustList(
                            application.IssuerCertificateStore);
                    security.TrustedIssuerCertificates.TrustedCertificates = SecuredApplication
                        .FromCertificateList(
                            application.IssuerCertificates);
                    security.TrustedPeerCertificates = SecuredApplication
                        .FromCertificateStoreIdentifierToTrustList(
                            application.TrustedCertificateStore);
                    security.TrustedPeerCertificates.TrustedCertificates = SecuredApplication
                        .FromCertificateList(
                            application.TrustedCertificates);
                    security.RejectedCertificateStore = SecuredApplication
                        .FromCertificateStoreIdentifier(
                            application.RejectedCertificatesStore);

                    node.InnerXml = SetObject(typeof(SecurityConfiguration), security);
                    continue;
                }

                if (node.Name == "ServerConfiguration" &&
                    node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    var configuration = (ServerConfiguration)GetObject(
                        typeof(ServerConfiguration),
                        node);

                    SecuredApplication.FromListOfBaseAddresses(
                        configuration,
                        application.BaseAddresses);
                    configuration.SecurityPolicies = SecuredApplication.FromListOfSecurityProfiles(
                        application.SecurityProfiles);

                    node.InnerXml = SetObject(typeof(ServerConfiguration), configuration);
                }
                else if (node.Name == "DiscoveryServerConfiguration" &&
                    node.NamespaceURI == Namespaces.OpcUaConfig)
                {
                    var configuration = (DiscoveryServerConfiguration)GetObject(
                        typeof(DiscoveryServerConfiguration),
                        node);

                    SecuredApplication.FromListOfBaseAddresses(
                        configuration,
                        application.BaseAddresses);
                    configuration.SecurityPolicies = SecuredApplication.FromListOfSecurityProfiles(
                        application.SecurityProfiles);

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
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(element.InnerXml));
            var reader = XmlDictionaryReader.CreateTextReader(
                memoryStream,
                Encoding.UTF8,
                new XmlDictionaryReaderQuotas(),
                null);
            var serializer = new DataContractSerializer(type);
            return serializer.ReadObject(reader);
        }

        /// <summary>
        /// Reads an object from the body of an XML element.
        /// </summary>
        private static string SetObject(Type type, object value)
        {
            using var memoryStream = new MemoryStream();
            var serializer = new DataContractSerializer(value?.GetType() ?? type);
            serializer.WriteObject(memoryStream, value);

            // must extract the inner xml.
            var document = new XmlDocument();
            document.LoadInnerXml(Encoding.UTF8.GetString(memoryStream.ToArray()));
            return document.DocumentElement.InnerXml;
        }
    }
}
