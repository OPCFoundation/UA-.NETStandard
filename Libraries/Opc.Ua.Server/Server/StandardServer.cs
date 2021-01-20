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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The standard implementation of a UA server.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    public partial class StandardServer : SessionServerBase
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public StandardServer()
        {

        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_serverInternal"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_registrationTimer"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_configurationWatcher")]
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // halt any outstanding timer.
                if (m_registrationTimer != null)
                {
                    Utils.SilentDispose(m_registrationTimer);
                    m_registrationTimer = null;
                }

                // close the watcher.
                if (m_configurationWatcher != null)
                {
                    Utils.SilentDispose(m_configurationWatcher);
                    m_configurationWatcher = null;
                }

                // close the server.
                if (m_serverInternal != null)
                {
                    Utils.SilentDispose(m_serverInternal);
                    m_serverInternal = null;
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region IServer Methods
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="localeIds">The locale ids.</param>
        /// <param name="serverUris">The server uris.</param>
        /// <param name="servers">List of Servers that meet criteria specified in the request.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader FindServers(
            RequestHeader requestHeader,
            string endpointUrl,
            StringCollection localeIds,
            StringCollection serverUris,
            out ApplicationDescriptionCollection servers)
        {
            servers = new ApplicationDescriptionCollection();

            ValidateRequest(requestHeader);

            lock (m_lock)
            {
                // parse the url provided by the client.
                IList<BaseAddress> baseAddresses = BaseAddresses;

                Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);

                if (parsedEndpointUrl != null)
                {
                    baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
                }

                // check if nothing to do.
                if (baseAddresses.Count == 0)
                {
                    servers = new ApplicationDescriptionCollection();
                    return CreateResponse(requestHeader, StatusCodes.Good);
                }

                // build list of unique servers.
                Dictionary<string, ApplicationDescription> uniqueServers = new Dictionary<string, ApplicationDescription>();

                foreach (EndpointDescription description in GetEndpoints())
                {
                    ApplicationDescription server = description.Server;

                    // skip servers that have been processed.
                    if (uniqueServers.ContainsKey(server.ApplicationUri))
                    {
                        continue;
                    }

                    // check client is filtering by server uri.
                    if (serverUris != null && serverUris.Count > 0)
                    {
                        if (!serverUris.Contains(server.ApplicationUri))
                        {
                            continue;
                        }
                    }

                    // localize the application name if requested.
                    LocalizedText applicationName = server.ApplicationName;

                    if (localeIds != null && localeIds.Count > 0)
                    {
                        applicationName = m_serverInternal.ResourceManager.Translate(localeIds, applicationName);
                    }

                    // get the application description.
                    ApplicationDescription application = TranslateApplicationDescription(
                        parsedEndpointUrl,
                        server,
                        baseAddresses,
                        applicationName);

                    uniqueServers.Add(server.ApplicationUri, application);

                    // add to list of servers to return.
                    servers.Add(application);
                }
            }

            return CreateResponse(requestHeader, StatusCodes.Good);
        }

        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="localeIds">The locale ids.</param>
        /// <param name="profileUris">The profile uris.</param>
        /// <param name="endpoints">The endpoints supported by the server.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader GetEndpoints(
            RequestHeader requestHeader,
            string endpointUrl,
            StringCollection localeIds,
            StringCollection profileUris,
            out EndpointDescriptionCollection endpoints)
        {
            endpoints = null;

            ValidateRequest(requestHeader);

            lock (m_lock)
            {
                // filter by profile.
                IList<BaseAddress> baseAddresses = FilterByProfile(profileUris, BaseAddresses);

                // get the descriptions.
                endpoints = GetEndpointDescriptions(
                    endpointUrl,
                    baseAddresses,
                    localeIds);
            }

            return CreateResponse(requestHeader, StatusCodes.Good);
        }

        /// <summary>
        /// Returns the endpoints that match the base addresss and endpoint url.
        /// </summary>
        protected EndpointDescriptionCollection GetEndpointDescriptions(
            string endpointUrl,
            IList<BaseAddress> baseAddresses,
            StringCollection localeIds)
        {
            EndpointDescriptionCollection endpoints = null;

            // parse the url provided by the client.
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);

            if (parsedEndpointUrl != null)
            {
                baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
            }

            // check if nothing to do.
            if (baseAddresses.Count != 0)
            {
                // localize the application name if requested.
                LocalizedText applicationName = this.ServerDescription.ApplicationName;

                if (localeIds != null && localeIds.Count > 0)
                {
                    applicationName = m_serverInternal.ResourceManager.Translate(localeIds, applicationName);
                }

                // translate the application description.
                ApplicationDescription application = TranslateApplicationDescription(
                    parsedEndpointUrl,
                    base.ServerDescription,
                    baseAddresses,
                    applicationName);

                // translate the endpoint descriptions.
                endpoints = TranslateEndpointDescriptions(
                    parsedEndpointUrl,
                    baseAddresses,
                    this.Endpoints,
                    application);
            }

            return endpoints;
        }

        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="clientDescription">Application description for the client application.</param>
        /// <param name="serverUri">The server URI.</param>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="sessionName">Name for the Session assigned by the client.</param>
        /// <param name="clientNonce">The client nonce.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="requestedSessionTimeout">The requested session timeout.</param>
        /// <param name="maxResponseMessageSize">Size of the max response message.</param>
        /// <param name="sessionId">The unique public identifier assigned by the Server to the Session.</param>
        /// <param name="authenticationToken">The unique private identifier assigned by the Server to the Session.</param>
        /// <param name="revisedSessionTimeout">The revised session timeout.</param>
        /// <param name="serverNonce">The server nonce.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="serverEndpoints">The server endpoints.</param>
        /// <param name="serverSoftwareCertificates">The server software certificates.</param>
        /// <param name="serverSignature">The server signature.</param>
        /// <param name="maxRequestMessageSize">Size of the max request message.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader CreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize)
        {
            sessionId = 0;
            revisedSessionTimeout = 0;
            serverNonce = null;
            serverCertificate = null;
            serverSoftwareCertificates = null;
            serverSignature = null;
            maxRequestMessageSize = (uint)MessageContext.MaxMessageSize;

            OperationContext context = ValidateRequest(requestHeader, RequestType.CreateSession);

            try
            {
                // check the server uri.
                if (!String.IsNullOrEmpty(serverUri))
                {
                    if (serverUri != this.Configuration.ApplicationUri)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerUriInvalid);
                    }
                }

                bool requireEncryption = ServerBase.RequireEncryption(context.ChannelContext.EndpointDescription);

                if (!requireEncryption && clientCertificate != null)
                {
                    requireEncryption = true;
                }

                // validate client application instance certificate.
                X509Certificate2 parsedClientCertificate = null;

                if (requireEncryption && clientCertificate != null && clientCertificate.Length > 0)
                {
                    try
                    {
                        X509Certificate2Collection clientCertificateChain = Utils.ParseCertificateChainBlob(clientCertificate);
                        parsedClientCertificate = clientCertificateChain[0];

                        if (context.SecurityPolicyUri != SecurityPolicies.None)
                        {
                            string certificateApplicationUri = X509Utils.GetApplicationUriFromCertificate(parsedClientCertificate);

                            // verify if applicationUri from ApplicationDescription matches the applicationUri in the client certificate.
                            if (!String.IsNullOrEmpty(certificateApplicationUri) &&
                                !String.IsNullOrEmpty(clientDescription.ApplicationUri) &&
                                certificateApplicationUri != clientDescription.ApplicationUri)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadCertificateUriInvalid,
                                    "The URI specified in the ApplicationDescription does not match the URI in the Certificate.");
                            }

                            CertificateValidator.Validate(clientCertificateChain);
                        }
                    }
                    catch (Exception e)
                    {
                        OnApplicationCertificateError(clientCertificate, new ServiceResult(e));
                    }
                }

                // verify the nonce provided by the client.
                if (clientNonce != null)
                {
                    if (clientNonce.Length < m_minNonceLength)
                    {
                        throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                    }

                    // ignore nonce if security policy set to none
                    if (context.SecurityPolicyUri == SecurityPolicies.None)
                    {
                        clientNonce = null;
                    }
                }

                // create the session.
                Session session = ServerInternal.SessionManager.CreateSession(
                    context,
                    requireEncryption ? InstanceCertificate : null,
                    sessionName,
                    clientNonce,
                    clientDescription,
                    endpointUrl,
                    parsedClientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    out sessionId,
                    out authenticationToken,
                    out serverNonce,
                    out revisedSessionTimeout);

                lock (m_lock)
                {
                    // return the application instance certificate for the server.
                    if (requireEncryption)
                    {
                        // check if complete chain should be sent.
                        if (Configuration.SecurityConfiguration.SendCertificateChain &&
                            InstanceCertificateChain != null &&
                            InstanceCertificateChain.Count > 0)
                        {
                            List<byte> serverCertificateChain = new List<byte>();

                            for (int i = 0; i < InstanceCertificateChain.Count; i++)
                            {
                                serverCertificateChain.AddRange(InstanceCertificateChain[i].RawData);
                            }

                            serverCertificate = serverCertificateChain.ToArray();
                        }
                        else
                        {
                            serverCertificate = InstanceCertificate.RawData;
                        }
                    }

                    // return the endpoints supported by the server.
                    serverEndpoints = GetEndpointDescriptions(endpointUrl, BaseAddresses, null);

                    // return the software certificates assigned to the server.
                    serverSoftwareCertificates = new SignedSoftwareCertificateCollection(ServerProperties.SoftwareCertificates);

                    // sign the nonce provided by the client.
                    serverSignature = null;

                    //  sign the client nonce (if provided).
                    if (parsedClientCertificate != null && clientNonce != null)
                    {
                        byte[] dataToSign = Utils.Append(parsedClientCertificate.RawData, clientNonce);
                        serverSignature = SecurityPolicies.Sign(InstanceCertificate, context.SecurityPolicyUri, dataToSign);
                    }
                }

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.CurrentSessionCount++;
                    ServerInternal.ServerDiagnostics.CumulatedSessionCount++;
                }

                Utils.Trace("Server - SESSION CREATED. SessionId={0}", sessionId);

                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (ServiceResultException e)
            {
                Utils.Trace("Server - SESSION CREATE failed. {0}", e.Message);

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedSessionCount++;
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedSessionCount++;
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException((DiagnosticsMasks)requestHeader.ReturnDiagnostics, new StringCollection(), e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="clientSignature">The client signature.</param>
        /// <param name="clientSoftwareCertificates">The client software certificates.</param>
        /// <param name="localeIds">The locale ids.</param>
        /// <param name="userIdentityToken">The user identity token.</param>
        /// <param name="userTokenSignature">The user token signature.</param>
        /// <param name="serverNonce">The server nonce.</param>
        /// <param name="results">The results.</param>
        /// <param name="diagnosticInfos">The diagnostic infos.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader ActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            serverNonce = null;
            results = null;
            diagnosticInfos = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.ActivateSession);

            try
            {
                // validate client's software certificates.
                List<SoftwareCertificate> softwareCertificates = new List<SoftwareCertificate>();

                if (context.SecurityPolicyUri != SecurityPolicies.None)
                {
                    bool diagnosticsExist = false;

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos = new DiagnosticInfoCollection();
                    }

                    results = new StatusCodeCollection();
                    diagnosticInfos = new DiagnosticInfoCollection();

                    foreach (SignedSoftwareCertificate signedCertificate in clientSoftwareCertificates)
                    {
                        SoftwareCertificate softwareCertificate = null;

                        ServiceResult result = SoftwareCertificate.Validate(
                            CertificateValidator,
                            signedCertificate.CertificateData,
                            out softwareCertificate);

                        if (ServiceResult.IsBad(result))
                        {
                            results.Add(result.Code);

                            // add diagnostics if requested.
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(ServerInternal, context, result);
                                diagnosticInfos.Add(diagnosticInfo);
                                diagnosticsExist = true;
                            }
                        }
                        else
                        {
                            softwareCertificates.Add(softwareCertificate);
                            results.Add(StatusCodes.Good);

                            // add diagnostics if requested.
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                diagnosticInfos.Add(null);
                            }
                        }
                    }

                    if (!diagnosticsExist && diagnosticInfos != null)
                    {
                        diagnosticInfos.Clear();
                    }
                }

                // check if certificates meet the server's requirements.
                ValidateSoftwareCertificates(softwareCertificates);

                // activate the session.
                bool identityChanged = ServerInternal.SessionManager.ActivateSession(
                    context,
                    requestHeader.AuthenticationToken,
                    clientSignature,
                    softwareCertificates,
                    userIdentityToken,
                    userTokenSignature,
                    localeIds,
                    out serverNonce);

                if (identityChanged)
                {
                    // TBD - call Node Manager and Subscription Manager.
                }

                Utils.Trace("Server - SESSION ACTIVATED.");

                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (ServiceResultException e)
            {
                Utils.Trace("Server - SESSION ACTIVATE failed. {0}", e.Message);

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedSessionCount++;
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedSessionCount++;
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException((DiagnosticsMasks)requestHeader.ReturnDiagnostics, localeIds, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Returns whether the error is a security error.
        /// </summary>
        /// <param name="error">The error.</param>
        /// <returns>
        /// 	<c>true</c> if the error is one of the security errors, otherwise <c>false</c>.
        /// </returns>
        protected bool IsSecurityError(StatusCode error)
        {
            switch (error.CodeBits)
            {
                case StatusCodes.BadUserSignatureInvalid:
                case StatusCodes.BadUserAccessDenied:
                case StatusCodes.BadSecurityPolicyRejected:
                case StatusCodes.BadSecurityModeRejected:
                case StatusCodes.BadSecurityChecksFailed:
                case StatusCodes.BadSecureChannelTokenUnknown:
                case StatusCodes.BadSecureChannelIdInvalid:
                case StatusCodes.BadNoValidCertificates:
                case StatusCodes.BadIdentityTokenInvalid:
                case StatusCodes.BadIdentityTokenRejected:
                case StatusCodes.BadIdentityChangeNotSupported:
                case StatusCodes.BadCertificateUseNotAllowed:
                case StatusCodes.BadCertificateUriInvalid:
                case StatusCodes.BadCertificateUntrusted:
                case StatusCodes.BadCertificateTimeInvalid:
                case StatusCodes.BadCertificateRevoked:
                case StatusCodes.BadCertificateRevocationUnknown:
                case StatusCodes.BadCertificateIssuerUseNotAllowed:
                case StatusCodes.BadCertificateIssuerTimeInvalid:
                case StatusCodes.BadCertificateIssuerRevoked:
                case StatusCodes.BadCertificateIssuerRevocationUnknown:
                case StatusCodes.BadCertificateInvalid:
                case StatusCodes.BadCertificateHostNameInvalid:
                case StatusCodes.BadApplicationSignatureInvalid:
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="exception">The exception used to create DiagnosticInfo assigned to the ServiceDiagnostics.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected ResponseHeader CreateResponse(RequestHeader requestHeader, ServiceResultException exception)
        {
            ResponseHeader responseHeader = new ResponseHeader();

            responseHeader.ServiceResult = exception.StatusCode;

            responseHeader.Timestamp = DateTime.UtcNow;
            responseHeader.RequestHandle = requestHeader.RequestHandle;

            StringTable stringTable = new StringTable();
            responseHeader.ServiceDiagnostics = new DiagnosticInfo(exception, (DiagnosticsMasks)requestHeader.ReturnDiagnostics, true, stringTable);
            responseHeader.StringTable = stringTable.ToArray();

            return responseHeader;
        }

        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="deleteSubscriptions">if set to <c>true</c> subscriptions are deleted.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader CloseSession(RequestHeader requestHeader, bool deleteSubscriptions)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.CloseSession);

            try
            {
                ServerInternal.CloseSession(context, context.Session.Id, deleteSubscriptions);
                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestHandle">The request handle assigned to the request.</param>
        /// <param name="cancelCount">The number of cancelled requests.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint requestHandle,
            out uint cancelCount)
        {
            cancelCount = 0;

            OperationContext context = ValidateRequest(requestHeader, RequestType.Cancel);

            try
            {
                m_serverInternal.RequestManager.CancelRequests(requestHandle, out cancelCount);
                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="view">The view.</param>
        /// <param name="requestedMaxReferencesPerNode">The maximum number of references to return for each node.</param>
        /// <param name="nodesToBrowse">The list of nodes to browse.</param>
        /// <param name="results">The list of results for the passed starting nodes and filters.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.Browse);

            try
            {
                if (nodesToBrowse == null || nodesToBrowse.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.Browse(
                    context,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="releaseContinuationPoints">if set to <c>true</c> the continuation points are released.</param>
        /// <param name="continuationPoints">A list of continuation points returned in a previous Browse or BrewseNext call.</param>
        /// <param name="results">The list of resulted references for browse.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.BrowseNext);

            try
            {
                if (continuationPoints == null || continuationPoints.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.BrowseNext(
                    context,
                    releaseContinuationPoints,
                    continuationPoints,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="nodesToRegister">The list of NodeIds to register.</param>
        /// <param name="registeredNodeIds">The list of NodeIds identifying the registered nodes. </param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds)
        {
            registeredNodeIds = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.RegisterNodes);

            try
            {
                if (nodesToRegister == null || nodesToRegister.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.RegisterNodes(
                    context,
                    nodesToRegister,
                    out registeredNodeIds);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="nodesToUnregister">The list of NodeIds to unregister</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader UnregisterNodes(RequestHeader requestHeader, NodeIdCollection nodesToUnregister)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.UnregisterNodes);

            try
            {
                if (nodesToUnregister == null || nodesToUnregister.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.UnregisterNodes(
                    context,
                    nodesToUnregister);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="browsePaths">The list of browse paths for which NodeIds are being requested.</param>
        /// <param name="results">The list of results for the list of browse paths.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            results = null;
            diagnosticInfos = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.TranslateBrowsePathsToNodeIds);

            try
            {
                if (browsePaths == null || browsePaths.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.TranslateBrowsePathsToNodeIds(
                    context,
                    browsePaths,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="maxAge">The Maximum age of the value to be read in milliseconds.</param>
        /// <param name="timestampsToReturn">The type of timestamps to be returned for the requested Variables.</param>
        /// <param name="nodesToRead">The list of Nodes and their Attributes to read.</param>
        /// <param name="results">The list of returned Attribute values</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.Read);

            try
            {
                if (nodesToRead == null || nodesToRead.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.Read(
                    context,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="historyReadDetails">The history read details.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="releaseContinuationPoints">if set to <c>true</c> continuation points are released.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <param name="results">The results.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.HistoryRead);

            try
            {
                if (nodesToRead == null || nodesToRead.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.HistoryRead(
                    context,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="nodesToWrite">The list of Nodes, Attributes, and values to write.</param>
        /// <param name="results">The list of write result status codes for each write operation.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.Write);

            try
            {
                if (nodesToWrite == null || nodesToWrite.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.Write(
                    context,
                    nodesToWrite,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="historyUpdateDetails">The details defined for the update.</param>
        /// <param name="results">The list of update results for the history update details.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.HistoryUpdate);

            try
            {
                if (historyUpdateDetails == null || historyUpdateDetails.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                m_serverInternal.NodeManager.HistoryUpdate(
                    context,
                    historyUpdateDetails,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestedPublishingInterval">The cyclic rate that the Subscription is being requested to return Notifications to the Client.</param>
        /// <param name="requestedLifetimeCount">The client-requested lifetime count for the Subscription</param>
        /// <param name="requestedMaxKeepAliveCount">The requested max keep alive count.</param>
        /// <param name="maxNotificationsPerPublish">The maximum number of notifications that the Client wishes to receive in a single Publish response.</param>
        /// <param name="publishingEnabled">If set to <c>true</c> publishing is enabled for the Subscription.</param>
        /// <param name="priority">The relative priority of the Subscription.</param>
        /// <param name="subscriptionId">The Server-assigned identifier for the Subscription.</param>
        /// <param name="revisedPublishingInterval">The actual publishing interval that the Server will use.</param>
        /// <param name="revisedLifetimeCount">The revised lifetime count.</param>
        /// <param name="revisedMaxKeepAliveCount">The revised max keep alive count.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.CreateSubscription);

            try
            {
                ServerInternal.SubscriptionManager.CreateSubscription(
                    context,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    out subscriptionId,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionIds">The list of Subscriptions to delete.</param>
        /// <param name="results">The list of result StatusCodes for the Subscriptions to delete.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.DeleteSubscriptions);

            try
            {
                if (subscriptionIds == null || subscriptionIds.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.DeleteSubscriptions(
                    context,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionAcknowledgements">The list of acknowledgements for one or more Subscriptions.</param>
        /// <param name="subscriptionId">The subscription identifier.</param>
        /// <param name="availableSequenceNumbers">The available sequence numbers.</param>
        /// <param name="moreNotifications">If set to <c>true</c> the number of Notifications that were ready to be sent could not be sent in a single response.</param>
        /// <param name="notificationMessage">The NotificationMessage that contains the list of Notifications.</param>
        /// <param name="results">The list of results for the acknowledgements.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object 
        /// </returns>
        public override ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.Publish);

            try
            {
                /*
                // check if there is an odd delay.
                if (DateTime.UtcNow > requestHeader.Timestamp.AddMilliseconds(100))
                {
                    Utils.Trace(
                        "WARNING. Unexpected delay receiving Publish request. Time={0:hh:mm:ss.fff}, ReceiveTime={1:hh:mm:ss.fff}",
                        DateTime.UtcNow,
                        requestHeader.Timestamp);
                }
                */

                Utils.Trace("PUBLISH #{0} RECEIVED. TIME={1:hh:mm:ss.fff}", requestHeader.RequestHandle, requestHeader.Timestamp);

                notificationMessage = ServerInternal.SubscriptionManager.Publish(
                    context,
                    subscriptionAcknowledgements,
                    null,
                    out subscriptionId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out results,
                    out diagnosticInfos);

                /*
                if (notificationMessage != null)
                {
                    Utils.Trace(
                        "PublishResponse: SubId={0} SeqNo={1}, PublishTime={2:mm:ss.fff}, Time={3:mm:ss.fff}",
                        subscriptionId,
                        notificationMessage.SequenceNumber,
                        notificationMessage.PublishTime,
                        DateTime.UtcNow);
                }
                */

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Begins an asynchronous publish operation.
        /// </summary>
        /// <param name="request">The request.</param>
        public virtual void BeginPublish(IEndpointIncomingRequest request)
        {
            PublishRequest input = (PublishRequest)request.Request;
            OperationContext context = ValidateRequest(input.RequestHeader, RequestType.Publish);

            try
            {
                AsyncPublishOperation operation = new AsyncPublishOperation(context, request, this);

                uint subscriptionId = 0;
                UInt32Collection availableSequenceNumbers = null;
                bool moreNotifications = false;
                NotificationMessage notificationMessage = null;
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                notificationMessage = ServerInternal.SubscriptionManager.Publish(
                    context,
                    input.SubscriptionAcknowledgements,
                    operation,
                    out subscriptionId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out results,
                    out diagnosticInfos);

                // request completed asychrnously.
                if (notificationMessage != null)
                {
                    OnRequestComplete(context);

                    operation.Response.ResponseHeader = CreateResponse(input.RequestHeader, context.StringTable);
                    operation.Response.SubscriptionId = subscriptionId;
                    operation.Response.AvailableSequenceNumbers = availableSequenceNumbers;
                    operation.Response.MoreNotifications = moreNotifications;
                    operation.Response.Results = results;
                    operation.Response.DiagnosticInfos = diagnosticInfos;
                    operation.Response.NotificationMessage = notificationMessage;

                    Utils.Trace("PUBLISH: #{0} Completed Synchronously", input.RequestHeader.RequestHandle);
                    request.OperationCompleted(operation.Response, null);
                }
            }
            catch (ServiceResultException e)
            {
                OnRequestComplete(context);

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
        }

        /// <summary>
        /// Completes an asynchronous publish operation.
        /// </summary>
        /// <param name="request">The request.</param>
        public virtual void CompletePublish(IEndpointIncomingRequest request)
        {
            AsyncPublishOperation operation = (AsyncPublishOperation)request.Calldata;
            OperationContext context = operation.Context;

            try
            {
                if (ServerInternal.SubscriptionManager.CompletePublish(context, operation))
                {
                    operation.Response.ResponseHeader = CreateResponse(request.Request.RequestHeader, context.StringTable);
                    request.OperationCompleted(operation.Response, null);
                    OnRequestComplete(context);
                }
            }
            catch (ServiceResultException e)
            {
                OnRequestComplete(context);

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
        }

        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="retransmitSequenceNumber">The sequence number of a specific NotificationMessage to be republished.</param>
        /// <param name="notificationMessage">The requested NotificationMessage.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.Republish);

            try
            {
                notificationMessage = ServerInternal.SubscriptionManager.Republish(
                    context,
                    subscriptionId,
                    retransmitSequenceNumber);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="requestedPublishingInterval">The cyclic rate that the Subscription is being requested to return Notifications to the Client.</param>
        /// <param name="requestedLifetimeCount">The client-requested lifetime count for the Subscription.</param>
        /// <param name="requestedMaxKeepAliveCount">The requested max keep alive count.</param>
        /// <param name="maxNotificationsPerPublish">The maximum number of notifications that the Client wishes to receive in a single Publish response.</param>
        /// <param name="priority">The relative priority of the Subscription.</param>
        /// <param name="revisedPublishingInterval">The revised publishing interval.</param>
        /// <param name="revisedLifetimeCount">The revised lifetime count.</param>
        /// <param name="revisedMaxKeepAliveCount">The revised max keep alive count.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.ModifySubscription);

            try
            {
                ServerInternal.SubscriptionManager.ModifySubscription(
                    context,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="publishingEnabled">If set to <c>true</c> publishing of NotificationMessages is enabled for the Subscription.</param>
        /// <param name="subscriptionIds">The list of subscription ids.</param>
        /// <param name="results">The list of StatusCodes for the Subscriptions to enable/disable.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object 
        /// </returns>
        public override ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.SetPublishingMode);

            try
            {
                if (subscriptionIds == null || subscriptionIds.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.SetPublishingMode(
                    context,
                    publishingEnabled,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="triggeringItemId">The id for the MonitoredItem used as the triggering item.</param>
        /// <param name="linksToAdd">The list of ids of the items to report that are to be added as triggering links.</param>
        /// <param name="linksToRemove">The list of ids of the items to report for the triggering links to be deleted.</param>
        /// <param name="addResults">The list of StatusCodes for the items to add.</param>
        /// <param name="addDiagnosticInfos">The list of diagnostic information for the links to add.</param>
        /// <param name="removeResults">The list of StatusCodes for the items to delete.</param>
        /// <param name="removeDiagnosticInfos">The list of diagnostic information for the links to delete.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object 
        /// </returns>
        public override ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            addResults = null;
            addDiagnosticInfos = null;
            removeResults = null;
            removeDiagnosticInfos = null;

            OperationContext context = ValidateRequest(requestHeader, RequestType.SetTriggering);

            try
            {
                if ((linksToAdd == null || linksToAdd.Count == 0) && (linksToRemove == null || linksToRemove.Count == 0))
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.SetTriggering(
                    context,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    out addResults,
                    out addDiagnosticInfos,
                    out removeResults,
                    out removeDiagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id that will report notifications.</param>
        /// <param name="timestampsToReturn">The type of timestamps to be returned for the MonitoredItems.</param>
        /// <param name="itemsToCreate">The list of MonitoredItems to be created and assigned to the specified subscription</param>
        /// <param name="results">The list of results for the MonitoredItems to create.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.CreateMonitoredItems);

            try
            {
                if (itemsToCreate == null || itemsToCreate.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.CreateMonitoredItems(
                    context,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="timestampsToReturn">The type of timestamps to be returned for the MonitoredItems.</param>
        /// <param name="itemsToModify">The list of MonitoredItems to modify.</param>
        /// <param name="results">The list of results for the MonitoredItems to modify.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.ModifyMonitoredItems);

            try
            {
                if (itemsToModify == null || itemsToModify.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.ModifyMonitoredItems(
                    context,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="monitoredItemIds">The list of MonitoredItems to delete.</param>
        /// <param name="results">The list of results for the MonitoredItems to delete.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.DeleteMonitoredItems);

            try
            {
                if (monitoredItemIds == null || monitoredItemIds.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.DeleteMonitoredItems(
                    context,
                    subscriptionId,
                    monitoredItemIds,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="subscriptionId">The subscription id.</param>
        /// <param name="monitoringMode">The monitoring mode to be set for the MonitoredItems.</param>
        /// <param name="monitoredItemIds">The list of MonitoredItems to modify.</param>
        /// <param name="results">The list of results for the MonitoredItems to modify.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.SetMonitoringMode);

            try
            {
                if (monitoredItemIds == null || monitoredItemIds.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                ServerInternal.SubscriptionManager.SetMonitoringMode(
                    context,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="methodsToCall">The methods to call.</param>
        /// <param name="results">The results.</param>
        /// <param name="diagnosticInfos">The diagnostic information for the results.</param>
        /// <returns>
        /// Returns a <see cref="ResponseHeader"/> object
        /// </returns>
        public override ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            OperationContext context = ValidateRequest(requestHeader, RequestType.Call);

            try
            {
                if (methodsToCall == null || methodsToCall.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                uint maxNodesPerMethodCall = 0;

                try
                {
                    maxNodesPerMethodCall = ServerInternal.ServerObject.ServerCapabilities.OperationLimits.MaxNodesPerMethodCall.Value;
                }
                catch
                {
                    //ignore erros
                }

                if (maxNodesPerMethodCall > 0 && methodsToCall.Count > maxNodesPerMethodCall)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }

                m_serverInternal.NodeManager.Call(
                    context,
                    methodsToCall,
                    out results,
                    out diagnosticInfos);

                return CreateResponse(requestHeader, context.StringTable);
            }
            catch (ServiceResultException e)
            {
                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.RejectedRequestsCount++;

                    if (IsSecurityError(e.StatusCode))
                    {
                        ServerInternal.ServerDiagnostics.SecurityRejectedRequestsCount++;
                    }
                }

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }
        #endregion

        #region Public Methods used by the Host Process
        /// <summary>
        /// The state object associated with the server.
        /// It provides the shared components for the Server.
        /// </summary>
        /// <value>The current instance.</value>
        public IServerInternal CurrentInstance
        {
            get
            {
                lock (m_lock)
                {
                    if (m_serverInternal == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerHalted);
                    }

                    return m_serverInternal;
                }
            }
        }

        /// <summary>
        /// Returns the current status of the server.
        /// </summary>
        /// <returns>Returns a ServerStatusDataType object</returns>
        public ServerStatusDataType GetStatus()
        {
            lock (m_lock)
            {
                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                return m_serverInternal.Status.Value;
            }
        }

        /// <summary>
        /// Registers the server with the discovery server.
        /// </summary>
        /// <returns>Boolean value.</returns>
        public async Task<bool> RegisterWithDiscoveryServer()
        {
            ApplicationConfiguration configuration = string.IsNullOrEmpty(base.Configuration.SourceFilePath) ? base.Configuration : await ApplicationConfiguration.Load(new FileInfo(base.Configuration.SourceFilePath), ApplicationType.Server, null, false);
            CertificateValidationEventHandler registrationCertificateValidator = new CertificateValidationEventHandler(RegistrationValidator_CertificateValidation);
            configuration.CertificateValidator.CertificateValidation += registrationCertificateValidator;

            try
            {
                // try each endpoint.
                if (m_registrationEndpoints != null)
                {
                    foreach (ConfiguredEndpoint endpoint in m_registrationEndpoints.Endpoints)
                    {
                        RegistrationClient client = null;
                        int i = 0;

                        while (i++ < 2)
                        {
                            try
                            {
                                // update from the server.
                                bool updateRequired = true;

                                lock (m_registrationLock)
                                {
                                    updateRequired = endpoint.UpdateBeforeConnect;
                                }

                                if (updateRequired)
                                {
                                    endpoint.UpdateFromServer();
                                }

                                lock (m_registrationLock)
                                {
                                    endpoint.UpdateBeforeConnect = false;
                                }

                                RequestHeader requestHeader = new RequestHeader();
                                requestHeader.Timestamp = DateTime.UtcNow;

                                // create the client.
                                client = RegistrationClient.Create(
                                    configuration,
                                    endpoint.Description,
                                    endpoint.Configuration,
                                    base.InstanceCertificate);

                                client.OperationTimeout = 10000;

                                // register the server.
                                if (m_useRegisterServer2)
                                {
                                    ExtensionObjectCollection discoveryConfiguration = new ExtensionObjectCollection();
                                    StatusCodeCollection configurationResults = null;
                                    DiagnosticInfoCollection diagnosticInfos = null;
                                    MdnsDiscoveryConfiguration mdnsDiscoveryConfig = new MdnsDiscoveryConfiguration();
                                    mdnsDiscoveryConfig.ServerCapabilities = configuration.ServerConfiguration.ServerCapabilities;
                                    mdnsDiscoveryConfig.MdnsServerName = Utils.GetHostName();
                                    ExtensionObject extensionObject = new ExtensionObject(mdnsDiscoveryConfig);
                                    discoveryConfiguration.Add(extensionObject);
                                    client.RegisterServer2(
                                        requestHeader,
                                        m_registrationInfo,
                                        discoveryConfiguration,
                                        out configurationResults,
                                        out diagnosticInfos);
                                }
                                else
                                {
                                    client.RegisterServer(requestHeader, m_registrationInfo);
                                }

                                return true;
                            }
                            catch (Exception e)
                            {
                                Utils.Trace("RegisterServer{0} failed for at: {1}. Exception={2}",
                                    m_useRegisterServer2 ? "2" : "", endpoint.EndpointUrl, e.Message);
                                m_useRegisterServer2 = !m_useRegisterServer2;
                            }
                            finally
                            {
                                if (client != null)
                                {
                                    try
                                    {
                                        client.Close();
                                        client = null;
                                    }
                                    catch (Exception e)
                                    {
                                        Utils.Trace("Could not cleanly close connection with LDS. Exception={0}", e.Message);
                                    }
                                }
                            }
                        }
                    }
                    // retry to start with RegisterServer2 if both failed
                    m_useRegisterServer2 = true;
                }
            }
            finally
            {
                if (configuration != null)
                {
                    configuration.CertificateValidator.CertificateValidation -= registrationCertificateValidator;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks that the domains in the certificate match the current host.
        /// </summary>
        private void RegistrationValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            System.Net.IPAddress[] targetAddresses = Utils.GetHostAddresses(Utils.GetHostName());

            foreach (string domain in X509Utils.GetDomainsFromCertficate(e.Certificate))
            {
                System.Net.IPAddress[] actualAddresses = Utils.GetHostAddresses(domain);

                foreach (System.Net.IPAddress actualAddress in actualAddresses)
                {
                    foreach (System.Net.IPAddress targetAddress in targetAddresses)
                    {
                        if (targetAddress.Equals(actualAddress))
                        {
                            e.Accept = true;
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Registers the server endpoints with the LDS.
        /// </summary>
        /// <param name="state">The state.</param>
        private async void OnRegisterServer(object state)
        {
            try
            {
                lock (m_registrationLock)
                {
                    // halt any outstanding timer.
                    if (m_registrationTimer != null)
                    {
                        m_registrationTimer.Dispose();
                        m_registrationTimer = null;
                    }
                }

                if (await RegisterWithDiscoveryServer())
                {
                    // schedule next registration.                        
                    lock (m_registrationLock)
                    {
                        if (m_maxRegistrationInterval > 0)
                        {
                            m_registrationTimer = new Timer(
                                OnRegisterServer,
                                this,
                                m_maxRegistrationInterval,
                                Timeout.Infinite);

                            m_lastRegistrationInterval = m_minRegistrationInterval;
                            Utils.Trace("Register server succeeded. Registering again in {0} ms", m_maxRegistrationInterval);
                        }
                    }
                }
                else
                {
                    lock (m_registrationLock)
                    {
                        if (m_registrationTimer == null)
                        {
                            // calculate next registration attempt.
                            m_lastRegistrationInterval *= 2;

                            if (m_lastRegistrationInterval > m_maxRegistrationInterval)
                            {
                                m_lastRegistrationInterval = m_maxRegistrationInterval;
                            }

                            Utils.Trace("Register server failed. Trying again in {0} ms", m_lastRegistrationInterval);

                            // create timer.        
                            m_registrationTimer = new Timer(OnRegisterServer, this, m_lastRegistrationInterval, Timeout.Infinite);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected exception handling registration timer.");
            }
        }
        #endregion

        #region Protected Members used for Request Processing
        /// <summary>
        /// The synchronization object.
        /// </summary>
        protected object Lock => m_lock;

        /// <summary>
        /// The state object associated with the server.
        /// </summary>
        /// <value>The server internal data.</value>
        protected ServerInternalData ServerInternal
        {
            get
            {
                ServerInternalData serverInternal = m_serverInternal;

                if (serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                return serverInternal;
            }
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        protected override void ValidateRequest(RequestHeader requestHeader)
        {
            // check for server error.
            ServiceResult error = ServerError;

            if (ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }

            // check server state.
            ServerInternalData serverInternal = m_serverInternal;

            if (serverInternal == null || !serverInternal.IsRunning)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            base.ValidateRequest(requestHeader);
        }

        /// <summary>
        /// Updates the server state.
        /// </summary>
        /// <param name="state">The state.</param>
        protected virtual void SetServerState(ServerState state)
        {
            lock (m_lock)
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_serverInternal.CurrentState = state;
            }
        }

        /// <summary>
        /// Reports an error during initialization after the base server object has been started.
        /// </summary>
        /// <param name="error">The error.</param>
        protected virtual void SetServerError(ServiceResult error)
        {
            lock (m_lock)
            {
                ServerError = error;
            }
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by a client.
        /// </summary>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="result">The result.</param>
        protected virtual void OnApplicationCertificateError(byte[] clientCertificate, ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Inspects the software certificates provided by the server.
        /// </summary>
        /// <param name="softwareCertificates">The software certificates.</param>
        protected virtual void ValidateSoftwareCertificates(List<SoftwareCertificate> softwareCertificates)
        {
            // always accept valid certificates.
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <returns></returns>
        protected virtual OperationContext ValidateRequest(RequestHeader requestHeader, RequestType requestType)
        {
            base.ValidateRequest(requestHeader);

            if (!ServerInternal.IsRunning)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            OperationContext context = ServerInternal.SessionManager.ValidateRequest(requestHeader, requestType);

            Utils.Trace(
                (int)Utils.TraceMasks.Service,
                "{0} Validated. ID={1}",
                context.RequestType,
                context.RequestId);

            // notify the request manager.
            ServerInternal.RequestManager.RequestReceived(context);

            return context;
        }

        /// <summary>
        /// Translates an exception.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The ServiceResultException e.</param>
        /// <returns>Returns an exception thrown when a UA defined error occurs, the return type is <seealso cref="ServiceResultException"/>.</returns>
        protected virtual ServiceResultException TranslateException(OperationContext context, ServiceResultException e)
        {
            IList<string> preferredLocales = null;

            if (context != null && context.Session != null)
            {
                preferredLocales = context.Session.PreferredLocales;
            }

            return TranslateException(context.DiagnosticsMask, preferredLocales, e);
        }

        /// <summary>
        /// Translates an exception.
        /// </summary>
        /// <param name="diagnosticsMasks">The fields to return.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="e">The ServiceResultException e.</param>
        /// <returns>Returns an exception thrown when a UA defined error occurs, the return type is <seealso cref="ServiceResultException"/>.</returns>
        protected virtual ServiceResultException TranslateException(DiagnosticsMasks diagnosticsMasks, IList<string> preferredLocales, ServiceResultException e)
        {
            if (e == null)
            {
                return null;
            }

            // check if inner result required.
            ServiceResult innerResult = null;

            if ((diagnosticsMasks & (DiagnosticsMasks.ServiceInnerDiagnostics | DiagnosticsMasks.ServiceInnerStatusCode)) != 0)
            {
                innerResult = e.InnerResult;
            }

            // check if translated text required.
            LocalizedText translatedText = null;

            if ((diagnosticsMasks & DiagnosticsMasks.ServiceLocalizedText) != 0)
            {
                translatedText = e.LocalizedText;
            }

            // create new result object.
            ServiceResult result = new ServiceResult(
                e.StatusCode,
                e.SymbolicId,
                e.NamespaceUri,
                translatedText,
                e.AdditionalInfo,
                innerResult);

            // translate result.
            result = m_serverInternal.ResourceManager.Translate(preferredLocales, result);
            return new ServiceResultException(result);
        }

        /// <summary>
        /// Translates a service result.
        /// </summary>
        /// <param name="diagnosticsMasks">The fields to return.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="result">The result.</param>
        /// <returns>Returns a class that combines the status code and diagnostic info structures.</returns>
        protected virtual ServiceResult TranslateResult(DiagnosticsMasks diagnosticsMasks, IList<string> preferredLocales, ServiceResult result)
        {
            if (result == null)
            {
                return null;
            }

            return m_serverInternal.ResourceManager.Translate(preferredLocales, result);
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="context">The operation context.</param>
        protected virtual void OnRequestComplete(OperationContext context)
        {
            lock (m_lock)
            {
                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_serverInternal.RequestManager.RequestCompleted(context);
            }
        }
        #endregion

        #region Protected Members used for Initialization
        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="Opc.Ua.ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2109:ReviewVisibleEventHandlers")]
        protected virtual async void OnConfigurationChanged(object sender, ConfigurationWatcherEventArgs args)
        {
            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration.Load(
                    new FileInfo(args.FilePath),
                    Configuration.ApplicationType,
                    Configuration.GetType());

                OnUpdateConfiguration(configuration);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not load updated configuration file from: {0}", args);
            }
        }

        /// <summary>
        /// Called when the server configuration is changed on disk.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <remarks>
        /// Servers are free to ignore changes if it is difficult/impossible to apply them without a restart.
        /// </remarks>
        protected override void OnUpdateConfiguration(ApplicationConfiguration configuration)
        {
            lock (m_lock)
            {
                // update security configuration.
                configuration.SecurityConfiguration.Validate();

                Configuration.SecurityConfiguration.TrustedIssuerCertificates = configuration.SecurityConfiguration.TrustedIssuerCertificates;
                Configuration.SecurityConfiguration.TrustedPeerCertificates = configuration.SecurityConfiguration.TrustedPeerCertificates;
                Configuration.SecurityConfiguration.RejectedCertificateStore = configuration.SecurityConfiguration.RejectedCertificateStore;

                Configuration.CertificateValidator.Update(Configuration.SecurityConfiguration).Wait();

                // update trace configuration.
                Configuration.TraceConfiguration = configuration.TraceConfiguration;

                if (Configuration.TraceConfiguration == null)
                {
                    Configuration.TraceConfiguration = new TraceConfiguration();
                }

                Configuration.TraceConfiguration.ApplySettings();
            }
        }


        /// <summary>
        /// Called before the server starts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            lock (m_lock)
            {
                base.OnServerStarting(configuration);

                // save minimum nonce length.
                m_minNonceLength = configuration.SecurityConfiguration.NonceLength;

                // try first RegisterServer2
                m_useRegisterServer2 = true;
            }
        }

        /// <summary>
        /// Creates the endpoints and creates the hosts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="serverDescription">The server description.</param>
        /// <param name="endpoints">The endpoints.</param>
        /// <returns>
        /// Returns IList of a host for a UA service.
        /// </returns>
        protected override IList<Task> InitializeServiceHosts(
            ApplicationConfiguration configuration,
            out ApplicationDescription serverDescription,
            out EndpointDescriptionCollection endpoints)
        {
            serverDescription = null;
            endpoints = null;

            Dictionary<string, Task> hosts = new Dictionary<string, Task>();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration.SecurityPolicies.Count == 0)
            {
                configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
            }

            // ensure at least one user token policy exists.
            if (configuration.ServerConfiguration.UserTokenPolicies.Count == 0)
            {
                UserTokenPolicy userTokenPolicy = new UserTokenPolicy();

                userTokenPolicy.TokenType = UserTokenType.Anonymous;
                userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
            }

            // set server description.
            serverDescription = new ApplicationDescription();

            serverDescription.ApplicationUri = configuration.ApplicationUri;
            serverDescription.ApplicationName = new LocalizedText("en-US", configuration.ApplicationName);
            serverDescription.ApplicationType = configuration.ApplicationType;
            serverDescription.ProductUri = configuration.ProductUri;
            serverDescription.DiscoveryUrls = GetDiscoveryUrls();

            endpoints = new EndpointDescriptionCollection();
            IList<EndpointDescription> endpointsForHost = null;

            foreach (var scheme in Utils.DefaultUriSchemes)
            {
                var binding = TransportBindings.Listeners.GetBinding(scheme);
                if (binding != null)
                {
                    endpointsForHost = binding.CreateServiceHost(
                        this,
                        hosts,
                        configuration,
                        configuration.ServerConfiguration.BaseAddresses,
                        serverDescription,
                        configuration.ServerConfiguration.SecurityPolicies,
                        InstanceCertificate,
                        InstanceCertificateChain
                        );
                    endpoints.AddRange(endpointsForHost);
                }
            }

            return new List<Task>(hosts.Values);
        }

        /// <summary>
        /// Returns the service contract to use.
        /// </summary>
        protected override Type GetServiceContract()
        {
            return typeof(ISessionEndpoint);
        }

        /// <summary>
        /// Returns an instance of the endpoint to use.
        /// </summary>
        protected override EndpointBase GetEndpointInstance(ServerBase server)
        {
            return new SessionEndpoint(server);
        }

        /// <summary>
        /// Starts the server application.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected override void StartApplication(ApplicationConfiguration configuration)
        {
            base.StartApplication(configuration);

            lock (m_lock)
            {
                try
                {
                    // create the datastore for the instance.
                    m_serverInternal = new ServerInternalData(
                        ServerProperties,
                        configuration,
                        MessageContext,
                        new CertificateValidator(),
                        InstanceCertificate);

                    // create the manager responsible for providing localized string resources.                    
                    ResourceManager resourceManager = CreateResourceManager(m_serverInternal, configuration);

                    // create the manager responsible for incoming requests.
                    RequestManager requestManager = CreateRequestManager(m_serverInternal, configuration);

                    // create the master node manager.
                    MasterNodeManager masterNodeManager = CreateMasterNodeManager(m_serverInternal, configuration);

                    // add the node manager to the datastore. 
                    m_serverInternal.SetNodeManager(masterNodeManager);

                    // put the node manager into a state that allows it to be used by other objects.
                    masterNodeManager.Startup();

                    // create the manager responsible for handling events.
                    EventManager eventManager = CreateEventManager(m_serverInternal, configuration);

                    // creates the server object. 
                    m_serverInternal.CreateServerObject(
                        eventManager,
                        resourceManager,
                        requestManager);

                    // do any additional processing now that the node manager is up and running.
                    OnNodeManagerStarted(m_serverInternal);

                    // create the manager responsible for aggregates.
                    m_serverInternal.AggregateManager = CreateAggregateManager(m_serverInternal, configuration);

                    // start the session manager.
                    SessionManager sessionManager = CreateSessionManager(m_serverInternal, configuration);
                    sessionManager.Startup();

                    // start the subscription manager.
                    SubscriptionManager subscriptionManager = CreateSubscriptionManager(m_serverInternal, configuration);
                    subscriptionManager.Startup();

                    // add the session manager to the datastore. 
                    m_serverInternal.SetSessionManager(sessionManager, subscriptionManager);

                    ServerError = null;

                    // setup registration information.
                    lock (m_registrationLock)
                    {
                        m_maxRegistrationInterval = configuration.ServerConfiguration.MaxRegistrationInterval;

                        ApplicationDescription serverDescription = ServerDescription;

                        m_registrationInfo = new RegisteredServer();

                        m_registrationInfo.ServerUri = serverDescription.ApplicationUri;
                        m_registrationInfo.ServerNames.Add(serverDescription.ApplicationName);
                        m_registrationInfo.ProductUri = serverDescription.ProductUri;
                        m_registrationInfo.ServerType = serverDescription.ApplicationType;
                        m_registrationInfo.GatewayServerUri = null;
                        m_registrationInfo.IsOnline = true;
                        m_registrationInfo.SemaphoreFilePath = null;

                        // add all discovery urls.
                        string computerName = Utils.GetHostName();

                        for (int ii = 0; ii < BaseAddresses.Count; ii++)
                        {
                            UriBuilder uri = new UriBuilder(BaseAddresses[ii].DiscoveryUrl);

                            if (String.Compare(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                uri.Host = computerName;
                            }

                            m_registrationInfo.DiscoveryUrls.Add(uri.ToString());
                        }

                        // build list of registration endpoints.
                        m_registrationEndpoints = new ConfiguredEndpointCollection(configuration);

                        EndpointDescription endpoint = configuration.ServerConfiguration.RegistrationEndpoint;

                        if (endpoint == null)
                        {
                            endpoint = new EndpointDescription();
                            endpoint.EndpointUrl = Utils.Format(Utils.DiscoveryUrls[0], "localhost");
                            endpoint.SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256);
                            endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                            endpoint.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                            endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;
                        }

                        m_registrationEndpoints.Add(endpoint);

                        m_minRegistrationInterval = 1000;
                        m_lastRegistrationInterval = m_minRegistrationInterval;

                        // start registration timer.
                        if (m_registrationTimer != null)
                        {
                            m_registrationTimer.Dispose();
                            m_registrationTimer = null;
                        }

                        if (m_maxRegistrationInterval > 0)
                        {
                            m_registrationTimer = new Timer(OnRegisterServer, this, m_minRegistrationInterval, Timeout.Infinite);
                        }
                    }

                    // set the server status as running.
                    SetServerState(ServerState.Running);

                    // all initialization is complete.
                    OnServerStarted(m_serverInternal);

                    // monitor the configuration file.
                    if (!String.IsNullOrEmpty(configuration.SourceFilePath))
                    {
                        m_configurationWatcher = new ConfigurationWatcher(configuration);
                        m_configurationWatcher.Changed += new EventHandler<ConfigurationWatcherEventArgs>(this.OnConfigurationChanged);
                    }

                    CertificateValidator.CertificateUpdate += OnCertificateUpdate;
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error starting application");
                    m_serverInternal = null;
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadInternalError, "Unexpected error starting application");
                    ServerError = error;
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// Called before the server stops
        /// </summary>
        protected override void OnServerStopping()
        {
            ShutDownDelay();

            // halt any outstanding timer.
            lock (m_registrationLock)
            {
                if (m_registrationTimer != null)
                {
                    m_registrationTimer.Dispose();
                    m_registrationTimer = null;
                }
            }

            // attempt graceful shutdown the server.
            try
            {
                if (m_maxRegistrationInterval > 0)
                {
                    // unregister from Discovery Server
                    m_registrationInfo.IsOnline = false;
                    RegisterWithDiscoveryServer().Wait();
                }

                lock (m_lock)
                {
                    if (m_serverInternal != null)
                    {
                        m_serverInternal.SubscriptionManager.Shutdown();
                        m_serverInternal.SessionManager.Shutdown();
                        m_serverInternal.NodeManager.Shutdown();
                    }
                }
            }
            catch (Exception e)
            {
                ServerError = new ServiceResult(e);
            }
            finally
            {
                // ensure that everything is cleaned up.
                if (m_serverInternal != null)
                {
                    Utils.SilentDispose(m_serverInternal);
                    m_serverInternal = null;
                }
            }
        }

        /// <summary>
        /// Implements the server shutdown delay if session are connected.
        /// </summary>
        protected void ShutDownDelay()
        {
            try
            {
                // check for connected clients.
                IList<Session> currentessions = this.ServerInternal.SessionManager.GetSessions();

                if (currentessions.Count > 0)
                {
                    // provide some time for the connected clients to detect the shutdown state.
                    ServerInternal.Status.Value.ShutdownReason = new LocalizedText("en-US", "Application closed.");
                    ServerInternal.Status.Variable.ShutdownReason.Value = new LocalizedText("en-US", "Application closed.");
                    ServerInternal.Status.Value.State = ServerState.Shutdown;
                    ServerInternal.Status.Variable.State.Value = ServerState.Shutdown;
                    ServerInternal.Status.Variable.ClearChangeMasks(ServerInternal.DefaultSystemContext, true);

                    for (int timeTillShutdown = Configuration.ServerConfiguration.ShutdownDelay; timeTillShutdown > 0; timeTillShutdown--)
                    {
                        ServerInternal.Status.Value.SecondsTillShutdown = (uint)timeTillShutdown;
                        ServerInternal.Status.Variable.SecondsTillShutdown.Value = (uint)timeTillShutdown;
                        ServerInternal.Status.Variable.ClearChangeMasks(ServerInternal.DefaultSystemContext, true);

                        // exit if all client connections are closed.
                        if (ServerInternal.SessionManager.GetSessions().Count == 0)
                        {
                            break;
                        }

                        Thread.Sleep(1000);
                    }
                }
            }
            catch
            {
                // ignore error during shutdown procedure.
            }
        }

        /// <summary>
        /// Creates the request manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>
        /// Returns an object that manages requests from within the server, return type is <seealso cref="RequestManager"/>.
        /// </returns>
        protected virtual RequestManager CreateRequestManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new RequestManager(server);
        }

        /// <summary>
        /// Creates the aggregate manager used by the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The manager.</returns>
        protected virtual AggregateManager CreateAggregateManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            AggregateManager manager = new AggregateManager(server);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Interpolative, BrowseNames.AggregateFunction_Interpolative, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Average, BrowseNames.AggregateFunction_Average, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_TimeAverage, BrowseNames.AggregateFunction_TimeAverage, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_TimeAverage2, BrowseNames.AggregateFunction_TimeAverage2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Total, BrowseNames.AggregateFunction_Total, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Total2, BrowseNames.AggregateFunction_Total2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Minimum, BrowseNames.AggregateFunction_Minimum, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Maximum, BrowseNames.AggregateFunction_Maximum, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MinimumActualTime, BrowseNames.AggregateFunction_MinimumActualTime, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MaximumActualTime, BrowseNames.AggregateFunction_MaximumActualTime, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Range, BrowseNames.AggregateFunction_Range, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Minimum2, BrowseNames.AggregateFunction_Minimum2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Maximum2, BrowseNames.AggregateFunction_Maximum2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MinimumActualTime2, BrowseNames.AggregateFunction_MinimumActualTime2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MaximumActualTime2, BrowseNames.AggregateFunction_MaximumActualTime2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Range2, BrowseNames.AggregateFunction_Range2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Count, BrowseNames.AggregateFunction_Count, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_AnnotationCount, BrowseNames.AggregateFunction_AnnotationCount, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationInStateZero, BrowseNames.AggregateFunction_DurationInStateZero, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationInStateNonZero, BrowseNames.AggregateFunction_DurationInStateNonZero, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_NumberOfTransitions, BrowseNames.AggregateFunction_NumberOfTransitions, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Start, BrowseNames.AggregateFunction_Start, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_End, BrowseNames.AggregateFunction_End, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Delta, BrowseNames.AggregateFunction_Delta, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_StartBound, BrowseNames.AggregateFunction_StartBound, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_EndBound, BrowseNames.AggregateFunction_EndBound, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DeltaBounds, BrowseNames.AggregateFunction_DeltaBounds, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationGood, BrowseNames.AggregateFunction_DurationGood, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationBad, BrowseNames.AggregateFunction_DurationBad, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_PercentGood, BrowseNames.AggregateFunction_PercentGood, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_PercentBad, BrowseNames.AggregateFunction_PercentBad, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_WorstQuality, BrowseNames.AggregateFunction_WorstQuality, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_WorstQuality2, BrowseNames.AggregateFunction_WorstQuality2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_StandardDeviationPopulation, BrowseNames.AggregateFunction_StandardDeviationPopulation, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_VariancePopulation, BrowseNames.AggregateFunction_VariancePopulation, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_StandardDeviationSample, BrowseNames.AggregateFunction_StandardDeviationSample, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_VarianceSample, BrowseNames.AggregateFunction_VarianceSample, Aggregators.CreateStandardCalculator);

            return manager;
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages access to localized resources, the return type is <seealso cref="ResourceManager"/>.</returns>
        protected virtual ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(server, configuration);

            // load default text for all status codes.
            resourceManager.LoadDefaultText();

            return resourceManager;
        }

        /// <summary>
        /// Creates the master node manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns the master node manager for the server, the return type is <seealso cref="MasterNodeManager"/>.</returns>
        protected virtual MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new MasterNodeManager(server, configuration, null);
        }

        /// <summary>
        /// Creates the event manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages all events raised within the server, the return type is <seealso cref="EventManager"/>.</returns>
        protected virtual EventManager CreateEventManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new EventManager(server, (uint)configuration.ServerConfiguration.MaxEventQueueSize);
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="SessionManager"/>.</returns>
        protected virtual SessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new SessionManager(server, configuration);
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="SubscriptionManager"/>.</returns>
        protected virtual SubscriptionManager CreateSubscriptionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new SubscriptionManager(server, configuration);
        }

        /// <summary>
        /// Called after the node managers have been started.
        /// </summary>
        /// <param name="server">The server.</param>
        protected virtual void OnNodeManagerStarted(IServerInternal server)
        {
            // may be overridden by the subclass.
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        /// <param name="server">The server.</param>
        protected virtual void OnServerStarted(IServerInternal server)
        {
            // may be overridden by the subclass.
        }
        #endregion

        #region Private Fields
        private readonly object m_lock = new object();
        private readonly object m_registrationLock = new object();
        private ServerInternalData m_serverInternal;
        private ConfigurationWatcher m_configurationWatcher;
        private ConfiguredEndpointCollection m_registrationEndpoints;
        private RegisteredServer m_registrationInfo;
        private Timer m_registrationTimer;
        private int m_minRegistrationInterval;
        private int m_maxRegistrationInterval;
        private int m_lastRegistrationInterval;
        private int m_minNonceLength;
        private bool m_useRegisterServer2;
        #endregion
    }
}
