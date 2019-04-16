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
using System.Globalization;
using Opc.Ua.Server;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Implements a basic UA Data Access Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// 
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the DataAccessServerNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    public partial class ComWrapperServer : StandardServer
    {
        #region Public Interface
        /// <summary>
        /// Returns the current server instance.
        /// </summary>
        public IServerInternal ServerInstance
        {
            get { return this.ServerInternal; }
        }

        /// <summary>
        /// Returns the UserNameValidator.
        /// </summary>
        public UserNameValidator UserNameValidator
        {
            get { return m_userNameValidator; }
        }

        #endregion

        #region Overridden Methods
        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new List<INodeManager>();
            m_availableLocales = new List<string>();

            // get the configuration for the wrapper.
            ComWrapperServerConfiguration wrapperConfiguration = configuration.ParseExtension<ComWrapperServerConfiguration>();

            if (wrapperConfiguration != null && wrapperConfiguration.WrappedServers != null)
            {
                // create a new node manager for each wrapped COM server.
                bool loadTypeModel = true;
                Dictionary<string, ComClientConfiguration> namespaceUris = new Dictionary<string, ComClientConfiguration>();

                foreach (ComClientConfiguration clientConfiguration in wrapperConfiguration.WrappedServers)
                {
                    // add the available locales.
                    if (clientConfiguration.AvailableLocales != null && clientConfiguration.AvailableLocales.Count > 0)
                    {
                        foreach (string locale in clientConfiguration.AvailableLocales)
                        {
                            try
                            {
                                CultureInfo culture = CultureInfo.GetCultureInfo(locale);

                                if (!m_availableLocales.Contains(culture.Name))
                                {
                                    m_availableLocales.Add(culture.Name);
                                }
                            }
                            catch (Exception e)
                            {
                                Utils.Trace(e, "Can't process an invalid locale: {0}.", locale);
                            }
                        }
                    }

                    string namespaceUri = clientConfiguration.ServerUrl;

                    if (clientConfiguration is ComDaClientConfiguration)
                    {
                        namespaceUri += "/DA";

                        if (namespaceUris.ContainsKey(namespaceUri))
                        {
                            Utils.Trace("COM server has already been wrapped {0}.", namespaceUri);
                            continue;
                        }

                        namespaceUris.Add(namespaceUri, clientConfiguration);

                        ComDaClientNodeManager manager = new ComDaClientNodeManager(
                            server,
                            namespaceUri,
                            (ComDaClientConfiguration)clientConfiguration,
                            loadTypeModel);

                        nodeManagers.Add(manager);
                        loadTypeModel = false;
                        continue;
                    }

                    if (clientConfiguration is ComAeClientConfiguration)
                    {
                        namespaceUri += "/AE";

                        if (namespaceUris.ContainsKey(namespaceUri))
                        {
                            Utils.Trace("COM server has already been wrapped {0}.", namespaceUri);
                            continue;
                        }

                        namespaceUris.Add(namespaceUri, clientConfiguration);

                        ComAeClientNodeManager manager = new ComAeClientNodeManager(
                            server,
                            namespaceUri,
                            (ComAeClientConfiguration)clientConfiguration,
                            loadTypeModel);

                        nodeManagers.Add(manager);
                        loadTypeModel = false;
                        continue;
                    }

                    if (clientConfiguration is ComHdaClientConfiguration)
                    {
                        namespaceUri += "/HDA";

                        if (namespaceUris.ContainsKey(namespaceUri))
                        {
                            Utils.Trace("COM server has already been wrapped {0}.", namespaceUri);
                            continue;
                        }

                        namespaceUris.Add(namespaceUri, clientConfiguration);

                        ComHdaClientNodeManager manager = new ComHdaClientNodeManager(
                            server,
                            namespaceUri,
                            (ComHdaClientConfiguration)clientConfiguration,
                            loadTypeModel);

                        nodeManagers.Add(manager);
                        loadTypeModel = false;
                        continue;
                    }
                }
            }

            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName = "OPC UA Quickstarts";
            properties.ProductUri = "http://opcfoundation.org/Quickstarts/ComDataAccessServer/v1.0";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            return properties;
        }

        /// <summary>
        /// Called after the node managers have started.
        /// </summary>
        protected override void OnNodeManagerStarted(IServerInternal server)
        {
            // check if wrapped server locales need to be added to the UA server locale list.
            if (m_availableLocales != null && m_availableLocales.Count > 0)
            {
                lock (ServerInstance.DiagnosticsNodeManager.Lock)
                {
                    // get the LocaleIdArray property.
                    BaseVariableState variable = ServerInstance.DiagnosticsNodeManager.Find(Opc.Ua.VariableIds.Server_ServerCapabilities_LocaleIdArray) as BaseVariableState;

                    if (variable != null)
                    {
                        List<string> locales = new List<string>();

                        // preserve any existing locales.
                        string[] existingLocales = variable.Value as string[];

                        if (existingLocales != null)
                        {
                            locales.AddRange(existingLocales);
                        }

                        // add locales from the wrapped servers.
                        foreach (string availableLocale in m_availableLocales)
                        {
                            if (!locales.Contains(availableLocale))
                            {
                                locales.Add(availableLocale);
                            }
                        }

                        // update the locale array.
                        variable.Value = locales.ToArray();
                    }
                }
            }

            m_userNameValidator = new UserNameValidator(Configuration.ApplicationName);

            base.OnNodeManagerStarted(server);
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by a client.
        /// </summary>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="result">The result.</param>
        protected override void OnApplicationCertificateError(byte[] clientCertificate, ServiceResult result)
        {
            throw new ServiceResultException(new ServiceResult(StatusCodes.BadCertificateUriInvalid));
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        /// <param name="server">The server.</param>
        protected override void OnServerStarted(IServerInternal server)
        {
            // verify session
            this.ServerInstance.SessionManager.ImpersonateUser += SessionManager_ImpersonateUser;
        }

        /// <summary>
        /// Check whether it is an acceptable session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="args">IdentityToken.</param>
        void SessionManager_ImpersonateUser(Session session, ImpersonateEventArgs args)
        {
            switch (args.UserTokenPolicy.TokenType)
            {
                case UserTokenType.UserName:

                    UserNameIdentityToken token = args.NewIdentity as UserNameIdentityToken;

                    if (!m_userNameValidator.Validate(token))
                    {   // Bad user access denied.
                        // construct translation object with default text.
                        TranslationInfo info = new TranslationInfo(
                            "InvalidUserInformation",
                            "en-US",
                            "Specified user information are not valid.  UserName='{0}'.",
                            token.UserName);

                        // create an exception with a vendor defined sub-code.
                        throw new ServiceResultException(new ServiceResult(
                            StatusCodes.BadUserAccessDenied,
                            "InvalidUserInformation",
                            "http://opcfoundation.org",
                            new LocalizedText(info)));
                    }
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region Private Fields
        private List<string> m_availableLocales;
        private UserNameValidator m_userNameValidator;
        #endregion
    }
}
