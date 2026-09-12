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
using Opc.Ua;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Reads published Robotics topology and telemetry without opening any robot commanding surface.
/// </summary>
internal sealed class RoboticsCompanionProvider : ICompanionProvider
{
    public RoboticsCompanionProvider()
        : this(static context => new RoboticsCompanionReader(context))
    {
    }

    public RoboticsCompanionProvider(Func<CompanionContext, IRoboticsCompanionReader> createReader)
    {
        m_createReader = createReader ?? throw new ArgumentNullException(nameof(createReader));
    }

    public CompanionDescriptor Descriptor { get; } = new(
        "robotics",
        "Robotics cell",
        Opc.Ua.Robotics.Namespaces.Robotics,
        "Published OPC 40010 Robotics; Robot Intent is a separate draft and is not actuated here");

    public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IRoboticsCompanionReader reader = m_createReader(context);
        var targets = new List<CompanionTarget>();
        var visited = new HashSet<NodeId>();
        await foreach (MotionDeviceSystemEntry entry in reader.EnumerateSystemsAsync(lifetime.Token)
            .WithCancellation(lifetime.Token).ConfigureAwait(false))
        {
            lifetime.Token.ThrowIfCancellationRequested();
            CellCompanionSupport.AddTarget(
                targets,
                visited,
                new CompanionTarget(
                    Descriptor.Id,
                    entry.NodeId,
                    entry.DisplayName.Text ?? entry.BrowseName.Name ?? entry.NodeId.ToString(),
                    "MotionDeviceSystem"),
                context.MaxTargets);
        }
        lifetime.Token.ThrowIfCancellationRequested();
        return [.. targets];
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IRoboticsCompanionReader reader = m_createReader(context);
        CancellationToken token = lifetime.Token;
        ArrayOf<NodeId> controllers = await reader.GetControllersAsync(target.NodeId, token).ConfigureAwait(false);
        ArrayOf<NodeId> devices = await reader.GetMotionDevicesAsync(target.NodeId, token).ConfigureAwait(false);
        CellCompanionSupport.CheckCount(controllers.Count + devices.Count + 1, context.MaxTargets, "Cell topology");

        var fields = new CellCompanionFields(context.MaxFields);
        fields.Add("System", Variant.From(target.NodeId));
        fields.Add("Controllers", Variant.From(controllers));
        fields.Add("Motion devices", Variant.From(devices));
        var visited = new HashSet<NodeId> { target.NodeId };
        for (int index = 0; index < controllers.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            AddTopologyNode(visited, controllers[index], context.MaxTargets);
            ControllerSnapshot controller = await reader.ReadControllerAsync(controllers[index], token)
                .ConfigureAwait(false);
            CellCompanionSupport.CheckCount(
                controller.TaskControlIds.Count + controller.ComponentIds.Count,
                context.MaxTargets,
                "Controller members");
            string prefix = $"Controller {index + 1}";
            AddIdentification(fields, prefix, controller.Identification);
            fields.Add($"{prefix} task controls", Variant.From(controller.TaskControlIds));
            fields.Add($"{prefix} components", Variant.From(controller.ComponentIds));
            RoboticsOperationState? state = await reader.ReadControllerStateAsync(controllers[index], token)
                .ConfigureAwait(false);
            fields.AddText($"{prefix} operation state", state?.ToString() ?? "Not exposed");
        }

        int axisCount = 0;
        for (int index = 0; index < devices.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            AddTopologyNode(visited, devices[index], context.MaxTargets);
            MotionDeviceSnapshot device = await reader.ReadMotionDeviceAsync(devices[index], token)
                .ConfigureAwait(false);
            CellCompanionSupport.CheckCount(visited.Count + device.AxisIds.Count, context.MaxTargets, "Cell topology");
            string prefix = $"Motion device {index + 1}";
            AddIdentification(fields, prefix, device.Identification);
            fields.AddText($"{prefix} category", device.Category.ToString());
            fields.AddDataValue($"{prefix} speed override", device.SpeedOverride);
            fields.Add($"{prefix} axes", Variant.From(device.AxisIds));
            for (int axisIndex = 0; axisIndex < device.AxisIds.Count; axisIndex++)
            {
                token.ThrowIfCancellationRequested();
                AddTopologyNode(visited, device.AxisIds[axisIndex], context.MaxTargets);
                AxisSnapshot axis = await reader.ReadAxisAsync(device.AxisIds[axisIndex], token).ConfigureAwait(false);
                string axisPrefix = $"{prefix} axis {axisIndex + 1}";
                fields.Add($"{axisPrefix} node", Variant.From(axis.Identification.NodeId));
                fields.AddText($"{axisPrefix} name", axis.Identification.ComponentName.Text);
                fields.AddText($"{axisPrefix} motion profile", axis.MotionProfile.ToString());
                fields.AddDataValue($"{axisPrefix} position", axis.State.ActualPosition);
                fields.AddDataValue($"{axisPrefix} speed", axis.State.ActualSpeed);
                fields.AddDataValue($"{axisPrefix} acceleration", axis.State.ActualAcceleration);
                axisCount++;
            }
        }
        token.ThrowIfCancellationRequested();
        return new CompanionInspection(
            fields.ToArray(),
            [new CompanionOperation("snapshot", "Read fresh cell snapshot", CompanionOperationSafety.ReadOnly)],
            $"Read {controllers.Count} controller(s), {devices.Count} motion device(s) and {axisCount} axis/axes. " +
            "No motion, program, system-state or Robot Intent command was sent; " +
            "loads and drive trains are not traversed.");
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        cancellationToken.ThrowIfCancellationRequested();
        CellCompanionSupport.RequireNoInput(input);
        if (operationId != "snapshot")
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "Only a read-only cell snapshot is supported.");
        }
        CompanionInspection inspection = await InspectAsync(context, target, cancellationToken).ConfigureAwait(false);
        return new CompanionOperationResult(inspection.Summary, inspection.Values);
    }

    private void ValidateTarget(CompanionContext context, CompanionTarget target)
    {
        CellCompanionSupport.ValidateTarget(context, target, Descriptor.Id);
        if (target.TypeName != "MotionDeviceSystem")
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Select a MotionDeviceSystem instance.");
        }
    }

    private static void AddIdentification(
        CellCompanionFields fields,
        string prefix,
        RoboticsComponentIdentification identification)
    {
        fields.Add($"{prefix} node", Variant.From(identification.NodeId));
        fields.AddText($"{prefix} name", identification.ComponentName.Text);
        fields.Add($"{prefix} manufacturer", Variant.From(identification.Manufacturer));
        fields.Add($"{prefix} model", Variant.From(identification.Model));
    }

    private static void AddTopologyNode(HashSet<NodeId> visited, NodeId nodeId, int maximum)
    {
        if (nodeId.IsNull || !visited.Add(nodeId))
        {
            throw new ServiceResultException(
                StatusCodes.BadNodeIdInvalid, "Cell containment contains a null, repeated or cyclic instance.");
        }
        CellCompanionSupport.CheckCount(visited.Count, maximum, "Cell topology");
    }

    private readonly Func<CompanionContext, IRoboticsCompanionReader> m_createReader;
}

