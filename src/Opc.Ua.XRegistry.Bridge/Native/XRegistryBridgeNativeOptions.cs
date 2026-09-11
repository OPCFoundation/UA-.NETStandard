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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Opt-in native transport and projection limits. The base address space has one
    /// explicit visibility scope; the extension always uses the authenticated caller.
    /// </summary>
    public sealed record XRegistryBridgeNativeOptions
    {
        /// <summary>
        /// Gets the unique namespace URI for this projection's instances.
        /// </summary>
        public string NamespaceUri { get; init; } = "urn:opcfoundation.org:xregistry:bridge";

        /// <summary>
        /// Gets the registry root's string identifier within the instance namespace.
        /// </summary>
        public string RootIdentifier { get; init; } = "XRegistryBridge";

        /// <summary>
        /// Gets the maximum encoded request, response or model transfer size.
        /// </summary>
        public int MaxMessageBytes { get; init; } = 33_554_432;

        /// <summary>
        /// Gets the maximum resource document size.
        /// </summary>
        public int MaxDocumentBytes { get; init; } = 16_777_216;

        /// <summary>
        /// Gets the maximum number of concurrent file handles and temporary transfers.
        /// </summary>
        public int MaxOpenFiles { get; init; } = 64;

        /// <summary>
        /// Gets the shared byte budget for retained file snapshots and upload buffers.
        /// </summary>
        public int MaxBufferedBytes { get; init; } = 134_217_728;

        /// <summary>
        /// Gets the maximum number of entities accepted in a complete projection inventory.
        /// </summary>
        public int MaxEntities { get; init; } = 4096;

        /// <summary>
        /// Gets the maximum continuation pages consumed by one inventory or native Browse.
        /// </summary>
        public int MaxBrowsePages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum FileType Read or Write chunk size.
        /// </summary>
        public int ChunkSize { get; init; } = 65_536;

        /// <summary>
        /// Gets the lifetime of a native file handle or temporary transfer.
        /// </summary>
        public TimeSpan FileLifetime { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Mutations always require SignAndEncrypt. A false configuration is rejected.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public bool RequireEncryptedWrites
        {
            get => true;
            init
            {
                if (!value)
                {
                    throw new ArgumentException(
                        "Native mutations always require SignAndEncrypt.", nameof(RequireEncryptedWrites));
                }
            }
        }

        /// <summary>
        /// Gets whether this opt-in manager publishes the experimental protocol extension.
        /// </summary>
        public bool EnableExperimentalExtension { get; init; } = true;

        /// <summary>
        /// Gets the upstream identity and visibility scope used to build the shared address space.
        /// </summary>
        public XRegistryCallContext ProjectionContext { get; init; } = XRegistryCallContext.Anonymous;

        /// <summary>
        /// Trusted host identity mapping, never a value decoded from a transport envelope.
        /// The default does not grant application-specific write roles.
        /// </summary>
        public Func<ISystemContext, XRegistryCallContext> ContextFactory { get; init; } = CreateContext;

        /// <summary>
        /// Authorizes the original authenticated native session before upstream identity mapping.
        /// The Boolean argument is true for a mutation. A missing policy denies mutations;
        /// reads still require an authenticated session and the configured projection scope.
        /// Authorization cannot relax the SignAndEncrypt requirement for mutations.
        /// </summary>
        public Func<ISystemContext, bool, CancellationToken, ValueTask<bool>>? AuthorizeCallerAsync { get; init; }

        /// <summary>
        /// An explicitly supplied model for a base server without a Model file.
        /// No collection schema is inferred from a partial inventory.
        /// </summary>
        public JsonElement BaseModel
        {
            get => m_baseModel;
            init => m_baseModel = value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
        }

        /// <summary>
        /// Gets the clock used to expire native file handles and transfers.
        /// </summary>
        public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

        /// <summary>
        /// Gets the registry root address without assuming a numeric namespace index.
        /// </summary>
        public ExpandedNodeId RootAddress => new(RootIdentifier, NamespaceUri);

        internal void Validate()
        {
            if (!Uri.TryCreate(NamespaceUri, UriKind.Absolute, out _) ||
                NamespaceUri == Ua.Namespaces.OpcUa ||
                NamespaceUri == XRegistryWellKnown.XRegistryNamespaceUri ||
                NamespaceUri == ExperimentalNamespaceUri)
            {
                throw new ArgumentException("A separate absolute instance namespace URI is required.");
            }
            if (string.IsNullOrWhiteSpace(RootIdentifier))
            {
                throw new ArgumentException("A root identifier is required.");
            }
            if (MaxMessageBytes is <= 0 or > int.MaxValue / 2 ||
                MaxDocumentBytes <= 0 ||
                MaxDocumentBytes > MaxMessageBytes ||
                MaxOpenFiles is < 1 or > ushort.MaxValue ||
                MaxBufferedBytes < MaxMessageBytes ||
                MaxEntities <= 0 ||
                MaxBrowsePages <= 0 ||
                ChunkSize <= 0 ||
                ChunkSize > MaxDocumentBytes ||
                FileLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxMessageBytes), "Invalid native transport limits.");
            }
            ProjectionContext.ThrowIfNull(nameof(ProjectionContext));
            ContextFactory.ThrowIfNull(nameof(ContextFactory));
            TimeProvider.ThrowIfNull(nameof(TimeProvider));
        }

        internal static XRegistryCallContext CreateContext(ISystemContext context)
        {
            context.ThrowIfNull(nameof(context));
            IUserIdentity? identity = (context as ISessionSystemContext)?.UserIdentity;
            bool authenticated = identity is not null && identity.TokenType != UserTokenType.Anonymous;
            return new XRegistryCallContext(authenticated ? identity!.DisplayName : "anonymous")
            {
                Authority = authenticated ? "opcua" : string.Empty,
                IsAuthenticated = authenticated,
                SessionId = context is ISessionSystemContext { SessionId: { } sessionId }
                    ? sessionId.ToString()
                    : null
            };
        }

        /// <summary>The repository-local experimental model namespace, not a published companion namespace.</summary>
        public const string ExperimentalNamespaceUri =
            "http://opcfoundation.org/UA/xRegistry/Bridge/Experimental/";

        private readonly JsonElement m_baseModel;
    }
}
