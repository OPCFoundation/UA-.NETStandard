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
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The closed set of model roots used by this module, never a method-name dispatcher.
    /// </summary>
    internal enum GdsTarget
    {
        /// <summary>
        /// Application registration directory.
        /// </summary>
        Directory,

        /// <summary>
        /// Pull certificate issuer directory.
        /// </summary>
        CertificateDirectory,

        /// <summary>
        /// Push-management server configuration.
        /// </summary>
        ServerConfiguration,

        /// <summary>
        /// Public certificate and revocation-list file.
        /// </summary>
        TrustList,

        /// <summary>
        /// Credential service for non-UA resources.
        /// </summary>
        KeyCredential,

        /// <summary>
        /// Modern two-phase token issuer.
        /// </summary>
        Authorization,

        /// <summary>
        /// Part 21 ticket administration.
        /// </summary>
        Registrar
    }

    /// <summary>
    /// Binds typed generated proxies to an authorized current session without owning that session.
    /// </summary>
    internal sealed class GdsAccess
    {
        /// <summary>
        /// Captures existing host services and validated resource ceilings.
        /// </summary>
        internal GdsAccess(
            OpcUaSessionManager sessions, McpFileTransfers transfers, McpSecretReferences secrets,
            GdsMcpOptions options, TimeProvider timeProvider)
        {
            Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            Transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
            Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            Options.Validate();
        }

        /// <summary>
        /// Gets the single shared session registry.
        /// </summary>
        internal OpcUaSessionManager Sessions { get; }

        /// <summary>
        /// Gets root-confined public transfers.
        /// </summary>
        internal McpFileTransfers Transfers { get; }

        /// <summary>
        /// Gets the host's protected reference gateway.
        /// </summary>
        internal McpSecretReferences Secrets { get; }

        /// <summary>
        /// Gets finite host limits.
        /// </summary>
        internal GdsMcpOptions Options { get; }

        /// <summary>
        /// Gets the injected clock.
        /// </summary>
        internal TimeProvider Time { get; }

        /// <summary>
        /// Resolves the real registration name before checking its grant and the encrypted endpoint.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal OpcUaSessionManager.SessionInfo Session(string? name, McpCapability capability)
        {
            _ = Sessions.OperationPolicy.Require(capability);
            OpcUaSessionManager.SessionInfo info = Sessions.GetSessionInfoOrThrow(name);
            _ = Sessions.OperationPolicy.RequireSession(capability, info.Name);
            if (info.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                info.Session.ConfiguredEndpoint.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient,
                    "GDS, credential and onboarding tools require SignAndEncrypt.");
            }
            if (!info.Session.MessageContext.Factory.ContainsEncodeableType(Gds.DataTypeIds.ApplicationRecordDataType))
            {
                info.Session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            }
            return info;
        }

        /// <summary>
        /// Captures dispatch-level identity after checking the real named registration and host grant.
        /// Disposal releases only the generation context, never the host's managed session.
        /// </summary>
        internal ValueTask<SessionRequestContext> CaptureAsync(
            string? name, McpCapability capability, CancellationToken ct)
        {
            OpcUaSessionManager.SessionInfo info = Session(name, capability);
            return SessionRequestContext.CaptureAsync(info.Session, ct);
        }

        /// <summary>
        /// Checks actual type or subtype on an explicit root; renamed roots retain their server identity.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal async ValueTask<GdsEndpoint> EndpointAsync(
            string nodeId, GdsTarget target, string? sessionName, CancellationToken ct)
        {
            OpcUaSessionManager.SessionInfo info = Session(sessionName, Capability(target));
            NodeId node = Node(nodeId);
            SessionRequestContext context = await SessionRequestContext.CaptureAsync(info.Session, ct)
                .ConfigureAwait(false);
            bool retained = false;
            try
            {
                ISession session = context.Session;
                ObjectTypeClient proxy = target switch
                {
                    GdsTarget.Directory => new DirectoryTypeClient(session, node, Sessions.Telemetry),
                    GdsTarget.CertificateDirectory => new CertificateDirectoryTypeClient(
                        session, node, Sessions.Telemetry),
                    GdsTarget.ServerConfiguration => new ServerConfigurationTypeClient(
                        session, node, Sessions.Telemetry),
                    GdsTarget.TrustList => new TrustListTypeClient(session, node, Sessions.Telemetry),
                    GdsTarget.KeyCredential => new KeyCredentialServiceTypeClient(session, node, Sessions.Telemetry),
                    GdsTarget.Authorization => new AuthorizationServiceTypeClient(session, node, Sessions.Telemetry),
                    GdsTarget.Registrar => new Gds.Client.OnboardingClient(session, node, Sessions.Telemetry),
                    _ => throw new ArgumentOutOfRangeException(nameof(target))
                };
                await McpModelAccess.RequireTypeAsync(session, node, Type(target), ct).ConfigureAwait(false);
                var endpoint = new GdsEndpoint(info, context, proxy, ownsContext: true);
                retained = true;
                return endpoint;
            }
            finally
            {
                if (!retained)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Executes a bounded operation with secret-free error diagnostics and no post-commit cancellation check.
        /// </summary>
        internal async Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, ValueTask<JsonObject>> operation, CancellationToken ct)
        {
            using var deadline = new CancellationTokenSource(Options.OperationTimeout, Time);
            using var operationToken = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            try
            {
                ct.ThrowIfCancellationRequested();
                JsonObject result = await operation(operationToken.Token).ConfigureAwait(false);
                if (!Fits(result))
                {
                    result = GdsJson.Compact(result);
                    if (!Fits(result))
                    {
                        JsonObject minimal = GdsJson.Error(StatusCodes.BadEncodingLimitsExceeded,
                            "The acknowledged response exceeded the output budget. Do not repeat a mutation blindly.");
                        minimal["remoteOutcome"] = "acknowledgedResponseTooLarge";
                        return McpCompanionTools.Result(minimal);
                    }
                }
                return McpCompanionTools.Result(result);
            }
            catch (ServiceResultException failure)
            {
                return McpCompanionTools.Result(GdsJson.Error(failure.StatusCode,
                    "The operation was refused, interrupted or its response was invalid. " +
                    "A dispatched mutation may have committed; do not retry blindly."));
            }
            catch (Exception failure) when (failure is IMcpSecretPersistenceFailure persistence)
            {
                return McpCompanionTools.Result(GdsJson.PersistenceFailure(persistence));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return McpCompanionTools.Result(GdsJson.Error(StatusCodes.BadTimeout,
                    "The operation deadline expired. A dispatched mutation may have committed; do not retry blindly."));
            }
            catch (OperationCanceledException failure)
            {
                throw new OperationCanceledException(failure.CancellationToken);
            }
            catch (Exception failure) when (failure is ArgumentException or FormatException or
                InvalidOperationException or IOException or UnauthorizedAccessException or
                NotSupportedException or CryptographicException or JsonException)
            {
                return McpCompanionTools.Result(GdsJson.Error(StatusCodes.BadInvalidArgument,
                    "The operation could not complete with the supplied model, file or secret-reference input."));
            }
        }

        /// <summary>
        /// Checks serialized size without an unbounded intermediate string and without observing late cancellation.
        /// </summary>
        private bool Fits(JsonObject result)
        {
            try
            {
                using var buffer = new McpBoundedMemoryStream(Options.MaxResponseBytes);
                using var writer = new Utf8JsonWriter(buffer);
                result.WriteTo(writer);
                writer.Flush();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads one public input through the transfer policy, with a stricter per-artifact allocation bound.
        /// </summary>
        internal async ValueTask<ByteString> PublicInputAsync(string path, CancellationToken ct)
        {
            using var buffer = new McpBoundedMemoryStream(Options.MaxArtifactBytes, ct);
            await Transfers.UploadAsync(path, async (source, token) =>
            {
                await source.CopyToAsync(buffer, token).ConfigureAwait(false);
                return buffer.Length;
            }, ct).ConfigureAwait(false);
            return ByteString.From(buffer.ToArray());
        }

        /// <summary>
        /// Publishes a public artifact without ever accepting private material at this seam.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal ValueTask<JsonObject> PublicOutputAsync(string path, ByteString value, CancellationToken ct)
        {
            if (value.IsEmpty || value.Length > Options.MaxArtifactBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return Transfers.DownloadAsync(path, (stream, token) => stream.WriteAsync(value.Memory, token), ct);
        }

        /// <summary>
        /// Parses a bounded non-null NodeId without reflecting input in error text.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static NodeId Node(string text)
        {
            Text(text);
            NodeId node = OpcUaJsonHelper.ParseNodeId(text);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.") : node;
        }

        /// <summary>
        /// Parses the protocol's optional default-group NodeId.
        /// </summary>
        internal static NodeId OptionalNode(string? text)
        {
            return string.IsNullOrEmpty(text) ? NodeId.Null : OpcUaJsonHelper.ParseNodeId(Text(text));
        }

        /// <summary>
        /// Bounds a public text input without normalizing its protocol identity.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static string Text(string text, bool allowEmpty = false)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length > 2048 || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
            {
                throw new ArgumentException("Text input is absent or exceeds 2048 characters.");
            }
            return text;
        }

        /// <summary>
        /// Bounds a typed string array and every entry before dispatch.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static ArrayOf<string> Strings(ArrayOf<string> values, int maximum = 100)
        {
            if (values.IsNull || values.Count > maximum)
            {
                throw new ArgumentException("The input must be a non-null, bounded string array.");
            }
            foreach (string value in values)
            {
                Text(value);
            }
            return values;
        }

        /// <summary>
        /// Checks page bounds before reading or mutating a server.
        /// </summary>
        internal static void Page(int offset, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, 1_000_000);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 500);
        }

        /// <summary>
        /// Returns the authoritative model identity for a closed target kind.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static ExpandedNodeId Type(GdsTarget target)
        {
            return target switch
            {
                GdsTarget.Directory => Gds.ObjectTypeIds.DirectoryType,
                GdsTarget.CertificateDirectory => Gds.ObjectTypeIds.CertificateDirectoryType,
                GdsTarget.ServerConfiguration => ObjectTypeIds.ServerConfigurationType,
                GdsTarget.TrustList => ObjectTypeIds.TrustListType,
                GdsTarget.KeyCredential => Gds.ObjectTypeIds.KeyCredentialServiceType,
                GdsTarget.Authorization => Gds.ObjectTypeIds.AuthorizationServiceType,
                GdsTarget.Registrar => Onboarding.ObjectTypeIds.DeviceRegistrarAdminType,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
        }

        /// <summary>
        /// Maps typed workflows to host-granted authority rather than catalogue membership.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static McpCapability Capability(GdsTarget target)
        {
            return target switch
            {
                GdsTarget.Directory => McpCapability.DirectoryAdministration,
                GdsTarget.CertificateDirectory or GdsTarget.ServerConfiguration or GdsTarget.TrustList =>
                    McpCapability.CertificateAdministration,
                GdsTarget.KeyCredential or GdsTarget.Authorization => McpCapability.CredentialIssuance,
                GdsTarget.Registrar => McpCapability.Onboarding,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
        }
    }

    /// <summary>
    /// Captures the session generation and typed proxy used for exactly one tool invocation.
    /// </summary>
    internal sealed class GdsEndpoint : IAsyncDisposable
    {
        /// <summary>
        /// Retains a generation-bound session context, optionally borrowed by a nested trust-list proxy.
        /// </summary>
        internal GdsEndpoint(
            OpcUaSessionManager.SessionInfo info, SessionRequestContext context,
            ObjectTypeClient proxy, bool ownsContext)
        {
            Info = info;
            RequestContext = context;
            Proxy = proxy;
            m_ownsContext = ownsContext;
            SessionId = context.SessionId;
            Session = context.Session;
            Context = Session.MessageContext;
            Resource = GdsAccess.Text("opcua:" +
                Session.ConfiguredEndpoint.Description.Server.ApplicationUri +
                "#" +
                NodeId.ToExpandedNodeId(proxy.ObjectId, Session.NamespaceUris));
        }

        /// <summary>
        /// Gets the original named registration for policy and cross-call ownership.
        /// </summary>
        internal OpcUaSessionManager.SessionInfo Info { get; }

        /// <summary>
        /// Gets the typed generated client using the generation-bound session.
        /// </summary>
        internal ObjectTypeClient Proxy { get; }

        /// <summary>
        /// Gets the borrowed session whose every wire dispatch enforces the captured authentication generation.
        /// </summary>
        internal ISession Session { get; }

        /// <summary>
        /// Gets the lifetime of the borrowed generation-bound session.
        /// </summary>
        internal SessionRequestContext RequestContext { get; }

        /// <summary>
        /// Gets the server's SessionId captured before a mutation can close the connection.
        /// </summary>
        internal NodeId SessionId { get; }

        /// <summary>
        /// Gets the public server/model label, without asserting protected-secret eligibility.
        /// </summary>
        internal string Resource { get; }

        /// <summary>
        /// Gets current registration/certificate-bound secret authority only when an operation actually needs it.
        /// Capture this value before dispatch if acknowledged issuance must survive later session invalidation.
        /// </summary>
        internal string SecretResource =>
            McpSecretReferences.GetResourceBinding(Info.WithSession(Session), Proxy.ObjectId);

        /// <summary>
        /// Gets the decode/encode context captured before a mutation can dispose the borrowed session.
        /// </summary>
        internal IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_ownsContext ? RequestContext.DisposeAsync() : ValueTask.CompletedTask;
        }

        /// <summary>
        /// Calls one compile-time selected method once, preserving its status and not retrying business effects.
        /// </summary>
        internal ValueTask<CallMethodResult> CallAsync(
            ExpandedNodeId method, ArrayOf<Variant> arguments, CancellationToken ct)
        {
            return Proxy.CallWithResultAsync(method, arguments, ct);
        }

        /// <summary>
        /// Adds stable session and root identities even when the call subsequently closes its channel.
        /// </summary>
        internal JsonObject Result(CallMethodResult call)
        {
            JsonObject result = GdsJson.Method(call);
            result["sessionName"] = Info.Name;
            result["sessionId"] = SessionId.ToString();
            result["sessionConnectedAt"] = Info.ConnectedAt;
            result["objectId"] = Proxy.ObjectId.ToString();
            result["resourceBinding"] = Resource;
            return result;
        }

        private readonly bool m_ownsContext;
    }
}
