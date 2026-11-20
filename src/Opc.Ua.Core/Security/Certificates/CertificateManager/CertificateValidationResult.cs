// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Describes the outcome of a certificate validation operation.
    /// </summary>
    public sealed class CertificateValidationResult
    {
        /// <summary>
        /// A cached successful result.
        /// </summary>
        public static CertificateValidationResult Success { get; } =
            new CertificateValidationResult(true, StatusCodes.Good, [], false);

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationResult"/> class.
        /// </summary>
        /// <param name="isValid">Whether the certificate is valid.</param>
        /// <param name="statusCode">The OPC UA status code.</param>
        /// <param name="errors">
        /// Individual <see cref="ServiceResult"/> entries describing each error.
        /// </param>
        /// <param name="isSuppressible">
        /// Whether the validation errors can be suppressed by a callback.
        /// </param>
        public CertificateValidationResult(
            bool isValid,
            StatusCode statusCode,
            IReadOnlyList<ServiceResult> errors,
            bool isSuppressible)
        {
            IsValid = isValid;
            StatusCode = statusCode;
            Errors = errors;
            IsSuppressible = isSuppressible;
        }

        /// <summary>
        /// Gets a value indicating whether the certificate passed validation.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Gets the primary OPC UA status code for the validation result.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the list of individual validation errors.
        /// </summary>
        public IReadOnlyList<ServiceResult> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the validation errors can be
        /// suppressed by an application-level callback.
        /// </summary>
        public bool IsSuppressible { get; }

        /// <summary>
        /// Throws a <see cref="ServiceResultException"/> when the result is
        /// not valid. Use this to flow validation failures through callers
        /// that expect the legacy throwing contract without writing the
        /// <c>if (!result.IsValid) throw …</c> boilerplate.
        /// </summary>
        /// <remarks>
        /// When <see cref="Errors"/> contains at least one entry, the first
        /// entry is used as the inner <see cref="ServiceResult"/> so the
        /// caller sees the detailed error context. Otherwise the exception
        /// carries only <see cref="StatusCode"/>.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when <see cref="IsValid"/> is <see langword="false"/>.
        /// </exception>
        public void ThrowIfInvalid()
        {
            if (IsValid)
            {
                return;
            }

            if (Errors.Count > 0)
            {
                throw new ServiceResultException(Errors[0]);
            }

            throw new ServiceResultException(StatusCode);
        }
    }
}
