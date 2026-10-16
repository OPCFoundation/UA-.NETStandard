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
