/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Security.Certificates;

namespace UaLens.Connection;

/// <summary>
/// Owns the validator hook for one primary connection's private configuration.
/// The synchronous hook only captures a rejected certificate or consults a
/// pre-existing decision. It never prompts, waits or writes a trust store.
/// </summary>
internal sealed class ConnectionTrustScope : IDisposable
{
    public ConnectionTrustScope(
        ConnectionProfile profile,
        EndpointDescription endpoint,
        ICertificateValidatorEx validator,
        ITelemetryContext telemetry)
    {
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        m_validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(telemetry);
        profile.RequireMatch(endpoint);
        if (!endpoint.ServerCertificate.IsEmpty)
        {
            using Certificate certificate = Utils.ParseCertificateBlob(endpoint.ServerCertificate, telemetry);
            m_expectedCertificate = ByteString.From(certificate.RawData);
        }
        m_previousCallback = validator.AcceptError;
        m_callback = AcceptError;
        validator.AcceptError = m_callback;
    }

    public void BeginAttempt()
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            m_rejectedCertificate?.Dispose();
            m_rejectedCertificate = null;
            m_rejectedError = null;
        }
    }

    public CertificateTrustRequest? TakeRequest()
    {
        lock (m_gate)
        {
            if (m_rejectedCertificate is null || m_rejectedError is null || m_disposed)
            {
                return null;
            }
            using Certificate certificate = m_rejectedCertificate;
            var request = new CertificateTrustRequest(
                m_profile,
                ByteString.From(certificate.RawData),
                m_rejectedError);
            m_rejectedCertificate = null;
            m_rejectedError = null;
            return request;
        }
    }

    public void AcceptOnce(CertificateTrustRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (request.Profile != m_profile ||
                !IsTrustError(request.Error) ||
                (!m_expectedCertificate.IsEmpty &&
                    !m_expectedCertificate.Span.SequenceEqual(request.CertificateData.Span)))
            {
                throw new InvalidOperationException("The trust decision does not belong to this connection.");
            }
            m_acceptedCertificate = request.CertificateData;
        }
    }

    public static async Task PersistAsync(
        ICertificateTrustListManager manager,
        CertificateTrustRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsTrustError(request.Error))
        {
            throw new ServiceResultException(request.Error);
        }

        ct.ThrowIfCancellationRequested();
        using var certificate = new Certificate(request.CertificateData.Span);
        ITrustListTransaction transaction = await manager
            .BeginUpdateAsync(TrustListIdentifier.Peers, ct)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await transaction.AddTrustedCertificateAsync(certificate, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (m_gate)
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_acceptedCertificate = default;
            m_rejectedCertificate?.Dispose();
            m_rejectedCertificate = null;
            m_rejectedError = null;
            if (ReferenceEquals(m_validator.AcceptError, m_callback))
            {
                m_validator.AcceptError = m_previousCallback;
            }
        }
    }

    private bool AcceptError(Certificate certificate, ServiceResult error)
    {
        lock (m_gate)
        {
            if (m_disposed || !IsTrustError(error))
            {
                return false;
            }
            byte[] data = certificate.RawData;
            if (!m_expectedCertificate.IsEmpty && !m_expectedCertificate.Span.SequenceEqual(data))
            {
                return false;
            }
            if (!m_acceptedCertificate.IsEmpty && m_acceptedCertificate.Span.SequenceEqual(data))
            {
                return true;
            }

            if (m_rejectedCertificate is null)
            {
                m_rejectedCertificate = certificate.AddRef();
                m_rejectedError = error;
            }
            return false;
        }
    }

    private static bool IsTrustError(ServiceResult error)
    {
        for (ServiceResult? current = error; current is not null; current = current.InnerResult)
        {
            if (current.StatusCode != StatusCodes.BadCertificateUntrusted)
            {
                return false;
            }
        }
        return true;
    }

    private readonly Lock m_gate = new();
    private readonly ConnectionProfile m_profile;
    private readonly ICertificateValidatorEx m_validator;
    private readonly Func<Certificate, ServiceResult, bool>? m_previousCallback;
    private readonly Func<Certificate, ServiceResult, bool> m_callback;
    private readonly ByteString m_expectedCertificate;
    private ByteString m_acceptedCertificate;
    private Certificate? m_rejectedCertificate;
    private ServiceResult? m_rejectedError;
    private bool m_disposed;
}
