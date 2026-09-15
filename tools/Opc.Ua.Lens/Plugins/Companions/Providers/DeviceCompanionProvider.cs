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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di.Client;
using Di = Opc.Ua.Di;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// DI device identification and explicitly prepared software-update tasks.
/// State, capability and package evidence are rechecked before every mutation.
/// </summary>
internal sealed class DeviceCompanionProvider : IPreparedCompanionProvider
{
    public DeviceCompanionProvider(ICompanionPackageReader? packages = null, TimeProvider? timeProvider = null)
    {
        m_packages = packages ?? new CompanionPackageReader();
        m_timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CompanionDescriptor Descriptor { get; } = new(
        "di", "Devices and software update", Di.Namespaces.OpcUaDi,
        "OPC 10000-100 Device Integration; application-specific software-update workflow");

    public ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IndustrialCompanionAccess.RequireNamespace(context, Di.Namespaces.OpcUaDi);
        var topology = new DiTopologyClient(context.Session, context.Telemetry);

        // The topology enumerator currently neither drains nor releases continuation
        // points. Reuse its namespace-resolved root with the bounded browse adapter.
        return IndustrialCompanionAccess.DiscoverAsync(
            context, Descriptor.Id, s_types, [topology.DeviceSetId], cancellationToken);
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (target.TypeName == kDeviceKind)
        {
            var device = new DiDeviceClient(context.Session, target.NodeId, context.Telemetry);

            // ReadIdentificationAsync discards per-property Bad statuses and flattens
            // LocalizedText to string. Preserve wire types and failures at this boundary.
            ArrayOf<CompanionValue> identity = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, device.DeviceNodeId, Di.Namespaces.OpcUaDi,
                [
                    "Manufacturer", "Model", "SerialNumber", "HardwareRevision",
                    "SoftwareRevision", "DeviceRevision", "DeviceClass", "ProductInstanceUri"
                ],
                cancellationToken).ConfigureAwait(false);
            return new CompanionInspection(
                identity, [new("refresh", "Read identification again", CompanionOperationSafety.ReadOnly)],
                "DI identification only. BadNotFound marks an absent optional property; read errors are not hidden.");
        }

