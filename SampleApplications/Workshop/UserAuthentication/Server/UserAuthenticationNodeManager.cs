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
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Quickstarts.UserAuthenticationServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class UserAuthenticationNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public UserAuthenticationNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, Namespaces.UserAuthentication)
        {
            SystemContext.NodeIdFactory = this;
            m_applicationConfiguration = configuration;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<UserAuthenticationServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new UserAuthenticationServerConfiguration();
            }
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {  
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // create a object to represent the process being controlled.
                BaseObjectState process = new BaseObjectState(null);

                process.NodeId = new NodeId(1, NamespaceIndex);
                process.BrowseName = new QualifiedName("My Process", NamespaceIndex);
                process.DisplayName = process.BrowseName.Name;
                process.TypeDefinitionId = ObjectTypeIds.BaseObjectType; 

                // ensure the process object can be found via the server object. 
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                process.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, process.NodeId));

                // a property to report the process state.
                PropertyState<string> state = new PropertyState<string>(process);

                state.NodeId = new NodeId(2, NamespaceIndex);
                state.BrowseName = new QualifiedName("LogFilePath", NamespaceIndex);
                state.DisplayName = state.BrowseName.Name;
                state.TypeDefinitionId = VariableTypeIds.PropertyType;
                state.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                state.DataType = DataTypeIds.String;
                state.ValueRank = ValueRanks.Scalar;
                state.AccessLevel = AccessLevels.CurrentReadOrWrite;
                state.UserAccessLevel = AccessLevels.CurrentRead;
                state.Value = ".\\Log.txt";

                process.AddChild(state);

                state.OnReadUserAccessLevel = OnReadUserAccessLevel;
                state.OnSimpleWriteValue = OnWriteValue;

                // save in dictionary. 
                AddPredefinedNode(SystemContext, process);

                // Create the nodes for AuthorizationService
                FolderState authorizationServices = CreateAuthorizationService();
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, authorizationServices.NodeId));

                AddPredefinedNode(SystemContext, authorizationServices);
            }
        }

        public ServiceResult OnWriteValue(ISystemContext context, NodeState node, ref object value)
        {
            if (context.UserIdentity == null || context.UserIdentity.TokenType == UserTokenType.Anonymous)
            {
                TranslationInfo info = new TranslationInfo(
                    "BadUserAccessDenied",
                    "en-US",
                    "User cannot change value.");

                return new ServiceResult(StatusCodes.BadUserAccessDenied, new LocalizedText(info));
            }

            // attempt to update file system.
            try
            {
                string filePath = value as string;
                PropertyState<string> variable = node as PropertyState<string>;

                if (!String.IsNullOrEmpty(variable.Value))
                {
                    FileInfo file = new FileInfo(variable.Value);

                    if (file.Exists)
                    {
                        file.Delete();
                    }
                }

                if (!String.IsNullOrEmpty(filePath))
                {
                    FileInfo file = new FileInfo(filePath);

                    using (StreamWriter writer = file.CreateText())
                    {
                        writer.WriteLine(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
                    }
                }

                value = filePath;
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e, StatusCodes.BadUserAccessDenied, "Could not update file system.");
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnReadUserAccessLevel(ISystemContext context, NodeState node, ref byte value)
        {
            if (context.UserIdentity == null || context.UserIdentity.TokenType == UserTokenType.Anonymous)
            {
                value = AccessLevels.CurrentRead;
            }
            else
            {
                value = AccessLevels.CurrentReadOrWrite;
            }

            return ServiceResult.Good;
        }

        #region AuthorizationService
        private FolderState CreateAuthorizationService()
        {
            FolderState authorizationServices = new FolderState(null);

            authorizationServices.NodeId = new NodeId(3, NamespaceIndex);
            authorizationServices.BrowseName = new QualifiedName("AuthorizationServices", NamespaceIndex);
            authorizationServices.DisplayName = authorizationServices.BrowseName.Name;
            authorizationServices.TypeDefinitionId = ObjectTypeIds.FolderType;
            authorizationServices.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

            BaseObjectState authorizationService = new BaseObjectState(authorizationServices);
            // Create token policies node.
            authorizationService.NodeId = new NodeId("AS:" + m_configuration.ServiceName, NamespaceIndex);
            authorizationService.BrowseName = new QualifiedName(m_configuration.ServiceName, NamespaceIndex);
            authorizationService.DisplayName = authorizationService.BrowseName.Name;
            authorizationService.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
            authorizationServices.AddChild(authorizationService);

            PropertyState userTokenpolices = authorizationService.AddProperty<UserTokenPolicy[]>(Opc.Ua.Gds.BrowseNames.UserTokenPolicies,
                DataTypeIds.UserTokenPolicy, ValueRanks.OneDimension);
            userTokenpolices.NodeId = new NodeId(5, NamespaceIndex);
            userTokenpolices.Value = m_configuration.UserTokenPolicies.ToArray();

            // Create RequestAccessToken method
            MethodState requestAccessToken = new MethodState(authorizationService);

            requestAccessToken.SymbolicName = Opc.Ua.Gds.BrowseNames.RequestAccessToken;
            requestAccessToken.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            requestAccessToken.NodeId = new NodeId(6, NamespaceIndex);
            requestAccessToken.BrowseName = new QualifiedName(Opc.Ua.Gds.BrowseNames.RequestAccessToken, NamespaceIndex);
            requestAccessToken.DisplayName = Opc.Ua.Gds.BrowseNames.RequestAccessToken;
            requestAccessToken.WriteMask = AttributeWriteMask.None;
            requestAccessToken.UserWriteMask = AttributeWriteMask.None;
            requestAccessToken.Executable = true;
            requestAccessToken.UserExecutable = true;

            // set input arguments
            requestAccessToken.InputArguments = new PropertyState<Argument[]>(requestAccessToken);
            requestAccessToken.InputArguments.NodeId = new NodeId(7, NamespaceIndex);
            requestAccessToken.InputArguments.BrowseName = BrowseNames.InputArguments;
            requestAccessToken.InputArguments.DisplayName = BrowseNames.InputArguments;
            requestAccessToken.InputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            requestAccessToken.InputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            requestAccessToken.InputArguments.DataType = DataTypeIds.Argument;
            requestAccessToken.InputArguments.ValueRank = ValueRanks.OneDimension;

            requestAccessToken.InputArguments.Value = new Argument[]
            {
                new Argument() { Name = "IdentityToken",  DataType = DataTypeIds.UserIdentityToken, ValueRank = ValueRanks.Scalar },
                new Argument() { Name = "ResourceId", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
            };

            // set output arguments
            requestAccessToken.OutputArguments = new PropertyState<Argument[]>(requestAccessToken);
            requestAccessToken.OutputArguments.NodeId = new NodeId(8, NamespaceIndex);
            requestAccessToken.OutputArguments.BrowseName = BrowseNames.OutputArguments;
            requestAccessToken.OutputArguments.DisplayName = BrowseNames.OutputArguments;
            requestAccessToken.OutputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            requestAccessToken.OutputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            requestAccessToken.OutputArguments.DataType = DataTypeIds.Argument;
            requestAccessToken.OutputArguments.ValueRank = ValueRanks.OneDimension;

            requestAccessToken.OutputArguments.Value = new Argument[]
            {
                 new Argument() { Name = "AccessToken", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
            };

            requestAccessToken.OnCallMethod = new GenericMethodCalledEventHandler(OnRequestAccessToken);

            authorizationService.AddChild(requestAccessToken);

            return authorizationServices;
        }

        private ServiceResult OnRequestAccessToken(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                // parse input arguments.
                ExtensionObject extensionObject = (ExtensionObject)inputArguments[0];
                UserIdentityToken identityToken = (UserIdentityToken)extensionObject.Body;
                string resourceId = (string)inputArguments[1];

                UserTokenPolicy selectedPolicy = null; 

                foreach (var policy in m_configuration.UserTokenPolicies)
                {
                    if (policy.PolicyId == identityToken.PolicyId)
                    {
                        selectedPolicy = policy;
                        break;
                    }
                }

                if (selectedPolicy == null)
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid);
                }

                var certificate = m_applicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;

                identityToken.Decrypt(
                    certificate,
                    certificate.RawData,
                    selectedPolicy.SecurityPolicyUri);

                IUserIdentity identity = new UserIdentity(identityToken, selectedPolicy);

                // check for an issued token.
                IssuedIdentityToken issuedToken = identityToken as IssuedIdentityToken;

                if (issuedToken != null)
                {
                    if (selectedPolicy.IssuedTokenType == Profiles.JwtUserToken)
                    {
                        JwtEndpointParameters parameters = new JwtEndpointParameters();
                        parameters.FromJson(selectedPolicy.IssuerEndpointUrl);
                        var jwt = new UTF8Encoding().GetString(issuedToken.DecryptedTokenData);

                        identity = JwtUtils.ValidateToken(new Uri(parameters.AuthorityUrl), null, null, parameters.ResourceId, jwt);
                    }
                }

                var signingKey = new X509SecurityKey(m_applicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate);

                var claimsIdentity = new ClaimsIdentity(new List<Claim>()
                {
                    new Claim("name", identity.DisplayName),
                    new Claim("scp", "UAServer"),
                    new Claim("roles", "admin"),
                }, "Custom");

                var signingCredentials = new SigningCredentials(
                signingKey,
                System.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256Signature,
                System.IdentityModel.Tokens.SecurityAlgorithms.Sha256Digest);

                var securityTokenDescriptor = new SecurityTokenDescriptor()
                {
                    Issuer = m_applicationConfiguration.ApplicationUri,
                    Audience = m_applicationConfiguration.ApplicationUri,
                    Subject = claimsIdentity,
                    SigningCredentials = signingCredentials,
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var plainToken = tokenHandler.CreateToken(securityTokenDescriptor);
                var signedAndEncodedToken = tokenHandler.WriteToken(plainToken);

                // set output parameter
                outputArguments[0] = signedAndEncodedToken;
                return ServiceResult.Good;
            }
            catch(Exception ex)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }
        #endregion

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace. 
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                if (!PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                NodeHandle handle = new NodeHandle();

                handle.NodeId = nodeId;
                handle.Node = node;
                handle.Validated = true;

                return handle;
            } 
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }
            
            // TBD

            return null;
        }
        #endregion

        #region Overridden UserAuthentication
        #endregion

        #region Private Fields
        private UserAuthenticationServerConfiguration m_configuration;
        private ApplicationConfiguration m_applicationConfiguration;
        #endregion
    }
}
