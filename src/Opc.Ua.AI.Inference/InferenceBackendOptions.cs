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

using System.Collections.Generic;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// How the Server authenticates ITSELF to an inference endpoint.
    /// </summary>
    /// <remarks>
    /// This is not how a client authenticates to this Server, which is the ordinary
    /// OPC UA Session security and is unaffected. It maps onto
    /// <c>AuthenticationKindEnum</c>.
    /// </remarks>
    public enum BackendAuthentication
    {
        /// <summary>
        /// No credential. Permitted only where the endpoint is reachable solely from
        /// a trusted network segment - which is the ordinary case for an on-device
        /// runtime listening on loopback.
        /// </summary>
        Anonymous,

        /// <summary>A shared secret presented as a key.</summary>
        ApiKey,

        /// <summary>A token obtained from an authorization service.</summary>
        BearerToken,

        /// <summary>
        /// An identity the hosting platform assigns, so no secret is stored at all.
        /// Preferred wherever the platform offers it.
        /// </summary>
        WorkloadIdentity
    }

    /// <summary>
    /// Which inference backend contract a deployment uses.
    /// </summary>
    public enum InferenceBackendKind
    {
        /// <summary>
        /// The host supplies a <c>Microsoft.Extensions.AI.IChatClient</c>.
        /// </summary>
        ChatClient,

        /// <summary>
        /// The backend speaks the OpenAI-compatible REST chat-completions contract
        /// directly.
        /// </summary>
        RestChatCompletions
    }

    /// <summary>
    /// Configuration for one inference backend.
    /// </summary>
    /// <remarks>
    /// Bound from configuration at startup. Cloud and on-device differ only in the
    /// values here - the endpoint, what authenticates a call, and which models exist -
    /// which is why one backend implementation serves both.
    /// </remarks>
    public sealed class InferenceBackendOptions
    {
        /// <summary>Configuration section this binds from.</summary>
        public const string SectionName = "InferenceBackend";

        /// <summary>
        /// Configuration section the fallback backend binds from.
        /// </summary>
        /// <remarks>
        /// A separate section rather than a nested one, because the fallback is a
        /// backend in its own right: it has its own endpoint, its own credentials
        /// and its own reachability, and sharing any of those with the primary would
        /// defeat the purpose of having it.
        /// </remarks>
        public const string FallbackSectionName = "FallbackInferenceBackend";

        /// <summary>
        /// Whether this backend is configured at all.
        /// </summary>
        /// <remarks>
        /// Meaningful for the fallback: a Server with nowhere to fall back to should
        /// publish no fallback deployment rather than one that always fails.
        /// </remarks>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Which backend contract to use. Defaults to the
        /// <c>Microsoft.Extensions.AI</c> abstraction.
        /// </summary>
        public InferenceBackendKind Kind { get; set; } = InferenceBackendKind.ChatClient;

        /// <summary>
        /// Audience a workload-identity token is requested for.
        /// </summary>
        public string TokenAudience { get; set; } = string.Empty;

        /// <summary>
        /// Where inference runs. Surfaced to clients as
        /// <c>DeploymentType.InferenceLocation</c>.
        /// </summary>
        public InferenceSite Site { get; set; } = InferenceSite.OnServer;

        /// <summary>
        /// Base address of the endpoint. For an on-device runtime this is loopback;
        /// for a hosted service it is the service address.
        /// </summary>
        public string EndpointUri { get; set; } = "http://localhost:5273/";

        /// <summary>Path of the chat completions operation.</summary>
        public string ChatCompletionsPath { get; set; } = "v1/chat/completions";

        /// <summary>Path probed to establish reachability.</summary>
        public string ProbePath { get; set; } = "v1/models";

        /// <summary>How the Server authenticates itself.</summary>
        public BackendAuthentication Authentication { get; set; } = BackendAuthentication.Anonymous;

        /// <summary>
        /// NAME of the credential, never the credential. Published as
        /// <c>ModelSourceType.CredentialReference</c>, where a client reading it
        /// learns which credential is used and nothing about what it is.
        /// </summary>
        public string CredentialReference { get; set; } = string.Empty;

        /// <summary>Header an API key is presented in.</summary>
        public string ApiKeyHeader { get; set; } = "api-key";

        /// <summary>Directory a credential Secret is mounted at.</summary>
        public string CredentialDirectory { get; set; } = "/var/run/secrets/ai";

        /// <summary>
        /// Jurisdiction the endpoint processes data in, published as
        /// <c>DeploymentType.DataJurisdiction</c>.
        /// </summary>
        public string DataJurisdiction { get; set; } = "on-premises";

        /// <summary>
        /// Whether calling this backend sends input outside the operator's boundary.
        /// A deployment reaching a hosted service sets this true; encryption does not
        /// make it false, because the question is where the data goes and not who can
        /// read it in flight.
        /// </summary>
        public bool EgressPermitted { get; set; }

        /// <summary>
        /// Whether the endpoint retains input beyond serving the request. Where this
        /// cannot be established it is reported true, because the assumption that
        /// keeps data in is the one that is safe to be wrong about.
        /// </summary>
        public bool RetainsInput { get; set; } = true;

        /// <summary>
        /// Largest payload carried inline through <c>Invoke</c>, in bytes. Beyond it
        /// a client uses <c>BeginTransfer</c>.
        /// </summary>
        public uint MaxInlinePayloadSize { get; set; } = 65536;

        /// <summary>Models this backend offers.</summary>
        public IList<BackendModel> Models { get; } = new List<BackendModel>();
    }
}
