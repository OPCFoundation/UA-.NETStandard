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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Options for the OPC 30010 node manager.
    /// </summary>
    public sealed class AutoIdServerOptions
    {
        /// <summary>
        /// Gets or sets the longest duration a synchronous <c>Scan</c> call may request and run.
        /// Defaults to 30 seconds.
        /// </summary>
        /// <remarks>
        /// OPC 30010 6.1.3.4 forbids a <c>Scan</c> with every termination condition infinite.
        /// The limit bounds the finite requests as well: a request for a longer
        /// <c>Duration</c> is rejected with <c>Bad_InvalidArgument</c>, and a provider that
        /// does not return in time is cancelled and the call fails with <c>Bad_Timeout</c>.
        /// </remarks>
        public TimeSpan MaximumScanDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the largest <c>Length</c> a <c>ReadTag</c> call may request, in bytes.
        /// Defaults to 65536.
        /// </summary>
        public uint MaximumTagReadLength { get; set; } = 65536;

        /// <summary>
        /// Gets or sets the largest number of scan results delivered in one scan event or one
        /// <c>Scan</c> response. Defaults to 1000.
        /// </summary>
        /// <remarks>
        /// A provider that reports more results than this fails the scan with
        /// <c>Bad_ResponseTooLarge</c> rather than truncating them silently.
        /// </remarks>
        public int MaximumScanResults { get; set; } = 1000;

        /// <summary>
        /// Gets or sets whether the readers publish an access event (OPC 30010 7.10 and 7.11)
        /// after a command accessed an AutoID identifier. Defaults to <see langword="true"/>.
        /// </summary>
        public bool PublishAccessEvents { get; set; } = true;

        /// <summary>
        /// Gets or sets extra model or vendor-extension namespace URIs the owner registers.
        /// </summary>
        public ArrayOf<string> AdditionalNamespaceUris { get; set; }

        internal AutoIdServerOptions Validate()
        {
            if (MaximumScanDuration <= TimeSpan.Zero || MaximumScanDuration > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumScanDuration),
                    MaximumScanDuration,
                    "The maximum scan duration must be positive and at most one hour.");
            }
            if (MaximumTagReadLength == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumTagReadLength),
                    "The maximum tag read length must be positive.");
            }
            if (MaximumScanResults < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumScanResults),
                    MaximumScanResults,
                    "The maximum scan result count must be positive.");
            }
            foreach (string uri in AdditionalNamespaceUris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException(
                        "An additional namespace URI must not be null or empty.",
                        nameof(AdditionalNamespaceUris));
                }
            }
            return this;
        }
    }
}