        IndustrialCompanionAccess.CheckFields(context, 16);
        var update = new SoftwareUpdateClient(context.Session, target.NodeId, context.Telemetry);
        var values = new List<CompanionValue>();
        var operations = new List<CompanionOperation>
        {
            new("refresh", "Read update state again", CompanionOperationSafety.ReadOnly)
        };
        bool prepareAvailable = false;
        for (int index = 0; index < s_facets.Count; index++)
        {
            string facet = s_facets[index];
            FiniteStateSnapshot? state = await ReadStateAsync(context, update, facet, cancellationToken)
                .ConfigureAwait(false);
            values.Add(new CompanionValue(facet + " available", Variant.From(state is not null)));
            if (state is null)
            {
                continue;
            }
            if (facet == "PrepareForUpdate")
            {
                prepareAvailable = true;
            }
            values.Add(new CompanionValue(facet + " state", Variant.From(state.CurrentState)));
            values.Add(new CompanionValue(facet + " state NodeId", Variant.From(state.CurrentStateId)));
            values.Add(new CompanionValue(facet + " state machine", Variant.From(state.StateMachineId)));
            for (int taskIndex = 0; taskIndex < s_tasks.Count; taskIndex++)
            {
                DeviceTask task = s_tasks[taskIndex];
                if (task.Facet == facet && await CanExecuteAsync(
                    context, state.StateMachineId, task.Method, cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                {
                    operations.Add(task.Operation);
                }
            }
        }
        if (prepareAvailable)
        {
            operations.Insert(1, new("prepare-sample", "Prepare sample device for update",
                CompanionOperationSafety.SampleMutation));
        }
        NodeId transfer = await ResolveTransferAsync(context, target, cancellationToken).ConfigureAwait(false);
        if (!transfer.IsNull &&
            await CanExecuteAsync(context, transfer, "GenerateFileForWrite", Namespaces.OpcUa, cancellationToken)
                .ConfigureAwait(false) &&
            await CanExecuteAsync(context, transfer, "CloseAndCommit", Namespaces.OpcUa, cancellationToken)
                .ConfigureAwait(false))
        {
            operations.Add(s_upload);
        }
        operations.Add(s_observe);
        return new CompanionInspection(
            [.. values], [.. operations],
            "Read-only state-machine inspection. Offered sample tasks use application-specific update handlers. " +
            "Upload, prepare, install and confirmation are separate explicit actions; no power cycle or rollback " +
            "is inferred. Only configured repository samples may be changed by these tasks.");
    }

    public async ValueTask<CompanionTaskInput> PrepareInputAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        ArrayOf<CompanionValue> inputs,
        CancellationToken cancellationToken)
    {
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (target.TypeName != kUpdateKind)
        {
            throw IndustrialCompanionAccess.Unsupported("Select a SoftwareUpdate instance.");
        }
        if (operationId == s_upload.Id)
        {
            RequireInputs(inputs, s_upload.Inputs);
            string path = Text(inputs, 0, 4096);
            string packageId = IndustrialCompanionAccess.SampleId(Text(inputs, 1, 32));
            ByteString expected = Digest(Text(inputs, 2, 64));
            ByteString package = await m_packages.ReadAsync(path, MaximumPackageBytes, cancellationToken)
                .ConfigureAwait(false);
            if (package.IsEmpty || package.Length > MaximumPackageBytes ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(package.Span), expected.Span))
            {
                throw new ArgumentException("The bounded package does not match the expected SHA-256 digest.");
            }
            NodeId transfer = await RequireTransferAsync(context, target, cancellationToken).ConfigureAwait(false);
            return new PreparedDeviceTask(operationId,
                $"Upload {package.Length.ToString(CultureInfo.InvariantCulture)} verified bytes as {packageId}.\n" +
                $"SHA-256: {Convert.ToHexString(expected.Span)}\nNo installation is requested.")
            {
                MachineId = transfer,
                Package = package.Copy(),
                PackageId = packageId,
                Hash = expected
            };
        }
        if (operationId == s_observe.Id)
        {
            RequireInputs(inputs, s_observe.Inputs);
            if (!inputs[0].Value.TryGetValue(out uint seconds) || seconds is < 1 or > 60)
            {
                throw new ArgumentException("Observation duration must be 1 through 60 seconds.");
            }
            return new PreparedDeviceTask(operationId, $"Observe state for {seconds} seconds; no device mutation.")
            {
                ObservationSeconds = seconds
            };
        }
        DeviceTask task = FindTask(operationId);
        RequireInputs(inputs, task.Operation.Inputs);
        var update = new SoftwareUpdateClient(context.Session, target.NodeId, context.Telemetry);
        FiniteStateSnapshot state = await ReadStateAsync(context, update, task.Facet, cancellationToken)
            .ConfigureAwait(false)
            ?? throw IndustrialCompanionAccess.Unsupported("The update state machine is unavailable.");
        await RequireExecutableAsync(
            context, state.StateMachineId, task.Method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (operationId == "install-package-sample")
        {
            string manufacturer = Text(inputs, 0, 2048);
            if (!Uri.TryCreate(manufacturer, UriKind.Absolute, out Uri? uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("Enter an absolute manufacturer URI without credentials or query.");
            }
            string revision = Text(inputs, 1, 128);
            string[] patches = Text(inputs, 2, 4096, required: false)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patches.Length > 32 || patches.Any(patch => patch.Length > 128))
            {
                throw new ArgumentException("Supply at most 32 patch identifiers of at most 128 characters.");
            }
            ByteString hash = Digest(Text(inputs, 3, 64));
            return new PreparedDeviceTask(operationId,
                $"Install revision {revision} from {manufacturer}.\n" +
                $"SHA-256: {Convert.ToHexString(hash.Span)}\n" +
                "This can take the sample device offline. Confirmation remains a separate operation.")
            {
                MachineId = state.StateMachineId, StateId = state.CurrentStateId,
                Manufacturer = manufacturer, Revision = revision, Patches = patches, Hash = hash
            };
        }
        return new PreparedDeviceTask(operationId,
            $"{task.Operation.DisplayName}\nObserved state: {state.CurrentState} ({state.CurrentStateId}).\n" +
            "Only this cause method is invoked; device recovery is not guaranteed.")
        {
            MachineId = state.StateMachineId, StateId = state.CurrentStateId
        };
    }

