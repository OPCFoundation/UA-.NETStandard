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
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.LADS;
using Opc.Ua.LADS.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// LADS device inspection with explicit program, function-control and result-file tasks.
    /// </summary>
    internal sealed class LadsCompanionProvider : IPreparedCompanionProvider
    {
        /// <summary>
        /// Creates a LADS adapter with an injectable preparation clock.
        /// </summary>
        public LadsCompanionProvider(TimeProvider? timeProvider = null)
        {
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "lads", "LADS", LadsModel.NamespaceUri,
            "OPC 30500-1 device, functional-unit, program, result and maintenance views.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            LadsClient client = CreateClient(context);
            ArrayOf<LadsDevice> devices = await client.GetDevicesAsync(lifetime.Token).ConfigureAwait(false);
            var targets = new List<CompanionTarget>();
            var seen = new HashSet<NodeId>();
            for (int index = 0; index < devices.Count; index++)
            {
                LadsDevice device = devices[index];
                CellCompanionSupport.AddTarget(targets, seen, new CompanionTarget(
                    Descriptor.Id, device.NodeId, device.DisplayName, "LADSDevice"), context.MaxTargets);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. targets];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            LadsClient client = CreateClient(context);
            LadsDeviceSnapshot device = await client.ReadDeviceAsync(target.NodeId, lifetime.Token)
                .ConfigureAwait(false);
            var values = new List<CompanionValue>
            {
                new("Device", Variant.From(device.DisplayName)),
                new("Manufacturer", Variant.From(device.Identification.Manufacturer ?? string.Empty)),
                new("Model", Variant.From(device.Identification.Model ?? string.Empty)),
                new("Serial number", Variant.From(device.Identification.SerialNumber ?? string.Empty)),
                new("Device state", Variant.From(device.DeviceState?.CurrentState ?? "Not exposed")),
                new("Machinery item state", Variant.From(device.MachineryItemState?.CurrentState ?? "Not exposed")),
                new("Operation mode", Variant.From(device.OperationMode?.CurrentState ?? "Not exposed"))
            };
            if (!device.DeviceHealth.IsNull)
            {
                values.Add(new CompanionValue("Device health", device.DeviceHealth));
            }
            ArrayOf<LadsFunctionalUnit> units = await client.GetFunctionalUnitsAsync(target.NodeId, lifetime.Token)
                .ConfigureAwait(false);
            for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                LadsFunctionalUnit unit = units[unitIndex];
                LadsFunctionalUnitSnapshot snapshot = await client.ReadFunctionalUnitAsync(
                    unit.NodeId, includeFunctions: false, cancellationToken: lifetime.Token).ConfigureAwait(false);
                Add(values, context, new CompanionValue(
                    "Functional unit / " + snapshot.DisplayName,
                    Variant.From(unit.NodeId.ToString())));
                AddState(values, context, "Functional unit / " + unit.Name, snapshot.State);
                AddState(values, context, "Running state / " + unit.Name, snapshot.RunningState);
                if (snapshot.ActiveProgram is { } program)
                {
                    Add(values, context, new CompanionValue(
                        "Active program / " + unit.Name,
                        Variant.From(program.CurrentProgramTemplate ?? program.DeviceProgramRunId ?? "Running"))
                    {
                        SourceNodeId = program.NodeId
                    });
                }
                ArrayOf<LadsFunction> functions = await client.GetFunctionsAsync(
                    unit.NodeId, recursive: true, cancellationToken: lifetime.Token).ConfigureAwait(false);
                for (int functionIndex = 0; functionIndex < functions.Count; functionIndex++)
                {
                    LadsFunction function = functions[functionIndex];
                    Add(values, context, new CompanionValue(
                        "Function / " + unit.Name + " / " + function.Path,
                        Variant.From(function.Kind.ToString()))
                    {
                        SourceNodeId = function.NodeId
                    });
                }
            }
            ArrayOf<LadsMaintenanceTask> maintenance =
                await client.GetMaintenanceTasksAsync(target.NodeId, lifetime.Token).ConfigureAwait(false);
            for (int taskIndex = 0; taskIndex < maintenance.Count; taskIndex++)
            {
                LadsMaintenanceTask task = maintenance[taskIndex];
                Add(values, context, new CompanionValue(
                    "Maintenance / " + task.Name,
                    Variant.From(task.State?.CurrentState ?? "State not exposed"))
                {
                    SourceNodeId = task.NodeId
                });
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new CompanionInspection(
                [.. values],
                [
                    s_snapshot,
                    s_programTemplates,
                    s_maintenance,
                    s_startProgram,
                    s_functionControl,
                    s_exportResultFile
                ],
                "Read LADS device, unit, function and maintenance metadata. Commands require a separate prepared run.");
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            CellCompanionSupport.RequireNoInput(input);
            if (operationId is not ("snapshot" or "program-templates" or "maintenance"))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Prepare the selected LADS task first.");
            }
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            LadsClient client = CreateClient(context);
            var values = new List<CompanionValue>();
            if (operationId == "snapshot")
            {
                LadsDeviceSnapshot device = await client.ReadDeviceAsync(target.NodeId, lifetime.Token)
                    .ConfigureAwait(false);
                Add(values, context, new CompanionValue("Device", Variant.From(device.DisplayName)));
                Add(values, context, new CompanionValue(
                    "Device state", Variant.From(device.DeviceState?.CurrentState ?? "Not exposed")));
            }
            else if (operationId == "program-templates")
            {
                ArrayOf<LadsFunctionalUnit> units = await client.GetFunctionalUnitsAsync(target.NodeId, lifetime.Token)
                    .ConfigureAwait(false);
                for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
                {
                    LadsFunctionalUnit unit = units[unitIndex];
                    ArrayOf<LadsProgramTemplate> templates = await client.GetProgramTemplatesAsync(
                        unit.NodeId, lifetime.Token).ConfigureAwait(false);
                    for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
                    {
                        LadsProgramTemplate template = templates[templateIndex];
                        Add(values, context, new CompanionValue(
                            "Program template / " + unit.Name + " / " + template.Name,
                            Variant.From(template.Description ?? template.DisplayName ?? template.Name))
                        {
                            SourceNodeId = template.NodeId
                        });
                    }
                }
            }
            else
            {
                ArrayOf<LadsMaintenanceTask> tasks =
                    await client.GetMaintenanceTasksAsync(target.NodeId, lifetime.Token).ConfigureAwait(false);
                for (int index = 0; index < tasks.Count; index++)
                {
                    LadsMaintenanceTask task = tasks[index];
                    Add(values, context, new CompanionValue(
                        "Maintenance / " + task.Name,
                        Variant.From(task.State?.CurrentState ?? "State not exposed"))
                    {
                        SourceNodeId = task.NodeId
                    });
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new CompanionOperationResult("Read the requested LADS section; no device command was sent.", [.. values]);
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            CompanionOperation operation = Operation(operationId);
            CompanionInputContract.ValidateShape(operation, inputs);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            NodeId first = ParseNode(ReadText(inputs, operation.Inputs[0].Name));
            NodeId second = operationId == "export-result-file"
                ? ParseNode(ReadText(inputs, operation.Inputs[1].Name))
                : NodeId.Null;
            string command = operationId == "function-control" ? ReadText(inputs, "command") : string.Empty;
            string? value = operationId == "function-control" ? ReadText(inputs, "value", required: false) : null;
            string? destination = operationId == "export-result-file"
                ? ReadText(inputs, "destination")
                : null;
            LadsClient client = CreateClient(context);
            if (operationId == "start-program")
            {
                await RequireUnitAsync(client, target.NodeId, first, lifetime.Token).ConfigureAwait(false);
                string templateId = ReadText(inputs, "templateId");
                ArrayOf<LadsProgramTemplate> templates = await client.GetProgramTemplatesAsync(first, lifetime.Token)
                    .ConfigureAwait(false);
                if (!ContainsTemplate(templates, templateId))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound, "The selected program template is unavailable.");
                }
                return Prepared(context, target, operation, inputs, first, second, templateId, null);
            }
            if (operationId == "function-control")
            {
                RequireFunctionControl commandKind = RequireFunctionCommand(command, value);
                await RequireFunctionAsync(client, target.NodeId, first, lifetime.Token).ConfigureAwait(false);
                return Prepared(context, target, operation, inputs, first, NodeId.Null, command, commandKind.Value);
            }
            if (operationId == "export-result-file")
            {
                await RequireResultFileAsync(client, target.NodeId, first, second, lifetime.Token)
                    .ConfigureAwait(false);
                _ = OpenUsdCompanionExporter.ValidateDestination(destination);
                return Prepared(context, target, operation, inputs, first, second, destination!, null);
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported, "The selected LADS task is unavailable.");
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (input is not PreparedLadsTask prepared)
            {
                throw new ArgumentException("Prepare the exact LADS task before execution.", nameof(input));
            }
            prepared.Consume(this, target, operationId, context.Session, m_time);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            await RequireTargetAsync(context, target, lifetime.Token).ConfigureAwait(false);
            LadsClient client = CreateClient(context);
            if (operationId == "start-program")
            {
                string runId = await client.StartProgramAsync(
                    prepared.First, LadsProgramRequest.For(prepared.Text), lifetime.Token).ConfigureAwait(false);
                return new CompanionOperationResult(
                    "The server accepted the program start and assigned run ID " + runId + ".", []);
            }
            if (operationId == "function-control")
            {
                await ExecuteFunctionCommandAsync(
                    client, prepared.First, prepared.Text, prepared.Number, lifetime.Token).ConfigureAwait(false);
                return new CompanionOperationResult(
                    "The function state-machine command was accepted. Equipment completion remains device-owned.", []);
            }
            if (operationId == "export-result-file")
            {
                progress?.Report(new CompanionTaskProgress("Downloading the selected result file."));
                var buffer = new MemoryStream();
                LadsResultFile file = await RequireResultFileAsync(
                    client, target.NodeId, prepared.First, prepared.Second, lifetime.Token).ConfigureAwait(false);
                _ = await client.DownloadResultFileAsync(
                    file, buffer, lifetime.Token).ConfigureAwait(false);
                buffer.Position = 0;
                await CompanionLocalExport.CopyAsync(
                    prepared.Text, buffer, 16 * 1024 * 1024, () => prepared.RequireCurrent(context.Session, m_time),
                    lifetime.Token).ConfigureAwait(false);
                return new CompanionOperationResult(
                    "Exported the selected LADS result file to a new local file.", []);
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported, "The prepared LADS task is not supported.");
        }

        private PreparedLadsTask Prepared(
            CompanionContext context,
            CompanionTarget target,
            CompanionOperation operation,
            ArrayOf<CompanionValue> inputs,
            NodeId first,
            NodeId second,
            string text,
            double? number)
        {
            var payload = new PreparedLadsTask(this, target, operation.Id, first, second, text, number,
                new CompanionOperationDraft(
                    target, operation, null, context.Session, m_time.GetUtcNow().AddMinutes(5),
                    inputs: inputs));
            return payload;
        }

        private async ValueTask RequireTargetAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            if (target.ProviderId != Descriptor.Id || target.TypeName != "LADSDevice")
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The target is not a LADS device.");
            }
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask RequireUnitAsync(
            LadsClient client, NodeId deviceId, NodeId unitId, CancellationToken cancellationToken)
        {
            ArrayOf<LadsFunctionalUnit> units = await client.GetFunctionalUnitsAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            for (int index = 0; index < units.Count; index++)
            {
                if (units[index].NodeId == unitId)
                {
                    return;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The functional unit is not on this device.");
        }

        private static async ValueTask RequireFunctionAsync(
            LadsClient client, NodeId deviceId, NodeId functionId, CancellationToken cancellationToken)
        {
            ArrayOf<LadsFunctionalUnit> units = await client.GetFunctionalUnitsAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            for (int index = 0; index < units.Count; index++)
            {
                ArrayOf<LadsFunction> functions = await client.GetFunctionsAsync(units[index].NodeId,
                    recursive: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                for (int functionIndex = 0; functionIndex < functions.Count; functionIndex++)
                {
                    if (functions[functionIndex].NodeId == functionId && !functions[functionIndex].IsSensor)
                    {
                        return;
                    }
                }
            }
            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                "The selected control function is not part of this device.");
        }

        private static async ValueTask<LadsResultFile> RequireResultFileAsync(
            LadsClient client, NodeId deviceId, NodeId resultId, NodeId fileId,
            CancellationToken cancellationToken)
        {
            LadsResultFile? file = await FindResultFileAsync(
                client, deviceId, resultId, fileId, cancellationToken).ConfigureAwait(false);
            if (file is null || !file.CanDownload)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected result file is not downloadable through OPC UA.");
            }
            return file;
        }

        private static async ValueTask<LadsResultFile?> FindResultFileAsync(
            LadsClient client, NodeId deviceId, NodeId resultId, NodeId fileId,
            CancellationToken cancellationToken)
        {
            ArrayOf<LadsFunctionalUnit> units = await client.GetFunctionalUnitsAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                ArrayOf<LadsResult> results = await client.GetResultsAsync(units[unitIndex].NodeId, cancellationToken)
                    .ConfigureAwait(false);
                bool resultFound = false;
                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    resultFound |= results[resultIndex].NodeId == resultId;
                }
                if (!resultFound)
                {
                    continue;
                }
                ArrayOf<LadsResultFile> files = await client.GetResultFilesAsync(resultId, cancellationToken)
                    .ConfigureAwait(false);
                for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
                {
                    if (files[fileIndex].NodeId == fileId)
                    {
                        return files[fileIndex];
                    }
                }
            }
            return null;
        }

        private static async ValueTask ExecuteFunctionCommandAsync(
            LadsClient client, NodeId functionId, string command, double? value,
            CancellationToken cancellationToken)
        {
            switch (command)
            {
                case "start":
                    await client.StartFunctionAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "start-target":
                    await client.StartFunctionWithTargetValueAsync(
                        functionId, Variant.From(RequireValue(value)), cancellationToken).ConfigureAwait(false);
                    break;
                case "stop":
                    await client.StopFunctionAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "abort":
                    await client.AbortFunctionAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "modify-target-by":
                    await client.ModifyTargetValueByAsync(
                        functionId, RequireValue(value), cancellationToken).ConfigureAwait(false);
                    break;
                case "reset-totalizer":
                    await client.ResetTotalizerAsync(functionId, value ?? 0, cancellationToken).ConfigureAwait(false);
                    break;
                case "open":
                    await client.OpenCoverAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "close":
                    await client.CloseCoverAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "lock":
                    await client.LockCoverAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "unlock":
                    await client.UnlockCoverAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                case "reset-cover":
                    await client.ResetCoverAsync(functionId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException("The selected control-function command is not supported.", nameof(command));
            }
        }

        private static RequireFunctionControl RequireFunctionCommand(string command, string? value)
        {
            if (command is not ("start" or "start-target" or "stop" or "abort" or "modify-target-by" or
                "reset-totalizer" or "open" or "close" or "lock" or "unlock" or "reset-cover"))
            {
                throw new ArgumentException("The selected control-function command is not supported.", nameof(command));
            }
            double? number = null;
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                    !double.IsFinite(parsed))
                {
                    throw new ArgumentException("The optional value must be a finite invariant-culture number.");
                }
                number = parsed;
            }
            if (command is "start-target" or "modify-target-by" && number is null)
            {
                throw new ArgumentException("This function command requires a numeric value.", nameof(value));
            }
            if (command is "start" or "stop" or "abort" or "open" or "close" or "lock" or "unlock" or "reset-cover" &&
                number is not null)
            {
                throw new ArgumentException("This function command does not accept a numeric value.", nameof(value));
            }
            return new RequireFunctionControl(number);
        }

        private static double RequireValue(double? value)
        {
            return value is { } number && double.IsFinite(number)
                ? number
                : throw new ArgumentException("This command requires a finite numeric value.", nameof(value));
        }

        private static bool ContainsTemplate(ArrayOf<LadsProgramTemplate> templates, string templateId)
        {
            for (int index = 0; index < templates.Count; index++)
            {
                if (string.Equals(templates[index].Name, templateId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static CompanionOperation Operation(string operationId)
        {
            return operationId switch
            {
                "start-program" => s_startProgram,
                "function-control" => s_functionControl,
                "export-result-file" => s_exportResultFile,
                _ => throw new ServiceResultException(StatusCodes.BadNotSupported, "The selected LADS task is unavailable.")
            };
        }

        private static string ReadText(ArrayOf<CompanionValue> inputs, string name, bool required = true)
        {
            for (int index = 0; index < inputs.Count; index++)
            {
                if (inputs[index].Name == name &&
                    inputs[index].Value.TryGetValue(out string? value))
                {
                    return required && string.IsNullOrWhiteSpace(value)
                        ? throw new ArgumentException($"{name} is required.", nameof(inputs))
                        : value ?? string.Empty;
                }
            }
            return required ? throw new ArgumentException($"{name} is required.", nameof(inputs)) : string.Empty;
        }

        private static NodeId ParseNode(string text)
        {
            NodeId node = NodeId.Parse(text);
            return node.IsNull
                ? throw new ArgumentException("A non-null NodeId is required.", nameof(text))
                : node;
        }

        private static LadsClient CreateClient(CompanionContext context)
        {
            var client = new LadsClient(context.Session, context.Telemetry);
            return client.IsSupported
                ? client
                : throw new ServiceResultException(StatusCodes.BadNotSupported, "The session does not publish LADS.");
        }

        private static CancellationTokenSource Begin(CompanionContext context, CancellationToken token)
        {
            return CellCompanionSupport.BeginOperation(context, LadsModel.NamespaceUri, token);
        }

        private static void AddState(
            List<CompanionValue> values, CompanionContext context, string name, LadsStateSnapshot? state)
        {
            if (state is not null)
            {
                Add(values, context, new CompanionValue(name, Variant.From(state.CurrentState))
                {
                    SourceNodeId = state.StateMachineId
                });
            }
        }

        private static void Add(List<CompanionValue> values, CompanionContext context, CompanionValue value)
        {
            CellCompanionSupport.CheckCount(values.Count + 1, context.MaxFields, "LADS inspection");
            values.Add(value);
        }

        private static readonly ArrayOf<IndustrialCompanionType> s_types =
        [
            new(Opc.Ua.LADS.ObjectTypeIds.LADSDeviceType, "LADSDevice")
        ];

        private static readonly CompanionOperation s_snapshot = new(
            "snapshot", "Read device snapshot", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_programTemplates = new(
            "program-templates", "Read program templates", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_maintenance = new(
            "maintenance", "Read maintenance tasks", CompanionOperationSafety.ReadOnly);

        private static readonly CompanionOperation s_startProgram = new(
            "start-program", "Start a program", CompanionOperationSafety.DeploymentMutation,
            "Select a functional unit and a published template ID.")
        {
            Inputs =
            [
                new("functionalUnitNodeId", "Functional unit NodeId", BuiltInType.String,
                    "NodeId of a functional unit on this device."),
                new("templateId", "Program template ID", BuiltInType.String,
                    "Exact template ID from the unit's ProgramTemplateSet.")
            ]
        };

        private static readonly CompanionOperation s_functionControl = new(
            "function-control", "Control a function", CompanionOperationSafety.DeploymentMutation,
            "Commands are explicit; the selected function must be a control function or cover.")
        {
            Inputs =
            [
                new("functionNodeId", "Function NodeId", BuiltInType.String,
                    "NodeId of a control function on this device."),
                new("command", "Command", BuiltInType.String,
                    "start, start-target, stop, abort, modify-target-by, reset-totalizer, open, close, lock, unlock, reset-cover."),
                new("value", "Numeric value", BuiltInType.String,
                    "Finite invariant-culture number for start-target and modify-target-by; optional reset-totalizer value.",
                    Required: false)
            ]
        };

        private static readonly CompanionOperation s_exportResultFile = new(
            "export-result-file", "Export result file", CompanionOperationSafety.LocalFile,
            "Writes a new local file; existing files are not overwritten.")
        {
            Inputs =
            [
                new("resultNodeId", "Result NodeId", BuiltInType.String,
                    "NodeId of a result belonging to this device."),
                new("resultFileNodeId", "Result-file NodeId", BuiltInType.String,
                    "NodeId of a downloadable file belonging to that result."),
                new("destination", "New local file path", BuiltInType.String,
                    "Absolute local path for the new export.")
            ]
        };

        private readonly TimeProvider m_time;

        private sealed record RequireFunctionControl(double? Value);

        private sealed class PreparedLadsTask : CompanionTaskInput
        {
            public PreparedLadsTask(
                LadsCompanionProvider owner, CompanionTarget target, string operationId, NodeId first,
                NodeId second, string text, double? number, CompanionOperationDraft draft)
            {
                m_owner = owner;
                m_target = target;
                m_operationId = operationId;
                First = first;
                Second = second;
                Text = text;
                Number = number;
                m_draft = draft;
            }

            public NodeId First { get; }
            public NodeId Second { get; }
            public string Text { get; }
            public double? Number { get; }
            public override string Review => "Prepared LADS task; values were validated against this device.";

            public void Consume(
                LadsCompanionProvider owner, CompanionTarget target, string operationId, ISession session,
                TimeProvider timeProvider)
            {
                if (!ReferenceEquals(owner, m_owner) || target != m_target || operationId != m_operationId ||
                    Interlocked.Exchange(ref m_consumed, 1) != 0 ||
                    !m_draft.Matches(session, timeProvider.GetUtcNow()))
                {
                    throw new InvalidOperationException("The LADS preparation is stale, mismatched or already used.");
                }
            }

            public void RequireCurrent(ISession session, TimeProvider timeProvider)
            {
                if (!m_draft.Matches(session, timeProvider.GetUtcNow()))
                {
                    throw new InvalidOperationException("The LADS preparation expired or the session changed.");
                }
            }

            private readonly LadsCompanionProvider m_owner;
            private readonly CompanionTarget m_target;
            private readonly string m_operationId;
            private readonly CompanionOperationDraft m_draft;
            private int m_consumed;
        }
    }
}
