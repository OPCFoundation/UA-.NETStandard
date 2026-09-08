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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di.Client;
using Di = Opc.Ua.Di;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// DI device identification and software-update observation. The only update
/// mutation is the explicitly gated Prepare step, never loading or installation.
/// </summary>
internal sealed class DeviceCompanionProvider : ICompanionProvider
{
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
        }
        ArrayOf<CompanionOperation> operations = prepareAvailable
            ? [
                new("refresh", "Read update state again", CompanionOperationSafety.ReadOnly),
                new("prepare-sample", "Prepare sample device for update", CompanionOperationSafety.SampleMutation)
            ]
            : [new("refresh", "Read update state again", CompanionOperationSafety.ReadOnly)];
        return new CompanionInspection(
            [.. values], operations,
            "Read-only state-machine inspection. The sample task calls Prepare only and can take a sample device " +
            "offline. Package validation, loading, installation, power cycling and confirmation require an " +
            "application-specific workflow and are not offered.");
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

    private const string kDeviceKind = "DI device";
    private const string kUpdateKind = "Software update";
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
    [
        new(Di.ObjectTypeIds.DeviceType, kDeviceKind),
        new(Di.ObjectTypeIds.SoftwareUpdateType, kUpdateKind)
    ];
    private static readonly ArrayOf<string> s_facets =
        ["PrepareForUpdate", "Installation", "Confirmation", "PowerCycle"];
}
