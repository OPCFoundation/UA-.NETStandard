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

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Configures the stock Machinery server hosting integration.
    /// </summary>
    public sealed class MachineryServerOptions
    {
        /// <summary>
        /// Default application-owned namespace for Machinery instances.
        /// </summary>
        public const string DefaultInstanceNamespaceUri =
            "urn:opcua-netstandard:machinery:instances";

        /// <summary>
        /// Gets or sets the application-owned namespace URI used for
        /// dynamically created Machinery instances.
        /// </summary>
        /// <remarks>
        /// This namespace is application-specific and is not an OPC Foundation
        /// companion-specification or configured model-provider namespace.
        /// </remarks>
        public string InstanceNamespaceUri { get; set; } = DefaultInstanceNamespaceUri;

        /// <summary>
        /// Gets or sets the OPC 40001 parts this server exposes. Defaults to
        /// the whole series; trim it so the server carries only the models it
        /// actually serves.
        /// </summary>
        public MachineryParts Parts { get; set; } = MachineryParts.All;

        /// <summary>
        /// Gets or sets the maximum number of concurrently open result-transfer
        /// handles a single <c>ResultTransfer</c> object hands out. Reached, a
        /// further <c>GenerateFileForRead</c> is refused with
        /// <c>Bad_TooManyOperations</c>.
        /// </summary>
        public int MaxConcurrentResultTransfers { get; set; } = 8;

        /// <summary>
        /// Gets or sets how long an open result-transfer handle survives
        /// without activity before the server reclaims it. Published on the
        /// transfer object's <c>ClientProcessingTimeout</c>.
        /// </summary>
        public TimeSpan ResultTransferTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Gets or sets the maximum number of result identifiers a single
        /// <c>ResultManagement</c> object keeps pinned at once across
        /// <c>GetLatestResult</c>, <c>GetResultById</c> and
        /// <c>GetResultIdListFiltered</c>. Reached, a further pin is refused
        /// with <c>Bad_TooManyOperations</c> instead of growing the handle
        /// table without bound.
        /// </summary>
        public int MaxPinnedResultHandles { get; set; } = 64;

        /// <summary>
        /// Gets or sets how long a pinned result handle survives without a
        /// client releasing it via <c>ReleaseResultHandle</c> before the
        /// server reclaims it. Reclaiming is swept at the start of every
        /// pin, the only moment the cap can actually bite.
        /// </summary>
        public TimeSpan PinnedResultHandleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Validates the configured options.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <see cref="InstanceNamespaceUri"/> is empty or is not an absolute
        /// URI, or a limit is out of range.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="InstanceNamespaceUri"/> is a standard model namespace or
        /// <see cref="Parts"/> is not a valid combination.
        /// </exception>
        public void Validate()
        {
            MachineryPartsValidation.Validate(Parts);

            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri))
            {
                throw new ArgumentException(
                    "MachineryServerOptions.InstanceNamespaceUri must not be empty.",
                    nameof(InstanceNamespaceUri));
            }

            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out Uri? uri) ||
                string.IsNullOrEmpty(uri.Scheme))
            {
                throw new ArgumentException(
                    "MachineryServerOptions.InstanceNamespaceUri must be an absolute URI or URN.",
                    nameof(InstanceNamespaceUri));
            }

            if (MaxConcurrentResultTransfers < 1)
            {
                throw new ArgumentException(
                    "MachineryServerOptions.MaxConcurrentResultTransfers must be positive.",
                    nameof(MaxConcurrentResultTransfers));
            }

            if (ResultTransferTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "MachineryServerOptions.ResultTransferTimeout must be positive.",
                    nameof(ResultTransferTimeout));
            }

            if (MaxPinnedResultHandles < 1)
            {
                throw new ArgumentException(
                    "MachineryServerOptions.MaxPinnedResultHandles must be positive.",
                    nameof(MaxPinnedResultHandles));
            }

            if (PinnedResultHandleTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "MachineryServerOptions.PinnedResultHandleTimeout must be positive.",
                    nameof(PinnedResultHandleTimeout));
            }

            if (InstanceNamespaceUri == global::Opc.Ua.Namespaces.OpcUa ||
                InstanceNamespaceUri == global::Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "MachineryServerOptions.InstanceNamespaceUri '{0}' is a model namespace. " +
                    "Configure a distinct application-owned namespace for Machinery instances.",
                    InstanceNamespaceUri);
            }

            ArrayOf<string> modelNamespaces = MachineryServer.GetNamespaceUris(Parts);
            for (int ii = 0; ii < modelNamespaces.Count; ii++)
            {
                if (InstanceNamespaceUri == modelNamespaces[ii])
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "MachineryServerOptions.InstanceNamespaceUri '{0}' is a model " +
                        "namespace. Configure a distinct application-owned namespace " +
                        "for Machinery instances.",
                        InstanceNamespaceUri);
                }
            }
        }
    }
}
