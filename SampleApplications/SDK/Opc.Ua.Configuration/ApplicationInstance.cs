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
using System.Text;
using System.Reflection;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Net;

namespace Opc.Ua.Configuration
{
    public abstract class IApplicationMessageDlg
    {
        public abstract void Message(string text, Boolean ask=false);
        public abstract Task<bool> ShowAsync();
    }

    /// <summary>
    /// A class that install, configures and runs a UA application.
    /// </summary>
    public class ApplicationInstance
    {
        #region Ctors
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        public ApplicationInstance()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInstance"/> class.
        /// </summary>
        /// <param name="applicationConfiguration">The application configuration.</param>
        public ApplicationInstance(ApplicationConfiguration applicationConfiguration)
        {
            m_applicationConfiguration = applicationConfiguration;
        } 
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the name of the application.
        /// </summary>
        /// <value>The name of the application.</value>
        public string ApplicationName
        {
            get { return m_applicationName; }
            set { m_applicationName = value; }
        }

        /// <summary>
        /// Gets or sets the type of the application.
        /// </summary>
        /// <value>The type of the application.</value>
        public ApplicationType ApplicationType
        {
            get { return m_applicationType; }
            set { m_applicationType = value; }
        }

        /// <summary>
        /// Gets or sets the name of the config section containing the path to the application configuration file.
        /// </summary>
        /// <value>The name of the config section.</value>
        public string ConfigSectionName
        {
            get { return m_configSectionName; }
            set { m_configSectionName = value; }
        }

        /// <summary>
        /// Gets or sets the type of configuration file.
        /// </summary>
        /// <value>The type of configuration file.</value>
        public Type ConfigurationType
        {
            get { return m_configurationType; }
            set { m_configurationType = value; }
        }

        /// <summary>
        /// Gets or sets the installation configuration.
        /// </summary>
        /// <value>The installation configuration.</value>
        public InstalledApplication InstallConfig
        {
            get { return m_installConfig; }
            set { m_installConfig = value; }
        }

        /// <summary>
        /// Gets the server.
        /// </summary>
        /// <value>The server.</value>
        public ServerBase Server
        {
            get { return m_server; }
        }

        /// <summary>
        /// Gets the application configuration used when the Start() method was called.
        /// </summary>
        /// <value>The application configuration.</value>
        public ApplicationConfiguration ApplicationConfiguration
        {
            get { return m_applicationConfiguration; }
            set { m_applicationConfiguration = value; }
        }

        /// <summary>
        /// Gets or sets a flag that indicates whether the application will be set up for management with the GDS agent.
        /// </summary>
        /// <value>If true the application will not be visible to the GDS local agent after installation.</value>
        public bool NoGdsAgentAdmin { get; set; }

        public static IApplicationMessageDlg MessageDlg { get; set; }
        #endregion

        #region InstallConfig Handling
        /// <summary>
        /// Loads the installation configuration from a file.
        /// </summary>
        public InstalledApplication LoadInstallConfigFromFile(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException("filePath");
            
            Stream istrm = null;

            try
            {
                istrm = File.Open(filePath, FileMode.Open, FileAccess.Read);
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, e, "Could not open file: {0}", filePath);
            }

            return LoadInstallConfigFromStream(istrm);
        }

        /// <summary>
        /// Loads the installation configuration from an embedded resource.
        /// </summary>
        public InstalledApplication LoadInstallConfigFromResource(string resourcePath, Assembly assembly)
        {
            if (resourcePath == null) throw new ArgumentNullException("resourcePath");

            Stream istrm = assembly.GetManifestResourceStream(resourcePath);

            if (istrm == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not find resource file: {0}", resourcePath);
            }

            return LoadInstallConfigFromStream(istrm);
        }