/// <summary>
/// Read-only seam for the sealed Robotics clients. It cannot issue robot commands or own a session.
/// </summary>
internal interface IRoboticsCompanionReader
{
    IAsyncEnumerable<MotionDeviceSystemEntry> EnumerateSystemsAsync(CancellationToken cancellationToken);

    ValueTask<ArrayOf<NodeId>> GetControllersAsync(NodeId system, CancellationToken cancellationToken);

    ValueTask<ArrayOf<NodeId>> GetMotionDevicesAsync(NodeId system, CancellationToken cancellationToken);

    Task<ControllerSnapshot> ReadControllerAsync(NodeId controller, CancellationToken cancellationToken);

    ValueTask<RoboticsOperationState?> ReadControllerStateAsync(NodeId controller, CancellationToken cancellationToken);

    Task<MotionDeviceSnapshot> ReadMotionDeviceAsync(NodeId device, CancellationToken cancellationToken);

    Task<AxisSnapshot> ReadAxisAsync(NodeId axis, CancellationToken cancellationToken);
}

/// <summary>
/// Composes focused client reads rather than the unbounded recursive full-system reader.
/// </summary>
internal sealed class RoboticsCompanionReader : IRoboticsCompanionReader
{
    public RoboticsCompanionReader(CompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        m_client = new RoboticsClient(context.Session, context.Telemetry);
    }

    public IAsyncEnumerable<MotionDeviceSystemEntry> EnumerateSystemsAsync(CancellationToken cancellationToken)
    {
        return m_client.EnumerateMotionDeviceSystemsAsync(cancellationToken);
    }

    public async ValueTask<ArrayOf<NodeId>> GetControllersAsync(NodeId system, CancellationToken cancellationToken)
    {
        var proxy = new MotionDeviceSystemTypeClient(m_client.Session, system, m_client.Telemetry);
        FolderTypeClient? folder = await proxy.GetControllersAsync(m_client.Telemetry, cancellationToken)
            .ConfigureAwait(false);
        return folder is null
            ? []
            : await m_client.DiscoverControllersAsync(folder.ObjectId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ArrayOf<NodeId>> GetMotionDevicesAsync(NodeId system, CancellationToken cancellationToken)
    {
        var proxy = new MotionDeviceSystemTypeClient(m_client.Session, system, m_client.Telemetry);
        FolderTypeClient? folder = await proxy.GetMotionDevicesAsync(m_client.Telemetry, cancellationToken)
            .ConfigureAwait(false);
        return folder is null
            ? []
            : await m_client.DiscoverMotionDevicesAsync(folder.ObjectId, cancellationToken).ConfigureAwait(false);
    }

    public Task<ControllerSnapshot> ReadControllerAsync(NodeId controller, CancellationToken cancellationToken)
    {
        return m_client.ReadControllerAsync(controller, cancellationToken);
    }

    public async ValueTask<RoboticsOperationState?> ReadControllerStateAsync(
        NodeId controller,
        CancellationToken cancellationToken)
    {
        var proxy = new ControllerTypeClient(m_client.Session, controller, m_client.Telemetry);
        SystemOperationTypeClient? operation = await proxy
            .GetSystemOperationAsync(m_client.Telemetry, cancellationToken).ConfigureAwait(false);
        if (operation is null)
        {
            return null;
        }
        return await new SystemOperationClient(m_client.Session, operation.ObjectId, m_client.Telemetry)
            .ReadStateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<MotionDeviceSnapshot> ReadMotionDeviceAsync(NodeId device, CancellationToken cancellationToken)
    {
        return m_client.ReadMotionDeviceAsync(device, cancellationToken);
    }

    public Task<AxisSnapshot> ReadAxisAsync(NodeId axis, CancellationToken cancellationToken)
    {
        return m_client.ReadAxisAsync(axis, cancellationToken);
    }

    private readonly RoboticsClient m_client;
}
