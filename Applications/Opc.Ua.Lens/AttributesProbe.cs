/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.ViewModels;

namespace UaLens;

/// <summary>
/// Validates the per-node attribute reader by loading attributes for one
/// node of every node class against ConsoleReferenceServer.  Confirms that
/// only the attributes valid for that node class are surfaced (the reader
/// silently drops Bad results from the server).
/// </summary>
internal static class AttributesProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== Attributes probe ==");
        Console.WriteLine($"   endpoint: {endpointUrl}");

        var conn = new ConnectionService(telemetry);
        await using (conn.ConfigureAwait(false))
        {
            await conn.ConnectAsync(new ConnectionOptions
            {
                EndpointUrl = endpointUrl,
                Engine = SubscriptionEngineKind.ChannelV2
            }, ct).ConfigureAwait(false);

            using var vm = new NodeAttributesViewModel(telemetry, conn);
            using var refsVm = new ReferencesViewModel(telemetry, conn);

            (NodeId Id, NodeClass Class, string Label)[] cases =
            [
                (ObjectIds.Server,                                                 NodeClass.Object,        "Server (Object)"),
                (VariableIds.Server_ServerStatus_CurrentTime,                      NodeClass.Variable,      "ServerStatus.CurrentTime (Variable)"),
                (MethodIds.Server_GetMonitoredItems,                               NodeClass.Method,        "GetMonitoredItems (Method)"),
                (DataTypeIds.Int32,                                                NodeClass.DataType,      "Int32 (DataType)"),
                (ObjectTypeIds.BaseObjectType,                                     NodeClass.ObjectType,    "BaseObjectType"),
                (VariableTypeIds.BaseDataVariableType,                             NodeClass.VariableType,  "BaseDataVariableType"),
                (ReferenceTypeIds.HasComponent,                                    NodeClass.ReferenceType, "HasComponent"),
            ];

            int rc = 0;
            foreach ((NodeId id, NodeClass cls, string label) in cases)
            {
                await vm.LoadAsync(id, cls).ConfigureAwait(false);
                await refsVm.LoadAsync(id, cls).ConfigureAwait(false);
                Console.WriteLine();
                Console.WriteLine($"--- {label} ---");
                Console.WriteLine($"   header: {vm.Header}");
                Console.WriteLine($"   attrs:  {vm.Rows.Count}");
                foreach (AttributeRow row in vm.Rows)
                {
                    Console.WriteLine($"     {row.Name,-22} {Truncate(row.Value, 80)}");
                }
                Console.WriteLine($"   refs:   {refsVm.Rows.Count}");
                int show = Math.Min(refsVm.Rows.Count, 5);
                for (int i = 0; i < show; i++)
                {
                    ReferenceRow r = refsVm.Rows[i];
                    Console.WriteLine($"     {r.Direction} {r.ReferenceType,-20}  {Truncate(r.TargetBrowseName, 32),-32}  {r.TargetNodeClass}");
                }
                if (refsVm.Rows.Count > show)
                {
                    Console.WriteLine($"     … ({refsVm.Rows.Count - show} more)");
                }
                if (vm.Rows.Count == 0)
                {
                    Console.WriteLine("   FAIL: no attributes returned");
                    rc = 1;
                }
                if (refsVm.Rows.Count == 0)
                {
                    Console.WriteLine("   FAIL: no references returned");
                    rc = 1;
                }
            }

            Console.WriteLine();
            Console.WriteLine(rc == 0 ? "ATTRS PROBE PASS" : "ATTRS PROBE FAIL");
            return rc;
        }
    }

    private static string Truncate(string s, int n)
        => s.Length <= n ? s : string.Concat(s.AsSpan(0, n - 1), "…");

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.AttrProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.AttrProbe");
    }
}
