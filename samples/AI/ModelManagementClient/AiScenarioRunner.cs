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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    internal sealed class AIScenarioRunner
    {
        private AIScenarioRunner(AIClient client)
        {
            m_client = client;
        }

        public static AIScenarioRunner? TryCreate(ISession session, ITelemetryContext telemetry)
        {
            var client = new AIClient(session, telemetry);
            return client.IsAINamespaceAvailable ? new AIScenarioRunner(client) : null;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Console.WriteLine("AI root: {0}", m_client.AIRootId);
            Console.WriteLine();
            Console.WriteLine("--- model catalogue");
            await ReportModelsAsync(ct).ConfigureAwait(false);

            ArrayOf<NodeId> deployments = await m_client.DiscoverDeploymentsAsync(ct)
                .ConfigureAwait(false);
            if (deployments.Count == 0)
            {
                Console.WriteLine("No deployments are published.");
                return;
            }

            for (int ii = 0; ii < deployments.Count; ii++)
            {
                await DescribeDeploymentAsync(deployments[ii], ct).ConfigureAwait(false);
            }

            AIDeploymentClient deployment = m_client.Deployment(deployments[0]);
            await RunCapabilitiesAsync(deployment, ct).ConfigureAwait(false);
            await RunInlineInferenceAsync(deployment, ct).ConfigureAwait(false);
            await RunTransferAsync(deployment, ct).ConfigureAwait(false);
            await RunAsynchronousInferenceAsync(deployment, ct).ConfigureAwait(false);
            await RunSourceAsync(ct).ConfigureAwait(false);
        }

        private async Task ReportModelsAsync(CancellationToken ct)
        {
            await foreach (AINodeEntry entry in m_client.EnumerateModelsAsync(ct).ConfigureAwait(false))
            {
                AIModelSnapshot model = await m_client.Model(entry.NodeId).ReadAsync(ct)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    "    {0} {1} {2} ({3})",
                    model.ModelId,
                    model.Name,
                    model.Version,
                    model.NodeId);
            }
        }

        private async Task DescribeDeploymentAsync(NodeId deploymentNodeId, CancellationToken ct)
        {
            AIDeploymentClient deployment = m_client.Deployment(deploymentNodeId);
            AIDeploymentSnapshot snapshot = await deployment.ReadAsync(ct).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("--- deployment {0}", snapshot.NodeId);
            Console.WriteLine("    DeploymentId          {0}", snapshot.DeploymentId);
            Console.WriteLine("    InferenceLocation     {0}", snapshot.InferenceLocation);
            Console.WriteLine("    State                 {0}", snapshot.State);
            Console.WriteLine("    DataJurisdiction      {0}", snapshot.DataJurisdiction);
            Console.WriteLine("    EgressPermitted       {0}", snapshot.EgressPermitted);
            Console.WriteLine("    MaxInlinePayloadSize  {0}", snapshot.MaxInlinePayloadSize);
            Console.WriteLine("    EndpointUri           {0}", snapshot.EndpointUri);

            AIModelClient? model = await deployment.OpenModelAsync(ct).ConfigureAwait(false);
            if (model is not null)
            {
                AIModelSnapshot modelSnapshot = await model.ReadAsync(ct).ConfigureAwait(false);
                Console.WriteLine(
                    "    uses model            {0} ({1})",
                    modelSnapshot.ModelId,
                    modelSnapshot.NodeId);
                Console.WriteLine(
                    "    digest                {0}",
                    modelSnapshot.Digest.Length > 0
                        ? Convert.ToHexString(modelSnapshot.Digest.Span)
                        : "(none declared)");
            }

            if (!snapshot.FallbackDeploymentId.IsNull)
            {
                Console.WriteLine("    falls back to         {0}", snapshot.FallbackDeploymentId);
            }
        }

        private static async Task RunCapabilitiesAsync(AIDeploymentClient deployment, CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("--- GetCapabilities");
            ArrayOf<CapabilityDataType> capabilities = await deployment.GetCapabilitiesAsync(ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < capabilities.Count; ii++)
            {
                Console.WriteLine(
                    "    {0}: {1}",
                    capabilities[ii].Name,
                    capabilities[ii].Supported);
            }
        }

        private static async Task RunInlineInferenceAsync(AIDeploymentClient deployment, CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("--- Invoke");
            ByteString payload = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Summarise the last shift.\"}]}"));
            AIInvokeResult result = await deployment.InvokeAsync(
                payload,
                "application/json",
                ArrayOf<KeyValuePair>.Empty,
                5000,
                cancellationToken: ct).ConfigureAwait(false);
            ReportInvokeOutputs(result);
        }

        private async Task RunTransferAsync(AIDeploymentClient deployment, CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("--- BeginTransfer");
            ByteString payload = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"" +
                new string('x', 4096) +
                "\"}]}"));
            AIBeginTransferResult begun = await deployment.BeginTransferAsync(
                "application/json", (ulong)payload.Length, ct).ConfigureAwait(false);
            if (!begun.Accepted)
            {
                Console.WriteLine("    refused");
                return;
            }
            Console.WriteLine("    transfer      {0}", begun.TransferId);

            AIInferenceTransferClient transfer = m_client.Transfer(begun.TransferId);
            await transfer.WriteRequestAsync(payload, cancellationToken: ct).ConfigureAwait(false);
            bool accepted = await transfer.ExecuteAsync(ct).ConfigureAwait(false);
            AITransferSnapshot snapshot = await transfer.ReadAsync(ct).ConfigureAwait(false);
            Console.WriteLine("    accepted      {0}", accepted);
            Console.WriteLine("    state         {0}", snapshot.State);
            Console.WriteLine("    ModelUsed     {0}", snapshot.ModelUsed);

            ByteString answer = await transfer.ReadResponseAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            if (answer.Length > 0)
            {
                Console.WriteLine("    response      {0}", Encoding.UTF8.GetString(answer.Span));
            }
        }

        private async Task RunAsynchronousInferenceAsync(AIDeploymentClient deployment, CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("--- InvokeAsync");
            ByteString payload = ByteString.From(Encoding.UTF8.GetBytes(
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Explain the trend.\"}]}"));
            NodeId jobId = await deployment.InvokeAsyncAsync(
                payload,
                "application/json",
                ArrayOf<KeyValuePair>.Empty,
                cancellationToken: ct).ConfigureAwait(false);
            if (jobId.IsNull)
            {
                return;
            }
            Console.WriteLine("    job           {0}", jobId);

            AIInferenceJobClient job = m_client.InferenceJob(jobId);
            AIInferenceJobSnapshot snapshot = new();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                snapshot = await job.ReadAsync(ct).ConfigureAwait(false);
                if (snapshot.ResponsePayload.Length > 0 || !snapshot.ModelUsed.IsNull)
                {
                    break;
                }
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            if (snapshot.ResponsePayload.Length > 0)
            {
                Console.WriteLine("    Response      {0}", Encoding.UTF8.GetString(snapshot.ResponsePayload.Span));
            }
            Console.WriteLine("    ModelUsed     {0}", snapshot.ModelUsed);
            Console.WriteLine("    FinishReason  {0}", snapshot.FinishReason);
        }

        private async Task RunSourceAsync(CancellationToken ct)
        {
            ArrayOf<NodeId> sourceIds = await m_client.DiscoverSourcesAsync(ct).ConfigureAwait(false);
            if (sourceIds.Count == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- model source");
            AIModelSourceClient source = m_client.Source(sourceIds[0]);
            AIModelSourceSnapshot snapshot = await source.ReadAsync(ct).ConfigureAwait(false);
            Console.WriteLine("    SourceId             {0}", snapshot.SourceId);
            Console.WriteLine("    EndpointUri          {0}", snapshot.EndpointUri);
            Console.WriteLine("    ApiDialect           {0}", snapshot.ApiDialect);
            Console.WriteLine("    AuthenticationKind   {0}", snapshot.AuthenticationKind);
            Console.WriteLine("    CredentialReference  {0}", snapshot.CredentialReference);

            AISourceConnectionResult connection = await source.TestConnectionAsync(ct)
                .ConfigureAwait(false);
            Console.WriteLine("    reachable            {0} ({1})", connection.Reachable, connection.Detail);

            AISourceModelListResult models = await source.ListModelsAsync(maxResults: 20, cancellationToken: ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < models.Models.Count; ii++)
            {
                ModelReferenceDataType model = models.Models[ii];
                Console.WriteLine(
                    "    offers               {0}/{1}/{2}",
                    model.Publisher,
                    model.Name,
                    model.Version);
            }
        }

        private static void ReportInvokeOutputs(AIInvokeResult result)
        {
            if (result.ResponsePayload.Length > 0)
            {
                Console.WriteLine("    response      {0}", Encoding.UTF8.GetString(result.ResponsePayload.Span));
            }
            Console.WriteLine("    ModelUsed     {0}", result.ModelUsed);
            if (result.Usage is not null)
            {
                Console.WriteLine(
                    "    Usage         {0} in, {1} out {2}",
                    result.Usage.InputUnits,
                    result.Usage.OutputUnits,
                    result.Usage.UnitKind);
            }
            Console.WriteLine("    FinishReason  {0}", result.FinishReason);
            if (result.TransferRequired)
            {
                Console.WriteLine("    payload too large; transfer at {0}", result.TransferId);
            }
        }

        private readonly AIClient m_client;
    }
}
