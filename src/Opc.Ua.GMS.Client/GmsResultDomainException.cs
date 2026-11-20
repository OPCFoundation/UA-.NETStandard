/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// A nonzero result-method domain code received with a successful UA service status.
    /// </summary>
    public sealed class GmsResultDomainException : Exception
    {
        /// <summary>
        /// Creates an exception without a received domain code.
        /// </summary>
        public GmsResultDomainException()
        {
        }

        /// <summary>
        /// Creates an exception with a diagnostic message and no received domain code.
        /// </summary>
        public GmsResultDomainException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception retaining an underlying failure and no received domain code.
        /// </summary>
        public GmsResultDomainException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Captures the original domain code without interpreting it as a UA service status.
        /// </summary>
        public GmsResultDomainException(string operation, int error)
            : base(string.Format(CultureInfo.InvariantCulture,
                "GMS result operation '{0}' returned domain code {1}.", operation, error))
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Error = error;
        }

        /// <summary>
        /// Gets the originating standard method.
        /// </summary>
        public string Operation { get; } = string.Empty;

        /// <summary>
        /// Gets the unmodified domain code, or null when no response supplied one.
        /// </summary>
        public int? Error { get; }
    }
}