    public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        CompanionTaskInput input,
        IProgress<CompanionTaskProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (input is not PreparedDeviceTask prepared || prepared.OperationId != operationId)
        {
            throw new ArgumentException("The device task input was not prepared for this operation.", nameof(input));
        }
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (target.TypeName != kUpdateKind)
        {
            throw IndustrialCompanionAccess.Unsupported("Select a SoftwareUpdate instance.");
        }
        var update = new SoftwareUpdateClient(context.Session, target.NodeId, context.Telemetry);
        if (operationId == s_upload.Id)
        {
            NodeId transfer = await RequireTransferAsync(context, target, cancellationToken).ConfigureAwait(false);
            if (transfer != prepared.MachineId)
            {
                throw new InvalidOperationException("The package transfer target changed. Prepare it again.");
            }
            progress?.Report(new CompanionTaskProgress("Uploading the verified package; no installation requested."));
            using var payload = new MemoryStream(prepared.Package.Span.ToArray(), writable: false);
            SoftwareUpdateUploadResult uploaded = await update.UploadPackageWithResultAsync(
                payload, prepared.PackageId, ct: cancellationToken).ConfigureAwait(false);
            if (uploaded.BytesUploaded != prepared.Package.Length)
            {
                throw new IOException("The completed upload byte count does not match the verified package.");
            }
            return new CompanionOperationResult(
                "The upload and CloseAndCommit calls succeeded. No install was requested. " +
                (uploaded.CompletionStateMachine.IsNull
                    ? string.Empty
                    : "The returned completion state machine must be observed before installation. ") +
                "Inspect device state before the separate installation step.",
                [
                    new("Package id", Variant.From(prepared.PackageId)),
                    new("Package bytes", Variant.From(uploaded.BytesUploaded)),
                    new("Package SHA-256", Variant.From(prepared.Hash)),
                    new("Completion state machine", Variant.From(uploaded.CompletionStateMachine))
                ]);
        }
        if (operationId == s_observe.Id)
        {
            long started = m_timeProvider.GetTimestamp();
            CompanionInspection observed = await InspectAsync(context, target, cancellationToken).ConfigureAwait(false);
            int reads = 1;
            while (m_timeProvider.GetElapsedTime(started) < TimeSpan.FromSeconds(prepared.ObservationSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CompanionTaskProgress(
                    $"Observed {reads} update snapshots. The returned values are the most recent server reads."));
                await Task.Delay(TimeSpan.FromSeconds(1), m_timeProvider, cancellationToken).ConfigureAwait(false);
                observed = await InspectAsync(context, target, cancellationToken).ConfigureAwait(false);
                reads++;
            }
            return new CompanionOperationResult(
                $"Observed {reads} state snapshots. Polling does not prove that every transition was received.",
                observed.Values);
        }
        DeviceTask task = FindTask(operationId);
        FiniteStateSnapshot state = await ReadStateAsync(context, update, task.Facet, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The prepared state machine disappeared.");
        if (state.StateMachineId != prepared.MachineId || state.CurrentStateId != prepared.StateId)
        {
            throw new InvalidOperationException("Device update state changed. Inspect and prepare the task again.");
        }
        await RequireExecutableAsync(
            context, prepared.MachineId, task.Method, cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(new CompanionTaskProgress("Calling " + task.Operation.DisplayName));
        switch (operationId)
        {
            case "install-package-sample":
                await update.InstallSoftwarePackageAsync(
                    prepared.Manufacturer, prepared.Revision, prepared.Patches, prepared.Hash, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "abort-prepare-sample":
                await update.AbortPrepareAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "resume-prepare-sample":
                var prepare = new Di.PrepareForUpdateStateMachineTypeClient(
                    context.Session, prepared.MachineId, context.Telemetry);
                await prepare.ResumeAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "resume-installation-sample":
                await update.ResumeInstallationAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "confirm-sample":
                await update.ConfirmAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw IndustrialCompanionAccess.Unsupported("This prepared update operation is not supported.");
        }
        state = await ReadStateAsync(context, update, task.Facet, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The device state is unavailable after the method returned.");
        return new CompanionOperationResult(
            $"{task.Operation.DisplayName}: the method returned successfully. " +
            "The displayed state is read from the device, not inferred as installation or recovery success.",
            [
                new(task.Facet + " state", Variant.From(state.CurrentState)),
                new(task.Facet + " state NodeId", Variant.From(state.CurrentStateId)),
                new(task.Facet + " state machine", Variant.From(state.StateMachineId))
            ]);
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IndustrialCompanionAccess.RequireNoInput(input);
        if (operationId == "refresh")
        {
            CompanionInspection inspection = await InspectAsync(context, target, cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(inspection.Summary, inspection.Values);
        }
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (target.TypeName != kUpdateKind || operationId != "prepare-sample")
        {
            throw IndustrialCompanionAccess.Unsupported(
                "Only refresh and the software-update sample Prepare are supported.");
        }
        IndustrialCompanionAccess.CheckFields(context, 2);
        var update = new SoftwareUpdateClient(context.Session, target.NodeId, context.Telemetry);
        _ = await ReadStateAsync(context, update, "PrepareForUpdate", cancellationToken).ConfigureAwait(false)
            ?? throw IndustrialCompanionAccess.Unsupported("The optional PrepareForUpdate state machine is absent.");
        await update.PrepareAsync(cancellationToken).ConfigureAwait(false);
        FiniteStateSnapshot state = await ReadStateAsync(
            context, update, "PrepareForUpdate", cancellationToken).ConfigureAwait(false)
            ?? throw IndustrialCompanionAccess.Unsupported("PrepareForUpdate disappeared after the Prepare request.");
        return new CompanionOperationResult(
            "Prepare completed; the returned state was read from the device. No package was loaded or installed.",
            [
                new("PrepareForUpdate state", Variant.From(state.CurrentState)),
                new("PrepareForUpdate state NodeId", Variant.From(state.CurrentStateId))
            ]);
    }

    private static async ValueTask<FiniteStateSnapshot?> ReadStateAsync(
        CompanionContext context,
        SoftwareUpdateClient update,
        string facet,
        CancellationToken cancellationToken)
    {
        NodeId child = await IndustrialCompanionAccess.ResolveChildAsync(
            context, update.SoftwareUpdateNodeId, Di.Namespaces.OpcUaDi, facet, true, cancellationToken)
            .ConfigureAwait(false);
        if (child.IsNull)
        {
            return null;
        }
        FiniteStateSnapshot? state = facet switch
        {
            "PrepareForUpdate" => await update.GetPrepareForUpdateStateAsync(cancellationToken).ConfigureAwait(false),
            "Installation" => await update.GetInstallationStateAsync(cancellationToken).ConfigureAwait(false),
            "Confirmation" => await update.GetConfirmationStateAsync(cancellationToken).ConfigureAwait(false),
            "PowerCycle" => await update.GetPowerCycleStateAsync(cancellationToken).ConfigureAwait(false),
            _ => throw IndustrialCompanionAccess.Unsupported("This software-update facet is not supported.")
        };
        if (state is null)
        {
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError, "A resolved state machine could not be read.");
        }
        IndustrialCompanionAccess.ThrowIfBad(state.Status);
        if (state.StateMachineId != child || state.CurrentState.IsNull || state.CurrentStateId.IsNull)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch,
                "The state machine did not return a current state and valid state NodeId.");
        }
        return state;
    }

    private static DeviceTask FindTask(string operationId)
    {
        foreach (DeviceTask task in s_tasks)
        {
            if (task.Operation.Id == operationId)
            {
                return task;
            }
        }
        throw IndustrialCompanionAccess.Unsupported("The device task is not supported.");
    }

    private static void RequireInputs(ArrayOf<CompanionValue> inputs, ArrayOf<CompanionInputDefinition> fields)
    {
        if (inputs.Count != fields.Count)
        {
            throw new ArgumentException("Supply the exact device task input fields.", nameof(inputs));
        }
        for (int i = 0; i < fields.Count; i++)
        {
            if (inputs[i] is null || inputs[i].Name != fields[i].Name ||
                inputs[i].Value.TypeInfo.BuiltInType != fields[i].DataType ||
                !inputs[i].Value.TypeInfo.IsScalar)
            {
                throw new ArgumentException("Device task input names or types do not match.", nameof(inputs));
            }
        }
    }

    private static string Text(ArrayOf<CompanionValue> fields, int index, int maximumLength, bool required = true)
    {
        if (!fields[index].Value.TryGetValue(out string? text) || text is null || text.Length > maximumLength ||
            (required && string.IsNullOrWhiteSpace(text)))
        {
            throw new ArgumentException($"Invalid {fields[index].Name}.");
        }
        return text;
    }

    private static ByteString Digest(string text)
    {
        if (text.Length != 64 || text.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 digest must contain exactly 64 hexadecimal characters.");
        }
        return new ByteString(Convert.FromHexString(text));
    }

    private static async ValueTask<NodeId> ResolveTransferAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        NodeId loading = await IndustrialCompanionAccess.ResolveChildAsync(
            context, target.NodeId, Di.Namespaces.OpcUaDi, "Loading", true, cancellationToken).ConfigureAwait(false);
        return loading.IsNull ? NodeId.Null : await IndustrialCompanionAccess.ResolveChildAsync(
            context, loading, Di.Namespaces.OpcUaDi, "FileTransfer", true, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<NodeId> RequireTransferAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        NodeId transfer = await ResolveTransferAsync(context, target, cancellationToken).ConfigureAwait(false);
        if (transfer.IsNull)
        {
            throw IndustrialCompanionAccess.Unsupported("The package file-transfer workflow is unavailable.");
        }
        await RequireExecutableAsync(
            context, transfer, "GenerateFileForWrite", Namespaces.OpcUa, cancellationToken).ConfigureAwait(false);
        await RequireExecutableAsync(
            context, transfer, "CloseAndCommit", Namespaces.OpcUa, cancellationToken).ConfigureAwait(false);
        return transfer;
    }

    private static async ValueTask RequireExecutableAsync(
        CompanionContext context, NodeId parent, string name,
        string? namespaceUri = null, CancellationToken cancellationToken = default)
    {
        if (!await CanExecuteAsync(context, parent, name, namespaceUri, cancellationToken).ConfigureAwait(false))
        {
            throw new ServiceResultException(
                StatusCodes.BadNotExecutable, "The offered device method is absent or is not executable by this user.");
        }
    }

    private static async ValueTask<bool> CanExecuteAsync(
        CompanionContext context, NodeId parent, string name,
        string? namespaceUri = null, CancellationToken cancellationToken = default)
    {
        NodeId method = await IndustrialCompanionAccess.ResolveChildAsync(
            context, parent, namespaceUri ?? Di.Namespaces.OpcUaDi, name, true, cancellationToken)
            .ConfigureAwait(false);
        if (method.IsNull)
        {
            return false;
        }
        ReadResponse response = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
            [
                new ReadValueId { NodeId = method, AttributeId = Attributes.Executable },
                new ReadValueId { NodeId = method, AttributeId = Attributes.UserExecutable }
            ],
            cancellationToken).ConfigureAwait(false);
        IndustrialCompanionAccess.ThrowIfBad(response.ResponseHeader.ServiceResult);
        if (response.Results.Count != 2)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid method permission response.");
        }
        IndustrialCompanionAccess.ThrowIfBad(response.Results[0].StatusCode);
        IndustrialCompanionAccess.ThrowIfBad(response.Results[1].StatusCode);
        if (!response.Results[0].WrappedValue.TryGetValue(out bool executable) ||
            !response.Results[1].WrappedValue.TryGetValue(out bool allowed))
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid method permission attributes.");
        }
        return executable && allowed;
    }

    internal const int MaximumPackageBytes = 64 * 1024 * 1024;

    private const string kDeviceKind = "DI device";
    private const string kUpdateKind = "Software update";
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
    [
        new(Di.ObjectTypeIds.DeviceType, kDeviceKind),
        new(Di.ObjectTypeIds.SoftwareUpdateType, kUpdateKind)
    ];
    private static readonly ArrayOf<string> s_facets =
        ["PrepareForUpdate", "Installation", "Confirmation", "PowerCycle"];

    private static readonly CompanionOperation s_upload = new(
        "upload-package-sample", "Upload verified sample package", CompanionOperationSafety.SampleMutation)
    {
        Inputs =
        [
            new("path", "Package file", BuiltInType.String, "Absolute local file path", IsFileSource: true),
            new("packageId", "Sample package suffix", BuiltInType.String,
                "Lowercase letters, digits or hyphens (1-32)"),
            new("sha256", "Expected SHA-256", BuiltInType.String, "64 hexadecimal characters from a trusted source")
        ]
    };
    private static readonly CompanionOperation s_observe = new(
        "observe-update", "Observe update state", CompanionOperationSafety.ReadOnly)
    {
        Inputs = [new("seconds", "Observation duration (seconds)", BuiltInType.UInt32, "1 through 60")]
    };
    private static readonly ArrayOf<DeviceTask> s_tasks =
    [
        new("Installation", "InstallSoftwarePackage", new CompanionOperation(
            "install-package-sample", "Install sample software package", CompanionOperationSafety.SampleMutation)
        {
            Inputs =
            [
                new("manufacturer", "Manufacturer URI", BuiltInType.String, "Absolute manufacturer URI"),
                new("revision", "Software revision", BuiltInType.String, "Revision of the staged package"),
                new("patches", "Patch identifiers", BuiltInType.String,
                    "One identifier per line, if required", false, IsMultiline: true),
                new("sha256", "Package SHA-256", BuiltInType.String, "64 hexadecimal characters")
            ]
        }),
        new("PrepareForUpdate", "Abort", new CompanionOperation(
            "abort-prepare-sample", "Abort sample preparation", CompanionOperationSafety.SampleMutation)
            { Inputs = [] }),
        new("PrepareForUpdate", "Resume", new CompanionOperation(
            "resume-prepare-sample", "Resume sample preparation", CompanionOperationSafety.SampleMutation)
            { Inputs = [] }),
        new("Installation", "Resume", new CompanionOperation(
            "resume-installation-sample", "Resume sample installation", CompanionOperationSafety.SampleMutation)
            { Inputs = [] }),
        new("Confirmation", "Confirm", new CompanionOperation(
            "confirm-sample", "Confirm sample update", CompanionOperationSafety.SampleMutation) { Inputs = [] })
    ];
    private readonly ICompanionPackageReader m_packages;
    private readonly TimeProvider m_timeProvider;

    private sealed record DeviceTask(string Facet, string Method, CompanionOperation Operation);

    private sealed class PreparedDeviceTask(string operationId, string review) : CompanionTaskInput
    {
        public override string Review => review;
        public string OperationId { get; } = operationId;
        public NodeId MachineId { get; init; }
        public NodeId StateId { get; init; }
        public ByteString Package { get; init; }
        public ByteString Hash { get; init; }
        public string PackageId { get; init; } = string.Empty;
        public string Manufacturer { get; init; } = string.Empty;
        public string Revision { get; init; } = string.Empty;
        public ArrayOf<string> Patches { get; init; }
        public uint ObservationSeconds { get; init; }
    }
}
