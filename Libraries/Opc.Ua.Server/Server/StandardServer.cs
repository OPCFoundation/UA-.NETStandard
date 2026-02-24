/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public class StandardServer : SessionServerBase, IStandardServer
    {
        /// <inheritdoc/>
        public StandardServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <inheritdoc/>
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

                if (CertificateValidator != null)
                {
                    CertificateValidator.CertificateUpdate -= OnCertificateUpdateAsync;
                }
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public override async ValueTask<FindServersResponse> FindServersAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            string endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> serverUris,
            CancellationToken ct)
        {
            ApplicationDescriptionCollection servers = [];

            ValidateRequest(requestHeader);

            await m_semaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
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
                    servers = [];
                    return new FindServersResponse
                    {
                        ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                        Servers = servers
                    };
                }

                // build list of unique servers.
                var uniqueServers = new Dictionary<string, ApplicationDescription>();

                foreach (EndpointDescription description in GetEndpoints())
                {
                    ApplicationDescription server = description.Server;

                    // skip servers that have been processed.
                    if (uniqueServers.ContainsKey(server.ApplicationUri))
                    {
                        continue;
                    }

                    // check client is filtering by server uri.
                    if (serverUris.Count > 0 &&
                        serverUris.Find(f => f == server.ApplicationUri) == null)
                    {
                        continue;
                    }

                    // localize the application name if requested.
                    LocalizedText applicationName = server.ApplicationName;

                    if (localeIds.Count > 0)
                    {
                        applicationName = m_serverInternal.ResourceManager
                            .Translate(localeIds, applicationName);
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
            finally
            {
                m_semaphoreSlim.Release();
            }

            return new FindServersResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                Servers = servers
            };
        }

        /// <inheritdoc/>
        public override async ValueTask<GetEndpointsResponse> GetEndpointsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            string endpointUrl,
            ArrayOf<string> localeIds,
            ArrayOf<string> profileUris,
            CancellationToken ct)
        {
            EndpointDescriptionCollection endpoints = null;

            ValidateRequest(requestHeader);

            await m_semaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // filter by profile.
                IList<BaseAddress> baseAddresses = FilterByProfile(profileUris, BaseAddresses);

                // get the descriptions.
                endpoints = GetEndpointDescriptions(endpointUrl, baseAddresses, localeIds);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }

            return new GetEndpointsResponse
            {
                ResponseHeader = CreateResponse(requestHeader, StatusCodes.Good),
                Endpoints = endpoints
            };
        }

        /// <summary>
        /// Returns the endpoints that match the base address and endpoint url.
        /// </summary>
        protected EndpointDescriptionCollection GetEndpointDescriptions(
            string endpointUrl,
            IList<BaseAddress> baseAddresses,
            ArrayOf<string> localeIds)
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
                LocalizedText applicationName = ServerDescription.ApplicationName;

                if (localeIds.Count > 0)
                {
                    applicationName = m_serverInternal.ResourceManager
                        .Translate(localeIds, applicationName);
                }

                // translate the application description.
                ApplicationDescription application = TranslateApplicationDescription(
                    parsedEndpointUrl,
                    ServerDescription,
                    baseAddresses,
                    applicationName);

                // translate the endpoint descriptions.
                endpoints = TranslateEndpointDescriptions(
                    parsedEndpointUrl,
                    baseAddresses,
                    Endpoints,
                    application);
            }

            return endpoints;
        }

        /// <inheritdoc/>
        public override void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            ServerInternal?.ReportAuditOpenSecureChannelEvent(
                globalChannelId,
                endpointDescription,
                request,
                clientCertificate,
                exception,
                m_logger);
        }

        /// <inheritdoc/>
        public override void ReportAuditCloseSecureChannelEvent(
            string globalChannelId,
            Exception exception)
        {
            ServerInternal?.ReportAuditCloseSecureChannelEvent(globalChannelId, exception, m_logger);
        }

        /// <inheritdoc/>
        public override void ReportAuditCertificateEvent(
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            ServerInternal?.ReportAuditCertificateEvent(clientCertificate, exception, m_logger);
        }

        /// <inheritdoc/>
        public override async ValueTask<CreateSessionResponse> CreateSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            ByteString clientNonce,
            ByteString clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct)
        {
            NodeId sessionId;
            NodeId authenticationToken;
            double revisedSessionTimeout = 0;
            ByteString serverNonce;
            ByteString serverCertificate = default;
            EndpointDescriptionCollection serverEndpoints = null;
            SignatureData serverSignature = null;
            uint maxRequestMessageSize = (uint)MessageContext.MaxMessageSize;

            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.CreateSession);
            ISession session = null;
            try
            {
                // check the server uri.
                if (!string.IsNullOrEmpty(serverUri) && serverUri != Configuration.ApplicationUri)
                {
                    throw new ServiceResultException(StatusCodes.BadServerUriInvalid);
                }

                bool requireEncryption = RequireEncryption(
                    context?.ChannelContext?.EndpointDescription);

                if (!requireEncryption && !clientCertificate.IsEmpty)
                {
                    requireEncryption = true;
                }

                X509Certificate2Collection clientIssuerCertificates = null;

                // validate client application instance certificate.
                X509Certificate2 parsedClientCertificate = null;

                if (requireEncryption && clientCertificate.Length > 0)
                {
                    try
                    {
                        X509Certificate2Collection clientCertificateChain
                            = Utils.ParseCertificateChainBlob(
                                clientCertificate,
                                m_serverInternal.Telemetry);
                        parsedClientCertificate = clientCertificateChain[0];

                        if (clientCertificateChain.Count > 1)
                        {
                            clientIssuerCertificates = [];
                            for (int i = 1; i < clientCertificateChain.Count; i++)
                            {
                                clientIssuerCertificates.Add(clientCertificateChain[i]);
                            }
                        }

                        if (context.SecurityPolicyUri != SecurityPolicies.None)
                        {
                            // verify if applicationUri from ApplicationDescription matches the applicationUris in the client certificate.
                            if (!string.IsNullOrEmpty(clientDescription.ApplicationUri))
                            {
                                if (!X509Utils.CompareApplicationUriWithCertificate(parsedClientCertificate, clientDescription.ApplicationUri))
                                {
                                    // report the AuditCertificateDataMismatch event for invalid uri
                                    ServerInternal?.ReportAuditCertificateDataMismatchEvent(
                                        parsedClientCertificate,
                                        null,
                                        clientDescription.ApplicationUri,
                                        StatusCodes.BadCertificateUriInvalid,
                                        m_logger);

                                    throw ServiceResultException.Create(
                                        StatusCodes.BadCertificateUriInvalid,
                                        "The URI specified in the ApplicationDescription {0} does not match the URIs in the Certificate.",
                                        clientDescription.ApplicationUri);
                                }

                                await CertificateValidator.ValidateAsync(clientCertificateChain, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // report audit event for client certificate
                        ReportAuditCertificateEvent(parsedClientCertificate, e);

                        OnApplicationCertificateError(clientCertificate, new ServiceResult(e));
                    }
                }

                // verify the nonce provided by the client.
                if (!clientNonce.IsEmpty)
                {
                    if (clientNonce.Length < m_minNonceLength)
                    {
                        throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                    }

                    // ignore nonce if security policy set to none
                    if (context.SecurityPolicyUri == SecurityPolicies.None)
                    {
                        clientNonce = default;
                    }
                }

                // load the certificate for the security profile
                X509Certificate2 instanceCertificate = InstanceCertificateTypesProvider
                    .GetInstanceCertificate(
                        context.SecurityPolicyUri);

                // create the session.
                CreateSessionResult result = await ServerInternal.SessionManager.CreateSessionAsync(
                        context,
                        instanceCertificate,
                        sessionName,
                        clientNonce,
                        clientDescription,
                        endpointUrl,
                        parsedClientCertificate,
                        clientIssuerCertificates,
                        requestedSessionTimeout,
                        maxResponseMessageSize,
                        ct)
                    .ConfigureAwait(false);

                session = result.Session;
                sessionId = result.SessionId;
                authenticationToken = result.AuthenticationToken;
                serverNonce = result.ServerNonce;
                revisedSessionTimeout = result.RevisedSessionTimeout;

                if (endpointUrl != null)
                {
                    try
                    {
                        // check the endpointurl
                        var configuredEndpoint = new ConfiguredEndpoint
                        {
                            EndpointUrl = new Uri(endpointUrl)
                        };

                        CertificateValidator.ValidateDomains(
                            instanceCertificate,
                            configuredEndpoint,
                            true);
                    }
                    catch (ServiceResultException sre)
                        when (sre.StatusCode == StatusCodes.BadCertificateHostNameInvalid)
                    {
                        m_logger.LogWarning(
                            "Server - Client connects with an endpointUrl [{EndpointUrl}] which does not match Server hostnames.",
                            endpointUrl);
                        ServerInternal.ReportAuditUrlMismatchEvent(
                            context?.AuditEntryId,
                            session,
                            revisedSessionTimeout,
                            endpointUrl,
                            m_logger);
                    }
                }

                var parameters =
                    ExtensionObject.ToEncodeable(
                        requestHeader.AdditionalHeader) as AdditionalParametersType;

                if (parameters != null)
                {
                    parameters = CreateSessionProcessAdditionalParameters(session, parameters);
                }

                await m_semaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // return the application instance certificate for the server.
                    if (requireEncryption)
                    {
                        // check if complete chain should be sent.
                        if (InstanceCertificateTypesProvider.SendCertificateChain)
                        {
                            serverCertificate = InstanceCertificateTypesProvider
                                .LoadCertificateChainRaw(instanceCertificate).ToByteString();
                        }
                        else
                        {
                            serverCertificate = instanceCertificate.RawData.ToByteString();
                        }
                    }

                    // return the endpoints supported by the server.
                    serverEndpoints = GetEndpointDescriptions(endpointUrl, BaseAddresses, default);

                    // sign the nonce provided by the client.
                    serverSignature = null;

                    //  sign the client nonce (if provided).
                    if (parsedClientCertificate != null && !clientNonce.IsEmpty)
                    {
                        var dataToSign = ByteString.Combine(
                            parsedClientCertificate.RawData.ToByteString(), clientNonce);
                        serverSignature = SecurityPolicies.Sign(
                            instanceCertificate,
                            context.SecurityPolicyUri,
                            dataToSign.ToArray());
                    }
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }

                lock (ServerInternal.DiagnosticsWriteLock)
                {
                    ServerInternal.ServerDiagnostics.CurrentSessionCount++;
                    ServerInternal.ServerDiagnostics.CumulatedSessionCount++;
                }

                m_logger.LogInformation("Server - SESSION CREATED. SessionId={SessionId}", sessionId);

                // report audit for successful create session
                ServerInternal.ReportAuditCreateSessionEvent(
                    context?.AuditEntryId,
                    session,
                    revisedSessionTimeout,
                    m_logger);

                ResponseHeader responseHeader = CreateResponse(requestHeader, StatusCodes.Good);

                if (parameters != null)
                {
                    responseHeader.AdditionalHeader = new ExtensionObject(parameters);
                }

                return new CreateSessionResponse
                {
                    ResponseHeader = responseHeader,
                    SessionId = sessionId,
                    AuthenticationToken = authenticationToken,
                    RevisedSessionTimeout = revisedSessionTimeout,
                    ServerNonce = serverNonce,
                    ServerCertificate = serverCertificate,
                    ServerEndpoints = serverEndpoints,
                    ServerSignature = serverSignature,
                    MaxRequestMessageSize = maxRequestMessageSize
                };
            }
            catch (ServiceResultException e)
            {
                m_logger.LogError("Server - SESSION CREATE failed. {ErrorMessage}", e.Message);

                // report the failed AuditCreateSessionEvent
                ServerInternal.ReportAuditCreateSessionEvent(
                    context?.AuditEntryId,
                    session,
                    revisedSessionTimeout,
                    m_logger,
                    e);

                if (session != null)
                {
                    ServerInternal.SessionManager.CloseSession(session.Id);
                }

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

                throw TranslateException((DiagnosticsMasks)requestHeader.ReturnDiagnostics, [], e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Process additional parameters during the ECC session creation and set the session's UserToken security policy
        /// </summary>
        /// <param name="session">The session</param>
        /// <param name="parameters">The additional parameters for the session</param>
        /// <returns>An AdditionalParametersType object containing the processed parameters</returns>
        protected virtual AdditionalParametersType CreateSessionProcessAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters)
        {
            AdditionalParametersType response = null;

            if (parameters != null && parameters.Parameters != null)
            {
                response = new AdditionalParametersType();

                foreach (KeyValuePair ii in parameters.Parameters)
                {
                    if (ii.Key == "ECDHPolicyUri")
                    {
                        string policyUri = ii.Value.ToString();

                        if (EccUtils.IsEccPolicy(policyUri))
                        {
                            session.SetEccUserTokenSecurityPolicy(policyUri);
                            EphemeralKeyType key = session.GetNewEccKey();
                            response.Parameters.Add(
                                new KeyValuePair
                                {
                                    Key = QualifiedName.From("ECDHKey"),
                                    Value = new ExtensionObject(key)
                                });
                        }
                        else
                        {
                            response.Parameters.Add(
                                new KeyValuePair
                                {
                                    Key = QualifiedName.From("ECDHKey"),
                                    Value = StatusCodes.BadSecurityPolicyRejected
                                });
                        }
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// Process additional parameters during ECC session activation
        /// </summary>
        /// <param name="session">The session</param>
        /// <param name="parameters">The additional parameters for the session</param>
        /// <returns>An AdditionalParametersType object containing the processed parameters</returns>
        protected virtual AdditionalParametersType ActivateSessionProcessAdditionalParameters(
            ISession session,
            AdditionalParametersType parameters)
        {
            AdditionalParametersType response = null;

            EphemeralKeyType key = session.GetNewEccKey();

            if (key != null)
            {
                response = new AdditionalParametersType();
                response.Parameters.Add(new KeyValuePair
                {
                    Key = QualifiedName.From("ECDHKey"),
                    Value = new ExtensionObject(key)
                });
            }

            return response;
        }

        /// <inheritdoc/>
        public override async ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            SignatureData clientSignature,
            ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
            ArrayOf<string> localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct)
        {
            ByteString serverNonce;
            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.ActivateSession);

            try
            {
                // activate the session.
                (bool identityChanged, serverNonce) = await ServerInternal.SessionManager.ActivateSessionAsync(
                        context,
                        requestHeader.AuthenticationToken,
                        clientSignature,
                        userIdentityToken,
                        userTokenSignature,
                        localeIds,
                        ct)
                    .ConfigureAwait(false);

                if (identityChanged)
                {
                    // TBD - call Node Manager and Subscription Manager.
                }

                ISession session = ServerInternal.SessionManager
                    .GetSession(requestHeader.AuthenticationToken);
                var parameters =
                    ExtensionObject.ToEncodeable(
                        requestHeader.AdditionalHeader) as AdditionalParametersType;
                parameters = ActivateSessionProcessAdditionalParameters(session, parameters);

                m_logger.LogInformation("Server - SESSION ACTIVATED.");

                // report the audit event for session activate
                ServerInternal.ReportAuditActivateSessionEvent(
                    m_logger,
                    context?.AuditEntryId,
                    session);

                ResponseHeader responseHeader = CreateResponse(requestHeader, StatusCodes.Good);

                if (parameters != null)
                {
                    responseHeader.AdditionalHeader = new ExtensionObject(parameters);
                }
                return new ActivateSessionResponse
                {
                    ResponseHeader = responseHeader,
                    ServerNonce = serverNonce,
                    Results = results,
                    DiagnosticInfos = diagnosticInfos
                };
            }
            catch (ServiceResultException e)
            {
                m_logger.LogInformation("Server - SESSION ACTIVATE failed. {ErrorMessage}", e.Message);

                // report the audit event for failed session activate
                ISession session = ServerInternal.SessionManager
                    .GetSession(requestHeader.AuthenticationToken);
                ServerInternal.ReportAuditActivateSessionEvent(
                    m_logger,
                    context?.AuditEntryId,
                    session,
                    e);

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

                throw TranslateException(
                    (DiagnosticsMasks)requestHeader.ReturnDiagnostics,
                    localeIds,
                    e);
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
            return
                error == StatusCodes.BadUserSignatureInvalid ||
                error == StatusCodes.BadUserAccessDenied ||
                error == StatusCodes.BadSecurityPolicyRejected ||
                error == StatusCodes.BadSecurityModeRejected ||
                error == StatusCodes.BadSecurityChecksFailed ||
                error == StatusCodes.BadSecureChannelTokenUnknown ||
                error == StatusCodes.BadSecureChannelIdInvalid ||
                error == StatusCodes.BadNoValidCertificates ||
                error == StatusCodes.BadIdentityTokenInvalid ||
                error == StatusCodes.BadIdentityTokenRejected ||
                error == StatusCodes.BadIdentityChangeNotSupported ||
                error == StatusCodes.BadCertificateUseNotAllowed ||
                error == StatusCodes.BadCertificateUriInvalid ||
                error == StatusCodes.BadCertificateUntrusted ||
                error == StatusCodes.BadCertificateTimeInvalid ||
                error == StatusCodes.BadCertificateRevoked ||
                error == StatusCodes.BadCertificateRevocationUnknown ||
                error == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                error == StatusCodes.BadCertificateIssuerTimeInvalid ||
                error == StatusCodes.BadCertificateIssuerRevoked ||
                error == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                error == StatusCodes.BadCertificateInvalid ||
                error == StatusCodes.BadCertificateHostNameInvalid ||
                error == StatusCodes.BadCertificatePolicyCheckFailed ||
                error == StatusCodes.BadApplicationSignatureInvalid;
        }

        /// <summary>
        /// Creates the response header.
        /// </summary>
        /// <param name="requestHeader">The object that contains description for the RequestHeader DataType.</param>
        /// <param name="exception">The exception used to create DiagnosticInfo assigned to the ServiceDiagnostics.</param>
        /// <returns>Returns a description for the ResponseHeader DataType. </returns>
        protected ResponseHeader CreateResponse(
            RequestHeader requestHeader,
            ServiceResultException exception)
        {
            var responseHeader = new ResponseHeader
            {
                ServiceResult = exception.StatusCode,

                Timestamp = DateTime.UtcNow,
                RequestHandle = requestHeader.RequestHandle
            };

            var stringTable = new StringTable();
            responseHeader.ServiceDiagnostics = new DiagnosticInfo(
                exception,
                (DiagnosticsMasks)requestHeader.ReturnDiagnostics,
                true,
                stringTable,
                m_logger);
            responseHeader.StringTable = stringTable.ToArray();

            return responseHeader;
        }

        /// <inheritdoc/>
        public override async ValueTask<CloseSessionResponse> CloseSessionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.CloseSession);
            try
            {
                ISession session = ServerInternal.SessionManager
                    .GetSession(requestHeader.AuthenticationToken);

                await ServerInternal.CloseSessionAsync(context, context.Session.Id, deleteSubscriptions, ct)
                    .ConfigureAwait(false);

                // report the audit event for close session
                ServerInternal.ReportAuditCloseSessionEvent(
                    context.AuditEntryId,
                    session,
                    m_logger,
                    "Session/CloseSession");

                return new CloseSessionResponse
                {
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override ValueTask<CancelResponse> CancelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint requestHandle,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Cancel);
            CancelResponse response;
            try
            {
                m_serverInternal.RequestManager.CancelRequests(requestHandle, out uint cancelCount);

                response = new CancelResponse
                {
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable),
                    CancelCount = cancelCount
                };
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
            return new ValueTask<CancelResponse>(response);
        }

        /// <inheritdoc/>
        public override async ValueTask<BrowseResponse> BrowseAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Browse);

            try
            {
                ValidateOperationLimits(nodesToBrowse, OperationLimits.MaxNodesPerBrowse);

                (BrowseResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.BrowseAsync(
                        context,
                        view,
                        requestedMaxReferencesPerNode,
                        nodesToBrowse,
                        ct)
                    .ConfigureAwait(false);

                return new BrowseResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override async ValueTask<BrowseNextResponse> BrowseNextAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ArrayOf<ByteString> continuationPoints,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.BrowseNext);

            try
            {
                ValidateOperationLimits(continuationPoints, OperationLimits.MaxNodesPerBrowse);

                (BrowseResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.BrowseNextAsync(
                        context,
                        releaseContinuationPoints,
                        continuationPoints,
                        ct)
                    .ConfigureAwait(false);

                return new BrowseNextResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<NodeId> nodesToRegister,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.RegisterNodes);
            RegisterNodesResponse response;
            try
            {
                ValidateOperationLimits(nodesToRegister, OperationLimits.MaxNodesPerRegisterNodes);

                m_serverInternal.NodeManager
                    .RegisterNodes(context, nodesToRegister, out NodeIdCollection registeredNodeIds);

                response = new RegisterNodesResponse
                {
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable),
                    RegisteredNodeIds = registeredNodeIds
                };
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
            return new ValueTask<RegisterNodesResponse>(response);
        }

        /// <inheritdoc/>
        public override ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<NodeId> nodesToUnregister,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.UnregisterNodes);
            UnregisterNodesResponse response;
            try
            {
                ValidateOperationLimits(
                    nodesToUnregister,
                    OperationLimits.MaxNodesPerRegisterNodes);

                m_serverInternal.NodeManager.UnregisterNodes(context, nodesToUnregister);

                response = new UnregisterNodesResponse
                {
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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
            return new ValueTask<UnregisterNodesResponse>(response);
        }

        /// <inheritdoc/>
        public override async ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<BrowsePath> browsePaths,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.TranslateBrowsePathsToNodeIds);

            try
            {
                ValidateOperationLimits(
                    browsePaths,
                    OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);

                foreach (BrowsePath bp in browsePaths)
                {
                    ValidateOperationLimits(
                        bp.RelativePath.Elements.Count,
                        OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);
                }

                (BrowsePathResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.TranslateBrowsePathsToNodeIdsAsync(
                        context,
                        browsePaths,
                        ct)
                    .ConfigureAwait(false);

                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override async ValueTask<ReadResponse> ReadAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Read);

            try
            {
                ValidateOperationLimits(nodesToRead, OperationLimits.MaxNodesPerRead);

                (DataValueCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.ReadAsync(
                        context,
                        maxAge,
                        timestampsToReturn,
                        nodesToRead,
                        ct).ConfigureAwait(false);

                return new ReadResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

                ServerInternal.ReportAuditEvent(context, "Read", e, m_logger);

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<HistoryReadResponse> HistoryReadAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.HistoryRead);

            try
            {
                if (historyReadDetails.Body is ReadEventDetails)
                {
                    ValidateOperationLimits(
                        nodesToRead,
                        OperationLimits.MaxNodesPerHistoryReadEvents);
                }
                else
                {
                    ValidateOperationLimits(
                        nodesToRead,
                        OperationLimits.MaxNodesPerHistoryReadData);
                }

                (HistoryReadResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.HistoryReadAsync(
                        context,
                        historyReadDetails,
                        timestampsToReturn,
                        releaseContinuationPoints,
                        nodesToRead,
                        ct)
                    .ConfigureAwait(false);

                return new HistoryReadResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

                ServerInternal.ReportAuditEvent(context, "HistoryRead", e, m_logger);

                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<WriteResponse> WriteAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Write);

            try
            {
                ValidateOperationLimits(nodesToWrite, OperationLimits.MaxNodesPerWrite);

                (StatusCodeCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager
                        .WriteAsync(context, nodesToWrite, ct)
                        .ConfigureAwait(false);

                return new WriteResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override async ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<ExtensionObject> historyUpdateDetails,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.HistoryUpdate);

            try
            {
                // check only for BadNothingToDo here
                // MaxNodesPerHistoryUpdateEvents & MaxNodesPerHistoryUpdateData
                // must be checked in NodeManager (TODO)
                ValidateOperationLimits(historyUpdateDetails);

                (HistoryUpdateResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.HistoryUpdateAsync(context, historyUpdateDetails, ct).ConfigureAwait(false);

                return new HistoryUpdateResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override async ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.CreateSubscription);

            try
            {
                CreateSubscriptionResponse response = await ServerInternal.SubscriptionManager.CreateSubscriptionAsync(
                    context,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return response;
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

        /// <inheritdoc/>
        public override async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.TransferSubscriptions);

            try
            {
                ValidateOperationLimits(subscriptionIds);

                TransferSubscriptionsResponse response = await ServerInternal.SubscriptionManager.TransferSubscriptionsAsync(
                    context,
                    subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return response;
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

        /// <inheritdoc/>
        public override async ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteSubscriptions);

            try
            {
                ValidateOperationLimits(subscriptionIds);

                DeleteSubscriptionsResponse response = await ServerInternal.SubscriptionManager.DeleteSubscriptionsAsync(
                    context,
                    subscriptionIds,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return response;
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

        /// <inheritdoc/>
        public override async ValueTask<PublishResponse> PublishAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Publish);

            try
            {
                /*
                // check if there is an odd delay.
                if (DateTime.UtcNow > requestHeader.Timestamp.AddMilliseconds(100))
                {
                    m_logger.LogTrace(m_eventId,
                        "WARNING. Unexpected delay receiving Publish request. Time={0:hh:mm:ss.fff}, ReceiveTime={1:hh:mm:ss.fff}",
                        DateTime.UtcNow,
                        requestHeader.Timestamp);
                }
                */

                m_logger.LogTrace(
                    "PUBLISH #{RequestHandle} RECEIVED. TIME={Timestamp:hh:mm:ss.fff}",
                    requestHeader.RequestHandle,
                    requestHeader.Timestamp);

                PublishResponse response = await ServerInternal.SubscriptionManager.PublishAsync(
                    context,
                    subscriptionAcknowledgements,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                /*
                if (response.NotificationMessage != null)
                {
                    m_logger.LogTrace(m_eventId,
                        "PublishResponse: SubId={0} SeqNo={1}, PublishTime={2:mm:ss.fff}, Time={3:mm:ss.fff}",
                        subscriptionId,
                        notificationMessage.SequenceNumber,
                        notificationMessage.PublishTime,
                        DateTime.UtcNow);
                }
                */

                return response;
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

        /// <inheritdoc/>
        public override ValueTask<RepublishResponse> RepublishAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Republish);
            RepublishResponse response;
            try
            {
                NotificationMessage notificationMessage = ServerInternal.SubscriptionManager.Republish(
                    context,
                    subscriptionId,
                    retransmitSequenceNumber);

                response = new RepublishResponse
                {
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable),
                    NotificationMessage = notificationMessage
                };
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
            return new ValueTask<RepublishResponse>(response);
        }

        /// <inheritdoc/>
        public override ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.ModifySubscription);
            ModifySubscriptionResponse response;
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
                    out double revisedPublishingInterval,
                    out uint revisedLifetimeCount,
                    out uint revisedMaxKeepAliveCount);

                response = new ModifySubscriptionResponse
                {
                    RevisedPublishingInterval = revisedPublishingInterval,
                    RevisedLifetimeCount = revisedLifetimeCount,
                    RevisedMaxKeepAliveCount = revisedMaxKeepAliveCount,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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
            return new ValueTask<ModifySubscriptionResponse>(response);
        }

        /// <inheritdoc/>
        public override ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            bool publishingEnabled,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.SetPublishingMode);
            SetPublishingModeResponse response;
            try
            {
                ValidateOperationLimits(subscriptionIds);

                ServerInternal.SubscriptionManager.SetPublishingMode(
                    context,
                    publishingEnabled,
                    subscriptionIds,
                    out StatusCodeCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                response = new SetPublishingModeResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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
            return new ValueTask<SetPublishingModeResponse>(response);
        }

        /// <inheritdoc/>
        public override ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.SetTriggering);
            SetTriggeringResponse response;
            try
            {
                if (linksToAdd.Count == 0 && linksToRemove.Count == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNothingToDo);
                }

                int monitoredItemsCount = 0;
                monitoredItemsCount += linksToAdd.Count;
                monitoredItemsCount += linksToRemove.Count;
                ValidateOperationLimits(
                    monitoredItemsCount,
                    OperationLimits.MaxMonitoredItemsPerCall);

                ServerInternal.SubscriptionManager.SetTriggering(
                    context,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    out StatusCodeCollection addResults,
                    out DiagnosticInfoCollection addDiagnosticInfos,
                    out StatusCodeCollection removeResults,
                    out DiagnosticInfoCollection removeDiagnosticInfos);

                response = new SetTriggeringResponse
                {
                    AddResults = addResults,
                    AddDiagnosticInfos = addDiagnosticInfos,
                    RemoveResults = removeResults,
                    RemoveDiagnosticInfos = removeDiagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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
            return new ValueTask<SetTriggeringResponse>(response);
        }

        /// <inheritdoc/>
        public override async ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.CreateMonitoredItems);

            try
            {
                ValidateOperationLimits(itemsToCreate, OperationLimits.MaxMonitoredItemsPerCall);

                CreateMonitoredItemsResponse result = await ServerInternal.SubscriptionManager.CreateMonitoredItemsAsync(
                    context,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct).ConfigureAwait(false);

                result.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return result;
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

        /// <inheritdoc/>
        public override async ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.ModifyMonitoredItems);

            try
            {
                ValidateOperationLimits(itemsToModify, OperationLimits.MaxMonitoredItemsPerCall);

                ModifyMonitoredItemsResponse response = await ServerInternal.SubscriptionManager.ModifyMonitoredItemsAsync(
                    context,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return response;
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

        /// <inheritdoc/>
        public override async ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.DeleteMonitoredItems);

            try
            {
                ValidateOperationLimits(monitoredItemIds, OperationLimits.MaxMonitoredItemsPerCall);

                DeleteMonitoredItemsResponse response = await ServerInternal.SubscriptionManager.DeleteMonitoredItemsAsync(
                    context,
                    subscriptionId,
                    monitoredItemIds,
                    ct).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                return response;
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

        /// <inheritdoc/>
        public override async ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(
                secureChannelContext,
                requestHeader,
                RequestType.SetMonitoringMode);

            try
            {
                ValidateOperationLimits(monitoredItemIds, OperationLimits.MaxMonitoredItemsPerCall);

                (StatusCodeCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await ServerInternal.SubscriptionManager.SetMonitoringModeAsync(
                    context,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct)
                    .ConfigureAwait(false);

                return new SetMonitoringModeResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public override async ValueTask<CallResponse> CallAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct)
        {
            OperationContext context = ValidateRequest(secureChannelContext, requestHeader, RequestType.Call);

            try
            {
                ValidateOperationLimits(methodsToCall, OperationLimits.MaxNodesPerMethodCall);

                (CallMethodResultCollection results, DiagnosticInfoCollection diagnosticInfos) =
                    await m_serverInternal.NodeManager.CallAsync(context, methodsToCall, ct)
                        .ConfigureAwait(false);

                return new CallResponse
                {
                    Results = results,
                    DiagnosticInfos = diagnosticInfos,
                    ResponseHeader = CreateResponse(requestHeader, context.StringTable)
                };
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

        /// <inheritdoc/>
        public IServerInternal CurrentInstance
        {
            get
            {
                m_semaphoreSlim.Wait();
                try
                {
                    if (m_serverInternal == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerHalted);
                    }
                    return m_serverInternal;
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }
            }
        }

        /// <summary>
        /// Returns the current status of the server.
        /// </summary>
        /// <returns>Returns a ServerStatusDataType object</returns>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete(
            "No longer thread safe. To read the value use CurrentState, to write use CurrentInstance.UpdateServerStatus."
        )]
        public ServerStatusDataType GetStatus()
        {
            lock (Lock)
            {
                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
                return m_serverInternal.Status.Value;
            }
        }

        /// <inheritdoc/>
        public ServerState CurrentState => m_serverInternal.CurrentState;

        /// <summary>
        /// Registers the server with the discovery server.
        /// </summary>
        /// <returns>Boolean value.</returns>
        [Obsolete("Use RegisterWithDiscoveryServerAsync instead.")]
        public bool RegisterWithDiscoveryServer()
        {
            return RegisterWithDiscoveryServerAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RegisterWithDiscoveryServerAsync(CancellationToken ct = default)
        {
            var configuration = new ApplicationConfiguration(Configuration)
            {
                // use a dedicated certificate validator with the registration, but derive behavior from server config
                CertificateValidator = new CertificateValidator(MessageContext.Telemetry)
            };
            await configuration
                .CertificateValidator.UpdateAsync(
                    configuration.SecurityConfiguration,
                    configuration.ApplicationUri,
                    ct)
                .ConfigureAwait(false);

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
                                await endpoint.UpdateFromServerAsync(MessageContext.Telemetry, ct).ConfigureAwait(false);
                            }

                            lock (m_registrationLock)
                            {
                                endpoint.UpdateBeforeConnect = false;
                            }

                            var requestHeader = new RequestHeader
                            {
                                Timestamp = DateTime.UtcNow
                            };

                            // create the client.
                            X509Certificate2 instanceCertificate =
                                InstanceCertificateTypesProvider.GetInstanceCertificate(
                                    endpoint.Description?.SecurityPolicyUri ??
                                    SecurityPolicies.None);
                            client = await RegistrationClient.CreateAsync(
                                configuration,
                                endpoint.Description,
                                endpoint.Configuration,
                                instanceCertificate,
                                ct: ct).ConfigureAwait(false);

                            client.OperationTimeout = 10000;

                            // register the server.
                            if (m_useRegisterServer2)
                            {
                                var discoveryConfiguration = new ExtensionObjectCollection();
                                var mdnsDiscoveryConfig = new MdnsDiscoveryConfiguration
                                {
                                    ServerCapabilities = configuration.ServerConfiguration
                                        .ServerCapabilities,
                                    MdnsServerName = Utils.GetHostName()
                                };
                                var extensionObject = new ExtensionObject(mdnsDiscoveryConfig);
                                discoveryConfiguration.Add(extensionObject);
                                await client.RegisterServer2Async(
                                    requestHeader,
                                    m_registrationInfo,
                                    discoveryConfiguration,
                                    ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await client.RegisterServerAsync(
                                    requestHeader,
                                    m_registrationInfo,
                                    ct)
                                    .ConfigureAwait(false);
                            }

                            m_registeredWithDiscoveryServer = m_registrationInfo.IsOnline;
                            return true;
                        }
                        catch (Exception e)
                        {
                            m_logger.LogWarning(
                                "RegisterServer{Api} failed for {EndpointUrl}. Exception={ErrorMessage}",
                                m_useRegisterServer2 ? "2" : string.Empty,
                                endpoint.EndpointUrl,
                                e.Message);
                            m_useRegisterServer2 = !m_useRegisterServer2;
                        }
                        finally
                        {
                            if (client != null)
                            {
                                try
                                {
                                    await client.CloseAsync(ct).ConfigureAwait(false);
                                    client = null;
                                }
                                catch (Exception e)
                                {
                                    m_logger.LogWarning(
                                        "Could not cleanly close connection with LDS. Exception={ErrorMessage}",
                                        e.Message);
                                }
                            }
                        }
                    }
                }
                // retry to start with RegisterServer2 if both failed
                m_useRegisterServer2 = true;
            }

            m_registeredWithDiscoveryServer = false;
            return false;
        }

        /// <summary>
        /// Registers the server endpoints with the LDS.
        /// </summary>
        /// <param name="state">The state.</param>
        private async void OnRegisterServerAsync(object state)
        {
            try
            {
                lock (m_registrationLock)
                {
                    // halt any outstanding timer.
                    m_registrationTimer?.Dispose();
                    m_registrationTimer = null;
                }

                if (await RegisterWithDiscoveryServerAsync().ConfigureAwait(false))
                {
                    // schedule next registration.
                    lock (m_registrationLock)
                    {
                        if (m_maxRegistrationInterval > 0)
                        {
                            m_registrationTimer = new Timer(
                                OnRegisterServerAsync,
                                this,
                                m_maxRegistrationInterval,
                                Timeout.Infinite);

                            m_lastRegistrationInterval = m_minRegistrationInterval;
                            m_logger.LogInformation(
                                "Register server succeeded. Registering again in {RegistrationInterval} ms",
                                m_maxRegistrationInterval);
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

                            m_logger.LogInformation(
                                "Register server failed. Trying again in {RegistrationInterval} ms",
                                m_lastRegistrationInterval);

                            // create timer.
                            m_registrationTimer = new Timer(
                                OnRegisterServerAsync,
                                this,
                                m_lastRegistrationInterval,
                                Timeout.Infinite);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected exception handling registration timer.");
            }
        }

        /// <summary>
        /// The synchronization object.
        /// </summary>
        [Obsolete("Use your own synchronization mechanism.")]
        protected object Lock => null;

        /// <summary>
        /// The state object associated with the server.
        /// </summary>
        /// <value>The server internal data.</value>
        /// <exception cref="ServiceResultException"></exception>
        protected IServerInternal ServerInternal =>
            m_serverInternal ?? throw new ServiceResultException(StatusCodes.BadServerHalted);

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected override void ValidateRequest(RequestHeader requestHeader)
        {
            // check for server error.
            ServiceResult error = ServerError;

            if (ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }

            // check server state.
            IServerInternal serverInternal = m_serverInternal;

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
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void SetServerState(ServerState state)
        {
            m_semaphoreSlim.Wait();
            try
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - Enter {State} state.", state);

                m_serverInternal.CurrentState = state;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Reports an error during initialization after the base server object has been started.
        /// </summary>
        /// <param name="error">The error.</param>
        protected virtual void SetServerError(ServiceResult error)
        {
            m_semaphoreSlim.Wait();
            try
            {
                ServerError = error;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Handles an error when validating the application instance certificate provided by a client.
        /// </summary>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="result">The result.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnApplicationCertificateError(
            ByteString clientCertificate,
            ServiceResult result)
        {
            throw new ServiceResultException(result);
        }

        /// <summary>
        /// Verifies that the request header is valid.
        /// </summary>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual OperationContext ValidateRequest(
            SecureChannelContext secureChannelContext,
            RequestHeader requestHeader,
            RequestType requestType)
        {
            base.ValidateRequest(requestHeader);

            if (!ServerInternal.IsRunning)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            OperationContext context = ServerInternal.SessionManager
                .ValidateRequest(requestHeader, secureChannelContext, requestType);

            if (ServerUtils.EventLog.IsEnabled())
            {
                string requestTypeString = Enum.GetName(
#if !NET8_0_OR_GREATER
                   typeof(RequestType),
#endif
                   context.RequestType);
                ServerUtils.EventLog.ServerCall(requestTypeString, context.RequestId);
            }
            m_logger.LogTrace("Server Call={RequestType}, Id={RequestId}", context.RequestType, context.RequestId);

            // notify the request manager.
            ServerInternal.RequestManager.RequestReceived(context);

            return context;
        }

        /// <summary>
        /// Validate operation limits.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="operation">A list of operations.</param>
        /// <param name="operationLimit">The operation limit property.</param>
        /// <exception cref="ServiceResultException">BadNothingToDo if list is null or empty.</exception>
        /// <exception cref="ServiceResultException">BadTooManyOperations if list is larger than operation limit property.</exception>
        protected void ValidateOperationLimits<T>(
            ArrayOf<T> operation,
            PropertyState<uint> operationLimit = null)
        {
            if (operation.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadNothingToDo);
            }
            ValidateOperationLimits(operation.Count, operationLimit);
        }

        /// <summary>
        /// Validate operation limits.
        /// </summary>
        /// <param name="count">A count of operations.</param>
        /// <param name="operationLimit">The operation limit property.</param>
        /// <exception cref="ServiceResultException">BadTooManyOperations if count is larger than operation limit property.</exception>
        protected void ValidateOperationLimits(int count, PropertyState<uint> operationLimit)
        {
            uint operationLimitValue = operationLimit != null ? operationLimit.Value : 0;
            if (operationLimitValue > 0 && count > operationLimitValue)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations);
            }
        }

        /// <summary>
        /// Translates an exception.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="e">The ServiceResultException e.</param>
        /// <returns>Returns an exception thrown when a UA defined error occurs, the return type is <seealso cref="ServiceResultException"/>.</returns>
        protected virtual ServiceResultException TranslateException(
            OperationContext context,
            ServiceResultException e)
        {
            ArrayOf<string> preferredLocales = default;

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
        protected virtual ServiceResultException TranslateException(
            DiagnosticsMasks diagnosticsMasks,
            ArrayOf<string> preferredLocales,
            ServiceResultException e)
        {
            if (e == null)
            {
                return null;
            }

            // check if inner result required.
            ServiceResult innerResult = null;

            if ((
                    diagnosticsMasks &
                    (DiagnosticsMasks.ServiceInnerDiagnostics |
                        DiagnosticsMasks.ServiceInnerStatusCode)
                ) != 0)
            {
                innerResult = e.InnerResult;
            }

            // check if translated text required.
            LocalizedText translatedText = default;

            if ((diagnosticsMasks & DiagnosticsMasks.ServiceLocalizedText) != 0)
            {
                translatedText = e.LocalizedText;
            }

            // create new result object.
            var result = new ServiceResult(
                e.NamespaceUri,
                new StatusCode(e.StatusCode.Code, e.SymbolicId),
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
        protected virtual ServiceResult TranslateResult(
            DiagnosticsMasks diagnosticsMasks,
            ArrayOf<string> preferredLocales,
            ServiceResult result)
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
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void OnRequestComplete(OperationContext context)
        {
            m_semaphoreSlim.Wait();
            try
            {
                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_serverInternal.RequestManager.RequestCompleted(context);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Raised when the configuration changes.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="ConfigurationWatcherEventArgs"/> instance containing the event data.</param>
        protected virtual async void OnConfigurationChangedAsync(
            object sender,
            ConfigurationWatcherEventArgs args)
        {
            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration
                    .LoadAsync(
                        new FileInfo(args.FilePath),
                        Configuration.ApplicationType,
                        Configuration.GetType(),
                        MessageContext.Telemetry)
                    .ConfigureAwait(false);

                await OnUpdateConfigurationAsync(configuration)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not load updated configuration file from: {FilePath}", args.FilePath);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnUpdateConfigurationAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // update security configuration.
                configuration.SecurityConfiguration.Validate(MessageContext.Telemetry);

                Configuration.SecurityConfiguration.TrustedIssuerCertificates = configuration
                    .SecurityConfiguration
                    .TrustedIssuerCertificates;
                Configuration.SecurityConfiguration.TrustedPeerCertificates = configuration
                    .SecurityConfiguration
                    .TrustedPeerCertificates;
                Configuration.SecurityConfiguration.RejectedCertificateStore = configuration
                    .SecurityConfiguration
                    .RejectedCertificateStore;

                await Configuration.CertificateValidator.UpdateAsync(
                    Configuration.SecurityConfiguration,
                    ct: cancellationToken).ConfigureAwait(false);

                // update trace configuration.
                Configuration.TraceConfiguration = configuration.TraceConfiguration ??
                    new TraceConfiguration();

#pragma warning disable CS0618 // Type or member is obsolete
                Configuration.TraceConfiguration.ApplySettings();
#pragma warning restore CS0618 // Type or member is obsolete
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Failed to update configuration.");
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Called before the server starts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            m_semaphoreSlim.Wait();
            try
            {
                base.OnServerStarting(configuration);

                // save minimum nonce length.
                m_minNonceLength = configuration.SecurityConfiguration.NonceLength;

                // try first RegisterServer2
                m_useRegisterServer2 = true;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Creates the endpoints and creates the hosts.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="bindingFactory">The transport listener binding factory.</param>
        /// <param name="serverDescription">The server description.</param>
        /// <param name="endpoints">The endpoints.</param>
        /// <returns>
        /// Returns IList of a host for a UA service.
        /// </returns>
        protected override IList<ServiceHost> InitializeServiceHosts(
            ApplicationConfiguration configuration,
            ITransportListenerBindings bindingFactory,
            out ApplicationDescription serverDescription,
            out EndpointDescriptionCollection endpoints)
        {
            serverDescription = null;
            endpoints = null;

            var hosts = new Dictionary<string, ServiceHost>();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration.SecurityPolicies.Count == 0)
            {
                configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
            }

            // ensure at least one user token policy exists.
            if (configuration.ServerConfiguration.UserTokenPolicies.Count == 0)
            {
                var userTokenPolicy = new UserTokenPolicy { TokenType = UserTokenType.Anonymous };
                userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
            }

            // set server description.
            serverDescription = new ApplicationDescription
            {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationName = new LocalizedText("en-US", configuration.ApplicationName),
                ApplicationType = configuration.ApplicationType,
                ProductUri = configuration.ProductUri,
                DiscoveryUrls = GetDiscoveryUrls()
            };

            endpoints = [];
            IList<EndpointDescription> endpointsForHost = null;

            ArrayOf<string> baseAddresses = configuration.ServerConfiguration.BaseAddresses;
            foreach (
                string scheme in Utils.DefaultUriSchemes.Where(scheme =>
                    baseAddresses.Find(a => a.StartsWith(scheme, StringComparison.Ordinal)) != null))
            {
                ITransportListenerFactory binding = bindingFactory.GetBinding(scheme, MessageContext.Telemetry);
                if (binding != null)
                {
                    endpointsForHost = binding.CreateServiceHost(
                        this,
                        hosts,
                        configuration,
                        configuration.ServerConfiguration.BaseAddresses,
                        serverDescription,
                        configuration.ServerConfiguration.SecurityPolicies,
                        InstanceCertificateTypesProvider);
                    endpoints.AddRange(endpointsForHost);
                }
            }

            return [.. hosts.Values];
        }

        /// <summary>
        /// Creates an instance of the service host.
        /// </summary>
        public override ServiceHost CreateServiceHost(ServerBase server, params Uri[] addresses)
        {
            return new ServiceHost(this, addresses);
        }

        /// <summary>
        /// Returns an instance of the endpoint to use.
        /// </summary>
        protected override EndpointBase GetEndpointInstance(ServerBase server)
        {
            return new SessionEndpoint(server);
        }

        /// <inheritdoc/>
        protected override async ValueTask StartApplicationAsync(ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await base.StartApplicationAsync(configuration, cancellationToken)
                .ConfigureAwait(false);
            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_logger.LogInformation(
                    Utils.TraceMasks.StartStop,
                    "Server - Start application {ApplicationName}.",
                    configuration.ApplicationName);

                // Setup the minimum nonce length
                Nonce.SetMinNonceValue((uint)configuration.SecurityConfiguration.NonceLength);

                // create the datastore for the instance.
                m_serverInternal = new ServerInternalData(
                    ServerProperties,
                    configuration,
                    MessageContext,
                    new CertificateValidator(MessageContext.Telemetry),
                    InstanceCertificateTypesProvider);

                // create the manager responsible for providing localized string resources.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateResourceManager.");
                ResourceManager resourceManager = CreateResourceManager(
                    m_serverInternal,
                    configuration);

                // create the manager responsible for incoming requests.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateRequestManager.");
                RequestManager requestManager = CreateRequestManager(
                    m_serverInternal,
                    configuration);

                //create the main node manager factory
                IMainNodeManagerFactory mainNodeManagerFactory = CreateMainNodeManagerFactory(m_serverInternal, configuration);

                m_serverInternal.SetMainNodeManagerFactory(mainNodeManagerFactory);

                // create the master node manager.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateMasterNodeManager.");
                IMasterNodeManager masterNodeManager = CreateMasterNodeManager(
                    m_serverInternal,
                    configuration);

                // add the node manager to the datastore.
                m_serverInternal.SetNodeManager(masterNodeManager);

                // put the node manager into a state that allows it to be used by other objects.
                await masterNodeManager.StartupAsync(cancellationToken)
                    .ConfigureAwait(false);

                // create the manager responsible for handling events.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateEventManager.");
                EventManager eventManager = CreateEventManager(m_serverInternal, configuration);

                // creates the server object.
                m_serverInternal.CreateServerObject(
                    eventManager,
                    resourceManager,
                    requestManager);

                // do any additional processing now that the node manager is up and running.
                OnNodeManagerStarted(m_serverInternal);

                // create the manager responsible for aggregates.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateAggregateManager.");
                m_serverInternal.SetAggregateManager(
                    CreateAggregateManager(m_serverInternal, configuration));

                // create the manager responsible for modelling rules.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateModellingRulesManager.");
                m_serverInternal.SetModellingRulesManager(
                    CreateModellingRulesManager(m_serverInternal, configuration));

                // start the session manager.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateSessionManager.");
                ISessionManager sessionManager = CreateSessionManager(
                    m_serverInternal,
                    configuration);
                await sessionManager.StartupAsync(cancellationToken)
                    .ConfigureAwait(false);

                // use event to trigger channel that should not be closed.
                sessionManager.SessionChannelKeepAlive += SessionChannelKeepAliveEvent;

                //create the MonitoredItemQueueFactory
                IMonitoredItemQueueFactory monitoredItemQueueFactory
                    = CreateMonitoredItemQueueFactory(
                    m_serverInternal,
                    configuration);

                //add the MonitoredItemQueueFactory to the datastore.
                m_serverInternal.SetMonitoredItemQueueFactory(monitoredItemQueueFactory);

                //create the SubscriptionStore
                ISubscriptionStore subscriptionStore = CreateSubscriptionStore(
                    m_serverInternal,
                    configuration);

                //add the SubscriptionStore to the datastore
                m_serverInternal.SetSubscriptionStore(subscriptionStore);

                // start the subscription manager.
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - CreateSubscriptionManager.");
                ISubscriptionManager subscriptionManager = CreateSubscriptionManager(
                    m_serverInternal,
                    configuration);
                await subscriptionManager.StartupAsync(cancellationToken)
                    .ConfigureAwait(false);

                // add the session manager to the datastore.
                m_serverInternal.SetSessionManager(sessionManager, subscriptionManager);

                ServerError = null;

                // setup registration information.
                lock (m_registrationLock)
                {
                    m_maxRegistrationInterval = configuration.ServerConfiguration
                        .MaxRegistrationInterval;

                    ApplicationDescription serverDescription = ServerDescription;

                    m_registrationInfo = new RegisteredServer
                    {
                        ServerUri = serverDescription.ApplicationUri
                    };
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
                        var uri = new UriBuilder(BaseAddresses[ii].DiscoveryUrl);

                        if (string.Equals(
                            uri.Host,
                            "localhost",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            uri.Host = computerName;
                        }

                        m_registrationInfo.DiscoveryUrls.Add(uri.ToString());
                    }

                    // build list of registration endpoints.
                    m_registrationEndpoints = new ConfiguredEndpointCollection(configuration);

                    EndpointDescription endpoint = configuration.ServerConfiguration
                        .RegistrationEndpoint;

                    if (endpoint == null)
                    {
                        endpoint = new EndpointDescription
                        {
                            EndpointUrl = Utils.Format(Utils.DiscoveryUrls[0], "localhost"),
                            SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                                MessageSecurityMode.SignAndEncrypt,
                                SecurityPolicies.Basic256Sha256,
                                m_logger),
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        };
                        endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;
                    }

                    m_registrationEndpoints.Add(endpoint);

                    m_registeredWithDiscoveryServer = false;
                    m_minRegistrationInterval = 1000;
                    m_lastRegistrationInterval = m_minRegistrationInterval;

                    // start registration timer.
                    m_registrationTimer?.Dispose();
                    m_registrationTimer = null;

                    if (m_maxRegistrationInterval > 0)
                    {
                        m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - Registration Timer started.");
                        m_registrationTimer = new Timer(
                            OnRegisterServerAsync,
                            this,
                            m_minRegistrationInterval,
                            Timeout.Infinite);
                    }
                }
            }
            catch (Exception e)
            {
                const string message = "Unexpected error starting application";
                m_logger.LogCritical(Utils.TraceMasks.StartStop, e, message);
                Utils.SilentDispose(m_serverInternal);
                m_serverInternal = null;
                var error = ServiceResult.Create(e, StatusCodes.BadInternalError, message);
                ServerError = error;
                throw new ServiceResultException(error);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }

            // set the server status as running.
            SetServerState(ServerState.Running);

            // all initialization is complete.
            m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - Started.");
            OnServerStarted(m_serverInternal);

            // monitor the configuration file.
            if (!string.IsNullOrEmpty(configuration.SourceFilePath))
            {
                m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - Configuration watcher started.");
                m_configurationWatcher = new ConfigurationWatcher(configuration, MessageContext.Telemetry);
                m_configurationWatcher.Changed += OnConfigurationChangedAsync;
            }

            CertificateValidator.CertificateUpdate += OnCertificateUpdateAsync;
        }

        /// <inheritdoc/>
        protected override async ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
        {
            m_logger.LogInformation(Utils.TraceMasks.StartStop, "Server - Stopping.");

            ShutDownDelay();

            // halt any outstanding timer.
            lock (m_registrationLock)
            {
                m_registrationTimer?.Dispose();
                m_registrationTimer = null;
            }

            // attempt graceful shutdown the server.
            try
            {
                if (m_maxRegistrationInterval > 0 && m_registeredWithDiscoveryServer)
                {
                    // unregister from Discovery Server if registered before
                    m_registrationInfo.IsOnline = false;
                    await RegisterWithDiscoveryServerAsync(cancellationToken).ConfigureAwait(false);
                }

                await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (m_serverInternal != null)
                    {
                        m_serverInternal.SessionManager.SessionChannelKeepAlive
                            -= SessionChannelKeepAliveEvent;
                        await m_serverInternal.SubscriptionManager.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                        m_serverInternal.SessionManager.Shutdown();
                        await m_serverInternal.NodeManager.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_semaphoreSlim.Release();
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
        /// Trys to get the secure channel id for an AuthenticationToken.
        /// The ChannelId is known to the sessions of the Server.
        /// Each session has an AuthenticationToken which can be used to identify the session.
        /// </summary>
        /// <param name="authenticationToken">The AuthenticationToken from the RequestHeader</param>
        /// <param name="channelId">The Channel id</param>
        /// <returns>returns true if a channelId was found for the provided AuthenticationToken</returns>
        public override bool TryGetSecureChannelIdForAuthenticationToken(
            NodeId authenticationToken,
            out uint channelId)
        {
            ISession session = ServerInternal.SessionManager.GetSession(authenticationToken);

            if (session == null)
            {
                channelId = 0;
                return false;
            }

            return uint.TryParse(session.SecureChannelId, out channelId);
        }

        /// <summary>
        /// Implements the server shutdown delay if session are connected.
        /// </summary>
        protected void ShutDownDelay()
        {
            try
            {
                // check for connected clients.
                IList<ISession> currentessions = ServerInternal.SessionManager.GetSessions();

                if (currentessions.Count > 0)
                {
                    // provide some time for the connected clients to detect the shutdown state.
                    ServerInternal.UpdateServerStatus(
                        (status) =>
                        {
                            // set the shutdown reason and state.
                            status.Value.ShutdownReason = new LocalizedText(
                                "en-US",
                                "Application is shutting down.");
                            status.Variable.ShutdownReason.Value = new LocalizedText(
                                "en-US",
                                "Application is shutting down.");
                            status.Value.State = ServerState.Shutdown;
                            status.Variable.State.Value = ServerState.Shutdown;
                            status.Variable
                                .ClearChangeMasks(ServerInternal.DefaultSystemContext, true);
                        });

                    foreach (ISession session in currentessions)
                    {
                        // raise close session audit event
                        ServerInternal.ReportAuditCloseSessionEvent(
                            null,
                            session,
                            m_logger,
                            "Session/Terminated");
                    }

                    for (int timeTillShutdown = Configuration.ServerConfiguration.ShutdownDelay;
                        timeTillShutdown > 0;
                        timeTillShutdown--)
                    {
                        ServerInternal.UpdateServerStatus(
                            (status) =>
                            {
                                status.Value.SecondsTillShutdown = (uint)timeTillShutdown;
                                status.Variable.SecondsTillShutdown.Value = (uint)timeTillShutdown;
                                status.Variable
                                    .ClearChangeMasks(ServerInternal.DefaultSystemContext, true);
                            });

                        // exit if all client connections are closed.
                        int sessionCount = ServerInternal.SessionManager.GetSessions().Count;
                        if (sessionCount == 0)
                        {
                            break;
                        }

                        m_logger.LogInformation(
                            Utils.TraceMasks.StartStop,
                            "{SessionCount} active sessions. Seconds until shutdown: {TimeTillShutdown}s",
                            sessionCount,
                            timeTillShutdown);

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
        protected virtual RequestManager CreateRequestManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new RequestManager(server);
        }

        /// <summary>
        /// Creates the aggregate manager used by the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The manager.</returns>
        protected virtual AggregateManager CreateAggregateManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var manager = new AggregateManager(server);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Interpolative,
                BrowseNames.AggregateFunction_Interpolative,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Average,
                BrowseNames.AggregateFunction_Average,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_TimeAverage,
                BrowseNames.AggregateFunction_TimeAverage,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_TimeAverage2,
                BrowseNames.AggregateFunction_TimeAverage2,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Total,
                BrowseNames.AggregateFunction_Total,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Total2,
                BrowseNames.AggregateFunction_Total2,
                Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Minimum,
                BrowseNames.AggregateFunction_Minimum,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Maximum,
                BrowseNames.AggregateFunction_Maximum,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_MinimumActualTime,
                BrowseNames.AggregateFunction_MinimumActualTime,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_MaximumActualTime,
                BrowseNames.AggregateFunction_MaximumActualTime,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Range,
                BrowseNames.AggregateFunction_Range,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Minimum2,
                BrowseNames.AggregateFunction_Minimum2,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Maximum2,
                BrowseNames.AggregateFunction_Maximum2,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_MinimumActualTime2,
                BrowseNames.AggregateFunction_MinimumActualTime2,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_MaximumActualTime2,
                BrowseNames.AggregateFunction_MaximumActualTime2,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Range2,
                BrowseNames.AggregateFunction_Range2,
                Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Count,
                BrowseNames.AggregateFunction_Count,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_AnnotationCount,
                BrowseNames.AggregateFunction_AnnotationCount,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_DurationInStateZero,
                BrowseNames.AggregateFunction_DurationInStateZero,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                BrowseNames.AggregateFunction_DurationInStateNonZero,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                BrowseNames.AggregateFunction_NumberOfTransitions,
                Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Start,
                BrowseNames.AggregateFunction_Start,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_End,
                BrowseNames.AggregateFunction_End,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Delta,
                BrowseNames.AggregateFunction_Delta,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_StartBound,
                BrowseNames.AggregateFunction_StartBound,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_EndBound,
                BrowseNames.AggregateFunction_EndBound,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_DeltaBounds,
                BrowseNames.AggregateFunction_DeltaBounds,
                Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_DurationGood,
                BrowseNames.AggregateFunction_DurationGood,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_DurationBad,
                BrowseNames.AggregateFunction_DurationBad,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_PercentGood,
                BrowseNames.AggregateFunction_PercentGood,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_PercentBad,
                BrowseNames.AggregateFunction_PercentBad,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_WorstQuality,
                BrowseNames.AggregateFunction_WorstQuality,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_WorstQuality2,
                BrowseNames.AggregateFunction_WorstQuality2,
                Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                BrowseNames.AggregateFunction_StandardDeviationPopulation,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_VariancePopulation,
                BrowseNames.AggregateFunction_VariancePopulation,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                BrowseNames.AggregateFunction_StandardDeviationSample,
                Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_VarianceSample,
                BrowseNames.AggregateFunction_VarianceSample,
                Aggregators.CreateStandardCalculator);

            return manager;
        }

        /// <summary>
        /// Creates the modelling rules manager used by the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The manager.</returns>
        protected virtual ModellingRulesManager CreateModellingRulesManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var manager = new ModellingRulesManager(server);

            manager.RegisterModellingRule(
                ObjectIds.ModellingRule_Mandatory,
                BrowseNames.ModellingRule_Mandatory);
            manager.RegisterModellingRule(
                ObjectIds.ModellingRule_Optional,
                BrowseNames.ModellingRule_Optional);
            manager.RegisterModellingRule(
                ObjectIds.ModellingRule_ExposesItsArray,
                BrowseNames.ModellingRule_ExposesItsArray);
            manager.RegisterModellingRule(
                ObjectIds.ModellingRule_OptionalPlaceholder,
                BrowseNames.ModellingRule_OptionalPlaceholder);
            manager.RegisterModellingRule(
                ObjectIds.ModellingRule_MandatoryPlaceholder,
                BrowseNames.ModellingRule_MandatoryPlaceholder);

            return manager;
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages access to localized resources, the return type is <seealso cref="ResourceManager"/>.</returns>
        protected virtual ResourceManager CreateResourceManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var resourceManager = new ResourceManager(configuration);

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
        protected virtual IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var nodeManagers = new List<INodeManager>();

            foreach (INodeManagerFactory nodeManagerFactory in m_nodeManagerFactories)
            {
                nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
            }

            var asyncNodeManagers = new List<IAsyncNodeManager>();

            foreach (IAsyncNodeManagerFactory nodeManagerFactory in m_asyncNodeManagerFactories)
            {
                asyncNodeManagers.Add(nodeManagerFactory.CreateAsync(server, configuration).AsTask().GetAwaiter().GetResult());
            }

            return new MasterNodeManager(server, configuration, null, asyncNodeManagers, nodeManagers);
        }

        /// <summary>
        /// Creates the master node manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns the master node manager for the server, the return type is <seealso cref="MasterNodeManager"/>.</returns>
        protected virtual IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new MainNodeManagerFactory(configuration, server);
        }

        /// <summary>
        /// Creates the event manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns an object that manages all events raised within the server, the return type is <seealso cref="EventManager"/>.</returns>
        protected virtual EventManager CreateEventManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new EventManager(
                server,
                (uint)configuration.ServerConfiguration.MaxEventQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableEventQueueSize);
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="ISessionManager"/>.</returns>
        protected virtual ISessionManager CreateSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new SessionManager(server, configuration);
        }

        /// <summary>
        /// Creates the session manager for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a generic session manager object for a server, the return type is <seealso cref="SubscriptionManager"/>.</returns>
        protected virtual ISubscriptionManager CreateSubscriptionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new SubscriptionManager(server, configuration);
        }

        /// <summary>
        /// Creates the (durable) monitored item queue factory for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a (durable) monitored item queue factory for a server, the return type is <seealso cref="IMonitoredItemQueueFactory"/>.</returns>
        protected virtual IMonitoredItemQueueFactory CreateMonitoredItemQueueFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return new MonitoredItemQueueFactory(MessageContext.Telemetry);
        }

        /// <summary>
        /// Creates the subscriptionStore for the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The configuration.</param>
        /// <returns>Returns a subscriptionStore for a server, the return type is <seealso cref="ISubscriptionStore"/>.</returns>
        protected virtual ISubscriptionStore CreateSubscriptionStore(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return null;
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

        /// <inheritdoc/>
        public IEnumerable<INodeManagerFactory> NodeManagerFactories => m_nodeManagerFactories;

        /// <inheritdoc/>
        public IEnumerable<IAsyncNodeManagerFactory> AsyncNodeManagerFactories
            => m_asyncNodeManagerFactories;

        /// <inheritdoc/>
        public virtual void AddNodeManager(INodeManagerFactory nodeManagerFactory)
        {
            m_nodeManagerFactories.Add(nodeManagerFactory);
        }

        /// <inheritdoc/>
        public virtual void AddNodeManager(IAsyncNodeManagerFactory nodeManagerFactory)
        {
            m_asyncNodeManagerFactories.Add(nodeManagerFactory);
        }

        /// <inheritdoc/>
        public virtual void RemoveNodeManager(INodeManagerFactory nodeManagerFactory)
        {
            m_nodeManagerFactories.Remove(nodeManagerFactory);
        }

        /// <inheritdoc/>
        public virtual void RemoveNodeManager(IAsyncNodeManagerFactory nodeManagerFactory)
        {
            m_asyncNodeManagerFactories.Remove(nodeManagerFactory);
        }

        /// <summary>
        /// Reacts to a session channel keep alive event to signal
        /// a listener channel that a session is still active.
        /// </summary>
        private void SessionChannelKeepAliveEvent(ISession session, SessionEventReason reason)
        {
            Debug.Assert(reason == SessionEventReason.ChannelKeepAlive);

            string secureChannelId = session?.SecureChannelId;
            if (!string.IsNullOrEmpty(secureChannelId))
            {
                ITransportListener transportListener = TransportListeners.FirstOrDefault(tl =>
                    secureChannelId.StartsWith(tl.ListenerId, StringComparison.Ordinal));
                transportListener?.UpdateChannelLastActiveTime(secureChannelId);
            }
        }

        private OperationLimitsState OperationLimits
            => ServerInternal.ServerObject.ServerCapabilities.OperationLimits;

        private readonly Lock m_registrationLock = new();
        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);
        private IServerInternal m_serverInternal;
        private ConfigurationWatcher m_configurationWatcher;
        private ConfiguredEndpointCollection m_registrationEndpoints;
        private RegisteredServer m_registrationInfo;
        private Timer m_registrationTimer;
        private int m_minRegistrationInterval;
        private int m_maxRegistrationInterval;
        private int m_lastRegistrationInterval;
        private bool m_registeredWithDiscoveryServer;
        private int m_minNonceLength;
        private bool m_useRegisterServer2;
        private readonly List<INodeManagerFactory> m_nodeManagerFactories = [];
        private readonly List<IAsyncNodeManagerFactory> m_asyncNodeManagerFactories = [];
    }
}
