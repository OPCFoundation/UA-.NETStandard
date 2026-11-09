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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PackML;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class PackMlCompanionProvider
    {
        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            if (!context.Session.MessageContext.Factory.ContainsEncodeableType(
                Opc.Ua.PackML.DataTypeIds.PackMLDescriptorDataType))
            {
                context.Session.MessageContext.Factory.Builder.AddOpcUaPackML().Commit();
            }
            Command command = s_commands.ToList().SingleOrDefault(item => item.Operation.Id == operationId) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Unknown PackML command.");
            CompanionInputContract.ValidateShape(command.Operation, inputs);
            ArrayOf<PackMLDescriptorDataType> parameters = [];
            if (command.IsStart)
            {
                if (!inputs[0].Value.TryGetValue(out parameters, context.Session.MessageContext) ||
                    parameters.IsNull ||
                    parameters.Count > 32)
                {
                    throw new ArgumentException("Supply a non-null array of at most 32 PackML descriptors.", nameof(inputs));
                }
                var ids = new HashSet<int>();
                foreach (PackMLDescriptorDataType parameter in parameters)
                {
                    if (parameter is null ||
                        !ids.Add(parameter.ID) ||
                        !float.IsFinite(parameter.Value) ||
                        parameter.Name is not { Length: > 0 and <= 256 } ||
                        parameter.Unit is null ||
                        parameter.Unit.NamespaceUri is not { Length: > 0 and <= 1024 } ||
                        parameter.Unit.DisplayName.Text is { Length: > 256 } ||
                        parameter.Unit.Description.Text is { Length: > 2048 })
                    {
                        throw new ArgumentException(
                            "A descriptor has invalid identity, units or nonfinite data.", nameof(inputs));
                    }
                }
            }
            ArrayOf<CompanionValue> copied = CompanionInputContract.Snapshot(inputs, context.Session.MessageContext);
            if (command.IsStart &&
                !copied[0].Value.TryGetValue(out parameters, context.Session.MessageContext))
            {
                throw new ArgumentException("The copied PackML descriptors could not be decoded.", nameof(inputs));
            }
            var draft = new CompanionOperationDraft(
                target, command.Operation, null, context.Session, m_time.GetUtcNow().AddMinutes(5), inputs: copied);
            var stamp = new PreparedTask(this, context, draft, command, parameters);
            ArrayOf<StateLevel> levels = await ReadLevelsAsync(context, target, lifetime.Token).ConfigureAwait(false);
            StateLevel level = levels.ToList().SingleOrDefault(item => item.Kind == command.Level) ??
                throw new ServiceResultException(StatusCodes.BadStateNotActive, "The requested PackML level is inactive.");
            NodeId method = await ResolveCommandAsync(context, level, command, lifetime.Token).ConfigureAwait(false);
            CompanionDeploymentGrant grant = await AuthorizeAsync(context, draft, lifetime.Token).ConfigureAwait(false);
            stamp.SetEvidence(levels, level.Owner, method, grant);
            stamp.RequireCurrent(context, m_time);
            return stamp;
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
        {
            if (input is not PreparedTask prepared)
            {
                throw new ArgumentException("Prepare this PackML command first.", nameof(input));
            }
            prepared.Consume(this, target, operationId);
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            prepared.RequireCurrent(context, m_time);
            await RequireEvidenceAsync(context, prepared, lifetime.Token).ConfigureAwait(false);
            CompanionDeploymentGrant grant = await AuthorizeAsync(context, prepared.Draft, lifetime.Token)
                .ConfigureAwait(false);
            if (grant != prepared.Grant)
            {
                throw new UnauthorizedAccessException("The PackML authorization changed; prepare again.");
            }
            await RequireEvidenceAsync(context, prepared, lifetime.Token).ConfigureAwait(false);
            progress?.Report(new CompanionTaskProgress("Sending one authorized PackML command."));
            using var client = new PreparedCompanionMethodClient(
                context, prepared.Owner, prepared.Command.Declaration, prepared.Method, false,
                () => prepared.RequireCurrent(context, m_time), static () => { });
            await DispatchAsync(client, context.Telemetry, prepared, lifetime.Token).ConfigureAwait(false);
            return new CompanionOperationResult(
                "The server accepted the PackML command. Completion of any acting state remains equipment-owned. " +
                "Read fresh state or use Monitor; no command is automatically replayed.",
                [new CompanionValue("Method result", Variant.From(client.ResultStatus))]);
        }

        private async Task RequireEvidenceAsync(
            CompanionContext context, PreparedTask prepared, CancellationToken cancellationToken)
        {
            ArrayOf<StateLevel> current = await ReadLevelsAsync(context, prepared.Draft.Target, cancellationToken)
                .ConfigureAwait(false);
            if (current.Count != prepared.Levels.Count)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The active PackML hierarchy changed.");
            }
            for (int index = 0; index < current.Count; index++)
            {
                if (!current[index].Matches(prepared.Levels[index]))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The PackML state changed; prepare again.");
                }
            }
            StateLevel level = current.ToList().Single(item => item.Kind == prepared.Command.Level);
            NodeId method = await ResolveCommandAsync(context, level, prepared.Command, cancellationToken)
                .ConfigureAwait(false);
            if (method != prepared.Method || level.Owner != prepared.Owner)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The PackML method binding changed.");
            }
            prepared.RequireCurrent(context, m_time);
        }

        private async Task<CompanionDeploymentGrant> AuthorizeAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            if (!context.Session.Connected ||
                context.Session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(context.Session.Endpoint.SecurityPolicyUri) ||
                context.Session.Endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new UnauthorizedAccessException("PackML commands require a signed and encrypted primary session.");
            }
            CompanionDeploymentGrant grant = await m_policy.AuthorizeAsync(context, draft, cancellationToken)
                .ConfigureAwait(false);
            if (grant is null ||
                string.IsNullOrEmpty(grant.RuleId) ||
                string.IsNullOrEmpty(grant.Revision) ||
                grant.ExpiresAt <= m_time.GetUtcNow() ||
                grant.ExpiresAt > draft.ExpiresAt)
            {
                throw new UnauthorizedAccessException("The PackML operation grant is invalid or expired.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return grant;
        }

        private static ValueTask DispatchAsync(
            ISessionClient client, ITelemetryContext telemetry, PreparedTask task, CancellationToken cancellationToken)
        {
            var root = new PackMLBaseStateMachineTypeClient(client, task.Owner, telemetry);
            var machine = new PackMLMachineStateMachineTypeClient(client, task.Owner, telemetry);
            var execute = new PackMLExecuteStateMachineTypeClient(client, task.Owner, telemetry);
            return task.Command.Operation.Id switch
            {
                "abort" => root.AbortAsync(cancellationToken),
                "clear" => root.ClearAsync(cancellationToken),
                "stop" => machine.StopAsync(cancellationToken),
                "reset-machine" => machine.ResetAsync(cancellationToken),
                "reset-execute" => execute.ResetAsync(cancellationToken),
                "start" => execute.StartAsync(task.Parameters, cancellationToken),
                "hold" => execute.HoldAsync(cancellationToken),
                "unhold" => execute.UnholdAsync(cancellationToken),
                "suspend" => execute.SuspendAsync(cancellationToken),
                "unsuspend" => execute.UnsuspendAsync(cancellationToken),
                "complete" => execute.ToCompleteAsync(cancellationToken),
                _ => throw new ServiceResultException(StatusCodes.BadNotSupported)
            };
        }

        /// <summary>
        /// Owns copied descriptors and a single dispatch pinned to the exact borrowed session and active hierarchy.
        /// </summary>
        private sealed class PreparedTask : CompanionTaskInput
        {
            public PreparedTask(
                PackMlCompanionProvider provider, CompanionContext context, CompanionOperationDraft draft,
                Command command, ArrayOf<PackMLDescriptorDataType> parameters)
            {
                m_provider = provider;
                m_session = context.Session;
                m_messageContext = context.Session.MessageContext;
                m_certificate = context.Session.Endpoint.ServerCertificate.Copy();
                Draft = draft;
                Command = command;
                Parameters = parameters;
            }

            public override string Review => $"{Command.Name} on {Owner}; {Parameters.Count} start parameter(s). " +
                "Current state and exact method are pinned. Server acceptance is not equipment completion.";

            public CompanionOperationDraft Draft { get; private set; }
            public Command Command { get; }
            public ArrayOf<PackMLDescriptorDataType> Parameters { get; }
            public ArrayOf<StateLevel> Levels { get; private set; }
            public NodeId Owner { get; private set; }
            public NodeId Method { get; private set; }
            public CompanionDeploymentGrant? Grant { get; private set; }

            public void SetEvidence(
                ArrayOf<StateLevel> levels, NodeId owner, NodeId method, CompanionDeploymentGrant grant)
            {
                Levels = levels;
                Owner = owner;
                Method = method;
                Grant = grant;
                Draft = Draft.WithGrant(grant);
            }

            public void RequireCurrent(CompanionContext context, TimeProvider time)
            {
                if (!ReferenceEquals(context.Session, m_session) ||
                    !ReferenceEquals(context.Session.MessageContext, m_messageContext) ||
                    context.Session.Endpoint.ServerCertificate != m_certificate ||
                    !Draft.Matches(context.Session, time.GetUtcNow()))
                {
                    throw new UnauthorizedAccessException("The PackML session, identity, peer or authorization changed.");
                }
            }

            public void Consume(PackMlCompanionProvider provider, CompanionTarget target, string operationId)
            {
                if (Interlocked.Exchange(ref m_consumed, 1) != 0)
                {
                    throw new InvalidOperationException("The PackML preparation is consumed; do not replay it.");
                }
                if (!ReferenceEquals(provider, m_provider) || target != Draft.Target || operationId != Command.Operation.Id)
                {
                    throw new ArgumentException("The PackML preparation belongs to another provider, target or command.");
                }
            }

            private readonly PackMlCompanionProvider m_provider;
            private readonly ISession m_session;
            private readonly IServiceMessageContext m_messageContext;
            private readonly ByteString m_certificate;
            private int m_consumed;
        }
    }
}
