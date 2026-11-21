// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Redaction;
using Opc.Ua.Schema;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// Session establishment of the server: the CreateSession and ActivateSession
        /// services, with their admission, validation, nonce and signature handling,
        /// audit events and rejected-session counters.
        /// </summary>
        /// <remarks>
        /// Nested in <see cref="StandardServer"/> because both services call the
        /// protected virtual signature and additional-parameter hooks that derived
        /// servers override, and the protected security-error and response helpers.
        /// </remarks>
        private sealed class SessionEstablishment
        {
            /// <summary>
            /// Creates the session establishment of a server.
            /// </summary>
            /// <param name="server">The server that establishes the sessions.</param>
            public SessionEstablishment(StandardServer server)
            {
                m_server = server;
            }

            /// <summary>
            /// Invokes the CreateSession service.
            /// </summary>
            public async ValueTask<CreateSessionResponse> CreateSessionAsync(
                SecureChannelContext secureChannelContext,
                RequestHeader? requestHeader,
                ApplicationDescription? clientDescription,
                string? serverUri,
                string? endpointUrl,
                string? sessionName,
                ByteString clientNonce,
                ByteString clientCertificate,
                double requestedSessionTimeout,
                uint maxResponseMessageSize,
                RequestLifetime requestLifetime)
            {
                NodeId sessionId;
                NodeId authenticationToken;
                double revisedSessionTimeout = 0;
                ByteString serverNonce;
                ByteString serverCertificate = default;
                ArrayOf<EndpointDescription> serverEndpoints = default;
                uint maxRequestMessageSize = (uint)m_server.MessageContext.MaxMessageSize;

                // Admission control: reject with BadServerTooBusy before doing the
                // CPU-bound certificate validation / signing when at capacity. The
                // lease (a concurrency permit) is held for the duration of the call.
                // It is acquired before the request is registered, so a handshake that
                // waits for a permit is not a request that lifecycle drains wait for.
                // Part 5 12.9: a CreateSession refused by admission control or request
                // validation is a rejected session establishment request too.
                IDisposable? admittedLease;
                OperationContext validatedContext;
                try
                {
                    admittedLease = await m_server.m_admission.BeginSessionEstablishmentOrThrowAsync(
                        secureChannelContext, requestHeader?.AuthenticationToken ?? default, requestLifetime)
                        .ConfigureAwait(false);
                    try
                    {
                        validatedContext = await m_server.ValidateRequestAsync(
                            secureChannelContext,
                            requestHeader,
                            RequestType.CreateSession,
                            requestLifetime).ConfigureAwait(false);
                    }
                    catch
                    {
                        admittedLease?.Dispose();
                        throw;
                    }
                }
                catch (Exception exception)
                {
                    ReportCreateSessionRejected(
                        requestHeader?.AuditEntryId,
                        secureChannelContext?.SecureChannelId,
                        clientCertificate,
                        exception);
                    throw;
                }

                using IDisposable? rateLimitLease = admittedLease;
                using OperationContext context = validatedContext;

                ISession? session = null;
                CertificateCollection? clientIssuerCertificates = null;
                Certificate? parsedClientCertificate = null;
                string? clientCertificateThumbprint = null;
                bool clientCertificateErrorAccepted = false;
                try
                {
                    // The serverUri is not checked: Part 4 5.7.2.2 says the parameter
                    // is no longer used and the Server shall ignore any value provided.
                    // A 1.04 Client behind a gateway still sends the underlying Server's
                    // URI there.

                    bool requireEncryption = RequireEncryption(
                        context.ChannelContext!.EndpointDescription!);

                    if (!requireEncryption && !clientCertificate.IsEmpty)
                    {
                        requireEncryption = true;
                    }

                    // validate client application instance certificate. Each check
                    // reports its own failure through m_server.OnApplicationCertificateError. An
                    // override that accepts the error (returns instead of throwing)
                    // keeps the certificate, so the session can still sign (Part 4
                    // 6.1.8), and the checks after the accepted one still run.
                    if (context.SecurityPolicyUri != SecurityPolicies.None)
                    {
                        CertificateCollection? clientCertificateChain = null;
                        try
                        {
                            try
                            {
                                if (clientCertificate.IsEmpty)
                                {
                                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                                }
                                clientCertificateChain = Utils.ParseCertificateChainBlob(
                                    clientCertificate,
                                    m_server.ServerInternal.Telemetry);
                                parsedClientCertificate = clientCertificateChain[0].AddRef();
                                clientCertificateThumbprint = parsedClientCertificate.Thumbprint;

                                if (clientCertificateChain.Count > 1)
                                {
                                    clientIssuerCertificates = [];
                                    for (int i = 1; i < clientCertificateChain.Count; i++)
                                    {
                                        clientIssuerCertificates.Add(clientCertificateChain[i]);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                HandleClientCertificateError(e);
                            }

                            // nothing left to check when no certificate could be parsed.
                            if (parsedClientCertificate != null && clientCertificateChain != null)
                            {
                                try
                                {
                                    CertificateValidationResult clientCertResult = await m_server.CertificateManager!
                                        .ValidateAsync(
                                            clientCertificateChain,
                                            TrustListIdentifier.Peers,
                                            options: null,
                                            ct: requestLifetime.CancellationToken)
                                        .ConfigureAwait(false);
                                    // Preserve nested validation failures for client-status masking and detailed audit reporting.
                                    clientCertResult.ThrowIfInvalid();
                                }
                                catch (Exception e)
                                {
                                    HandleClientCertificateError(e);
                                }

                                try
                                {
                                    string applicationUri = clientDescription?.ApplicationUri ?? string.Empty;
                                    if (string.IsNullOrEmpty(applicationUri) ||
                                        !X509Utils.CompareApplicationUriWithCertificate(parsedClientCertificate!, applicationUri))
                                    {
                                        m_server.ServerInternal?.ReportAuditCertificateDataMismatchEvent(
                                            parsedClientCertificate!,
                                            null,
                                            applicationUri,
                                            StatusCodes.BadCertificateUriInvalid,
                                            m_server.m_logger);

                                        throw ServiceResultException.Create(
                                            StatusCodes.BadCertificateUriInvalid,
                                            "The URI specified in the ApplicationDescription {0} does not match the URIs in the Certificate.",
                                            applicationUri);
                                    }
                                }
                                catch (Exception e)
                                {
                                    HandleClientCertificateError(e);
                                }

                                try
                                {
                                    string? profile = context.ChannelContext.EndpointDescription!.TransportProfileUri;
                                    if (profile is Profiles.UaTcpTransport or Profiles.UaWssTransport &&
                                        !context.ChannelContext.ClientChannelCertificate.Span.SequenceEqual(
                                            parsedClientCertificate!.RawData))
                                    {
                                        throw ServiceResultException.Create(
                                            StatusCodes.BadSecurityChecksFailed,
                                            "The session certificate does not match the SecureChannel certificate.");
                                    }
                                }
                                catch (Exception e)
                                {
                                    HandleClientCertificateError(e);
                                }
                            }
                        }
                        finally
                        {
                            clientCertificateChain?.Dispose();
                        }
                    }

                    // verify the nonce provided by the client.
                    if (context.SecurityPolicyUri != SecurityPolicies.None)
                    {
                        if (clientNonce.Length < m_server.m_minNonceLength || clientNonce.Length > 128)
                        {
                            throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                        }
                    }
                    else if (clientNonce.Length > 128)
                    {
                        // A nonce sent on a None channel is kept: the None channel
                        // variant of an enhanced user token signature
                        // (ServerNonce | Hash(ServerCertificate) | ClientNonce)
                        // covers the nonce the client sent. Part 4 5.7.2.3 (Table 16)
                        // requires Bad_NonceInvalid for a nonce longer than 128 bytes
                        // on every channel. An empty or short nonce stays accepted on
                        // a None channel, where nothing else requires one.
                        throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                    }

                    // load the certificate for the security profile. The session
                    // takes its own ref-counted handle on the certificate, so the
                    // acquired entry is disposed when this scope exits.
                    CertificateManager certificates = m_server.CertificateManager ??
                        throw ServiceResultException.ConfigurationError("CertificateManager has not been initialized.");
                    using CertificateEntry? instanceEntry = AcquireEndpointCertificate(
                        context.ChannelContext!.EndpointDescription!, certificates, m_server.SecurityPolicyRegistry);
                    Certificate instanceCertificate = instanceEntry?.Certificate!;

                    // create the session. The session manager marks the Session as not
                    // validated before it publishes it, so nothing reads it as validated.
                    context.ClientCertificateErrorAccepted = clientCertificateErrorAccepted;
                    CreateSessionResult result = await m_server.ServerInternal.SessionManager.CreateSessionAsync(
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
                            requestLifetime.CancellationToken)
                        .ConfigureAwait(false);

                    session = result.Session;

                    // recorded for every ISession implementation, so a session from a
                    // custom CreateSession factory is not treated as a trusted application.
                    // SessionManager already did so before publishing the session; this
                    // covers a session manager that does not derive from it.
                    if (clientCertificateErrorAccepted)
                    {
                        ClientCertificateProvenance.SetValidated(session, false);
                    }

                    // Part 5 12.11: SessionDiagnostics.ServerUri reports the serverUri of
                    // the CreateSession request, although the Server otherwise ignores it.
                    if (!string.IsNullOrEmpty(serverUri))
                    {
                        session.UpdateDiagnostics(d => d.ServerUri = serverUri);
                    }

                    // CumulatedSessionCount counts established sessions and is only
                    // incremented on the success path below; a failure from here on
                    // closes the session and counts it as rejected (Part 5 12.9).
                    m_server.ServerInternal.UpdateServerDiagnostics(diagnostics => diagnostics.CurrentSessionCount++);
                    sessionId = result.SessionId;
                    authenticationToken = result.AuthenticationToken;
                    serverNonce = result.ServerNonce;
                    revisedSessionTimeout = result.RevisedSessionTimeout;

                    if (!string.IsNullOrEmpty(endpointUrl))
                    {
                        try
                        {
                            // check the endpointurl. Part 4 5.7.2.2 defines no status
                            // code for a malformed endpointUrl, so a value that is not
                            // an absolute URL is reported like a host name mismatch
                            // instead of failing the service.
                            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? parsedEndpointUrl))
                            {
                                throw new ServiceResultException(StatusCodes.BadCertificateHostNameInvalid);
                            }

                            var configuredEndpoint = new ConfiguredEndpoint
                            {
                                EndpointUrl = parsedEndpointUrl
                            };

                            certificates.ValidateDomains(
                                instanceCertificate,
                                configuredEndpoint,
                                serverValidation: true);
                        }
                        catch (ServiceResultException sre)
                            when (sre.StatusCode == StatusCodes.BadCertificateHostNameInvalid)
                        {
                            Debug.Assert(session != null);
                            m_server.m_logger.ServerClientConnectsWithAnEndpointUrlEndpointUrl(endpointUrl);
                            m_server.ServerInternal.ReportAuditUrlMismatchEvent(
                                context.AuditEntryId!,
                                session!,
                                revisedSessionTimeout,
                                endpointUrl,
                                m_server.m_logger);
                        }
                    }

                    // the ephemeral key is not created for a request that was already
                    // cancelled or timed out.
                    requestLifetime.CancellationToken.ThrowIfCancellationRequested();
                    AdditionalParametersType? parameters = m_server.CreateSessionProcessAdditionalParameters(
                        session,
                        requestHeader.AdditionalHeader);

                    await m_server.m_semaphoreSlim.WaitAsync(requestLifetime.CancellationToken).ConfigureAwait(false);
                    try
                    {
                        // return the application instance certificate for the server.
                        if (requireEncryption)
                        {
                            // check if complete chain should be sent.
                            if (certificates.SendCertificateChain)
                            {
                                serverCertificate = instanceEntry!.GetEncodedChainBlob().ToByteString();
                            }
                            else
                            {
                                serverCertificate = instanceCertificate.RawData.ToByteString();
                            }
                        }

                        // return the endpoints supported by the server.
                        serverEndpoints = m_server.GetEndpointDescriptions(endpointUrl, m_server.BaseAddresses, default);
                    }
                    finally
                    {
                        m_server.m_semaphoreSlim.Release();
                    }

                    // The acquired certificate entry is a caller-owned ref-counted handle, so rotation cannot
                    // dispose the X509Certificate2 core while the signature is created outside the server lock.
                    SignatureData? serverSignature = m_server.CreateSessionServerSignature(
                        context,
                        instanceCertificate,
                        parsedClientCertificate,
                        clientNonce,
                        serverNonce);

                    m_server.m_logger.ServerSESSIONCREATEDSessionIdSessionId(sessionId);

                    // report audit for successful create session
                    m_server.ServerInternal.ReportAuditCreateSessionEvent(
                        context.AuditEntryId!,
                        session!,
                        context.ChannelContext.SecureChannelId,
                        clientCertificate,
                        clientCertificateThumbprint,
                        revisedSessionTimeout,
                        m_server.m_logger);

                    ResponseHeader responseHeader = m_server.CreateResponse(requestHeader, StatusCodes.Good);

                    if (parameters != null)
                    {
                        responseHeader.AdditionalHeader = new ExtensionObject(parameters);
                    }

                    m_server.ServerInternal.UpdateServerDiagnostics(diagnostics => diagnostics.CumulatedSessionCount++);

                    return new CreateSessionResponse
                    {
                        ResponseHeader = responseHeader,
                        SessionId = sessionId,
                        AuthenticationToken = authenticationToken,
                        RevisedSessionTimeout = revisedSessionTimeout,
                        ServerNonce = serverNonce,
                        ServerCertificate = serverCertificate,
                        ServerEndpoints = serverEndpoints,
                        ServerSignature = serverSignature!,
                        MaxRequestMessageSize = maxRequestMessageSize
                    };
                }
                catch (Exception exception)
                {
                    ServiceResultException e = exception as ServiceResultException
                        ?? ServiceResultException.Create(
                            StatusCodes.BadUnexpectedError,
                            exception,
                            "CreateSession failed: {0}",
                            exception.Message);
                    m_server.m_logger.ServerSESSIONCREATEFailedErrorMessage(e.Message);

                    // report the failed AuditCreateSessionEvent. The channel id and the
                    // request certificate are known even when no session was created.
                    m_server.ServerInternal.ReportAuditCreateSessionEvent(
                        context.AuditEntryId!,
                        session,
                        context.ChannelContext?.SecureChannelId,
                        clientCertificate,
                        clientCertificateThumbprint,
                        revisedSessionTimeout,
                        m_server.m_logger,
                        e);

                    if (session != null)
                    {
                        await m_server.ServerInternal.SessionManager.CloseSessionAsync(session.Id, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        parsedClientCertificate?.Dispose();
                        clientIssuerCertificates?.Dispose();
                    }

                    CountRejectedSession(m_server.ServerInternal, e.StatusCode);

                    throw m_server.TranslateException((DiagnosticsMasks)requestHeader.ReturnDiagnostics, [], e)!;
                }
                finally
                {
                    m_server.OnRequestComplete(context);
                }

                // Audits a failed client certificate check and lets m_server.OnApplicationCertificateError
                // decide. The certificate is released only when the error is not accepted.
                void HandleClientCertificateError(Exception e)
                {
                    try
                    {
                        // report audit event for client certificate
                        m_server.ReportAuditCertificateEvent(parsedClientCertificate!, e);

                        m_server.OnApplicationCertificateError(clientCertificate, new ServiceResult(e));

                        // accepted: the session may use the certificate, but it did not
                        // pass validation and grants no application-based roles.
                        clientCertificateErrorAccepted = true;
                    }
                    catch
                    {
                        parsedClientCertificate?.Dispose();
                        parsedClientCertificate = null;
                        clientIssuerCertificates?.Dispose();
                        clientIssuerCertificates = null;
                        throw;
                    }
                }
            }

            /// <summary>
            /// Audits and counts a CreateSession refused before the request was admitted
            /// and validated, for example with Bad_ServerTooBusy or Bad_ServerHalted.
            /// </summary>
            private void ReportCreateSessionRejected(
                string? auditEntryId,
                string? secureChannelId,
                ByteString clientCertificate,
                Exception exception)
            {
                // a server that is not started or already stopped has no diagnostics
                // to update; the original rejection is what the caller has to see.
                ServerInternalData? server = m_server.m_serverInternal;
                if (server == null)
                {
                    return;
                }

                // Reporting is best effort: it must never replace the rejection the client gets.
                try
                {
                    server.ReportAuditCreateSessionEvent(
                        auditEntryId!,
                        null,
                        secureChannelId,
                        clientCertificate,
                        null,
                        0,
                        m_server.m_logger,
                        exception);
                    CountRejectedSession(
                        server,
                        exception is ServiceResultException sre ? sre.StatusCode : StatusCodes.BadUnexpectedError);
                }
                catch (Exception e)
                {
                    m_server.m_logger.ReportingRejectedCreateSessionFailed(e);
                }
            }

            /// <summary>
            /// Counts a rejected session establishment request (Part 5 12.9).
            /// </summary>
            private void CountRejectedSession(IServerInternal server, StatusCode statusCode)
            {
                server.UpdateServerDiagnostics(diagnostics =>
                {
                    diagnostics.RejectedSessionCount++;
                    diagnostics.RejectedRequestsCount++;

                    if (m_server.IsSecurityError(statusCode))
                    {
                        diagnostics.SecurityRejectedSessionCount++;
                        diagnostics.SecurityRejectedRequestsCount++;
                    }
                });
            }

            /// <summary>
            /// Looks up a session whose activation committed; a session that was removed
            /// or started closing since then fails with Bad_SessionClosed.
            /// </summary>
            private ISession GetActivatedSessionOrThrowClosed(NodeId authenticationToken)
            {
                ISession? session = m_server.ServerInternal.SessionManager.GetSession(authenticationToken);
                if (session == null || SessionTermination.IsClosingOrClaimed(session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
                return session;
            }

            /// <summary>
            /// Invokes the ActivateSession service.
            /// </summary>
            public async ValueTask<ActivateSessionResponse> ActivateSessionAsync(
                SecureChannelContext secureChannelContext,
                RequestHeader? requestHeader,
                SignatureData? clientSignature,
                ArrayOf<SignedSoftwareCertificate> clientSoftwareCertificates,
                ArrayOf<string> localeIds,
                ExtensionObject userIdentityToken,
                SignatureData? userTokenSignature,
                RequestLifetime requestLifetime)
            {
                ByteString serverNonce;

                // Admission control: reject with BadServerTooBusy before the CPU-bound
                // signature / identity-token verification when at capacity. The lease
                // (a concurrency permit) is held for the duration of the call. It is
                // acquired before the request is registered, so a handshake that waits
                // for a permit is not a request that lifecycle drains wait for.
                using IDisposable? rateLimitLease = await m_server.m_admission.BeginSessionEstablishmentOrThrowAsync(
                    secureChannelContext, requestHeader?.AuthenticationToken ?? default, requestLifetime)
                    .ConfigureAwait(false);

                using OperationContext context = await m_server.ValidateRequestAsync(
                    secureChannelContext,
                    requestHeader,
                    RequestType.ActivateSession,
                    requestLifetime).ConfigureAwait(false);

                try
                {
                    (serverNonce, ServiceResult activationStatus) = await ActivateAndNotifyAsync().ConfigureAwait(false);
                    ISession session = GetActivatedSessionOrThrowClosed(requestHeader.AuthenticationToken);

                    AdditionalParametersType? parameters = null;
                    try
                    {
                        parameters = m_server.ActivateSessionProcessAdditionalParameters(
                            session,
                            requestHeader.AdditionalHeader);
                    }
                    catch (Exception e)
                    {
                        // e.g. the session started closing.
                        m_server.m_logger.ActivateSessionPostCommitStepFailed(e, session.Id);
                    }

                    // A key the identity token used is replaced inside the activation; the
                    // replacement must reach the client, whose next token can only be
                    // encrypted with it (OPC 10000-6 6.8.2). Return it when the processing
                    // above failed or an override did not hand it out.
                    parameters = AppendUnsentEphemeralKey(session, parameters);

                    // a close that started during the awaited callbacks above must not be
                    // reported as a successful activation of a closing or removed session.
                    if (!ReferenceEquals(
                            GetActivatedSessionOrThrowClosed(requestHeader.AuthenticationToken),
                            session))
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    }

                    m_server.m_logger.ServerSESSIONACTIVATED(session.Id);

                    // report the audit event for session activate
                    m_server.ServerInternal.ReportAuditActivateSessionEvent(
                        m_server.m_logger,
                        context.AuditEntryId!,
                        session!);

                    // Use the activation status from SessionManager so spec-defined
                    // success codes (e.g. Good_PasswordChangeRequired per Part 18
                    // §5.2.8) reach the client.
                    StatusCode resultStatus = ServiceResult.IsGood(activationStatus)
                        ? activationStatus.StatusCode
                        : StatusCodes.Good;
                    ResponseHeader responseHeader = m_server.CreateResponse(requestHeader, resultStatus);

                    if (parameters != null)
                    {
                        responseHeader.AdditionalHeader = new ExtensionObject(parameters);
                    }
                    return new ActivateSessionResponse
                    {
                        ResponseHeader = responseHeader,
                        ServerNonce = serverNonce,
                        Results = [],
                        DiagnosticInfos = []
                    };
                }
                catch (Exception e)
                {
                    // report the audit event for failed session activate. Every failure,
                    // including cancellation and unexpected exceptions, is audited and
                    // counted, not only ServiceResultExceptions.
                    ISession? session = m_server.ServerInternal.SessionManager
                        .GetSession(requestHeader.AuthenticationToken);

                    m_server.m_logger.ServerSESSIONACTIVATEFailedErrorMessage(session?.Id, e.Message);
                    m_server.ServerInternal.ReportAuditActivateSessionEvent(
                        m_server.m_logger,
                        context.AuditEntryId!,
                        session!,
                        ExtractAuditUserIdentityToken(userIdentityToken),
                        e);

                    var sre = e as ServiceResultException;
                    m_server.ServerInternal.UpdateServerDiagnostics(diagnostics =>
                    {
                        diagnostics.RejectedSessionCount++;
                        diagnostics.RejectedRequestsCount++;

                        if (sre != null && m_server.IsSecurityError(sre.StatusCode))
                        {
                            diagnostics.SecurityRejectedSessionCount++;
                            diagnostics.SecurityRejectedRequestsCount++;
                        }
                    });

                    if (sre == null)
                    {
                        throw;
                    }

                    throw m_server.TranslateException(
                        (DiagnosticsMasks)requestHeader.ReturnDiagnostics,
                        localeIds,
                        sre)!;
                }
                finally
                {
                    m_server.OnRequestComplete(context);
                }

                async ValueTask<(ByteString ServerNonce, ServiceResult ActivationStatus)> ActivateAndNotifyAsync()
                {
                    (bool identityChanged, ByteString nonce, ServiceResult status) =
                        await m_server.ServerInternal.SessionManager.ActivateSessionAsync(
                            context,
                            requestHeader.AuthenticationToken,
                            clientSignature,
                            userIdentityToken,
                            userTokenSignature,
                            localeIds,
                            requestLifetime.CancellationToken).ConfigureAwait(false);
                    ISession activatedSession = GetActivatedSessionOrThrowClosed(requestHeader.AuthenticationToken);
                    if (identityChanged)
                    {
                        try
                        {
                            await m_server.ServerInternal.NodeManager.SessionActivatedAsync(
                                context, activatedSession.Id, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            m_server.m_logger.ActivateSessionPostCommitStepFailed(e, activatedSession.Id);
                        }
                    }
                    return (nonce, status);
                }
            }

            private static UserIdentityToken? ExtractAuditUserIdentityToken(ExtensionObject userIdentityToken)
            {
                if (userIdentityToken.TryGetValue(out UserIdentityToken? decodedToken))
                {
                    return decodedToken;
                }

                if (userIdentityToken.Encoding != ExtensionObjectEncoding.Binary ||
                    !userIdentityToken.TryGetAsBinary(out ByteString _))
                {
                    return null;
                }

                return BaseVariableState.DecodeExtensionObject(
                    null!,
                    typeof(UserIdentityToken),
                    userIdentityToken,
                    false) as UserIdentityToken;
            }

            private readonly StandardServer m_server;
        }
    }
}
