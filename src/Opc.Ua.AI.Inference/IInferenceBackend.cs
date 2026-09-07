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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// The inference backend the Server executes through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One abstraction covers a hosted service and an on-device runtime because both
    /// speak the same wire contract. That is not a convenience of this sample: it is
    /// the property <c>OPC UA - AI Model Management and Inference</c> clause 8.1
    /// asserts, that where inference runs changes the trust boundary and the latency
    /// and nothing else. If satisfying it here had required two shapes, the claim
    /// would have been wrong.
    /// </para>
    /// <para>
    /// Implementations are expected to be thread-safe. The Server calls them from
    /// whichever thread a Method arrived on.
    /// </para>
    /// </remarks>
    public interface IInferenceBackend
    {
        /// <summary>
        /// Where inference runs, as reported to a client through
        /// <c>DeploymentType.InferenceLocation</c>.
        /// </summary>
        InferenceSite Site { get; }

        /// <summary>
        /// Models this backend offers, as the catalogue and the address space
        /// present them.
        /// </summary>
        ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
            string? filter,
            uint maxResults,
            CancellationToken ct);

        /// <summary>
        /// Runs one inference.
        /// </summary>
        /// <remarks>
        /// The payload is opaque to the Server, which is the specification's position
        /// and not an omission here: what goes into a model and comes back is domain
        /// vocabulary, and an envelope that typed it would need extending for every
        /// domain that adopted it.
        /// </remarks>
        ValueTask<InferenceResult> InvokeAsync(
            InferenceRequest request,
            CancellationToken ct);

        /// <summary>
        /// Probes the backend, so that a commissioning engineer can establish that
        /// credentials and network policy are right BEFORE a deployment depends on
        /// them rather than learning it from the first failed inference.
        /// </summary>
        ValueTask<BackendProbe> ProbeAsync(CancellationToken ct);
    }

    /// <summary>
    /// Where a backend executes. Maps onto <c>InferenceLocationEnum</c>.
    /// </summary>
    public enum InferenceSite
    {
        /// <summary>In the Server's own process or on its host.</summary>
        OnServer,

        /// <summary>On a separate node reached over the local network.</summary>
        EdgeOffServer,

        /// <summary>In a remote or hosted service.</summary>
        Cloud
    }

    /// <summary>
    /// One model a backend offers.
    /// </summary>
    /// <remarks>
    /// Identity is a publisher, name and version triple rather than a URL, because a
    /// URL says where a copy is today and the triple says which artefact is meant -
    /// and the two diverge the moment anyone mirrors anything.
    /// </remarks>
    public sealed record BackendModel
    {
        /// <summary>Organisation or namespace that published the model.</summary>
        public string Publisher { get; init; } = string.Empty;

        /// <summary>Model name within that publisher.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Version identifier.</summary>
        public string Version { get; init; } = string.Empty;

        /// <summary>What the model does, for example <c>chat</c>.</summary>
        public string TaskKind { get; init; } = "chat";

        /// <summary>Runtime or library the artefact targets.</summary>
        public string Framework { get; init; } = string.Empty;

        /// <summary>Capability names the backend reports, for example <c>chat</c>.</summary>
        public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Digest of the artefact, where the backend can name one.
        /// </summary>
        /// <remarks>
        /// Empty by default and deliberately so. A provenance walk terminates at
        /// this value, which makes it the one field a sample must not invent: a
        /// hosted endpoint that will not say which weights answered cannot be made
        /// to say so by hashing its name, and a digest that looks like an artefact
        /// digest but is not one is worse than none, because it will be compared.
        /// </remarks>
        public ReadOnlyMemory<byte> Digest { get; init; }

        /// <summary>
        /// Algorithm that produced <see cref="Digest"/>, empty when there is none.
        /// </summary>
        public string DigestAlgorithm { get; init; } = string.Empty;

        /// <summary>
        /// How the weights are quantized, where that is known.
        /// </summary>
        /// <remarks>
        /// Distinguishes a stand-in from the artefact it stands in for. Two models
        /// with the same name and version but different quantization give different
        /// answers, so a client comparing results across a fallback needs to be able
        /// to see that they are not the same thing.
        /// </remarks>
        public string Quantization { get; init; } = string.Empty;
    }

    /// <summary>
    /// One inference request.
    /// </summary>
    public sealed record InferenceRequest
    {
        /// <summary>Model to route to, as the backend names it.</summary>
        public string Model { get; init; } = string.Empty;

        /// <summary>Request body.</summary>
        public ReadOnlyMemory<byte> Payload { get; init; }

        /// <summary>Media type of <see cref="Payload"/>.</summary>
        public string ContentType { get; init; } = "application/json";

        /// <summary>
        /// Call parameters. A backend rejects one it does not support rather than
        /// ignoring it: a caller whose parameter was silently dropped believes it
        /// took effect, and there is no later point at which it can find out.
        /// </summary>
        public IReadOnlyDictionary<string, string> Parameters { get; init; } =
            new Dictionary<string, string>();

        /// <summary>How long the caller will wait, or zero for the backend default.</summary>
        public TimeSpan Timeout { get; init; }
    }

    /// <summary>
    /// What one inference produced.
    /// </summary>
    public sealed record InferenceResult
    {
        /// <summary>Whether the call succeeded.</summary>
        public bool Ok { get; init; }

        /// <summary>Response body.</summary>
        public ReadOnlyMemory<byte> Payload { get; init; }

        /// <summary>Media type of <see cref="Payload"/>.</summary>
        public string ContentType { get; init; } = "application/json";

        /// <summary>
        /// The model that ACTUALLY answered, which is not always the one asked for.
        /// A backend that silently substituted one reports the substitute here, and
        /// the Server passes it through to <c>ModelUsed</c>.
        /// </summary>
        public string ModelUsed { get; init; } = string.Empty;

        /// <summary>Unit the usage counts are in, for example <c>tokens</c>.</summary>
        public string UsageUnit { get; init; } = "tokens";

        /// <summary>Units consumed by the input.</summary>
        public ulong InputUnits { get; init; }

        /// <summary>Units produced as output.</summary>
        public ulong OutputUnits { get; init; }

        /// <summary>Units metered for the call, which is not always the sum.</summary>
        public ulong TotalUnits { get; init; }

        /// <summary>Why output stopped.</summary>
        public InferenceFinish Finish { get; init; } = InferenceFinish.Stop;

        /// <summary>
        /// How long to wait before retrying, where the failure was a capacity one.
        /// Zero when retrying immediately is as good as waiting.
        /// </summary>
        public TimeSpan RetryAfter { get; init; }

        /// <summary>Diagnostic. For a human; not to be parsed.</summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// Why an inference stopped producing output. Maps onto
    /// <c>FinishReasonEnum</c>.
    /// </summary>
    public enum InferenceFinish
    {
        /// <summary>The model finished normally.</summary>
        Stop,

        /// <summary>Output was truncated by a length or budget limit.</summary>
        Length,

        /// <summary>The model requested a tool call.</summary>
        ToolCall,

        /// <summary>Output was withheld by a safety policy.</summary>
        Filtered,

        /// <summary>The call was cancelled.</summary>
        Cancelled,

        /// <summary>The call failed.</summary>
        Error
    }

    /// <summary>
    /// The outcome of probing a backend.
    /// </summary>
    public sealed record BackendProbe
    {
        /// <summary>Whether the backend answered.</summary>
        public bool Reachable { get; init; }

        /// <summary>
        /// Whether it answered but is refusing work for capacity reasons. Separated
        /// from unreachable deliberately: the two look alike from outside and call
        /// for opposite responses, since failing over a throttled endpoint merely
        /// moves load for no reason.
        /// </summary>
        public bool Throttled { get; init; }

        /// <summary>How long to wait, where the backend said.</summary>
        public TimeSpan RetryAfter { get; init; }

        /// <summary>Diagnostic. For a human.</summary>
        public string? Detail { get; init; }
    }
}
