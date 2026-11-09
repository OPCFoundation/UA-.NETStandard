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

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Selects the standalone application namespace. Per-instance providers and optional features belong to GmsBuilder.
    /// </summary>
    public sealed class GmsServerOptions
    {
        /// <summary>
        /// Gets or sets the standalone instance namespace; shared hosting retains its existing owner's namespace.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcfoundation:gms:instances";

        /// <summary>
        /// Rejects invalid application namespace selection before registering a namespace owner.
        /// </summary>
        internal void Validate()
        {
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("GMS instances require an absolute application-owned namespace URI.");
            }
            foreach (string namespaceUri in GmsModel.GetNamespaceUris())
            {
                if (string.Equals(InstanceNamespaceUri, namespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException("GMS instances cannot use a standard model namespace.");
                }
            }
        }
    }
}
