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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    /// <summary>
    /// Walks the address space the way a client that has never seen this Server
    /// would, and exercises what it finds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is hard-coded to the sample's own NodeIds. Everything is reached
    /// by browsing from the well-known entry point under the Server Object, because
    /// that is the only thing the specification lets a client assume - and a client
    /// that cheated here would not demonstrate that the address space is navigable.
    /// </para>
    /// <para>
    /// It also means this client works against any Server implementing the
    /// companion specification, not just this one.
    /// </para>
    /// </remarks>
    internal sealed class AiScenarioRunner
    {
        private readonly ISession m_session;
        private readonly ushort m_ns;
        private readonly AiBrowseClient m_client;

        private AiScenarioRunner(ISession session, ushort namespaceIndex)
        {
            m_session = session;
            m_ns = namespaceIndex;
            m_client = new AiBrowseClient(session, namespaceIndex);
        }

        /// <summary>
        /// Finds the AI namespace and prepares a runner, or returns null when the
        /// Server does not implement the specification.
        /// </summary>
        public static AiScenarioRunner? TryCreate(ISession session)
        {
            int index = session.NamespaceUris.GetIndex(AiNamespaceUri);
            return index < 0 ? null : new AiScenarioRunner(session, (ushort)index);
        }

        private const string AiNamespaceUri = "http://opcfoundation.org/UA/AI/";

        /// <summary>
        /// Reads the value out of a DataValue as an object.
        /// </summary>
        /// <remarks>
        /// A generic client cannot know the type in advance, which is exactly what
        /// the legacy boxing behaviour is for. Code that knows the type should use
        /// the TryGet pattern instead.
        /// </remarks>
        private static object? Unbox(DataValue value)
        {
            return value.WrappedValue.AsBoxedObject(Variant.BoxingBehavior.Legacy);
        }
        /// <summary>
        /// Runs every scenario the Server publishes the means for.
        /// </summary>
        public async Task RunAsync(CancellationToken ct)
        {
            NodeId root = await FindAiRootAsync(ct).ConfigureAwait(false);

            if (root.IsNull)
            {
                Console.WriteLine("This Server publishes no AI root.");
                return;
            }

            Console.WriteLine("AI root: {0}", root);
            await ReportSpecificationVersionAsync(root, ct).ConfigureAwait(false);

            IReadOnlyList<NodeId> deployments =
                await m_client.BrowseFolderAsync(root, "Deployments", ct).ConfigureAwait(false);

            if (deployments.Count == 0)
            {
                Console.WriteLine("No deployments are published.");
                return;
            }

            foreach (NodeId deployment in deployments)
            {
                await DescribeDeploymentAsync(deployment, ct).ConfigureAwait(false);
            }

            NodeId primary = deployments[0];

            await RunCapabilitiesAsync(primary, ct).ConfigureAwait(false);
            await RunInlineInferenceAsync(primary, ct).ConfigureAwait(false);
            await RunTransferAsync(primary, ct).ConfigureAwait(false);
            await RunAsynchronousInferenceAsync(primary, ct).ConfigureAwait(false);
            await RunSourceAsync(root, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the entry point by browsing the Server Object.
        /// </summary>
        private async Task<NodeId> FindAiRootAsync(CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children =
                await m_client.BrowseAsync(Opc.Ua.ObjectIds.Server, ct).ConfigureAwait(false);

            foreach (ReferenceDescription child in children)
            {
                if (child.BrowseName.NamespaceIndex == m_ns)
                {
                    return ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        private async Task ReportSpecificationVersionAsync(NodeId root, CancellationToken ct)
        {
            NodeId version = await m_client.FindChildAsync(root, "SpecificationVersion", ct)
                .ConfigureAwait(false);

            if (!version.IsNull)
            {
                DataValue value = await m_client.ReadAsync(version, ct).ConfigureAwait(false);
                Console.WriteLine("Specification version: {0}", Unbox(value));
            }
        }

        private async Task DescribeDeploymentAsync(NodeId deployment, CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("--- deployment {0}", deployment);

            foreach (string name in new[]
            {
                "DeploymentId",
                "InferenceLocation",
                "State",
                "DataJurisdiction",
                "EgressPermitted",
                "MaxInlinePayloadSize",
                "EndpointUri"
            })
            {
                NodeId child = await m_client.FindChildAsync(deployment, name, ct).ConfigureAwait(false);

                if (!child.IsNull)
                {
                    DataValue value = await m_client.ReadAsync(child, ct).ConfigureAwait(false);
                    Console.WriteLine("    {0,-22} {1}", name, Unbox(value));
                }
            }

            // The provenance walk, done the way an auditing client would: follow
            // UsesModel to the artefact and read the digest it terminates at.
            NodeId model = await m_client.FollowAsync(deployment, "UsesModel", ct).ConfigureAwait(false);

            if (!model.IsNull)
            {
                NodeId modelId = await m_client.FindChildAsync(model, "ModelId", ct).ConfigureAwait(false);
                NodeId digest = await m_client.FindChildAsync(model, "Digest", ct).ConfigureAwait(false);

                Console.WriteLine(
                    "    uses model            {0} ({1})",
                    Unbox(await m_client.ReadAsync(modelId, ct).ConfigureAwait(false)),
                    model);

                if (!digest.IsNull)
                {
                    DataValue value = await m_client.ReadAsync(digest, ct).ConfigureAwait(false);
                    var bytes = Unbox(value) as byte[];
                    Console.WriteLine(
                        "    digest                {0}",
                        bytes is { Length: > 0 }
                            ? Convert.ToHexString(bytes)
                            : "(none declared)");
                }
            }

            NodeId fallback = await m_client.FollowAsync(deployment, "FallsBackTo", ct)
                .ConfigureAwait(false);

            if (!fallback.IsNull)
            {
                Console.WriteLine("    falls back to         {0}", fallback);
            }
        }

        private async Task RunCapabilitiesAsync(NodeId deployment, CancellationToken ct)
        {
            NodeId method = await m_client.FindChildAsync(deployment, "GetCapabilities", ct)
                .ConfigureAwait(false);

            if (method.IsNull)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- GetCapabilities");

            IList<object> outputs = await m_client.CallAsync(deployment, method, [], ct)
                .ConfigureAwait(false);

            if (outputs.Count > 0)
            {
                Console.WriteLine("    {0}", AiBrowseClient.Describe(outputs[0]));
            }
        }

        private async Task RunInlineInferenceAsync(NodeId deployment, CancellationToken ct)
        {
            NodeId method = await m_client.FindChildAsync(deployment, "Invoke", ct).ConfigureAwait(false);

            if (method.IsNull)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- Invoke");

            byte[] payload = Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Summarise the last shift.\"}]}");

            IList<object> outputs = await m_client.CallAsync(
                deployment,
                method,
                [
                    Variant.From(ByteString.From(payload)),
                    Variant.From("application/json"),
                    Variant.FromStructure(ArrayOf<Opc.Ua.KeyValuePair>.Empty),
                    Variant.From(5000d)
                ],
                ct).ConfigureAwait(false);

            ReportInvokeOutputs(outputs);
        }

        private static void ReportInvokeOutputs(IList<object> outputs)
        {
            if (outputs.Count < 8)
            {
                Console.WriteLine("    (unexpected output shape)");
                return;
            }

            if (outputs[0] is ByteString body && body.Length > 0)
            {
                Console.WriteLine("    response      {0}", Encoding.UTF8.GetString(body.Span));
            }

            // The output that matters. A caller that cannot see which model produced
            // a result cannot tell a degraded answer from a good one.
            Console.WriteLine("    ModelUsed     {0}", outputs[2]);
            Console.WriteLine("    Usage         {0}", AiBrowseClient.Describe(outputs[3]));
            Console.WriteLine("    FinishReason  {0}", outputs[4]);

            if (outputs[6] is bool transferRequired && transferRequired)
            {
                Console.WriteLine("    payload too large; transfer at {0}", outputs[7]);
            }
        }

        private async Task RunTransferAsync(NodeId deployment, CancellationToken ct)
        {
            NodeId begin = await m_client.FindChildAsync(deployment, "BeginTransfer", ct)
                .ConfigureAwait(false);

            if (begin.IsNull)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- BeginTransfer");

            byte[] payload = Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"" +
                new string('x', 4096) +
                "\"}]}");

            IList<object> outputs = await m_client.CallAsync(
                deployment,
                begin,
                ["application/json", (ulong)payload.Length],
                ct).ConfigureAwait(false);

            if (outputs.Count < 2 || outputs[1] is not bool accepted || !accepted)
            {
                Console.WriteLine("    refused");
                return;
            }

            var transfer = (NodeId)outputs[0];
            Console.WriteLine("    transfer      {0}", transfer);

            NodeId request = await m_client.FindChildAsync(transfer, "Request", ct).ConfigureAwait(false);
            await m_client.WriteFileAsync(request, payload, ct).ConfigureAwait(false);

            NodeId execute = await m_client.FindChildAsync(transfer, "Execute", ct).ConfigureAwait(false);
            await m_client.CallAsync(transfer, execute, [], ct).ConfigureAwait(false);

            NodeId state = await m_client.FindChildAsync(transfer, "State", ct).ConfigureAwait(false);
            NodeId modelUsed = await m_client.FindChildAsync(transfer, "ModelUsed", ct)
                .ConfigureAwait(false);

            Console.WriteLine(
                "    state         {0}",
                Unbox(await m_client.ReadAsync(state, ct).ConfigureAwait(false)));

            if (!modelUsed.IsNull)
            {
                Console.WriteLine(
                    "    ModelUsed     {0}",
                    Unbox(await m_client.ReadAsync(modelUsed, ct).ConfigureAwait(false)));
            }

            NodeId response = await m_client.FindChildAsync(transfer, "Response", ct)
                .ConfigureAwait(false);
            byte[] answer = await m_client.ReadFileAsync(response, ct).ConfigureAwait(false);

            if (answer.Length > 0)
            {
                Console.WriteLine("    response      {0}", Encoding.UTF8.GetString(answer));
            }
        }

        private async Task RunAsynchronousInferenceAsync(NodeId deployment, CancellationToken ct)
        {
            NodeId method = await m_client.FindChildAsync(deployment, "InvokeAsync", ct)
                .ConfigureAwait(false);

            if (method.IsNull)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- InvokeAsync");

            byte[] payload = Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Explain the trend.\"}]}");

            IList<object> outputs = await m_client.CallAsync(
                deployment,
                method,
                [
                    Variant.From(ByteString.From(payload)),
                    Variant.From("application/json"),
                    Variant.FromStructure(ArrayOf<Opc.Ua.KeyValuePair>.Empty)
                ],
                ct).ConfigureAwait(false);

            if (outputs.Count == 0 || outputs[0] is not NodeId job || job.IsNull)
            {
                return;
            }

            Console.WriteLine("    job           {0}", job);

            NodeId currentState = await m_client.FindChildAsync(job, "CurrentState", ct)
                .ConfigureAwait(false);

            // Poll the program state machine rather than sleeping a fixed interval:
            // the point of the job is that its duration is not known in advance.
            for (int attempt = 0; attempt < 50; attempt++)
            {
                DataValue value = await m_client.ReadAsync(currentState, ct).ConfigureAwait(false);
                string state = Unbox(value)?.ToString() ?? string.Empty;

                if (state.Contains("Halted", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            foreach (string name in new[] { "ResponsePayload", "ModelUsed", "FinishReason" })
            {
                NodeId child = await m_client.FindChildAsync(job, name, ct).ConfigureAwait(false);

                if (child.IsNull)
                {
                    continue;
                }

                DataValue value = await m_client.ReadAsync(child, ct).ConfigureAwait(false);
                object? shown = Unbox(value) is byte[] bytes
                    ? Encoding.UTF8.GetString(bytes)
                    : Unbox(value);

                Console.WriteLine("    {0,-13} {1}", name, shown);
            }
        }

        private async Task RunSourceAsync(NodeId root, CancellationToken ct)
        {
            IReadOnlyList<NodeId> sources =
                await m_client.BrowseFolderAsync(root, "Sources", ct).ConfigureAwait(false);

            if (sources.Count == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- model source");

            NodeId source = sources[0];

            foreach (string name in new[]
            {
                "SourceId",
                "EndpointUri",
                "ApiDialect",
                "AuthenticationKind",
                "CredentialReference"
            })
            {
                NodeId child = await m_client.FindChildAsync(source, name, ct).ConfigureAwait(false);

                if (!child.IsNull)
                {
                    DataValue value = await m_client.ReadAsync(child, ct).ConfigureAwait(false);
                    Console.WriteLine("    {0,-20} {1}", name, Unbox(value));
                }
            }

            NodeId test = await m_client.FindChildAsync(source, "TestConnection", ct)
                .ConfigureAwait(false);

            if (!test.IsNull)
            {
                IList<object> outputs = await m_client.CallAsync(source, test, [], ct)
                    .ConfigureAwait(false);

                if (outputs.Count >= 2)
                {
                    Console.WriteLine("    reachable            {0} ({1})", outputs[0], outputs[1]);
                }
            }

            NodeId list = await m_client.FindChildAsync(source, "ListModels", ct).ConfigureAwait(false);

            if (!list.IsNull)
            {
                IList<object> outputs = await m_client.CallAsync(source, list, [Variant.From(string.Empty), Variant.From(20u)], ct)
                    .ConfigureAwait(false);

                if (outputs.Count > 0)
                {
                    Console.WriteLine("    offers               {0}", AiBrowseClient.Describe(outputs[0]));
                }
            }
        }
    }
}
