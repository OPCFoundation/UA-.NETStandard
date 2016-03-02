/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using Windows.UI.Xaml.Controls;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls.Primitives;
using System.Runtime.CompilerServices;
using Windows.Storage;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A class that provide various common utility functions and shared resources.
    /// </summary>
    public partial class GuiUtils
    {
        public static string CallerName([CallerMemberName]string caller = "")
        {
            return caller;
        }
        
        /// <summary>
         /// The list of icon images.
         /// </summary>
        public List<Image> ImageList;

        /// <summary>
        /// Displays the details of an exception.
        /// </summary>
        public delegate void ExceptionMessageDlgEventHandler(string message);
        public static event ExceptionMessageDlgEventHandler ExceptionMessageDlg;
        public static void HandleException(string caption, string callerName, Exception e)
        {
            if (String.IsNullOrEmpty(caption))
            {
                caption = callerName;
            }
            Utils.Trace("HandleException:{0}:{1}:{2}", caption, callerName, e.Message);
            if (ExceptionMessageDlg != null)
            {
                StringBuilder buffer = new StringBuilder();
                buffer.AppendFormat("HandleException: {0}\r\n\r\n", caption);
                buffer.AppendFormat("Caller: {0}\r\n", callerName);
                buffer.AppendFormat("Exception: {0}\r\n", e.Message);
                ExceptionMessageDlg(buffer.ToString());
            }
        }

        /// <summary>
        /// Defines names for the available 16x16 icons.
        /// </summary>
        public static class Icons
        {
            /// <summary>
            /// An attribute
            /// </summary>
            public const string Attribute = "SimpleItem";

            /// <summary>
            /// A property
            /// </summary>
            public const string Property = "Property";

            /// <summary>
            /// A variable
            /// </summary>
            public const string Variable = "Variable";

            /// <summary>
            /// An object
            /// </summary>
            public const string Object = "Object";

            /// <summary>
            /// A method
            /// </summary>
            public const string Method = "Method";

            /// <summary>
            /// A single computer.
            /// </summary>
            public const string Computer = "Computer";

            /// <summary>
            /// A computer network.
            /// </summary>
            public const string Network = "Network";

            /// <summary>
            /// A folder.
            /// </summary>
            public const string Folder = "Folder";

            /// <summary>
            /// A selected folder.
            /// </summary>
            public const string SelectedFolder = "SelectedFolder";

            /// <summary>
            /// A process or application.
            /// </summary>
            public const string Process = "Process";

            /// <summary>
            /// A certificate
            /// </summary>
            public const string Certificate = "Certificate";

            /// <summary>
            /// An invalid certificate
            /// </summary>
            public const string InvalidCertificate = "InvalidCertificate";

            /// <summary>
            /// A certificate store
            /// </summary>
            public const string CertificateStore = "CertificateStore";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string Users = "Users";

            /// <summary>
            /// A service.
            /// </summary>
            public const string Service = "Service";

            /// <summary>
            /// A logical drive.
            /// </summary>
            public const string Drive = "Drive";

            /// <summary>
            /// The computer desktop.
            /// </summary>
            public const string Desktop = "Desktop";

            /// <summary>
            /// A single user.
            /// </summary>
            public const string SingleUser = "SingleUser";

            /// <summary>
            /// A group of services.
            /// </summary>
            public const string ServiceGroup = "ServiceGroup";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string UserGroup = "UserGroup";

            /// <summary>
            /// A green check
            /// </summary>
            public const string GreenCheck = "GreenCheck";

            /// <summary>
            /// A red cross
            /// </summary>
            public const string RedCross = "RedCross";

            /// <summary>
            /// A users icon with a red cross through it.
            /// </summary>
            public const string UsersRedCross = "UsersRedCross";
        }

        /// <summary>
        /// Uses the command line to override the UA TCP implementation specified in the configuration.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.
        /// </param>
        public static void OverrideUaTcpImplementation(ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }
        }

        /// <summary>
        /// Displays the UA-TCP configuration in the form.
        /// </summary>
        /// <param name="form">The form to display the UA-TCP configuration.</param>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        public static void DisplayUaTcpImplementation(Page form, ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="caller">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="validator">The validator (not used).</param>
        /// <param name="e">The <see cref="Opc.Ua.CertificateValidationEventArgs"/> instance event arguments provided when a certificate validation error occurs.</param>
        public static async Task HandleCertificateValidationError(Page caller, CertificateValidator validator, CertificateValidationEventArgs e)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("Certificate could not validated: {0}\r\n\r\n", e.Error.StatusCode);
            buffer.AppendFormat("Subject: {0}\r\n", e.Certificate.Subject);
            buffer.AppendFormat("Issuer: {0}\r\n", (e.Certificate.Subject == e.Certificate.Issuer) ? "Self-signed" : e.Certificate.Issuer);
            buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", e.Certificate.Thumbprint);

            buffer.AppendFormat("Accept anyways?");
            MessageDlg dialog = new MessageDlg(buffer.ToString(), MessageDlgButton.Yes, MessageDlgButton.No);
            MessageDlgButton result = await dialog.ShowAsync();
            if (result == MessageDlgButton.Yes)
            {
                e.Accept = true;
            }
        }

        /// <summary>
        /// Does any configuration checks before starting up.
        /// </summary>
        public static async Task<ApplicationConfiguration> LoadConfiguration(
            string configSectionName,
            ApplicationType applicationType,
            string defaultConfigFile,
            bool interactive)
        {
            // get the location of the config file.
            string filePath = ApplicationConfiguration.GetFilePathFromAppConfig(configSectionName);

            if (filePath == null || !System.IO.File.Exists(filePath))
            {
                filePath = Utils.GetAbsoluteFilePath(defaultConfigFile, false, false, false);
            }

            try
            {
                // load the configuration file.
                ApplicationConfiguration configuration = await ApplicationConfiguration.Load(new System.IO.FileInfo(filePath), applicationType, null);

                if (configuration == null)
                {
                    return null;
                }

                return configuration;
            }
            catch (Exception e)
            {
                // warn user.
                if (interactive)
                {
                    StringBuilder message = new StringBuilder();

                    message.Append("Could not load configuration file.\r\n");
                    message.Append(filePath);
                    message.Append("\r\n");
                    message.Append("\r\n");
                    message.Append(e.Message);
                    MessageDlg dialog = new MessageDlg(message.ToString());
                    await dialog.ShowAsync();
                    Utils.Trace(e, "Could not load configuration file. {0}", filePath);
                }

                return null;
            }
        }

        /// <summary>
        /// Does any configuration checks before starting up.
        /// </summary>
        public static async Task<ApplicationConfiguration> DoStartupChecks(
            string configSectionName,
            ApplicationType applicationType,
            string defaultConfigFile,
            bool interactive)
        {
            // load the configuration file.
            ApplicationConfiguration configuration = await LoadConfiguration(configSectionName, applicationType, defaultConfigFile, interactive);

            if (configuration == null)
            {
                return null;
            }

            // check the certificate.
            X509Certificate2 certificate = await CheckApplicationInstanceCertificate(configuration, 1024, interactive, true);

            if (certificate == null)
            {
                return null;
            }

            // ensure the application uri matches the certificate.
            string applicationUri = Utils.GetApplicationUriFromCertficate(certificate);

            if (applicationUri != null)
            {
                configuration.ApplicationUri = applicationUri;
            }

            return configuration;
        }

        /// <summary>
        /// Configures the firewall.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="interactive">if set to <c>true</c> if the user should be prompted.</param>
        public static void ConfigureFirewall(ApplicationConfiguration configuration, bool interactive)
        {
            // check if application configuration requires it. 
            if (!configuration.SecurityConfiguration.ConfigureFirewall)
            {
                return;
            }

            // ensure the firewall is configured.
            if (configuration.ServerConfiguration == null && configuration.DiscoveryServerConfiguration == null)
            {
                return;
            }

            // check if there are any ports to open.
            StringCollection baseAddresses = null;

            if (configuration.ServerConfiguration != null)
            {
                baseAddresses = configuration.ServerConfiguration.BaseAddresses;
            }

            if (configuration.DiscoveryServerConfiguration != null)
            {
                baseAddresses = configuration.DiscoveryServerConfiguration.BaseAddresses;
            }

            if (baseAddresses == null || baseAddresses.Count == 0)
            {
                return;
            }
        }

        /// <summary>
        /// Deletes an existing application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        public static async Task DeleteApplicationInstanceCertificate(ApplicationConfiguration configuration)
        {
            // create a default certificate id none specified.
            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            if (id == null)
            {
                return;
            }

            // delete private key.
            X509Certificate2 certificate = await id.Find();

            // delete trusted peer certificate.
            if (configuration.SecurityConfiguration != null && configuration.SecurityConfiguration.TrustedPeerCertificates != null)
            {
                string thumbprint = id.Thumbprint;

                if (certificate != null)
                {
                    thumbprint = certificate.Thumbprint;
                }

                if (!String.IsNullOrEmpty(thumbprint))
                {
                    using (ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore())
                    {
                        await store.Delete(thumbprint);
                    }
                }
            }

            // delete private key.
            if (certificate != null)
            {
                using (ICertificateStore store = id.OpenStore())
                {
                    await store.Delete(certificate.Thumbprint);
                }
            }
        }

        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        public static async Task<X509Certificate2> CheckApplicationInstanceCertificate(ApplicationConfiguration configuration)
        {
            return await CheckApplicationInstanceCertificate(configuration, 1024, true, true);
        }

        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        public static async Task<X509Certificate2> CheckApplicationInstanceCertificate(
            ApplicationConfiguration configuration,
            ushort keySize,
            bool interactive,
            bool updateFile)
        {
            // create a default certificate if none is specified.
            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            if (id == null)
            {
                id = new CertificateIdentifier();
                id.StoreType = Utils.DefaultStoreType;
                id.StorePath = ApplicationData.Current.LocalFolder.Path + "\\OPC Foundation\\CertificateStores\\MachineDefault";
                id.SubjectName = configuration.ApplicationName;
            }

            bool createNewCertificate = false;
            IList<string> serverDomainNames = configuration.GetServerDomainNames();

            // check for private key.
            X509Certificate2 certificate = await id.Find(true);

            if (certificate == null)
            {
                // check if config file has wrong thumprint.
                if (!String.IsNullOrEmpty(id.SubjectName) && !String.IsNullOrEmpty(id.Thumbprint))
                {
                    CertificateIdentifier id2 = new CertificateIdentifier();
                    id2.StoreType = id.StoreType;
                    id2.StorePath = id.StorePath;
                    id2.SubjectName = id.SubjectName;
                    id = id2;

                    certificate = await id2.Find(true);

                    if (certificate != null)
                    {
                        string message = Utils.Format(
                            "Matching certificate with SubjectName={0} found but with a different thumbprint. Use certificate?",
                            id.SubjectName);

                        if (interactive)
                        {
                            MessageDlg dialog = new MessageDlg(message, MessageDlgButton.Yes, MessageDlgButton.No);
                            MessageDlgButton result = await dialog.ShowAsync();
                            if (result != MessageDlgButton.Yes)
                            {
                                certificate = null;
                            }
                        }
                    }
                }

                // check if private key is missing.
                if (certificate == null)
                {
                    certificate = await id.Find(false);

                    if (certificate != null)
                    {
                        string message = Utils.Format(
                            "Matching certificate with SubjectName={0} found but without a private key. Create a new certificate?",
                            id.SubjectName);

                        if (interactive)
                        {
                            MessageDlg dialog = new MessageDlg(message, MessageDlgButton.Yes, MessageDlgButton.No);
                            MessageDlgButton result = await dialog.ShowAsync();
                            if (result != MessageDlgButton.Yes)
                            {
                                certificate = null;
                            }
                        }
                    }
                }

                // check domains.
                if (certificate != null)
                {
                    IList<string> certificateDomainNames = Utils.GetDomainsFromCertficate(certificate);

                    for (int ii = 0; ii < serverDomainNames.Count; ii++)
                    {
                        if (Utils.FindStringIgnoreCase(certificateDomainNames, serverDomainNames[ii]))
                        {
                            continue;
                        }

                        if (String.Compare(serverDomainNames[ii], "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // check computer name.
                            string computerName = Utils.GetHostName();

                            if (Utils.FindStringIgnoreCase(certificateDomainNames, computerName))
                            {
                                continue;
                            }
                        }

                        string message = Utils.Format(
                            "The server is configured to use domain '{0}' which does not appear in the certificate. Create new certificate?",
                            serverDomainNames[ii]);

                        createNewCertificate = true;

                        if (interactive)
                        {
                            MessageDlg dialog = new MessageDlg(message, MessageDlgButton.Yes, MessageDlgButton.No);
                            MessageDlgButton result = await dialog.ShowAsync();
                            if (result != MessageDlgButton.Yes)
                            {
                                createNewCertificate = false;
                                continue;
                            }
                        }

                        Utils.Trace(message);
                        break;
                    }

                    if (!createNewCertificate)
                    {
                        // check if key size matches.
                        if (keySize == certificate.GetRSAPublicKey().KeySize)
                        {
                            await AddToTrustedStore(configuration, certificate);
                            return certificate;
                        }
                    }
                }

                // prompt user.
                if (interactive)
                {
                    if (!createNewCertificate)
                    {
                        MessageDlg dialog = new MessageDlg("Application does not have an instance certificate.\n Create one automatically?", MessageDlgButton.Yes, MessageDlgButton.No);
                        MessageDlgButton result = await dialog.ShowAsync();
                        if (result != MessageDlgButton.Yes)
                        {
                            return null;
                        }
                    }
                }

                // delete existing certificate.
                if (certificate != null)
                {
                    await DeleteApplicationInstanceCertificate(configuration);
                }

                // add the localhost.
                if (serverDomainNames.Count == 0)
                {
                    serverDomainNames.Add(Utils.GetHostName());
                }

                certificate = await Opc.Ua.CertificateFactory.CreateCertificate(
                    id.StoreType,
                    id.StorePath,
                    configuration.ApplicationUri,
                    configuration.ApplicationName,
                    null,
                    serverDomainNames,
                    keySize,
                    300);

                id.Certificate = certificate;
                await AddToTrustedStore(configuration, certificate);

                if (updateFile && !String.IsNullOrEmpty(configuration.SourceFilePath))
                {
                    configuration.SaveToFile(configuration.SourceFilePath);
                }

                await configuration.CertificateValidator.Update(configuration.SecurityConfiguration);

                return await configuration.SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null);
            }

            return certificate;
        }

        /// <summary>
        /// Adds the certificate to the Trusted Certificate Store
        /// </summary>
        /// <param name="configuration">The application's configuration which specifies the location of the TrustedStore.</param>
        /// <param name="certificate">The certificate to register.</param>
        public static async Task AddToTrustedStore(ApplicationConfiguration configuration, X509Certificate2 certificate)
        {
            ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();

            try
            {
                // check if it already exists.
                X509Certificate2Collection existingCertificates = await store.FindByThumbprint(certificate.Thumbprint);

                if (existingCertificates.Count > 0)
                {
                    return;
                }

                List<string> subjectName = Utils.ParseDistinguishedName(certificate.Subject);
            
                // check for old certificate.
                X509Certificate2Collection certificates = await store.Enumerate();

                for (int ii = 0; ii < certificates.Count; ii++)
                {
                    if (Utils.CompareDistinguishedName(certificates[ii], subjectName))
                    {
                        if (certificates[ii].Thumbprint == certificate.Thumbprint)
                        {
                            return;
                        }

                        await store.Delete(certificates[ii].Thumbprint);
                        break;
                    }
                }

                // add new certificate.
                X509Certificate2 publicKey = new X509Certificate2(certificate.RawData);
                await store.Add(publicKey);
            }
            finally
            {
                store.Close();
            }
        }

        /// <summary>
        /// Returns a default value for the data type.
        /// </summary>
        public static object GetDefaultValue(NodeId datatypeId, int valueRank)
        {
            Type type = TypeInfo.GetSystemType(datatypeId, EncodeableFactory.GlobalFactory);

            if (type == null)
            {
                return null;
            }

            if (valueRank < 0)
            {
                if (type == typeof(String))
                {
                    return System.String.Empty;
                }
                
                if (type == typeof(byte[]))
                {
                    return new byte[0];
                }

                if (type == typeof(NodeId))
                {
                    return Opc.Ua.NodeId.Null;
                }

                if (type == typeof(ExpandedNodeId))
                {
                    return Opc.Ua.ExpandedNodeId.Null;
                }

                if (type == typeof(QualifiedName))
                {
                    return Opc.Ua.QualifiedName.Null;
                }

                if (type == typeof(LocalizedText))
                {
                    return Opc.Ua.LocalizedText.Null;
                }

                if (type == typeof(Guid))
                {
                    return System.Guid.Empty;
                }

                if (type == typeof(System.Xml.XmlElement))
                {
                    System.Xml.XmlDocument document = new System.Xml.XmlDocument();
                    document.InnerXml = "<Null/>";
                    return document.DocumentElement;
                }

                return Activator.CreateInstance(type);
            }

            return Array.CreateInstance(type, new int[valueRank]);
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value)
        {
            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (typeInfo != null)
            {
                return EditValue(session, value, (uint)typeInfo.BuiltInType, typeInfo.ValueRank);
            }

            return null;
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value, NodeId datatypeId, int valueRank)
        {
            if (value == null)
            {
                value = GetDefaultValue(datatypeId, valueRank);
            }

            Popup myPopup = new Popup();

            BuiltInType builtinType = TypeInfo.GetBuiltInType(datatypeId, session.TypeTree);

            switch (builtinType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.Byte:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.Enumeration:
                case BuiltInType.String:
                {
                    
                    myPopup.Child = new SimpleValueEditCtrl(value);
                    myPopup.IsOpen = true;
                    value = ((SimpleValueEditCtrl)myPopup.Child).localValue;
                    break;
                }

                case BuiltInType.NodeId:
                {
                    return new NodeIdValueEditDlg().ShowDialog(session, (NodeId)value);
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return new NodeIdValueEditDlg().ShowDialog(session, (ExpandedNodeId)value);
                }

                case BuiltInType.DateTime:
                {
                    DateTime datetime = (DateTime)value;

                    myPopup.Child = new DateTimeValueEditCtrl(datetime);
                    myPopup.IsOpen = true;
                    value = ((DateTimeValueEditCtrl)myPopup.Child).localValue;
                    break;
                }

                case BuiltInType.QualifiedName:
                {
                    QualifiedName qname = (QualifiedName)value;

                    myPopup.Child = new SimpleValueEditCtrl(qname.Name);
                    myPopup.IsOpen = true;
                    string name = ((SimpleValueEditCtrl)myPopup.Child).localValue.ToString();
                    if (name != null)
                    {
                        return new QualifiedName(name, qname.NamespaceIndex);
                    }

                    return null;
                }
                    
                case BuiltInType.LocalizedText:
                {
                    LocalizedText ltext = (LocalizedText)value;

                    myPopup.Child = new SimpleValueEditCtrl(ltext.Text);
                    myPopup.IsOpen = true;
                    string text = ((SimpleValueEditCtrl)myPopup.Child).localValue.ToString();
                    if (text != null)
                    {
                        return new LocalizedText(text, ltext.Locale);
                    }

                    return null;
                }
            }

            return value;
        }
        
        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static string GetTargetIcon(Session session, ReferenceDescription reference)
        {
            return GetTargetIcon(session, reference.NodeClass, reference.TypeDefinition);
        }

        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static string GetTargetIcon(Session session, NodeClass nodeClass, ExpandedNodeId typeDefinitionId)
        { 
            // make sure the type definition is in the cache.
            INode typeDefinition = session.NodeCache.Find(typeDefinitionId);

            switch (nodeClass)
            {
                case NodeClass.Object:
                {                    
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, ObjectTypes.FolderType))
                    {
                        return "Folder";
                    }

                    return "Object";
                }
                    
                case NodeClass.Variable:
                {                    
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, VariableTypes.PropertyType))
                    {
                        return "Property";
                    }

                    return "Variable";
                }                   
            }

            return nodeClass.ToString();
        }

#region Private Methods
#endregion
    }
}
