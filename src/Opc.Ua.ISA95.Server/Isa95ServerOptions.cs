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

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Configures the ISA-95 address-space instances hosted by
    /// <see cref="Isa95NodeManager"/>.
    /// </summary>
    public sealed class Isa95ServerOptions
    {
        /// <summary>
        /// Namespace used for server-owned ISA-95 instances.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } =
            "urn:opcfoundation:ua:isa95:server";

        /// <summary>
        /// BrowseName of the root folder organized by the Objects folder.
        /// </summary>
        public string RootBrowseName { get; set; } = "ISA95";

        /// <summary>
        /// Enables the OPC-10031-4 V1 endpoint instances.
        /// </summary>
        public bool EnableJobControlV1 { get; set; } = true;

        /// <summary>
        /// Enables the OPC-10031-4 V2 endpoint instances.
        /// </summary>
        public bool EnableJobControlV2 { get; set; } = true;

        /// <summary>
        /// Exposes the optional V2 prepare, interrupted and ended substates.
        /// </summary>
        public bool EnableJobControlSubStates { get; set; } = true;

        /// <summary>
        /// BrowseName prefix for the V1 endpoint instances.
        /// </summary>
        public string JobControlV1BrowseName { get; set; } = "JobControlV1";

        /// <summary>
        /// BrowseName prefix for the V2 endpoint instances.
        /// </summary>
        public string JobControlV2BrowseName { get; set; } = "JobControlV2";

        /// <summary>
        /// Exposes Job Response Provider endpoint instances.
        /// </summary>
        public bool ExposeJobResponseProvider { get; set; } = true;

        /// <summary>
        /// Exposes Job Response Receiver endpoint instances.
        /// </summary>
        public bool ExposeJobResponseReceiver { get; set; } = true;

        internal void Validate()
        {
            ValidateUri(InstanceNamespaceUri, nameof(InstanceNamespaceUri));
            ValidateBrowseName(RootBrowseName, nameof(RootBrowseName));
            ValidateBrowseName(JobControlV1BrowseName, nameof(JobControlV1BrowseName));
            ValidateBrowseName(JobControlV2BrowseName, nameof(JobControlV2BrowseName));
        }

        private static void ValidateUri(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.IsWellFormedUriString(value, UriKind.Absolute))
            {
                throw new ArgumentException("A valid absolute URI is required.", name);
            }
        }

        private static void ValidateBrowseName(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty BrowseName is required.", name);
            }
        }
    }
}
