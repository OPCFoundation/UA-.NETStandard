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

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Options for the standalone Vision node manager.
    /// </summary>
    public sealed class VisionServerOptions
    {
        /// <summary>
        /// Default application-owned namespace for Vision instances.
        /// </summary>
        public const string DefaultInstanceNamespaceUri =
            "urn:opcua-netstandard:vision:instances";

        /// <summary>
        /// Default version reported on Server/Vision.
        /// </summary>
        public const string DefaultSpecificationVersion = "0.1.0";

        /// <summary>
        /// Gets or sets the application-owned namespace URI used for the
        /// Vision instances created underneath the well-known Vision root.
        /// </summary>
        /// <remarks>
        /// This namespace is application-specific and must be distinct from
        /// the OPC UA base namespace and the Vision companion namespace.
        /// </remarks>
        public string InstanceNamespaceUri { get; set; } = DefaultInstanceNamespaceUri;

        /// <summary>
        /// Gets or sets the specification version this Server reports.
        /// </summary>
        public string SpecificationVersion { get; set; } = DefaultSpecificationVersion;

        /// <summary>
        /// Gets or sets the additional facets the Server declares beyond those
        /// the facet calculator derives from the address space. This is the
        /// escape hatch for facets whose requirements cannot be inspected
        /// structurally (for example, an interop facet requiring behavioural
        /// contract that is met by the host).
        /// </summary>
        public ArrayOf<string> AdditionalFacets { get; set; } = ArrayOf<string>.Empty;

        /// <summary>
        /// Validates the option values.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// One of the required options is empty or invalid.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The instance namespace is one of the standard model namespaces.
        /// </exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri))
            {
                throw new ArgumentException(
                    "VisionServerOptions.InstanceNamespaceUri must not be empty.",
                    nameof(InstanceNamespaceUri));
            }
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out Uri? uri) ||
                string.IsNullOrEmpty(uri.Scheme))
            {
                throw new ArgumentException(
                    "VisionServerOptions.InstanceNamespaceUri must be an absolute URI or URN.",
                    nameof(InstanceNamespaceUri));
            }
            if (string.IsNullOrWhiteSpace(SpecificationVersion))
            {
                throw new ArgumentException(
                    "VisionServerOptions.SpecificationVersion must not be empty.",
                    nameof(SpecificationVersion));
            }
            if (InstanceNamespaceUri == global::Opc.Ua.Namespaces.OpcUa ||
                InstanceNamespaceUri == global::Opc.Ua.Vision.Namespaces.Vision)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "VisionServerOptions.InstanceNamespaceUri '{0}' is a model namespace. " +
                    "Configure a distinct application-owned namespace for Vision instances.",
                    InstanceNamespaceUri);
            }
        }
    }
}
