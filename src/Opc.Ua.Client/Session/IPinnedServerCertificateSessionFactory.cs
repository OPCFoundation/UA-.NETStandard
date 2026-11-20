// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// A session factory suitable for managed-application verification with a pinned server certificate.
    /// </summary>
    /// <remarks>
    /// Implementations must create the selected channel without receiving or sending the target user
    /// identity. For UA-TCP, before <c>CreateChannelAsync</c> returns, the implementation must
    /// authenticate the signed OpenSecureChannel response, verify its sender certificate against the selected
    /// endpoint certificate, and verify the requested security mode and policy. The channel must be safe to
    /// dispose more than once because the verifier retries channel disposal if session cleanup fails.
    /// Implementations must preserve the selected endpoint in both the channel endpoint description and the
    /// session's configured endpoint description. The caller verifies both before opening the session and after
    /// it opens. The implementation must reject a CreateSession response whose server application URI differs
    /// from the selected application URI before sending ActivateSession credentials. The caller verifies the
    /// UA-TCP channel certificate and endpoint before opening the session. HTTPS channels must implement
    /// <see cref="IHttpsServerCertificateValidation"/> and defer requests until the caller selects TLS trust or a
    /// TLS pin. HTTPS uses Sign with a non-None policy. Before sending ActivateSession credentials the session
    /// must verify the exact selected application certificate, its ApplicationUri and the server's application
    /// signature. The TLS certificate is independent of the application certificate; the TLS terminator is
    /// trusted with transport and session data. Legacy application signatures do not cryptographically bind
    /// that terminator. Implementations must not substitute a
    /// different channel or endpoint. The returned session must implement
    /// <see cref="IExpectedServerApplicationUriSession"/> so the caller can require the selected application URI
    /// before opening it. A consumer can require the concrete factory type to declare this interface rather than
    /// inherit it.
    /// </remarks>
    public interface IPinnedServerCertificateSessionFactory : ISessionFactory
    {
    }
}
