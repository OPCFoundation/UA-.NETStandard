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
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Configures the LADS server runtime.
    /// </summary>
    public sealed class LadsServerOptions
    {
        /// <summary>
        /// Gets or sets the standalone owner's application namespace.
        /// Composing into an existing Machinery owner uses that owner's namespace instead.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcua-netstandard:lads:instances";

        /// <summary>
        /// Gets or sets the largest result file, in bytes, that clients can read through the <c>FileType</c>
        /// methods. The default is 16 MiB.
        /// </summary>
        public int MaxResultFileBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets how many results each program manager keeps. When a new result exceeds the limit the
        /// oldest result is removed. The default is 100.
        /// </summary>
        public int MaxRetainedResults { get; set; } = 100;

        /// <summary>
        /// Gets the Machinery parts LADS needs from its owner.
        /// </summary>
        public MachineryParts RequiredMachineryParts => MachineryParts.BuildingBlocks;

        /// <summary>
        /// Rejects invalid limits and application namespaces.
        /// </summary>
        /// <exception cref="ArgumentException">The application namespace is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public void Validate()
        {
            if (MaxResultFileBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxResultFileBytes));
            }
            if (MaxRetainedResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRetainedResults));
            }
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute application namespace URI is required.",
                    nameof(InstanceNamespaceUri));
            }
            foreach (string model in LadsModel.GetNamespaceUris())
            {
                if (string.Equals(model, InstanceNamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The application namespace must not be a model namespace.",
                        nameof(InstanceNamespaceUri));
                }
            }
            if (string.Equals(InstanceNamespaceUri, global::Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The application namespace must not be the OPC UA namespace.",
                    nameof(InstanceNamespaceUri));
            }
        }

        /// <summary>
        /// Copies the settings to another instance.
        /// </summary>
        /// <param name="target">The instance to update.</param>
        internal void CopyTo(LadsServerOptions target)
        {
            target.InstanceNamespaceUri = InstanceNamespaceUri;
            target.MaxResultFileBytes = MaxResultFileBytes;
            target.MaxRetainedResults = MaxRetainedResults;
        }
    }
}
