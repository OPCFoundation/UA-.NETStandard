/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Xml;
using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Utility functions used by COM applications.
    /// </summary>
    public static class ConfigUtils
    {
        /// <summary>
        /// Gets or sets a directory which contains files representing users roles.
        /// </summary>
        /// <remarks>
        /// The write permissions on these files are used to determine which users are allowed to act in the role.
        /// </remarks>
        public static string UserRoleDirectory { get; set; }

        /// <summary>
        /// Gets the log file directory and ensures it is writeable.
        /// </summary>
        public static string GetLogFileDirectory()
        {
            // try the program data directory.
            string logFileDirectory = ApplicationData.Current.LocalFolder.Path;
            logFileDirectory += "\\OPC Foundation\\Logs";

            try
            {
                // create the directory.
                if (!Directory.Exists(logFileDirectory))
                {
                    Directory.CreateDirectory(logFileDirectory);
                }

                // ensure everyone has write access to it.
                List<ApplicationAccessRule> rules = new List<ApplicationAccessRule>();

                ApplicationAccessRule rule = new ApplicationAccessRule();

                rule.IdentityName = WellKnownSids.Users;
                rule.Right = ApplicationAccessRight.Configure;
                rule.RuleType = AccessControlType.Allow;

                rules.Add(rule);

                rule = new ApplicationAccessRule();

                rule.IdentityName = WellKnownSids.NetworkService;
                rule.Right = ApplicationAccessRight.Configure;
                rule.RuleType = AccessControlType.Allow;

                rules.Add(rule);

                rule = new ApplicationAccessRule();

                rule.IdentityName = WellKnownSids.LocalService;
                rule.Right = ApplicationAccessRight.Configure;
                rule.RuleType = AccessControlType.Allow;

                rules.Add(rule);

                ApplicationAccessRule.SetAccessRules(logFileDirectory, rules, false);
            }
            catch (Exception)
            {
                // try the MyDocuments directory instead.
                logFileDirectory = ApplicationData.Current.LocalFolder.Path;
                logFileDirectory += "OPC Foundation\\Logs";

                if (!Directory.Exists(logFileDirectory))
                {
                    Directory.CreateDirectory(logFileDirectory);
                }
            }

            return logFileDirectory;
        }
        
        /// <summary>
        /// Finds the first child element with the specified name.
        /// </summary>
        private static XmlElement FindFirstElement(XmlElement parent, string localName, string namespaceUri)
        {
            if (parent == null)
            {
                return null;
            }

            for (XmlNode child = parent.FirstChild; child != null; child = child.NextSibling)
            {
                XmlElement element = child as XmlElement;

                if (element != null)
                {
                    if (element.LocalName == localName && element.NamespaceURI == namespaceUri)
                    {
                        return element;
                    }

                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the configuration location for the specified 
        /// </summary>
        public static void UpdateConfigurationLocation(string executablePath, string configurationPath)
        {
            string configFilePath = Utils.Format("{0}.config", executablePath);

            // not all apps have an app.config file.
            if (!File.Exists(configFilePath))
            {
                return;
            }

            // load from file.
            XmlDocument document = new XmlDocument();
            document.Load(new FileStream(configFilePath, FileMode.Open));

            for (XmlNode child = document.DocumentElement.FirstChild; child != null; child = child.NextSibling)
            {
                // ignore non-element.
                XmlElement element = child as XmlElement;

                if (element == null)
                {
                    continue;
                }

                // look for the configuration location.
                XmlElement location = FindFirstElement(element, "ConfigurationLocation", Namespaces.OpcUaConfig);

                if (location == null)
                {
                    continue;
                }
                
                // find the file path.
                XmlElement filePath = FindFirstElement(location, "FilePath", Namespaces.OpcUaConfig);

                if (filePath == null)
                {
                    filePath = location.OwnerDocument.CreateElement("FilePath", Namespaces.OpcUaConfig);
                    location.InsertBefore(filePath, location.FirstChild);
                }
                
                filePath.InnerText = configurationPath;
                break;
            }
            
            // save configuration file.
            Stream ostrm = File.Open(configFilePath, FileMode.Create, FileAccess.Write);
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
        
        /// <summary>
        /// Sets the defaults for all fields.
        /// </summary>
        /// <param name="application">The application.</param>
        private static void SetDefaults(InstalledApplication application)
        { 
            // create a default product name.
            if (String.IsNullOrEmpty(application.ProductName))
            {
                application.ProductName = application.ApplicationName;
            }

            // create a default uri.
            if (String.IsNullOrEmpty(application.ApplicationUri))
            {
                application.ApplicationUri = Utils.Format("http://localhost/{0}/{1}", application.ApplicationName, Guid.NewGuid());
            }

            // make the uri specify the local machine.
            application.ApplicationUri = Utils.ReplaceLocalhost(application.ApplicationUri);

            // set a default application store.
            if (application.ApplicationCertificate == null)
            {
                application.ApplicationCertificate = new Opc.Ua.Security.CertificateIdentifier();
                application.ApplicationCertificate.StoreType = Utils.DefaultStoreType;
                application.ApplicationCertificate.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
                application.ApplicationCertificate.SubjectName = application.ApplicationName;
            }

            if (application.IssuerCertificateStore == null)
            {
                application.IssuerCertificateStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                application.IssuerCertificateStore.StoreType = Utils.DefaultStoreType;
                application.IssuerCertificateStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
            }

            if (application.TrustedCertificateStore == null)
            {
                application.TrustedCertificateStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                application.TrustedCertificateStore.StoreType = Utils.DefaultStoreType;
                application.TrustedCertificateStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
            }

            try
            {
                Utils.GetAbsoluteDirectoryPath(application.ApplicationCertificate.StorePath, true, true, true);
            }
            catch (Exception e)
            {
                Utils.Trace("Could not access the machine directory: {0} '{1}'", application.ApplicationCertificate.StorePath, e);
            }

            if (application.RejectedCertificatesStore == null)
            {
                application.RejectedCertificatesStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                application.RejectedCertificatesStore.StoreType = CertificateStoreType.Directory;
                application.RejectedCertificatesStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\RejectedCertificates";
            }

            if (application.RejectedCertificatesStore.StoreType == CertificateStoreType.Directory)
            {
                try
                {
                    Utils.GetAbsoluteDirectoryPath(application.RejectedCertificatesStore.StorePath, true, true, true);
                }
                catch (Exception e)
                {
                    Utils.Trace("Could not access rejected certificates directory: {0} '{1}'", application.RejectedCertificatesStore.StorePath, e);
                }
            }
        }

        /// <summary>
        /// Installs a UA application.
        /// </summary>
        public static async Task InstallApplication(
            InstalledApplication application,
            bool autostart,
            bool configureFirewall)
        {
            // validate the executable file.
            string executableFile = Utils.GetAbsoluteFilePath(application.ExecutableFile, true, true, false);
            
            // get the default application name from the executable file.
            FileInfo executableFileInfo = new FileInfo(executableFile);

            string applicationName = executableFileInfo.Name.Substring(0, executableFileInfo.Name.Length-4);

            // choose a default configuration file.
            if (String.IsNullOrEmpty(application.ConfigurationFile))
            {
                application.ConfigurationFile = Utils.Format(
                    "{0}\\{1}.Config.xml", 
                    executableFileInfo.DirectoryName, 
                    applicationName);                
            }

            // validate the configuration file.
            string configurationFile = Utils.GetAbsoluteFilePath(application.ConfigurationFile, true, false, false);
            
            // create a new file if one does not exist.
            bool useExisting = true;

            if (configurationFile == null)
            {
                configurationFile = Utils.GetAbsoluteFilePath(application.ConfigurationFile, true, true, true);
                useExisting = false;
            }

            // create the default configuration file.

            if (useExisting)
            {
                try
                {
                    Opc.Ua.Security.SecuredApplication existingSettings = new Opc.Ua.Security.SecurityConfigurationManager().ReadConfiguration(configurationFile);
                    
                    // copy current settings
                    application.ApplicationType = existingSettings.ApplicationType;
                    application.BaseAddresses = existingSettings.BaseAddresses;
                    application.ApplicationCertificate = existingSettings.ApplicationCertificate;
                    application.ApplicationName = existingSettings.ApplicationName;
                    application.ProductName = existingSettings.ProductName;
                    application.RejectedCertificatesStore = existingSettings.RejectedCertificatesStore;
                    application.TrustedCertificateStore = existingSettings.TrustedCertificateStore;
                    application.TrustedCertificates = existingSettings.TrustedCertificates;
                    application.IssuerCertificateStore = existingSettings.IssuerCertificateStore;
                    application.IssuerCertificates = application.IssuerCertificates;
                    application.UseDefaultCertificateStores = false;
                }
                catch (Exception e)
                {
                    useExisting = false;
                    Utils.Trace("WARNING. Existing configuration file could not be loaded: {0}.\r\nReplacing with default: {1}", e.Message, configurationFile);
                    File.Copy(configurationFile, configurationFile + ".bak", true);
                }
            }
            
            // create the configuration file from the default.
            if (!useExisting)
            {
                try
                {
                    string installationFile = Utils.Format(
                        "{0}\\Install\\{1}.Config.xml", 
                        executableFileInfo.Directory.Parent.FullName, 
                        applicationName);
                    
                    if (!File.Exists(installationFile))
                    {
                        Utils.Trace("Could not find default configuation at: {0}", installationFile);
                    }
                        
                    File.Copy(installationFile, configurationFile, true);
                    Utils.Trace("File.Copy({0}, {1})", installationFile, configurationFile);
                }
                catch (Exception e)
                {
                    Utils.Trace("Could not copy default configuation to: {0}. Error={1}.", configurationFile, e.Message);
                }
            }

            // create a default application name.
            if (String.IsNullOrEmpty(application.ApplicationName))
            {
                application.ApplicationName = applicationName;
            }
                        
            // create a default product name.
            if (String.IsNullOrEmpty(application.ProductName))
            {
                application.ProductName = application.ApplicationName;
            }

            // create a default uri.
            if (String.IsNullOrEmpty(application.ApplicationUri))
            {
                application.ApplicationUri = Utils.Format("http://localhost/{0}/{1}", applicationName, Guid.NewGuid());
            }
            
            // make the uri specify the local machine.
            application.ApplicationUri = Utils.ReplaceLocalhost(application.ApplicationUri);

            // set a default application store.
            if (application.ApplicationCertificate == null)
            {
                application.ApplicationCertificate = new Opc.Ua.Security.CertificateIdentifier();
                application.ApplicationCertificate.StoreType = Utils.DefaultStoreType;
                application.ApplicationCertificate.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
            }
            
            if (application.UseDefaultCertificateStores)
            {
                if (application.IssuerCertificateStore == null)
                {
                    application.IssuerCertificateStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                    application.IssuerCertificateStore.StoreType = Utils.DefaultStoreType;
                    application.IssuerCertificateStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
                }

                if (application.TrustedCertificateStore == null)
                {
                    application.TrustedCertificateStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                    application.TrustedCertificateStore.StoreType = Utils.DefaultStoreType;
                    application.TrustedCertificateStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
                }
                
                try
                {
                    Utils.GetAbsoluteDirectoryPath(application.TrustedCertificateStore.StorePath, true, true, true);
                }
                catch (Exception e)
                {
                    Utils.Trace("Could not access the machine directory: {0} '{1}'", application.RejectedCertificatesStore.StorePath, e);
                }

                if (application.RejectedCertificatesStore == null)
                {
                    application.RejectedCertificatesStore = new Opc.Ua.Security.CertificateStoreIdentifier();
                    application.RejectedCertificatesStore.StoreType = CertificateStoreType.Directory;
                    application.RejectedCertificatesStore.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\RejectedCertificates";

                    StringBuilder buffer = new StringBuilder();

                    buffer.Append(ApplicationData.Current.LocalFolder.Path);
                    buffer.Append("\\OPC Foundation");
                    buffer.Append("\\RejectedCertificates");

                    string folderPath = buffer.ToString();

                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }
                }
            }
            
            // check for valid certificate (discard invalid certificates).
            CertificateIdentifier applicationCertificate = Opc.Ua.Security.SecuredApplication.FromCertificateIdentifier(application.ApplicationCertificate); 
            X509Certificate2 certificate = await applicationCertificate.Find(true);

            if (certificate == null)
            {
                certificate = await applicationCertificate.Find(false);

                if (certificate != null)
                {
                    Utils.Trace(
                        "Found existing certificate but it does not have a private key: Store={0}, Certificate={1}", 
                        application.ApplicationCertificate.StorePath,
                        application.ApplicationCertificate);
                }
                else
                {            
                    Utils.Trace(
                        "Existing certificate could not be found: Store={0}, Certificate={1}", 
                        application.ApplicationCertificate.StorePath,
                        application.ApplicationCertificate);
                }
            }

            // check if no certificate exists.
            if (certificate == null)
            {
                certificate = await CreateCertificateForApplication(application);
            }

            // ensure the application certificate is in the trusted peers store.
            try
            {
                CertificateStoreIdentifier certificateStore = Opc.Ua.Security.SecuredApplication.FromCertificateStoreIdentifier(application.TrustedCertificateStore);

                using (ICertificateStore store = certificateStore.OpenStore())
                {
                    X509Certificate2Collection peerCertificates = await store.FindByThumbprint(certificate.Thumbprint);

                    if (peerCertificates.Count == 0)
                    {
                        await store.Add(new X509Certificate2(certificate.RawData));
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(
                    "Could not add certificate '{0}' to trusted peer store '{1}'. Error={2}", 
                    certificate.Subject,
                    application.TrustedCertificateStore, 
                    e.Message);
            }

            // update configuration file location.
            UpdateConfigurationLocation(executableFile, configurationFile);

            // update configuration file.
            new Opc.Ua.Security.SecurityConfigurationManager().WriteConfiguration(configurationFile, application);
            
            ApplicationAccessRuleCollection accessRules = application.AccessRules;
            bool noRulesDefined = application.AccessRules == null || application.AccessRules.Count == 0;
            
            // add the default access rules.
            if (noRulesDefined)
            {
                ApplicationAccessRule rule = new ApplicationAccessRule();
                
                rule.IdentityName = WellKnownSids.Administrators;
                rule.RuleType     = AccessControlType.Allow;
                rule.Right        = ApplicationAccessRight.Configure;

                accessRules.Add(rule);

                rule = new ApplicationAccessRule();
                
                rule.IdentityName = WellKnownSids.Users;
                rule.RuleType     = AccessControlType.Allow;
                rule.Right        = ApplicationAccessRight.Update;

                accessRules.Add(rule);
            }
             
            // ensure the service account has priviledges.
            if (application.InstallAsService)
            {
                // check if a specific account is assigned.
                AccountInfo accountInfo = null;

                if (!String.IsNullOrEmpty(application.ServiceUserName))
                {
                    accountInfo = AccountInfo.Create(application.ServiceUserName);
                }

                // choose a built-in service account.
                if (accountInfo == null)
                {
                    accountInfo = AccountInfo.Create(WellKnownSids.NetworkService);
                    
                    if (accountInfo == null)
                    {
                        accountInfo = AccountInfo.Create(WellKnownSids.LocalSystem);
                    }
                }

                ApplicationAccessRule rule = new ApplicationAccessRule();
                
                rule.IdentityName = accountInfo.ToString();
                rule.RuleType     = AccessControlType.Allow;
                rule.Right        = ApplicationAccessRight.Run;

                accessRules.Add(rule);
            }

            // set permissions on the local certificate store.
            SetCertificatePermissions(
                application,
                applicationCertificate, 
                accessRules, 
                false);

           // set permissions on the local certificate store.
            if (application.RejectedCertificatesStore != null)
            {
                // need to grant full control to certificates in the RejectedCertificatesStore.
                foreach (ApplicationAccessRule rule in accessRules)
                {
                    if (rule.RuleType == AccessControlType.Allow)
                    {
                        rule.Right = ApplicationAccessRight.Configure;
                    }
                }

                CertificateStoreIdentifier rejectedCertificates = Opc.Ua.Security.SecuredApplication.FromCertificateStoreIdentifier(application.RejectedCertificatesStore);

                using (ICertificateStore store = rejectedCertificates.OpenStore())
                {
                    if (store.SupportsAccessControl)
                    {
                        store.SetAccessRules(accessRules, false);
                    }
                }
            }

            
        }

        /// <summary>
        /// Creates a new certificate for application.
        /// </summary>
        /// <param name="application">The application.</param>
        private static async Task<X509Certificate2> CreateCertificateForApplication(InstalledApplication application)
        {
            // build list of domains.
            List<string> domains = new List<string>();

            if (application.BaseAddresses != null)
            {
                foreach (string baseAddress in application.BaseAddresses)
                {
                    Uri uri = Utils.ParseUri(baseAddress);

                    if (uri != null)
                    {
                        string domain = uri.DnsSafeHost;

                        if (String.Compare(domain, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            domain = Utils.GetHostName();
                        }

                        if (!Utils.FindStringIgnoreCase(domains, domain))
                        {
                            domains.Add(domain);
                        }
                    }
                }
            }

            // must at least of the localhost.
            if (domains.Count == 0)
            {
                domains.Add(Utils.GetHostName());
            }

            // create the certificate.
            X509Certificate2 certificate = await Opc.Ua.CertificateFactory.CreateCertificate(
                application.ApplicationCertificate.StoreType,
                application.ApplicationCertificate.StorePath,
                application.ApplicationUri,
                application.ApplicationName,
                Utils.Format("CN={0}/DC={1}", application.ApplicationName, domains[0]),
                domains,
                1024,
                300);

            CertificateIdentifier applicationCertificate = Opc.Ua.Security.SecuredApplication.FromCertificateIdentifier(application.ApplicationCertificate);
            return await applicationCertificate.LoadPrivateKey(null);
        }

        /// <summary>
        /// Updates the access permissions for the certificate store.
        /// </summary>
        private static void SetCertificatePermissions(
            Opc.Ua.Security.SecuredApplication application,
            CertificateIdentifier id,
            IList<ApplicationAccessRule> accessRules,
            bool replaceExisting)
        {
            if (id == null || accessRules == null || accessRules.Count == 0)
            {
                return;
            }

            try
            {
                using (ICertificateStore store = id.OpenStore())
                {
                    if (store.SupportsCertificateAccessControl)
                    {
                        store.SetAccessRules(id.Thumbprint, accessRules, replaceExisting);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace("Could not set permissions for certificate store: {0}. Error={1}", id, e.Message);

                for (int jj = 0; jj < accessRules.Count; jj++)
                {
                    ApplicationAccessRule rule = accessRules[jj];

                    Utils.Trace(
                        (int)Utils.TraceMasks.Error,
                        "IdentityName={0}, Right={1}, RuleType={2}",
                        rule.IdentityName,
                        rule.Right,
                        rule.RuleType);
                }
            }
        }

        /// <summary>
        /// Uninstalls a UA application.
        /// </summary>
        public static async Task UninstallApplication(InstalledApplication application)
        {
            // validate the executable file.
            string executableFile = Utils.GetAbsoluteFilePath(application.ExecutableFile, true, true, false);

            // get the default application name from the executable file.
            FileInfo executableFileInfo = new FileInfo(executableFile);
            string applicationName = executableFileInfo.Name.Substring(0, executableFileInfo.Name.Length-4);

            // choose a default configuration file.
            if (String.IsNullOrEmpty(application.ConfigurationFile))
            {
                application.ConfigurationFile = Utils.Format(
                    "{0}\\{1}.Config.xml", 
                    executableFileInfo.DirectoryName, 
                    applicationName);                
            }
            
            // validate the configuration file.
            string configurationFile = Utils.GetAbsoluteFilePath(application.ConfigurationFile, true, false, false); 
            
            if (configurationFile != null)
            {
                // load the current configuration.
                Opc.Ua.Security.SecuredApplication security = new Opc.Ua.Security.SecurityConfigurationManager().ReadConfiguration(configurationFile);

                // delete the application certificates.
                if (application.DeleteCertificatesOnUninstall)
                {
                    CertificateIdentifier id = Opc.Ua.Security.SecuredApplication.FromCertificateIdentifier(security.ApplicationCertificate);
                                        
                    // delete public key from trusted peers certificate store.
                    try
                    {
                        CertificateStoreIdentifier certificateStore = Opc.Ua.Security.SecuredApplication.FromCertificateStoreIdentifier(security.TrustedCertificateStore);

                        using (ICertificateStore store = certificateStore.OpenStore())
                        {
                            X509Certificate2Collection peerCertificates = await store.FindByThumbprint(id.Thumbprint);

                            if (peerCertificates.Count > 0)
                            {
                                await store.Delete(peerCertificates[0].Thumbprint);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace("Could not delete certificate '{0}' from store. Error={1}", id, e.Message);
                    }    

                    // delete private key from application certificate store.
                    try
                    {
                        using (ICertificateStore store = id.OpenStore())
                        {
                            await store.Delete(id.Thumbprint);
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace("Could not delete certificate '{0}' from store. Error={1}", id, e.Message);
                    }
                    
                    // permentently delete any UA defined stores if they are now empty.
                    try
                    {
#if TODO
                        WindowsCertificateStore store = new WindowsCertificateStore();
                        await store.Open("LocalMachine\\UA Applications");

                        X509Certificate2Collection collection = await store.Enumerate();
                        if (collection.Count == 0)
                        {
                            store.PermanentlyDeleteStore();
                        }
#endif
                    }
                    catch (Exception e)
                    {
                        Utils.Trace("Could not delete certificate '{0}' from store. Error={1}", id, e.Message);
                    }
                }
            }
        }
        
        /// <summary>
        /// The category identifier for UA servers that are registered as COM servers on a machine.
        /// </summary>
        public static readonly Guid CATID_PseudoComServers = new Guid("899A3076-F94E-4695-9DF8-0ED25B02BDBA");

        /// <summary>
        /// The CLSID for the UA COM DA server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComDaProxyServer = new Guid("B25384BD-D0DD-4d4d-805C-6E9F309F27C1");

        /// <summary>
        /// The CLSID for the UA COM AE server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComAeProxyServer = new Guid("4DF1784C-085A-403d-AF8A-B140639B10B3");

        /// <summary>
        /// The CLSID for the UA COM HDA server host process (note: will be eventually replaced the proxy server).
        /// </summary>
        public static readonly Guid CLSID_UaComHdaProxyServer = new Guid("2DA58B69-2D85-4de0-A934-7751322132E2");
        
        /// <summary>
        /// COM servers that support the DA 2.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCDAServer20  = new Guid("63D5F432-CFE4-11d1-B2C8-0060083BA1FB");

        /// <summary>
        /// COM servers that support the DA 3.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCDAServer30  = new Guid("CC603642-66D7-48f1-B69A-B625E73652D7");

        /// <summary>
        /// COM servers that support the AE 1.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCAEServer10  = new Guid("58E13251-AC87-11d1-84D5-00608CB8A7E9");

        /// <summary>
        /// COM servers that support the HDA 1.0 specification.
        /// </summary>
        public static readonly Guid CATID_OPCHDAServer10 = new Guid("7DE5B060-E089-11d2-A5E6-000086339399");
		
		private const uint CLSCTX_INPROC_SERVER	 = 0x1;
		private const uint CLSCTX_INPROC_HANDLER = 0x2;
		private const uint CLSCTX_LOCAL_SERVER	 = 0x4;
		private const uint CLSCTX_REMOTE_SERVER	 = 0x10;

		private static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
		
        private static readonly Guid CLSID_StdComponentCategoriesMgr = new Guid("0002E005-0000-0000-C000-000000000046");

        private const string CATID_OPCDAServer20_Description  = "OPC Data Access Servers Version 2.0";
        private const string CATID_OPCDAServer30_Description  = "OPC Data Access Servers Version 3.0";
        private const string CATID_OPCAEServer10_Description  = "OPC Alarm & Event Server Version 1.0";
        private const string CATID_OPCHDAServer10_Description = "OPC History Data Access Servers Version 1.0";

        private const Int32 CRYPT_OID_INFO_OID_KEY = 1;
        private const Int32  CRYPT_INSTALL_OID_INFO_BEFORE_FLAG  = 1;
    }
}
