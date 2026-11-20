// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Extended certificate validator that supports trust-list scoping,
    /// validation options, and structured validation results.
    /// </summary>
    /// <remarks>
    /// This is the modern certificate validation contract used by
    /// <see cref="CertificateManager"/> and the OPC UA stack.
    /// </remarks>
    public interface ICertificateValidatorEx
    {
        /// <summary>
        /// Validates a certificate chain against the specified trust list.
        /// </summary>
        /// <param name="chain">The certificate chain to validate.</param>
        /// <param name="trustList">
        /// The trust list to validate against. When <see langword="null"/>
        /// (the default), <see cref="TrustListIdentifier.Peers"/> is used.
        /// Pass <see cref="TrustListIdentifier.Users"/> for X.509 user
        /// identity tokens or <see cref="TrustListIdentifier.Https"/>
        /// for HTTPS server certificates.
        /// </param>
        /// <param name="options">
        /// Optional validation options that control which checks are performed.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A <see cref="CertificateValidationResult"/> describing the outcome.
        /// </returns>
        Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            TrustListIdentifier? trustList = null,
            Security.Certificates.CertificateValidationOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Validates a single certificate against the specified trust list.
        /// </summary>
        /// <param name="certificate">The certificate to validate.</param>
        /// <param name="trustList">
        /// The trust list to validate against. When <see langword="null"/>
        /// (the default), <see cref="TrustListIdentifier.Peers"/> is used.
        /// Pass <see cref="TrustListIdentifier.Users"/> for X.509 user
        /// identity tokens or <see cref="TrustListIdentifier.Https"/>
        /// for HTTPS server certificates.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A <see cref="CertificateValidationResult"/> describing the outcome.
        /// </returns>
        Task<CertificateValidationResult> ValidateAsync(
            Certificate certificate,
            TrustListIdentifier? trustList = null,
            CancellationToken ct = default);

        /// <summary>
        /// Gets or sets a global per-error accept callback that is consulted on
        /// every validation performed by this validator. The callback receives
        /// the offending <see cref="Certificate"/> and the <see cref="ServiceResult"/>
        /// describing the error. Returning <see langword="true"/> accepts the
        /// individual error; returning <see langword="false"/> (or throwing)
        /// rejects it.
        /// </summary>
        /// <remarks>
        /// Per-call
        /// <see cref="Security.Certificates.CertificateValidationOptions.AcceptError"/>
        /// callbacks (when set on a particular <c>ValidateAsync</c> call) take
        /// precedence over this global hook.
        /// </remarks>
        System.Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }
    }
}