        /// <summary>
        /// Loads the installation configuration from a stream.
        /// </summary>
        public InstalledApplication LoadInstallConfigFromStream(Stream istrm)
        {
            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(InstalledApplication));
                return (InstalledApplication)serializer.ReadObject(istrm);
                
            }
            catch (Exception e)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, e, "Could not parse install configuration.");
            }
        }

        /// <summary>
        /// Loads the installation configuration.
        /// </summary>
        /// <param name="configFile">The config file (may be null).</param>
        public virtual void LoadInstallConfig(string configFile)
        {
            // load configuration from command line.
            if (!String.IsNullOrEmpty(configFile))
            {
                InstallConfig = LoadInstallConfigFromFile(configFile);
            }

            // load it from a resource if not already loaded.
            else if (InstallConfig == null)
            {
                foreach (string resourcePath in this.GetType().GetTypeInfo().Assembly.GetManifestResourceNames())
                {
                    if (resourcePath.EndsWith("InstallConfig.xml"))
                    {
                        InstallConfig = LoadInstallConfigFromResource(resourcePath, this.GetType().GetTypeInfo().Assembly);
                        break;
                    }
                }

                if (InstallConfig == null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, "Could not load default installation config file.");
                }
            }

            // override the application name.
            if (String.IsNullOrEmpty(InstallConfig.ApplicationName))
            {
                InstallConfig.ApplicationName = ApplicationName;
            }
            else
            {
                ApplicationName = InstallConfig.ApplicationName;
            }

            // update fixed fields in the installation config.
            InstallConfig.ApplicationType = (Opc.Ua.Security.ApplicationType)(int)ApplicationType;
            InstallConfig.ExecutableFile = Directory.GetCurrentDirectory();

            if (InstallConfig.TraceConfiguration != null)
            {
                InstallConfig.TraceConfiguration.ApplySettings();
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Processes the command line.
        /// </summary>
        /// <returns>
        /// True if the arguments were processed; False otherwise.
        /// </returns>
        public bool ProcessCommandLine()
        {
            // ignore processing of command line
            return false;
        }

        /// <summary>
        /// Starts the UA server as a Windows Service.
        /// </summary>
        /// <param name="server">The server.</param>
        public void StartAsService(ServerBase server)
        {
            throw new NotImplementedException(".NetStandard Opc.Ua libraries do not support to start as a windows service");
        }

        /// <summary>
        /// Starts the UA server.
        /// </summary>
        /// <param name="server">The server.</param>
        public async Task Start(ServerBase server)
        {
            m_server = server;

            if (m_applicationConfiguration == null)
            {
                await LoadApplicationConfiguration(false);
            }

            if (m_applicationConfiguration.CertificateValidator != null)
            {
                m_applicationConfiguration.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;
            }

            server.Start(m_applicationConfiguration);
        }

        /// <summary>
        /// Stops the UA server.
        /// </summary>
        public void Stop()
        {
            m_server.Stop();
        }
        #endregion

        #region WindowsService Class
        /// <summary>
        /// Manages the interface between the UA server and the Windows SCM.
        /// </summary>
        protected class WindowsService
        {
            #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="WindowsService"/> class.
            /// </summary>
            /// <param name="server">The server.</param>
            /// <param name="configSectionName">Name of the config section.</param>
            /// <param name="applicationType">Type of the application.</param>
            /// <param name="configurationType">Type of the configuration.</param>
            public WindowsService(ServerBase server, string configSectionName, ApplicationType applicationType, Type configurationType)
            {
                m_server = server;
                m_configSectionName = configSectionName;
                m_applicationType = applicationType;
                m_configurationType = configurationType;
            }
            #endregion

            #region Private Methods
            /// <summary>
            /// Runs the service in a background thread.
            /// </summary>
            private async Task OnBackgroundStart(object state)
            {
                string filePath = null;
                ApplicationConfiguration configuration = null;

                try
                {
                    filePath = ApplicationConfiguration.GetFilePathFromAppConfig(m_configSectionName);
                    configuration = await ApplicationInstance.LoadAppConfig(false, filePath, m_applicationType, m_configurationType, true);
                }
                catch (Exception e)
                {
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadConfigurationError, "Could not load UA Service configuration file.\r\nPATH={0}", filePath);
                }

                try
                {
                    if (configuration.CertificateValidator != null)
                    {
                        ApplicationInstance applicationInstance = new ApplicationInstance(configuration);
                        configuration.CertificateValidator.CertificateValidation += applicationInstance.CertificateValidator_CertificateValidation;
                    }

                    m_server.Start(configuration);
                }
                catch (Exception e)
                {
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadConfigurationError, "Could not start UA Service.");
                    Utils.Trace((int)Utils.TraceMasks.Error, error.ToLongString());
                }
            }
            #endregion

            #region Private Fields
            private ServerBase m_server;
            private string m_configSectionName;
            private ApplicationType m_applicationType;
            private Type m_configurationType;
            #endregion
        }
        #endregion

        #region ArgumentDescription Class
        /// <summary>
        /// Stores the description of an argument.
        /// </summary>
        protected class ArgumentDescription
        {
            /// <summary>
            /// The argument name.
            /// </summary>
            public string Name;

            /// <summary>
            /// The argument description.
            /// </summary>
            public string Description;

            /// <summary>
            /// Whether the argument requires a value.
            /// </summary>
            public bool ValueRequired;

            /// <summary>
            /// Whether the argument allows a value.
            /// </summary>
            public bool ValueAllowed;

            /// <summary>
            /// Initializes a new instance of the <see cref="ArgumentDescription"/> class.
            /// </summary>
            /// <param name="name">The name.</param>
            /// <param name="valueRequired">if set to <c>true</c> a value is required.</param>
            /// <param name="valueAllowed">if set to <c>true</c> a value is allowed.</param>
            /// <param name="description">The description.</param>
            public ArgumentDescription(
                 string name,
                 bool valueRequired,
                 bool valueAllowed,
                 string description)
            {
                Name = name;
                ValueRequired = valueRequired;
                ValueAllowed = valueAllowed;
                Description = description;
            }
        }

        private static ArgumentDescription[] s_SupportedArguments = new ArgumentDescription[]
        {            
            new ArgumentDescription("/start", false, false, "Starts the application as a service (/start [/silent] [/configFile:<filepath>])."),
            new ArgumentDescription("/install", false, false, "Installs the application (/install [/silent] [/configFile:<filepath>])."),
            new ArgumentDescription("/uninstall", false, false, "Uninstalls the application (/uninstall [/silent] [/configFile:<filepath>])."),
            new ArgumentDescription("/silent", false, false, "Performs operations without prompting user to confirm or displaying errors."),
            new ArgumentDescription("/configFile", true, true, "Specifies the installation configuration file."),
        };
        #endregion

        #region Protected Methods
        /// <summary>
        /// Gets the descriptions for the supported arguments.
        /// </summary>
        protected virtual ArgumentDescription[] GetArgumentDescriptions()
        {
            return s_SupportedArguments;
        }

        /// <summary>
        /// Gets the help string.
        /// </summary>
        protected virtual string GetHelpString(ArgumentDescription[] commands)
        {
            StringBuilder text = new StringBuilder();
            text.Append("These are the supported arguments:\r\n");

            for (int ii = 0; ii < commands.Length; ii++)
            {
                ArgumentDescription command = commands[ii];

                text.Append("\r\n");

                if (command.ValueRequired)
                {
                    text.AppendFormat("{0}:<value> {1}", command.Name, command.Description);
                }
                else if (command.ValueAllowed)
                {
                    text.AppendFormat("{0}[:<value>] {1}", command.Name, command.Description);
                }
                else
                {
                    text.AppendFormat("{0} {1}", command.Name, command.Description);
                }
            }

            text.Append("\r\n");
            return text.ToString();
        }

        /// <summary>
        /// Validates the arguments.
        /// </summary>
        protected virtual string ValidateArguments(bool ignoreUnknownArguments, Dictionary<string, string> args)
        {
            ArgumentDescription[] commands = GetArgumentDescriptions();

            // check if help was requested.
            if (args.ContainsKey("/?"))
            {
                return GetHelpString(commands);
            }

            // validate the arguments.
            StringBuilder error = new StringBuilder();

            foreach (KeyValuePair<string,string> arg in args)
            {
                ArgumentDescription command = null;

                for (int ii = 0; ii < commands.Length; ii++)
                {
                    if (String.Compare(commands[ii].Name, arg.Key, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        command = commands[ii];
                        break;
                    }
                }

                if (command == null)
                {
                    if (!ignoreUnknownArguments)
                    {
                        if (error.Length > 0)
                        {
                            error.Append("\r\n");
                        }

                        error.AppendFormat("Unrecognized argument: {0}", arg.Key);
                    }

                    continue;
                }

                if (command.ValueRequired && String.IsNullOrEmpty(arg.Value))
                {
                    if (error.Length > 0)
                    {
                        error.Append("\r\n");
                    }

                    error.AppendFormat("{0} requires a value to be specified (syntax {0}:<value>).", arg.Key);
                    continue;
                }

                if (!command.ValueAllowed && !String.IsNullOrEmpty(arg.Value))
                {
                    if (error.Length > 0)
                    {
                        error.Append("\r\n");
                    }

                    error.AppendFormat("{0} does not allow a value to be specified.", arg.Key);
                    continue;
                }
            }

            // return any error text.
            return error.ToString();
        }

        /// <summary>
        /// Updates the application configuration with the values from the installation configuration.
        /// </summary>
        /// <param name="configuration">The configuration to update.</param>
        protected virtual async Task UpdateAppConfigWithInstallConfig(ApplicationConfiguration configuration)
        {
            // override the application name.
            if (InstallConfig.ApplicationName != null)
            {
                if (configuration.SecurityConfiguration != null && configuration.SecurityConfiguration.ApplicationCertificate != null)
                {
                    if (configuration.SecurityConfiguration.ApplicationCertificate.SubjectName == configuration.ApplicationName)
                    {
                        configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = InstallConfig.ApplicationName;
                    }
                }

                configuration.ApplicationName = InstallConfig.ApplicationName;
            }

            if (InstallConfig.ApplicationUri != null)
            {
                configuration.ApplicationUri = InstallConfig.ApplicationUri;
            }

            // replace localhost with the current machine name.
            if (configuration.ApplicationUri != null)
            {
                int index = configuration.ApplicationUri.IndexOf("localhost", StringComparison.OrdinalIgnoreCase);

                if (index != -1)
                {
                    StringBuilder buffer = new StringBuilder();
                    buffer.Append(configuration.ApplicationUri.Substring(0, index));
                    buffer.Append(Utils.GetHostName());
                    buffer.Append(configuration.ApplicationUri.Substring(index + "localhost".Length));
                    configuration.ApplicationUri = buffer.ToString();
                }
            }

            ServerBaseConfiguration serverConfiguration = null;

            if (configuration.ServerConfiguration != null)
            {
                serverConfiguration = configuration.ServerConfiguration;
            }
            else if (configuration.DiscoveryServerConfiguration != null)
            {
                serverConfiguration = configuration.DiscoveryServerConfiguration;
            }

            if (serverConfiguration != null)
            {
                if (InstallConfig.BaseAddresses != null && InstallConfig.BaseAddresses.Count > 0)
                {
                    Dictionary<string, string> addresses = new Dictionary<string, string>();
                    serverConfiguration.BaseAddresses.Clear();

                    for (int ii = 0; ii < InstallConfig.BaseAddresses.Count; ii++)
                    {
                        Uri url = Utils.ParseUri(InstallConfig.BaseAddresses[ii]);

                        if (url != null)
                        {
                            if (!addresses.ContainsKey(url.Scheme))
                            {
                                serverConfiguration.BaseAddresses.Add(url.ToString());
                                addresses.Add(url.Scheme, String.Empty);
                            }
                            else
                            {
                                serverConfiguration.AlternateBaseAddresses.Add(url.ToString());
                            }
                        }
                    }
                }

                if (InstallConfig.SecurityProfiles != null && InstallConfig.SecurityProfiles.Count > 0)
                {
                    ServerSecurityPolicyCollection securityPolicies = new ServerSecurityPolicyCollection();

                    for (int ii = 0; ii < InstallConfig.SecurityProfiles.Count; ii++)
                    {
                        for (int jj = 0; jj < serverConfiguration.SecurityPolicies.Count; jj++)
                        {
                            if (serverConfiguration.SecurityPolicies[jj].SecurityPolicyUri == InstallConfig.SecurityProfiles[ii].ProfileUri)
                            {
                                securityPolicies.Add(serverConfiguration.SecurityPolicies[jj]);
                            }
                        }
                    }

                    serverConfiguration.SecurityPolicies = securityPolicies;
                }
            }

            if (InstallConfig.ApplicationCertificate != null)
            {
                configuration.SecurityConfiguration.ApplicationCertificate.StoreType = InstallConfig.ApplicationCertificate.StoreType;
                configuration.SecurityConfiguration.ApplicationCertificate.StorePath = InstallConfig.ApplicationCertificate.StorePath;

                if (String.IsNullOrEmpty(InstallConfig.ApplicationCertificate.SubjectName))
                {
                    configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = InstallConfig.ApplicationCertificate.SubjectName;
                }
            }

            if (InstallConfig.RejectedCertificatesStore != null)
            {
                configuration.SecurityConfiguration.RejectedCertificateStore = Opc.Ua.Security.SecuredApplication.FromCertificateStoreIdentifier(InstallConfig.RejectedCertificatesStore);
            }

            if (InstallConfig.IssuerCertificateStore != null)
            {
                configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType = InstallConfig.IssuerCertificateStore.StoreType;
                configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath = InstallConfig.IssuerCertificateStore.StorePath;
                configuration.SecurityConfiguration.TrustedIssuerCertificates.ValidationOptions = (CertificateValidationOptions)(int)InstallConfig.IssuerCertificateStore.ValidationOptions;
            }

            if (InstallConfig.TrustedCertificateStore != null)
            {
                configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType = InstallConfig.TrustedCertificateStore.StoreType;
                configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = InstallConfig.TrustedCertificateStore.StorePath;
                configuration.SecurityConfiguration.TrustedPeerCertificates.ValidationOptions = (CertificateValidationOptions)(int)InstallConfig.TrustedCertificateStore.ValidationOptions;
            }

            await configuration.CertificateValidator.Update(configuration);
        }

        /// <summary>
        /// Installs the service.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs such be displayed.</param>
        /// <param name="args">Additional arguments provided on the command line.</param>
        protected virtual async Task Install(bool silent, Dictionary<string, string> args)
        {
            Utils.Trace(Utils.TraceMasks.Information, "Installing application.");

            // check the configuration.
            string filePath = Utils.GetAbsoluteFilePath(InstallConfig.ConfigurationFile, true, false, false);

            if (filePath == null)
            {
                Utils.Trace("WARNING: Could not load config file specified in the installation configuration: {0}", InstallConfig.ConfigurationFile);
                filePath = ApplicationConfiguration.GetFilePathFromAppConfig(ConfigSectionName);
                InstallConfig.ConfigurationFile = filePath;
            }

            ApplicationConfiguration configuration = await LoadAppConfig(silent, filePath, Opc.Ua.Security.SecuredApplication.FromApplicationType(InstallConfig.ApplicationType), ConfigurationType, false);

            if (configuration == null)
            {
                return;
            }

            // update the configuration.
            await UpdateAppConfigWithInstallConfig(configuration);
            ApplicationConfiguration = configuration;

            // update configuration with information form the install config.
            // check the certificate.
            X509Certificate2 certificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            if (certificate != null)
            {
                if (!silent)
                {
                    bool result = await CheckApplicationInstanceCertificate(configuration, certificate, silent, InstallConfig.MinimumKeySize);
                    if (!result)
                    {
                        certificate = null;
                    }
                }
            }

            if (certificate == null)
            {
                // create a new certificate.
                certificate = await CreateApplicationInstanceCertificate(configuration, InstallConfig.MinimumKeySize, InstallConfig.LifeTimeInMonths);
            }
            else
            {
                // ensure the certificate is trusted.
                if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
                {
                    await AddToTrustedStore(configuration, certificate);
                }
            }

            // update configuration file.
            ConfigUtils.UpdateConfigurationLocation(InstallConfig.ExecutableFile, InstallConfig.ConfigurationFile);

            try
            {
                // ensure the RawData does not get serialized.
                certificate = configuration.SecurityConfiguration.ApplicationCertificate.Certificate;

                configuration.SecurityConfiguration.ApplicationCertificate.Certificate = null;
                configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = certificate.Subject;
                configuration.SecurityConfiguration.ApplicationCertificate.Thumbprint = certificate.Thumbprint;

                configuration.SaveToFile(configuration.SourceFilePath);

                // restore the configuration.
                configuration.SecurityConfiguration.ApplicationCertificate.Certificate = certificate;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not save configuration file. FilePath={0}", configuration.SourceFilePath);
            }

            if (!NoGdsAgentAdmin)
            {
                try
                {
                    // install the GDS agent configuration file
                    string agentPath = Utils.GetAbsoluteDirectoryPath(
                        Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "GDS" + Path.DirectorySeparatorChar + "Applications",
                        false,
                        false,
                        true);

                    if (agentPath != null)
                    {
                        Opc.Ua.Security.SecuredApplication export = new Opc.Ua.Security.SecurityConfigurationManager().ReadConfiguration(configuration.SourceFilePath);
                        export.ExecutableFile = InstallConfig.ExecutableFile;

                        DataContractSerializer serializer = new DataContractSerializer(typeof(Opc.Ua.Security.SecuredApplication));

                        using (FileStream ostrm = File.Open(agentPath + Path.DirectorySeparatorChar + configuration.ApplicationName + ".xml", FileMode.Create))
                        {
                            serializer.WriteObject(ostrm, export);
                            Utils.Trace(Utils.TraceMasks.Information, "Created GDS agent configuration file.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Could not create GDS agent configuration file: {0}", e.Message);
                }
            }
        }

        /// <summary>
        /// Uninstalls the service.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs such be displayed.</param>
        /// <param name="args">Additional arguments provided on the command line.</param>
        protected virtual async Task Uninstall(bool silent, Dictionary<string, string> args)
        {
            // check the configuration.
            string filePath = Utils.GetAbsoluteFilePath(InstallConfig.ConfigurationFile, true, false, false);

            if (filePath == null)
            {
                Utils.Trace("WARNING: Could not load config file specified in the installation configuration: {0}", InstallConfig.ConfigurationFile);
                filePath = ApplicationConfiguration.GetFilePathFromAppConfig(ConfigSectionName);
                InstallConfig.ConfigurationFile = filePath;
            }

            ApplicationConfiguration configuration = await LoadAppConfig(silent, filePath, Opc.Ua.Security.SecuredApplication.FromApplicationType(InstallConfig.ApplicationType), ConfigurationType, false);
            ApplicationConfiguration = configuration;

            if (!NoGdsAgentAdmin)
            {
                try
                {
                    string agentPath = Utils.GetAbsoluteDirectoryPath(
                        Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "OPC Foundation" + Path.DirectorySeparatorChar + "GDS" + Path.DirectorySeparatorChar + "Applications",
                        false,
                        false,
                        false);

                    if (agentPath != null)
                    {
                        File.Delete(agentPath + Path.DirectorySeparatorChar + configuration.ApplicationName + ".xml");
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Could not create GDS agent configuration file: {0}", e.Message);
                }
            }
        }
        #endregion

        #region Static Methods
        public static ApplicationConfiguration FixupAppConfig(
            ApplicationConfiguration configuration)
        {
            configuration.ApplicationUri = Utils.ReplaceLocalhost(configuration.ApplicationUri);
            if (configuration.ServerConfiguration != null)
            {
                for (int i = 0; i < configuration.ServerConfiguration.BaseAddresses.Count; i++)
                {
                    configuration.ServerConfiguration.BaseAddresses[i] =
                        Utils.ReplaceLocalhost(configuration.ServerConfiguration.BaseAddresses[i]);
                }
            }
            return configuration;
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        public static async Task<ApplicationConfiguration> LoadAppConfig(
            bool silent,
            string filePath,
            ApplicationType applicationType,
            Type configurationType,
            bool applyTraceSettings)
        {
            Utils.Trace(Utils.TraceMasks.Information, "Loading application configuration file. {0}", filePath);

            try
            {
                // load the configuration file.
                ApplicationConfiguration configuration = await ApplicationConfiguration.Load(
                    new System.IO.FileInfo(filePath),
                    applicationType,
                    configurationType,
                    applyTraceSettings);

                if (configuration == null)
                {
                    return null;
                }

                return configuration;
            }
            catch (Exception e)
            {
                // warn user.
                if (!silent && MessageDlg != null)
                {
                    MessageDlg.Message("Load Application Configuration: " + e.Message);
                    await MessageDlg.ShowAsync();
                }

                Utils.Trace(e, "Could not load configuration file. {0}", filePath);
                return null;
            }
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadApplicationConfiguration(string filePath, bool silent)
        {
            ApplicationConfiguration configuration = await LoadAppConfig(silent, filePath, ApplicationType, ConfigurationType, true);

            if (configuration == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Could not load configuration file.");
            }

            m_applicationConfiguration = FixupAppConfig(configuration);

            return configuration;
        }

        /// <summary>
        /// Loads the application configuration.
        /// </summary>
        public async Task<ApplicationConfiguration> LoadApplicationConfiguration(bool silent)
        {
            string filePath = ApplicationConfiguration.GetFilePathFromAppConfig(ConfigSectionName);
            ApplicationConfiguration configuration = await LoadAppConfig(silent, filePath, ApplicationType, ConfigurationType, true);

            if (configuration == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Could not load configuration file.");
            }

            m_applicationConfiguration = FixupAppConfig(configuration);

            return m_applicationConfiguration;
        }

        /// <summary>
        /// Checks for a valid application instance certificate.
        /// </summary>
        /// <param name="silent">if set to <c>true</c> no dialogs will be displayed.</param>
        /// <param name="minimumKeySize">Minimum size of the key.</param>
        /// <param name="lifeTimeInMonths">The lifetime in months.</param>
        public async Task<bool> CheckApplicationInstanceCertificate(
            bool silent,
            ushort minimumKeySize,
            ushort lifeTimeInMonths = CertificateFactory.defaultLifeTime)
        {
            Utils.Trace(Utils.TraceMasks.Information, "Checking application instance certificate.");

            ApplicationConfiguration configuration = null;

            if (m_applicationConfiguration == null)
            {
                await LoadApplicationConfiguration(silent);
            }

            configuration = m_applicationConfiguration;
            bool certificateValid = false;

            // find the existing certificate.
            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            if (id == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Configuration file does not specify a certificate.");
            }

            X509Certificate2 certificate = await id.Find(true);

            // check that it is ok.
            if (certificate != null)
            {
                certificateValid = await CheckApplicationInstanceCertificate(configuration, certificate, silent, minimumKeySize);
            }
            else
            {
                // check for missing private key.
                certificate = await id.Find(false);

                if (certificate != null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Cannot access certificate private key. Subject={0}", certificate.Subject);
                }

                // check for missing thumbprint.
                if (!String.IsNullOrEmpty(id.Thumbprint))
                {
                    if (!String.IsNullOrEmpty(id.SubjectName))
                    {
                        CertificateIdentifier id2 = new CertificateIdentifier();
                        id2.StoreType = id.StoreType;
                        id2.StorePath = id.StorePath;
                        id2.SubjectName = id.SubjectName;

                        certificate = await id2.Find(true);
                    }

                    if (certificate != null)
                    {
                        string message = Utils.Format(
                            "Thumbprint was explicitly specified in the configuration." +
                            "\r\nAnother certificate with the same subject name was found." +
                            "\r\nUse it instead?\r\n" +
                            "\r\nRequested: {0}" +
                            "\r\nFound: {1}",
                            id.SubjectName,
                            certificate.Subject);

                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError, message);
                    }
                    else
                    {
                        string message = Utils.Format("Thumbprint was explicitly specified in the configuration. Cannot generate a new certificate.");
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError, message);
                    }
                }
            }

            if ((certificate == null) || !certificateValid)
            {
                certificate = await CreateApplicationInstanceCertificate(configuration, minimumKeySize, lifeTimeInMonths);

                if (certificate == null)
                {
                    string message = Utils.Format(
                        "There is no cert with subject {0} in the configuration." +
                        "\r\n Please generate a cert for your application,",
                        "\r\n then copy the new cert to this location:" +
                        "\r\n{1}",
                        id.SubjectName,
                        id.StorePath);
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, message);
                }
            }
            else
            {
                if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
                {
                    // ensure it is trusted.
                    await AddToTrustedStore(configuration, certificate);
                }
            }

            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            try
            {
                if (m_applicationConfiguration.SecurityConfiguration != null
                    && m_applicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates
                    && e.Error != null && e.Error.Code == StatusCodes.BadCertificateUntrusted)
                {
                    e.Accept = true;
                    Utils.Trace((int)Utils.TraceMasks.Security, "Automatically accepted certificate: {0}", e.Certificate.Subject);
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error accepting certificate.");
            }
        }

        /// <summary>
        /// Creates an application instance certificate if one does not already exist.
        /// </summary>
        private static async Task<bool> CheckApplicationInstanceCertificate(
            ApplicationConfiguration configuration,
            X509Certificate2 certificate,
            bool silent,
            ushort minimumKeySize)
        {
            if (certificate == null)
            {
                return false;
            }

            Utils.Trace(Utils.TraceMasks.Information, "Checking application instance certificate. {0}", certificate.Subject);

            // validate certificate.
            configuration.CertificateValidator.Validate(certificate);

            // check key size.
            if (minimumKeySize > certificate.GetRSAPublicKey().KeySize)
            {
                string message = Utils.Format(
                    "The key size ({0}) in the certificate is less than the minimum provided ({1}). Use certificate anyway?",
                    certificate.GetRSAPublicKey().KeySize,
                    minimumKeySize);

                if (!silent && MessageDlg!=null)
                {
                    MessageDlg.Message(message, true);
                    if (!await MessageDlg.ShowAsync())
                    {
                        return false;
                    }
                }
                else
                {
                    Utils.Trace(message);
                    return false;
                }
            }

            // check domains.
            if (configuration.ApplicationType != ApplicationType.Client)
            {
                if (!await CheckDomainsInCertificate(configuration, certificate, silent))
                {
                    return false;
                }
            }

            // check uri.
            string applicationUri = Utils.GetApplicationUriFromCertificate(certificate);

            if (String.IsNullOrEmpty(applicationUri))
            {
                string message = "The Application URI could not be read from the certificate. Use certificate anyway?";

                if (!silent && MessageDlg != null)
                {
                    MessageDlg.Message(message, true);
                    if (!await MessageDlg.ShowAsync())
                    {
                        return false;
                    }
                }
                else
                {
                    Utils.Trace(message);
                    return false;
                }
            }
            else
            {
                configuration.ApplicationUri = applicationUri;
            }

            // update configuration.
            configuration.SecurityConfiguration.ApplicationCertificate.Certificate = certificate;

            return true;
        }

        /// <summary>
        /// Checks that the domains in the server addresses match the domains in the certificates.
        /// </summary>
        private static async Task<bool> CheckDomainsInCertificate(
            ApplicationConfiguration configuration,
            X509Certificate2 certificate,
            bool silent)
        {
            Utils.Trace(Utils.TraceMasks.Information, "Checking domains in certificate. {0}", certificate.Subject);

            bool valid = true;
            IList<string> serverDomainNames = configuration.GetServerDomainNames();
            IList<string> certificateDomainNames = Utils.GetDomainsFromCertficate(certificate);

            // get computer name.
            string computerName = Utils.GetHostName();

            // get IP addresses.
            IPAddress[] addresses = null;

            for (int ii = 0; ii < serverDomainNames.Count; ii++)
            {
                if (Utils.FindStringIgnoreCase(certificateDomainNames, serverDomainNames[ii]))
                {
                    continue;
                }

                if (String.Compare(serverDomainNames[ii], "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (Utils.FindStringIgnoreCase(certificateDomainNames, computerName))
                    {
                        continue;
                    }

                    // check for aliases.
                    bool found = false;

                    // get IP addresses only if necessary.
                    if (addresses == null)
                    {
                        addresses = await Utils.GetHostAddresses(computerName);
                    }

                    // check for ip addresses.
                    for (int jj = 0; jj < addresses.Length; jj++)
                    {
                        if (Utils.FindStringIgnoreCase(certificateDomainNames, addresses[jj].ToString()))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        continue;
                    }
                }

                string message = Utils.Format(
                    "The server is configured to use domain '{0}' which does not appear in the certificate. Use certificate?",
                    serverDomainNames[ii]);

                valid = false;

                if (!silent && MessageDlg != null)
                {
                    MessageDlg.Message(message, true);
                    if (await MessageDlg.ShowAsync())
                    {
                        valid = true;
                        continue;
                    }
                }

                Utils.Trace(message);
                break;
            }

            return valid;
        }

        /// <summary>
        /// Creates the application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="keySize">Size of the key.</param>
        /// <param name="lifetimeInMonths">The lifetime in months.</param>
        /// <returns>The new certificate</returns>
        private static async Task<X509Certificate2> CreateApplicationInstanceCertificate(
            ApplicationConfiguration configuration,
            ushort minimumKeySize = CertificateFactory.defaultKeySize,
            ushort lifeTimeInMonths = CertificateFactory.defaultLifeTime
            )
        {
            Utils.Trace(Utils.TraceMasks.Information, "Creating application instance certificate.");

            // delete any existing certificate.
            await DeleteApplicationInstanceCertificate(configuration);

            CertificateIdentifier id = configuration.SecurityConfiguration.ApplicationCertificate;

            // get the domains from the configuration file.
            IList<string> serverDomainNames = configuration.GetServerDomainNames();

            if (serverDomainNames.Count == 0)
            {
                serverDomainNames.Add(Utils.GetHostName());
            }

            // ensure the certificate store directory exists.
            if (id.StoreType == CertificateStoreType.Directory)
            {
                Utils.GetAbsoluteDirectoryPath(id.StorePath, true, true, true);
            }

            X509Certificate2 certificate = CertificateFactory.CreateCertificate(
                id.StoreType,
                id.StorePath,
                null,
                configuration.ApplicationUri,
                configuration.ApplicationName,
                id.SubjectName,
                serverDomainNames,
                minimumKeySize,
                DateTime.UtcNow - TimeSpan.FromDays(1),
                lifeTimeInMonths,
                CertificateFactory.defaultHashSize,
                false,
                null,
                null
                );

            id.Certificate = certificate;

            // ensure the certificate is trusted.
            if (configuration.SecurityConfiguration.AddAppCertToTrustedStore)
            {
                await AddToTrustedStore(configuration, certificate);
            }

            await configuration.CertificateValidator.Update(configuration.SecurityConfiguration);

            Utils.Trace(Utils.TraceMasks.Information, "Certificate created. Thumbprint={0}", certificate.Thumbprint);

            // reload the certificate from disk.
            await configuration.SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null);

            return certificate;
        }

        /// <summary>
        /// Deletes an existing application instance certificate.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        private static async Task DeleteApplicationInstanceCertificate(ApplicationConfiguration configuration)
        {
            Utils.Trace(Utils.TraceMasks.Information, "Deleting application instance certificate.");

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

                using (ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore())
                {
                    await store.Delete(thumbprint);
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
        /// Adds the certificate to the Trusted Certificate Store
        /// </summary>
        /// <param name="configuration">The application's configuration which specifies the location of the TrustedStore.</param>
        /// <param name="certificate">The certificate to register.</param>
        private static async Task AddToTrustedStore(ApplicationConfiguration configuration, X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");

            string storePath = null;

            if (configuration != null && configuration.SecurityConfiguration != null && configuration.SecurityConfiguration.TrustedPeerCertificates != null)
            {
                storePath = configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath;
            }

            if (String.IsNullOrEmpty(storePath))
            {
                Utils.Trace(Utils.TraceMasks.Information, "WARNING: Trusted peer store not specified.");
                return;
            }

            try
            {
                ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();

                if (store == null)
                {
                    Utils.Trace("Could not open trusted peer store. StorePath={0}", storePath);
                    return;
                }

                try
                {
                    // check if it already exists.
                    X509Certificate2Collection existingCertificates = await store.FindByThumbprint(certificate.Thumbprint);

                    if (existingCertificates.Count > 0)
                    {
                        return;
                    }

                    Utils.Trace(Utils.TraceMasks.Information, "Adding certificate to trusted peer store. StorePath={0}", storePath);

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
            catch (Exception e)
            {
                Utils.Trace(e, "Could not add certificate to trusted peer store. StorePath={0}", storePath);
            }
        }
        #endregion

        #region Private Fields
        private string m_applicationName;
        private ApplicationType m_applicationType;
        private string m_configSectionName;
        private Type m_configurationType;
        private InstalledApplication m_installConfig;
        private ServerBase m_server;
        private ApplicationConfiguration m_applicationConfiguration;
        #endregion
    }
}
