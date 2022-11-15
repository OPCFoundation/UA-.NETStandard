/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a ComPseudoServerDlg.
    /// </summary>
    public partial class ConfiguredServerDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        public ConfiguredServerDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_userIdentities = new Dictionary<string, UserIdentityToken>();
            m_statusObject = new StatusObject((int)StatusChannel.MaxStatusChannels);
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The possible encodings.
        /// </summary>
        private enum Encoding
        {
            Default,
            Xml,
            Binary
        }

        /// <summary>
        /// The type of status (for coloring the status textbox).
        /// </summary>
        private enum StatusType
        {
            Ok = 0,
            Warning = 1,
            Error = 2
        }

        /// <summary>
        /// The status channel inside the StatusObject.
        /// </summary>
        private enum StatusChannel
        {
            Discovery = 0,
            SelectedSecurityMode = 1,
            ApplicationType = 2,
            SelectedProtocol = 3,
            ApplicationUri = 4,
            DiscoveryURLs = 5,
            Server = 6,
            DifferentCertificate = 7,
            SecurityPolicyUri = 8,
            TransportProfileUri = 9,
            SelectedSecurityPolicy = 10,
            MaxStatusChannels = 11
        }

        /// <summary>
        /// Whether to override limits
        /// </summary>
        private enum UseDefaultLimits
        {
            Yes,
            No
        }

        /// <summary>
        /// This class merges multiple error/warning/status codes from multiple sources.
        /// Initialize it with the number of status channels and update "StatusChannel" accordingly.
        /// Provides a general view of all the statuses (joined texts, worst status).
        /// </summary>
        private class StatusObject
        {
            public StatusObject(int maxChannels)
            {
                m_maxChannels = maxChannels;
                m_statusTexts = new string[maxChannels];
                m_statusTypes = new StatusType[maxChannels];

                for (int i = 0; i < m_maxChannels; ++i)
                {
                    m_statusTexts[i] = String.Empty;
                    m_statusTypes[i] = StatusType.Ok;
                }
            }

            public String StatusString
            {
                get
                {
                    String status = String.Empty;

                    for (int i = 0; i < m_maxChannels; ++i)
                    {
                        if (!String.IsNullOrEmpty(m_statusTexts[i]))
                        {
                            if (!String.IsNullOrEmpty(status))
                            {
                                status += " | ";
                            }

                            status += m_statusTexts[i];
                        }
                    }

                    return status;
                }
            }

            public StatusType StatusType
            {
                get
                {
                    StatusType type = StatusType.Ok;

                    for (int i = 0; i < m_maxChannels; ++i)
                    {
                        if (m_statusTypes[i] > type)
                        {
                            type = m_statusTypes[i];
                        }
                    }

                    return type;
                }
            }

            public void SetStatus(StatusChannel channel, String text, StatusType type)
            {
                int intChannel = (int)channel;

                if ((intChannel >= 0) && (intChannel < m_maxChannels))
                {
                    m_statusTexts[intChannel] = text;
                    m_statusTypes[intChannel] = type;
                }
            }

            public void ClearStatus(StatusChannel channel)
            {
                int intChannel = (int)channel;

                if ((intChannel >= 0) && (intChannel < m_maxChannels))
                {
                    m_statusTexts[intChannel] = String.Empty;
                    m_statusTypes[intChannel] = StatusType.Ok;
                }
            }

            private int m_maxChannels;
            private String[] m_statusTexts;
            private StatusType[] m_statusTypes;
        }

        /// <summary>
        /// This class is used by the EndopintListLB (list box).
        /// Holds references to the received EndpointDescription and its MessageSecurityMode, SecurityPolicyUri, MessageSecurityMode and EncodingSupport.
        /// Also prepares a user-friendly text representation of all the endpoint-rellevant characteristics.
        /// The extracted EndpointDescription properties are used in selecting the right combo-box values when user clicks in the endpoint list box.
        /// </summary>
        private class EndpointDescriptionString
        {
            public EndpointDescriptionString(EndpointDescription endpointDescription)
            {
                m_endpointDescription = endpointDescription;
                m_protocol = new Protocol(endpointDescription);
                m_currentPolicy = SecurityPolicies.GetDisplayName(endpointDescription.SecurityPolicyUri);
                m_messageSecurityMode = endpointDescription.SecurityMode;

                switch (m_endpointDescription.EncodingSupport)
                {
                    case BinaryEncodingSupport.None:
                    {
                        m_encoding = Encoding.Xml;
                        break;
                    }

                    case BinaryEncodingSupport.Optional:
                    case BinaryEncodingSupport.Required:
                    {
                        m_encoding = Encoding.Binary;
                        break;
                    }
                }

                BuildEndpointDescription();
            }

            public EndpointDescription EndpointDescription
            {
                get
                {
                    return m_endpointDescription;
                }
            }

            public Protocol Protocol
            {
                get
                {
                    return m_protocol;
                }
            }

            public string CurrentPolicy
            {
                get
                {
                    return m_currentPolicy;
                }
            }

            public MessageSecurityMode MessageSecurityMode
            {
                get
                {
                    return m_messageSecurityMode;
                }
            }

            public Encoding Encoding
            {
                get
                {
                    return m_encoding;
                }
            }

            public override string ToString()
            {
                return m_stringRepresentation;
            }

            private void BuildEndpointDescription()
            {
                m_stringRepresentation = m_protocol.ToString() + " - ";
                m_stringRepresentation += m_endpointDescription.SecurityMode + " - ";
                m_stringRepresentation += SecurityPolicies.GetDisplayName(m_endpointDescription.SecurityPolicyUri) + " - ";

                switch (m_endpointDescription.EncodingSupport)
                {
                    case BinaryEncodingSupport.None:
                    {
                        m_stringRepresentation += Encoding.Xml;
                        break;
                    }

                    case BinaryEncodingSupport.Required:
                    {
                        m_stringRepresentation += Encoding.Binary;
                        break;
                    }

                    case BinaryEncodingSupport.Optional:
                    {
                        m_stringRepresentation += Encoding.Binary + "/" + Encoding.Xml;
                        break;
                    }
                }

            }

            private Protocol m_protocol;
            private EndpointDescription m_endpointDescription;
            private MessageSecurityMode m_messageSecurityMode;
            private string m_currentPolicy;
            private Encoding m_encoding;
            private string m_stringRepresentation;
        }

        private ConfiguredEndpoint m_endpoint;
        private EndpointDescription m_currentDescription;
        private EndpointDescriptionCollection m_availableEndpoints;
        private List<EndpointDescriptionString> m_availableEndpointsDescriptions;
        private int m_discoveryTimeout;
        private int m_discoverCount;
        private ApplicationConfiguration m_configuration;
        private bool m_updating;
        private bool m_selecting;
        private Dictionary<string, UserIdentityToken> m_userIdentities;
        private EndpointConfiguration m_endpointConfiguration;
        private bool m_discoverySucceeded;
        private Uri m_discoveryUrl;
        private bool m_showAllOptions;
        private StatusObject m_statusObject;
        #endregion

        #region Public Interface
        public EndpointDescriptionCollection AvailableEnpoints
        {
            get { return m_availableEndpoints; }
        }

        /// <summary>
        /// The timeout in milliseconds to use when discovering servers.
        /// </summary>
        [System.ComponentModel.DefaultValue(20000)]
        public int DiscoveryTimeout
        {
            get { return m_discoveryTimeout; }
            set { Interlocked.Exchange(ref m_discoveryTimeout, value); }
        }
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ConfiguredEndpoint ShowDialog(ApplicationDescription server, ApplicationConfiguration configuration)
        {
            if (server == null) throw new ArgumentNullException("server");

            m_configuration = configuration;

            // construct a list of available endpoint descriptions for the application.
            m_availableEndpoints = new EndpointDescriptionCollection();
            m_availableEndpointsDescriptions = new List<EndpointDescriptionString>();
            m_endpointConfiguration = EndpointConfiguration.Create(configuration);

            // create a default endpoint description.
            m_endpoint = null;
            m_currentDescription = null;

            // initializing the protocol will trigger an update to all other controls.
            InitializeProtocols(m_availableEndpoints);
            BuildEndpointDescriptionStrings(m_availableEndpoints);

            // discover endpoints in the background.
            m_discoverySucceeded = false;
            Interlocked.Increment(ref m_discoverCount);
            ThreadPool.QueueUserWorkItem(new WaitCallback(OnDiscoverEndpoints), server);

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_endpoint;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ConfiguredEndpoint ShowDialog(ConfiguredEndpoint endpoint, ApplicationConfiguration configuration)
        {
            if (endpoint == null) throw new ArgumentNullException("endpoint");

            m_endpoint = endpoint;
            m_configuration = configuration;

            // construct a list of available endpoint descriptions for the application.
            m_availableEndpoints = new EndpointDescriptionCollection();
            m_availableEndpointsDescriptions = new List<EndpointDescriptionString>();

            m_availableEndpoints.Add(endpoint.Description);
            m_currentDescription = endpoint.Description;
            m_endpointConfiguration = endpoint.Configuration;

            if (m_endpointConfiguration == null)
            {
                m_endpointConfiguration = EndpointConfiguration.Create(configuration);
            }

            if (endpoint.Collection != null)
            {
                foreach (ConfiguredEndpoint existingEndpoint in endpoint.Collection.Endpoints)
                {
                    if (existingEndpoint.Description.Server.ApplicationUri == endpoint.Description.Server.ApplicationUri)
                    {
                        m_availableEndpoints.Add(existingEndpoint.Description);
                    }
                }
            }

            BuildEndpointDescriptionStrings(m_availableEndpoints);

            UserTokenPolicy policy = m_endpoint.SelectedUserTokenPolicy;

            if (policy == null)
            {
                if (m_endpoint.Description.UserIdentityTokens.Count > 0)
                {
                    policy = m_endpoint.Description.UserIdentityTokens[0];
                }
            }

            if (policy != null)
            {
                UserTokenItem userTokenItem = new UserTokenItem(policy);

                if (policy.TokenType == UserTokenType.UserName && m_endpoint.UserIdentity is UserNameIdentityToken)
                {
                    m_userIdentities[userTokenItem.ToString()] = m_endpoint.UserIdentity;
                }

                if (policy.TokenType == UserTokenType.Certificate && m_endpoint.UserIdentity is X509IdentityToken)
                {
                    m_userIdentities[userTokenItem.ToString()] = m_endpoint.UserIdentity;
                }

                if (policy.TokenType == UserTokenType.IssuedToken && m_endpoint.UserIdentity is IssuedIdentityToken)
                {
                    m_userIdentities[userTokenItem.ToString()] = m_endpoint.UserIdentity;
                }
            }

            // initializing the protocol will trigger an update to all other controls.
            InitializeProtocols(m_availableEndpoints);

            // check if the current settings match the defaults.
            EndpointConfiguration defaultConfiguration = EndpointConfiguration.Create(configuration);

            // discover endpoints in the background.
            Interlocked.Increment(ref m_discoverCount);
            ThreadPool.QueueUserWorkItem(new WaitCallback(OnDiscoverEndpoints), m_endpoint.Description.Server);

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return m_endpoint;
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Creates the string representation of each EndpointDescription - to be used in the Endpoint Description List
        /// </summary>
        private void BuildEndpointDescriptionStrings(EndpointDescriptionCollection endpoints)
        {
            lock (m_availableEndpointsDescriptions)
            {
                m_availableEndpointsDescriptions.Clear();

                foreach (EndpointDescription endpoint in endpoints)
                {
                    m_availableEndpointsDescriptions.Add(new EndpointDescriptionString(endpoint));
                }

                InitializeEndpointList(m_availableEndpointsDescriptions);
            }
        }

        /// <summary>
        /// Returns true if the configuration is the same as the default.
        /// </summary>
        private bool SameAsDefaults(EndpointConfiguration defaultConfiguration, EndpointConfiguration currentConfiguration)
        {
            if (defaultConfiguration.ChannelLifetime != currentConfiguration.ChannelLifetime)
            {
                return false;
            }

            if (defaultConfiguration.MaxArrayLength != currentConfiguration.MaxArrayLength)
            {
                return false;
            }

            if (defaultConfiguration.MaxBufferSize != currentConfiguration.MaxBufferSize)
            {
                return false;
            }

            if (defaultConfiguration.MaxByteStringLength != currentConfiguration.MaxByteStringLength)
            {
                return false;
            }

            if (defaultConfiguration.MaxMessageSize != currentConfiguration.MaxMessageSize)
            {
                return false;
            }

            if (defaultConfiguration.MaxStringLength != currentConfiguration.MaxStringLength)
            {
                return false;
            }

            if (defaultConfiguration.OperationTimeout != currentConfiguration.OperationTimeout)
            {
                return false;
            }

            if (defaultConfiguration.SecurityTokenLifetime != currentConfiguration.SecurityTokenLifetime)
            {
                return false;
            }

            if (defaultConfiguration.UseBinaryEncoding != currentConfiguration.UseBinaryEncoding)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds the best match for the current protocol and security selections.
        /// </summary>
        private EndpointDescription FindBestEndpointDescription(EndpointDescriptionCollection endpoints)
        {
            // filter by the current protocol.
            Protocol currentProtocol = (Protocol)ProtocolCB.SelectedItem;

            // filter by the current security mode.
            MessageSecurityMode currentMode = MessageSecurityMode.None;

            if (SecurityModeCB.SelectedIndex != -1)
            {
                currentMode = (MessageSecurityMode)SecurityModeCB.SelectedItem;
            }

            // filter by the current security policy.
            string currentPolicy = (string)SecurityPolicyCB.SelectedItem;

            // find all matching descriptions.      
            EndpointDescriptionCollection matches = new EndpointDescriptionCollection();

            if (endpoints != null)
            {
                foreach (EndpointDescription endpoint in endpoints)
                {
                    Uri url = Utils.ParseUri(endpoint.EndpointUrl);

                    if (url == null)
                    {
                        continue;
                    }

                    if ((currentProtocol != null) && (!currentProtocol.Matches(url)))
                    {
                        continue;
                    }

                    if (currentMode != endpoint.SecurityMode)
                    {
                        continue;
                    }

                    if (currentPolicy != SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri))
                    {
                        continue;
                    }

                    matches.Add(endpoint);
                }
            }

            // check for no matches.
            if (matches.Count == 0)
            {
                return null;
            }

            // check for single match.
            if (matches.Count == 1)
            {
                return matches[0];
            }

            // choose highest priority.
            EndpointDescription bestMatch = matches[0];

            for (int ii = 1; ii < matches.Count; ii++)
            {
                if (bestMatch.SecurityLevel < matches[ii].SecurityLevel)
                {
                    bestMatch = matches[ii];
                }
            }

            return bestMatch;
        }

        private class Protocol
        {
            public Uri Url;
            public string Profile;

            public Protocol(string url)
            {
                Url = Utils.ParseUri(url);
            }

            public Protocol(EndpointDescription url)
            {
                Url = null;

                if (url != null)
                {
                    Url = Utils.ParseUri(url.EndpointUrl);

                    if ((Url != null) && (Url.Scheme == Utils.UriSchemeHttps))
                    {
                        switch (url.TransportProfileUri)
                        {
                            case Profiles.HttpsBinaryTransport:
                            {
                                Profile = "REST";
                                break;
                            }
                        }
                    }
                }
            }

            public bool Matches(Uri url)
            {
                if (url == null || Url == null)
                {
                    return false;
                }

                if (url.Scheme != Url.Scheme)
                {
                    return false;
                }

                if (url.DnsSafeHost != Url.DnsSafeHost)
                {
                    return false;
                }

                if (url.Port != Url.Port)
                {
                    return false;
                }

                return true;
            }

            public override string ToString()
            {
                if (Url == null)
                {
                    return String.Empty;
                }

                StringBuilder builder = new StringBuilder();
                builder.Append(Url.Scheme);

                if (!String.IsNullOrEmpty(Profile))
                {
                    builder.Append(" ");
                    builder.Append(Profile);
                }

                builder.Append(" [");
                builder.Append(Url.DnsSafeHost);

                if (Url.Port != -1)
                {
                    builder.Append(":");
                    builder.Append(Url.Port);
                }

                builder.Append("]");

                return builder.ToString();
            }
        }

        /// <summary>
        /// Initializes the protocol dropdown.
        /// </summary>
        private void InitializeProtocols(EndpointDescriptionCollection endpoints)
        {
            // preserve the existing value.
            Protocol currentProtocol = (Protocol)ProtocolCB.SelectedItem;

            ProtocolCB.Items.Clear();

            // set all available protocols.
            if (m_showAllOptions)
            {
                ProtocolCB.Items.Add(new Protocol("http://localhost"));
                ProtocolCB.Items.Add(new Protocol("https://localhost"));
                ProtocolCB.Items.Add(new Protocol("opc.tcp://localhost"));
            }

            // find all unique protocols.
            else
            {
                if (endpoints != null)
                {
                    foreach (EndpointDescription endpoint in endpoints)
                    {
                        Uri url = Utils.ParseUri(endpoint.EndpointUrl);

                        if (url != null)
                        {
                            bool found = false;

                            for (int ii = 0; ii < ProtocolCB.Items.Count; ii++)
                            {
                                if (((Protocol)ProtocolCB.Items[ii]).Matches(url))
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                ProtocolCB.Items.Add(new Protocol(endpoint));
                            }
                        }
                    }
                }

                // add at least one protocol.
                if (ProtocolCB.Items.Count == 0)
                {
                    ProtocolCB.Items.Add(new Protocol("opc.tcp://localhost"));
                }
            }

            // set the current value.
            int index = 0;

            if (currentProtocol != null)
            {
                index = 0;

                for (int ii = 0; ii < ProtocolCB.Items.Count; ii++)
                {
                    if (((Protocol)ProtocolCB.Items[ii]).Matches(currentProtocol.Url))
                    {
                        index = ii;
                        break;
                    }
                }
            }

            ProtocolCB.SelectedIndex = index;
        }

        /// <summary>
        /// Initializes the security modes dropdown.
        /// </summary>
        private void InitializeSecurityModes(EndpointDescriptionCollection endpoints)
        {
            // filter by the current protocol.
            Protocol currentProtocol = (Protocol)ProtocolCB.SelectedItem;

            // preserve the existing value.
            MessageSecurityMode currentMode = MessageSecurityMode.None;

            if (SecurityModeCB.SelectedIndex != -1)
            {
                currentMode = (MessageSecurityMode)SecurityModeCB.SelectedItem;
            }

            SecurityModeCB.Items.Clear();

            // set all available security modes.
            if (m_showAllOptions)
            {
                SecurityModeCB.Items.Add(MessageSecurityMode.None);
                SecurityModeCB.Items.Add(MessageSecurityMode.Sign);
                SecurityModeCB.Items.Add(MessageSecurityMode.SignAndEncrypt);
            }

            // find all unique security modes.
            else
            {
                if (endpoints != null)
                {
                    foreach (EndpointDescription endpoint in endpoints)
                    {
                        Uri url = Utils.ParseUri(endpoint.EndpointUrl);

                        if ((url != null) && (currentProtocol != null))
                        {
                            if (!currentProtocol.Matches(url))
                            {
                                continue;
                            }

                            if (!SecurityModeCB.Items.Contains(endpoint.SecurityMode))
                            {
                                SecurityModeCB.Items.Add(endpoint.SecurityMode);
                            }
                        }
                    }
                }

                // add at least one policy.
                if (SecurityModeCB.Items.Count == 0)
                {
                    SecurityModeCB.Items.Add(MessageSecurityMode.None);
                }
            }

            // set the current value.
            int index = SecurityModeCB.Items.IndexOf(currentMode);

            if (index == -1)
            {
                index = 0;
            }

            SecurityModeCB.SelectedIndex = index;
        }

        /// <summary>
        /// Initializes the security policies dropdown.
        /// </summary>
        private void InitializeSecurityPolicies(EndpointDescriptionCollection endpoints)
        {
            // filter by the current protocol.
            Protocol currentProtocol = (Protocol)ProtocolCB.SelectedItem;

            // filter by the current security mode.
            MessageSecurityMode currentMode = MessageSecurityMode.None;

            if (SecurityModeCB.SelectedIndex != -1)
            {
                currentMode = (MessageSecurityMode)SecurityModeCB.SelectedItem;
            }

            // preserve the existing value.
            string currentPolicy = (string)SecurityPolicyCB.SelectedItem;

            SecurityPolicyCB.Items.Clear();

            // set all available security policies.
            if (m_showAllOptions)
            {
                var securityPolicies = SecurityPolicies.GetDisplayNames();
                foreach (var policy in securityPolicies)
                {
                    SecurityPolicyCB.Items.Add(policy);
                }
            }

            // find all unique security policies.    
            else
            {
                if (endpoints != null)
                {
                    foreach (EndpointDescription endpoint in endpoints)
                    {
                        Uri url = Utils.ParseUri(endpoint.EndpointUrl);

                        if ((url != null) && (currentProtocol != null))
                        {
                            if (!currentProtocol.Matches(url))
                            {
                                continue;
                            }

                            if (currentMode != endpoint.SecurityMode)
                            {
                                continue;
                            }

                            string policyName = SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri);

                            if (policyName != null)
                            {
                                int existingIndex = SecurityPolicyCB.FindStringExact(policyName);

                                if (existingIndex == -1)
                                {
                                    SecurityPolicyCB.Items.Add(policyName);
                                }
                            }
                        }
                    }
                }
            }

            // add at least one policy.
            if (SecurityPolicyCB.Items.Count == 0)
            {
                SecurityPolicyCB.Items.Add(SecurityPolicies.GetDisplayName(SecurityPolicies.None));
            }

            // set the current value.
            int index = 0;

            if (!String.IsNullOrEmpty(currentPolicy))
            {
                index = SecurityPolicyCB.FindStringExact(currentPolicy);

                if (index == -1)
                {
                    index = 0;
                }
            }

            SecurityPolicyCB.SelectedIndex = index;
        }

        /// <summary>
        /// Initializes the message encodings dropdown.
        /// </summary>
        private void InitializeEncodings(EndpointDescriptionCollection endpoints, EndpointDescription endpoint)
        {
            // preserve the existing value.
            Encoding currentEncoding = Encoding.Default;

            if (EncodingCB.SelectedIndex != -1)
            {
                currentEncoding = (Encoding)EncodingCB.SelectedItem;
            }

            EncodingCB.Items.Clear();

            if (endpoint != null)
            {
                Protocol protocol = new Protocol(endpoint);
                String securityPolicy = SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri);

                foreach (EndpointDescription endpointDescription in endpoints)
                {
                    if ((protocol.Matches(Utils.ParseUri(endpointDescription.EndpointUrl))) &&
                        (endpoint.SecurityMode == endpointDescription.SecurityMode) &&
                        (securityPolicy == SecurityPolicies.GetDisplayName(endpointDescription.SecurityPolicyUri)))
                    {
                        switch (endpointDescription.EncodingSupport)
                        {
                            case BinaryEncodingSupport.None:
                            {
                                if (!EncodingCB.Items.Contains(Encoding.Xml))
                                {
                                    EncodingCB.Items.Add(Encoding.Xml);
                                }
                                break;
                            }

                            case BinaryEncodingSupport.Required:
                            {
                                if (!EncodingCB.Items.Contains(Encoding.Binary))
                                {
                                    EncodingCB.Items.Add(Encoding.Binary);
                                }
                                break;
                            }

                            case BinaryEncodingSupport.Optional:
                            {
                                if (!EncodingCB.Items.Contains(Encoding.Binary))
                                {
                                    EncodingCB.Items.Add(Encoding.Binary);
                                }
                                if (!EncodingCB.Items.Contains(Encoding.Xml))
                                {
                                    EncodingCB.Items.Add(Encoding.Xml);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // add at least one encoding.
            if (EncodingCB.Items.Count == 0)
            {
                EncodingCB.Items.Add(Encoding.Default);
            }

            // set the current value.
            int index = EncodingCB.Items.IndexOf(currentEncoding);

            if (index == -1)
            {
                index = 0;
            }

            EncodingCB.SelectedIndex = index;
        }

        private class UserTokenItem
        {
            public UserTokenPolicy Policy;

            public UserTokenItem(UserTokenPolicy policy)
            {
                Policy = policy;
            }

            public UserTokenItem(UserTokenType tokenType)
            {
                Policy = new UserTokenPolicy(tokenType);
            }

            public override string ToString()
            {
                if (Policy != null)
                {
                    if (String.IsNullOrEmpty(Policy.PolicyId))
                    {
                        return Policy.TokenType.ToString();
                    }

                    return Utils.Format("{0} [{1}]", Policy.TokenType, Policy.PolicyId);
                }

                return UserTokenType.Anonymous.ToString();
            }
        }

        /// <summary>
        /// Initializes the endpoint list control.
        /// </summary>
        private void InitializeEndpointList(List<EndpointDescriptionString> endpoints)
        {
            EndpointListLB.Items.Clear();

            foreach (EndpointDescriptionString endpointString in endpoints)
            {
                EndpointListLB.Items.Add(endpointString);
            }
        }

        private void SelectCorrespondingEndpointFromList(EndpointDescription endpoint)
        {
            if (!m_selecting)
            {
                int index = -1;

                // try to match endpoint description id
                if (endpoint != null)
                {
                    for (int ii = 0; ii < EndpointListLB.Items.Count; ii++)
                    {
                        if (endpoint == ((EndpointDescriptionString)EndpointListLB.Items[ii]).EndpointDescription)
                        {
                            index = ii;
                            break;
                        }
                    }
                }

                EndpointListLB.SelectedIndex = index;
            }
        }

        /// <summary>
        /// Attempts fetch the list of servers from the discovery server.
        /// </summary>
        private void OnDiscoverEndpoints(object state)
        {
            int discoverCount = m_discoverCount;

            // do nothing if a valid list is not provided.
            ApplicationDescription server = state as ApplicationDescription;

            if (server == null)
            {
                return;

            }

            OnUpdateStatus(new Tuple<String, StatusType>("Attempting to read latest configuration options from server.", StatusType.Ok));

            String discoveryMessage = String.Empty;

            // process each url.
            foreach (string discoveryUrl in server.DiscoveryUrls)
            {
                Uri url = Utils.ParseUri(discoveryUrl);

                if (url != null)
                {
                    if (DiscoverEndpoints(url, out discoveryMessage))
                    {
                        m_discoverySucceeded = true;
                        m_discoveryUrl = url;
                        OnUpdateStatus(new Tuple<String, StatusType>("Configuration options are up to date.", StatusType.Ok));
                        return;
                    }

                    // check if another discover operation has started.
                    if (discoverCount != m_discoverCount)
                    {
                        return;
                    }
                }
            }

            OnUpdateEndpoints(m_availableEndpoints);
            OnUpdateStatus(new Tuple<String, StatusType>("Warning: Configuration options may not be correct because the server is not available (" + discoveryMessage + ").", StatusType.Warning));
        }

        /// <summary>
        /// Fetches the servers from the discovery server.
        /// </summary>
        private bool DiscoverEndpoints(Uri discoveryUrl, out String message)
        {
            // use a short timeout.
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            endpointConfiguration.OperationTimeout = m_discoveryTimeout;

            DiscoveryClient client = DiscoveryClient.Create(
                discoveryUrl,
                EndpointConfiguration.Create(m_configuration),
                m_configuration);

            try
            {
                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                OnUpdateEndpoints(endpoints);
                message = String.Empty;
                return true;
            }
            catch (Exception e)
            {
                Utils.Trace("Could not fetch endpoints from url: {0}. Reason={1}", discoveryUrl, e.Message);
                message = e.Message;
                return false;
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Updates the status displayed in the dialog.
        /// </summary>
        private void OnUpdateStatus(object status)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new WaitCallback(OnUpdateStatus), status);
                return;
            }

            Tuple<String, StatusType> statusTuple = status as Tuple<String, StatusType>;
            m_statusObject.SetStatus(StatusChannel.Discovery, statusTuple.Item1, statusTuple.Item2);
            UpdateStatus();
        }

        /// <summary>
        /// Updates the list of servers displayed in the control.
        /// </summary>
        private void OnUpdateEndpoints(object state)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new WaitCallback(OnUpdateEndpoints), state);
                return;
            }

            try
            {
                // get the updated descriptions.
                EndpointDescriptionCollection endpoints = state as EndpointDescriptionCollection;

                if (endpoints == null)
                {
                    m_showAllOptions = true;
                    InitializeProtocols(m_availableEndpoints);
                }

                else
                {
                    m_showAllOptions = false;

                    m_availableEndpoints = endpoints;
                    BuildEndpointDescriptionStrings(m_availableEndpoints);

                    if (endpoints.Count > 0)
                    {
                        m_currentDescription = endpoints[0];
                    }

                    // initializing the protocol will trigger an update to all other controls.
                    InitializeProtocols(m_availableEndpoints);

                    // select the best security mode.
                    MessageSecurityMode bestMode = MessageSecurityMode.Invalid;

                    foreach (MessageSecurityMode securityMode in SecurityModeCB.Items)
                    {
                        if (securityMode > bestMode)
                        {
                            bestMode = securityMode;
                        }
                    }

                    SecurityModeCB.SelectedItem = bestMode;

                    // select the best encoding.
                    Encoding bestEncoding = Encoding.Default;

                    foreach (Encoding encoding in EncodingCB.Items)
                    {
                        if (encoding > bestEncoding)
                        {
                            bestEncoding = encoding;
                        }
                    }

                    EncodingCB.SelectedItem = bestEncoding;
                }

                if (m_endpoint != null)
                {
                    Uri url = m_endpoint.EndpointUrl;

                    foreach (Protocol protocol in ProtocolCB.Items)
                    {
                        if (protocol.Matches(url))
                        {
                            ProtocolCB.SelectedItem = protocol;
                            break;
                        }
                    }

                    foreach (MessageSecurityMode securityMode in SecurityModeCB.Items)
                    {
                        if (securityMode == m_endpoint.Description.SecurityMode)
                        {
                            SecurityModeCB.SelectedItem = securityMode;
                            break;
                        }
                    }

                    foreach (string securityPolicy in SecurityPolicyCB.Items)
                    {
                        if (securityPolicy == m_endpoint.Description.SecurityPolicyUri)
                        {
                            SecurityPolicyCB.SelectedItem = securityPolicy;
                            break;
                        }
                    }

                    foreach (Encoding encoding in EncodingCB.Items)
                    {
                        if (encoding == Encoding.Binary && m_endpoint.Configuration.UseBinaryEncoding)
                        {
                            EncodingCB.SelectedItem = encoding;
                            break;
                        }

                        if (encoding == Encoding.Xml && !m_endpoint.Configuration.UseBinaryEncoding)
                        {
                            EncodingCB.SelectedItem = encoding;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating endpoints.");
            }
        }

        /// <summary>
        /// Creates the endpoint description from current selections.
        /// </summary>
        private EndpointDescription CreateDescriptionFromSelections()
        {
            Protocol currentProtocol = (Protocol)ProtocolCB.SelectedItem;

            EndpointDescription endpoint = null;

            for (int ii = 0; ii < m_availableEndpoints.Count; ii++)
            {
                Uri url = Utils.ParseUri(m_availableEndpoints[ii].EndpointUrl);

                if (url == null)
                {
                    continue;
                }

                if (endpoint == null)
                {
                    endpoint = m_availableEndpoints[ii];
                }

                if (currentProtocol.Matches(url))
                {
                    endpoint = m_availableEndpoints[ii];
                    break;
                }
            }

            UriBuilder builder = null;
            string scheme = Utils.UriSchemeOpcTcp;

            if (currentProtocol != null && currentProtocol.Url != null)
            {
                scheme = currentProtocol.Url.Scheme;
            }

            if (endpoint == null)
            {
                builder = new UriBuilder();
                builder.Host = "localhost";

                if (scheme == Utils.UriSchemeOpcTcp)
                {
                    builder.Port = Utils.UaTcpDefaultPort;
                }
            }
            else
            {
                builder = new UriBuilder(endpoint.EndpointUrl);
            }

            builder.Scheme = scheme;

            endpoint = new EndpointDescription();
            endpoint.EndpointUrl = builder.ToString();
            endpoint.SecurityMode = (MessageSecurityMode)SecurityModeCB.SelectedItem;
            endpoint.SecurityPolicyUri = SecurityPolicies.GetUri((string)SecurityPolicyCB.SelectedItem);
            endpoint.Server.ApplicationName = endpoint.EndpointUrl;
            endpoint.Server.ApplicationType = ApplicationType.Server;
            endpoint.Server.ApplicationUri = endpoint.EndpointUrl;

            return endpoint;
        }
        #endregion

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                // check that discover has completed.
                if (!m_discoverySucceeded)
                {
                    DialogResult result = MessageBox.Show(
                        "Endpoint information may be out of date because the discovery process has not completed. Continue anyways?",
                        this.Text,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result != DialogResult.Yes)
                    {
                        return;
                    }
                }

                EndpointConfiguration configuration = m_endpointConfiguration;

                if (configuration == null)
                {
                    configuration = EndpointConfiguration.Create(m_configuration);
                }

                if (m_currentDescription == null)
                {
                    m_currentDescription = CreateDescriptionFromSelections();
                }

                // the discovery endpoint should always be on the same machine as the server.
                // if there is a mismatch it is likely because the server has multiple addresses
                // and was not configured to return the current address to the client.
                // The code automatically updates the domain in the url. 
                Uri endpointUrl = Utils.ParseUri(m_currentDescription.EndpointUrl);

                if (m_discoverySucceeded)
                {
                    if (!Utils.AreDomainsEqual(endpointUrl, m_discoveryUrl))
                    {
                        UriBuilder url = new UriBuilder(endpointUrl);

                        url.Host = m_discoveryUrl.DnsSafeHost;

                        if (url.Scheme == m_discoveryUrl.Scheme)
                        {
                            url.Port = m_discoveryUrl.Port;
                        }

                        endpointUrl = url.Uri;

                        m_currentDescription.EndpointUrl = endpointUrl.ToString();
                    }
                }

                // set the encoding.
                Encoding encoding = (Encoding)EncodingCB.SelectedItem;
                configuration.UseBinaryEncoding = encoding != Encoding.Xml;

                if (m_endpoint == null)
                {
                    m_endpoint = new ConfiguredEndpoint(null, m_currentDescription, configuration);
                }
                else
                {
                    m_endpoint.Update(m_currentDescription);
                    m_endpoint.Update(configuration);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void ProtocolCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                InitializeSecurityModes(m_availableEndpoints);

                if (!m_updating)
                {
                    try
                    {
                        m_updating = true;

                        // update current description.
                        m_currentDescription = FindBestEndpointDescription(m_availableEndpoints);

                        InitializeEncodings(m_availableEndpoints, m_currentDescription);
                        SelectCorrespondingEndpointFromList(m_currentDescription);
                    }
                    finally
                    {
                        m_updating = false;
                    }
                }

                if (ProtocolCB.SelectedItem != null)
                {
                    if (((Protocol)ProtocolCB.SelectedItem).Url.DnsSafeHost != m_endpoint.EndpointUrl.DnsSafeHost)
                    {
                        m_statusObject.SetStatus(StatusChannel.SelectedProtocol, "Warning: Selected Endpoint hostname is different than initial hostname.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.SelectedProtocol);
                    }
                }
                else
                {
                    m_statusObject.SetStatus(StatusChannel.SelectedProtocol, "Error: Selected Protocol is invalid.", StatusType.Warning);
                }

                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SecurityModeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                InitializeSecurityPolicies(m_availableEndpoints);

                if (!m_updating)
                {
                    try
                    {
                        m_updating = true;

                        // update current description.
                        m_currentDescription = FindBestEndpointDescription(m_availableEndpoints);

                        InitializeEncodings(m_availableEndpoints, m_currentDescription);
                        SelectCorrespondingEndpointFromList(m_currentDescription);
                    }
                    finally
                    {
                        m_updating = false;
                    }
                }

                if (SecurityModeCB.SelectedItem != null)
                {
                    if ((((MessageSecurityMode)SecurityModeCB.SelectedItem) == MessageSecurityMode.None) &&
                        (ProtocolCB.SelectedItem != null) && (((Protocol)ProtocolCB.SelectedItem).ToString().IndexOf("https") != 0))
                    {
                        m_statusObject.SetStatus(StatusChannel.SelectedSecurityMode, "Warning: Selected Endpoint has no security.", StatusType.Warning);
                    }
                    else if (((MessageSecurityMode)SecurityModeCB.SelectedItem) == MessageSecurityMode.Invalid)
                    {
                        m_statusObject.SetStatus(StatusChannel.SelectedSecurityMode, "Error: Selected Endpoint Security Mode is unsupported.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.SelectedSecurityMode);
                    }
                }
                else
                {
                    m_statusObject.SetStatus(StatusChannel.SelectedSecurityMode, "Error: Selected Endpoint Security Mode is invalid.", StatusType.Warning);
                }

                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SecurityPolicyCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (!m_updating)
                {
                    try
                    {
                        m_updating = true;

                        // update current description.
                        m_currentDescription = FindBestEndpointDescription(m_availableEndpoints);

                        InitializeEncodings(m_availableEndpoints, m_currentDescription);
                        SelectCorrespondingEndpointFromList(m_currentDescription);
                    }
                    finally
                    {
                        m_updating = false;
                    }
                }

                if (SecurityPolicyCB.SelectedItem != null)
                {
                    m_statusObject.ClearStatus(StatusChannel.SelectedSecurityPolicy);
                }
                else
                {
                    m_statusObject.SetStatus(StatusChannel.SelectedSecurityPolicy, "Error: Selected Security Policy is invalid.", StatusType.Warning);
                }

                UpdateStatus();

            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void EndpointListLB_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!m_updating)
            {
                try
                {
                    m_updating = true;
                    m_selecting = true;

                    int selectedIndex = EndpointListLB.SelectedIndex;

                    if (selectedIndex != -1)
                    {
                        EndpointDescriptionString selection = (EndpointDescriptionString)EndpointListLB.SelectedItem;

                        int index = -1;

                        for (int i = 0; i < ProtocolCB.Items.Count; ++i)
                        {
                            if (((Protocol)ProtocolCB.Items[i]).ToString() == selection.Protocol.ToString())
                            {
                                index = i;
                                break;
                            }
                        }

                        ProtocolCB.SelectedIndex = index;

                        InitializeSecurityModes(m_availableEndpoints);

                        m_currentDescription = m_availableEndpoints[selectedIndex];

                        InitializeEncodings(m_availableEndpoints, m_currentDescription);

                        index = -1;

                        for (int i = 0; i < SecurityModeCB.Items.Count; ++i)
                        {
                            if ((MessageSecurityMode)SecurityModeCB.Items[i] == selection.MessageSecurityMode)
                            {
                                index = i;
                                break;
                            }
                        }

                        SecurityModeCB.SelectedIndex = index;

                        index = -1;

                        for (int i = 0; i < SecurityPolicyCB.Items.Count; ++i)
                        {
                            if ((string)SecurityPolicyCB.Items[i] == selection.CurrentPolicy)
                            {
                                index = i;
                                break;
                            }
                        }

                        SecurityPolicyCB.SelectedIndex = index;

                        index = -1;

                        for (int i = 0; i < EncodingCB.Items.Count; ++i)
                        {
                            if ((Encoding)EncodingCB.Items[i] == selection.Encoding)
                            {
                                index = i;
                                break;
                            }
                        }

                        EncodingCB.SelectedIndex = index;
                    }
                }
                catch (Exception exception)
                {
                    GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
                }
                finally
                {
                    m_updating = false;
                    m_selecting = false;
                }
            }

            UpdateAdvancedEndpointInformation();
        }

        /// <summary>
        /// Updates advanced endpoint information.
        /// </summary>
        private void UpdateAdvancedEndpointInformation()
        {
            try
            {
                ApplicationNameTB.Text = String.Empty;
                ApplicationTypeTB.Text = String.Empty;
                ApplicationUriTB.Text = String.Empty;
                ProductUriTB.Text = String.Empty;
                GatewayServerUriTB.Text = String.Empty;
                DiscoveryProfileUriTB.Text = String.Empty;
                TransportProfileUriTB.Text = String.Empty;
                UserSecurityPoliciesTB.Text = String.Empty;
                SecurityLevelTB.Text = String.Empty;

                if (m_currentDescription != null)
                {
                    UserSecurityPoliciesTB.Text = "Anonymous";

                    if (m_currentDescription.Server != null)
                    {
                        if (m_currentDescription.Server.ApplicationName != null)
                        {
                            ApplicationNameTB.Text = m_currentDescription.Server.ApplicationName.ToString();
                        }

                        ApplicationTypeTB.Text = m_currentDescription.Server.ApplicationType.ToString();
                        ApplicationUriTB.Text = m_currentDescription.Server.ApplicationUri;
                        ProductUriTB.Text = m_currentDescription.Server.ProductUri;
                        GatewayServerUriTB.Text = m_currentDescription.Server.GatewayServerUri;
                        DiscoveryProfileUriTB.Text = m_currentDescription.Server.DiscoveryProfileUri;
                    }

                    SecurityLevelTB.Text = m_currentDescription.SecurityLevel.ToString();
                    TransportProfileUriTB.Text = m_currentDescription.TransportProfileUri;

                    if (m_currentDescription.UserIdentityTokens.Count > 0)
                    {
                        UserSecurityPoliciesTB.Text = String.Join(", ", m_currentDescription.UserIdentityTokens);
                    }
                }

                UpdateStatus();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <summary>
        /// Updates the StatusTB text and color.
        /// Also enables/disables the OK button, should any error occurr (unsupported stuff etc).
        /// </summary>
        private void UpdateStatus()
        {
            try
            {
                if ((m_currentDescription != null) && (m_currentDescription.Server != null))
                {
                    m_statusObject.ClearStatus(StatusChannel.Server);

                    if (m_currentDescription.Server.ApplicationType == ApplicationType.Client)
                    {
                        m_statusObject.SetStatus(StatusChannel.ApplicationType, "Warning: Application type is unsupported.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.ApplicationType);
                    }

                    if (string.IsNullOrEmpty(m_currentDescription.Server.ApplicationUri))
                    {
                        m_statusObject.SetStatus(StatusChannel.ApplicationUri, "Warning: Application URI is missing.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.ApplicationUri);
                    }

                    if (string.IsNullOrEmpty(m_currentDescription.TransportProfileUri))
                    {
                        m_statusObject.SetStatus(StatusChannel.TransportProfileUri, "Warning: Transport Profile URI is missing.", StatusType.Warning);
                    }
                    else if (Utils.ParseUri(m_currentDescription.TransportProfileUri) == null)
                    {
                        m_statusObject.SetStatus(StatusChannel.TransportProfileUri, "Warning: Transport Profile URI is invalid.", StatusType.Warning);
                    }

                    if ((m_currentDescription.Server.DiscoveryUrls == null) || (m_currentDescription.Server.DiscoveryUrls.Count == 0))
                    {
                        m_statusObject.SetStatus(StatusChannel.DiscoveryURLs, "Warning: Discovery URLs are missing.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.DiscoveryURLs);
                    }

                    if ((m_currentDescription.ServerCertificate != null) && (m_currentDescription.ServerCertificate.Length > 0))
                    {
                        X509Certificate2 serverCertificate = new X509Certificate2(m_currentDescription.ServerCertificate);
                        String certificateApplicationUri = X509Utils.GetApplicationUriFromCertificate(serverCertificate);

                        if (certificateApplicationUri != m_currentDescription.Server.ApplicationUri)
                        {
                            m_statusObject.SetStatus(StatusChannel.DifferentCertificate, "Warning: Application URI host different than the certificate host.", StatusType.Warning);
                        }
                        else
                        {
                            m_statusObject.ClearStatus(StatusChannel.DifferentCertificate);
                        }
                    }

                    if (string.IsNullOrEmpty(m_currentDescription.SecurityPolicyUri))
                    {
                        m_statusObject.SetStatus(StatusChannel.SecurityPolicyUri, "Error: Security Policy URI is missing.", StatusType.Warning);
                    }
                    else if (string.IsNullOrEmpty(SecurityPolicies.GetDisplayName(m_currentDescription.SecurityPolicyUri)))
                    {
                        m_statusObject.SetStatus(StatusChannel.SecurityPolicyUri, "Error: Security Policy URI is invalid.", StatusType.Warning);
                    }
                    else
                    {
                        m_statusObject.ClearStatus(StatusChannel.SecurityPolicyUri);
                    }
                }
                else
                {
                    m_statusObject.SetStatus(StatusChannel.Server, "Warning: Server endpoint is invalid.", StatusType.Warning);
                }


                OkBTN.Enabled = true;
                StatusTB.ForeColor = SystemColors.WindowText;
                StatusTB.Text = m_statusObject.StatusString;

                if (m_statusObject.StatusType == StatusType.Error)
                {
                    OkBTN.Enabled = false;
                    StatusTB.ForeColor = Color.Red;
                }
                else if (m_statusObject.StatusType == StatusType.Warning)
                {
                    StatusTB.ForeColor = Color.DarkOrange;
                }

                // hack for WinForms to update color
                StatusTB.BackColor = StatusTB.BackColor;

            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        #endregion
    }
}
