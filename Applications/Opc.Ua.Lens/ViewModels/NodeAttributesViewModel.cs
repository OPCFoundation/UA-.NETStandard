/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens.ViewModels;

/// <summary>
/// View model backing the per-node attribute panel.  When the address-space
/// tree selection changes, <see cref="LoadAsync"/> issues a single batched
/// <see cref="Opc.Ua.Client.ManagedSession.ReadAsync"/> for the attribute
/// IDs supported by the node's <see cref="NodeClass"/> and presents the
/// results as <see cref="AttributeRow"/>s, formatted for human reading.
/// </summary>
internal sealed partial class NodeAttributesViewModel : ObservableObject, IDisposable
{
    private readonly ConnectionService m_connection;
    private readonly ILogger m_log;
    private CancellationTokenSource? m_cts;

    public ObservableCollection<AttributeRow> Rows { get; } = new();

    [ObservableProperty]
    private string m_header = "(no node selected)";

    public NodeAttributesViewModel(ITelemetryContext telemetry, ConnectionService connection)
    {
        m_connection = connection;
        m_log = telemetry.CreateLogger("NodeAttributes");
    }

    public void Clear()
    {
        m_cts?.Cancel();
        Rows.Clear();
        Header = "(no node selected)";
    }

    public async Task LoadAsync(NodeId nodeId, NodeClass nodeClass)
    {
        m_cts?.Cancel();
        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;

        Header = $"{Glyph(nodeClass)} {nodeId}  ({nodeClass})";
        Rows.Clear();

        if (m_connection.Session is not { } session)
        {
            Rows.Add(new AttributeRow("(disconnected)", string.Empty));
            return;
        }

        IReadOnlyList<NodeAttributeSets.Entry> attrs = NodeAttributeSets.SupportedAttributes(nodeClass);
        if (attrs.Count == 0)
        {
            return;
        }

        var idList = new List<ReadValueId>(attrs.Count);
        foreach (NodeAttributeSets.Entry e in attrs)
        {
            idList.Add(new ReadValueId { NodeId = nodeId, AttributeId = e.AttributeId });
        }
        var ids = new ArrayOf<ReadValueId>(idList.ToArray());

        try
        {
            ReadResponse resp = await session.ReadAsync(null, 0, TimestampsToReturn.Neither, ids, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                return;
            }
            for (int i = 0; i < resp.Results.Count; i++)
            {
                DataValue dv = resp.Results[i];
                NodeAttributeSets.Entry entry = attrs[i];
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    // Security attributes are diagnostic — keep them visible
                    // even when the server reports them as not-applicable, so
                    // the user knows whether the node advertises role-based
                    // access control at all.
                    if (entry.AttributeId == Attributes.RolePermissions
                        || entry.AttributeId == Attributes.UserRolePermissions
                        || entry.AttributeId == Attributes.AccessRestrictions)
                    {
                        Rows.Add(new AttributeRow(entry.Name, $"(not supported: {dv.StatusCode})"));
                    }
                    continue;
                }
                string formatted = FormatValue(entry.AttributeId, dv);
                Rows.Add(new AttributeRow(entry.Name, formatted));
            }
            if (Rows.Count == 0)
            {
                Rows.Add(new AttributeRow("(no readable attributes)", string.Empty));
            }
        }
        catch (OperationCanceledException)
        {
            // Selection moved on — abandon this load.
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Failed to read attributes for {NodeId}", nodeId);
            Rows.Add(new AttributeRow("(read failed)", ex.Message));
        }
    }

    private static string FormatValue(uint attributeId, DataValue dv)
    {
        Variant v = dv.WrappedValue;
        if (v.IsNull)
        {
            return "(null)";
        }
        return attributeId switch
        {
            Attributes.NodeClass => FormatNodeClass(v),
            Attributes.AccessLevel
                or Attributes.UserAccessLevel => FormatAccessLevel(v),
            Attributes.AccessLevelEx => FormatAccessLevelEx(v),
            Attributes.EventNotifier => FormatEventNotifier(v),
            Attributes.ValueRank => FormatValueRank(v),
            Attributes.AccessRestrictions => FormatAccessRestrictions(v),
            Attributes.RolePermissions
                or Attributes.UserRolePermissions => FormatRolePermissions(v),
            _ => FormatGeneric(v)
        };
    }

    private static string FormatNodeClass(Variant v)
    {
        if (v.TryGetValue(out int i))
        {
            return ((NodeClass)i).ToString();
        }
        return FormatGeneric(v);
    }

    private static string FormatAccessLevel(Variant v)
    {
        if (!v.TryGetValue(out byte b))
        {
            return FormatGeneric(v);
        }
        var bits = new List<string>();
        if ((b & AccessLevels.CurrentRead) != 0)
        {
            bits.Add("CurrentRead");
        }

        if ((b & AccessLevels.CurrentWrite) != 0)
        {
            bits.Add("CurrentWrite");
        }

        if ((b & AccessLevels.HistoryRead) != 0)
        {
            bits.Add("HistoryRead");
        }

        if ((b & AccessLevels.HistoryWrite) != 0)
        {
            bits.Add("HistoryWrite");
        }

        if ((b & AccessLevels.SemanticChange) != 0)
        {
            bits.Add("SemanticChange");
        }

        if ((b & AccessLevels.StatusWrite) != 0)
        {
            bits.Add("StatusWrite");
        }

        if ((b & AccessLevels.TimestampWrite) != 0)
        {
            bits.Add("TimestampWrite");
        }

        return bits.Count == 0
            ? $"None (0x{b:X2})"
            : $"{string.Join(" | ", bits)} (0x{b:X2})";
    }

    private static string FormatAccessLevelEx(Variant v)
    {
        if (!v.TryGetValue(out uint u))
        {
            return FormatGeneric(v);
        }
        return $"0x{u:X8}";
    }

    private static string FormatEventNotifier(Variant v)
    {
        if (!v.TryGetValue(out byte b))
        {
            return FormatGeneric(v);
        }
        var bits = new List<string>();
        if ((b & EventNotifiers.SubscribeToEvents) != 0)
        {
            bits.Add("SubscribeToEvents");
        }

        if ((b & EventNotifiers.HistoryRead) != 0)
        {
            bits.Add("HistoryRead");
        }

        if ((b & EventNotifiers.HistoryWrite) != 0)
        {
            bits.Add("HistoryWrite");
        }

        return bits.Count == 0
            ? $"None (0x{b:X2})"
            : $"{string.Join(" | ", bits)} (0x{b:X2})";
    }

    private static string FormatValueRank(Variant v)
    {
        if (!v.TryGetValue(out int i))
        {
            return FormatGeneric(v);
        }
        return i switch
        {
            ValueRanks.ScalarOrOneDimension => $"ScalarOrOneDimension ({i})",
            ValueRanks.Any => $"Any ({i})",
            ValueRanks.Scalar => $"Scalar ({i})",
            ValueRanks.OneOrMoreDimensions => $"OneOrMoreDimensions ({i})",
            ValueRanks.OneDimension => $"OneDimension ({i})",
            ValueRanks.TwoDimensions => $"TwoDimensions ({i})",
            _ => i.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Format the <see cref="Attributes.AccessRestrictions"/> bit mask
    /// (UInt16) per OPC UA Part 3 §5.2.10.
    /// </summary>
    private static string FormatAccessRestrictions(Variant v)
    {
        ushort u;
        if (v.TryGetValue(out ushort s))
        {
            u = s;
        }
        else if (v.TryGetValue(out uint i))
        {
            u = (ushort)i;
        }
        else
        {
            return FormatGeneric(v);
        }
        var bits = new List<string>();
        if ((u & 0x01) != 0)
        {
            bits.Add("SigningRequired");
        }

        if ((u & 0x02) != 0)
        {
            bits.Add("EncryptionRequired");
        }

        if ((u & 0x04) != 0)
        {
            bits.Add("SessionRequired");
        }

        if ((u & 0x08) != 0)
        {
            bits.Add("ApplyRestrictionsToBrowse");
        }

        return bits.Count == 0
            ? $"None (0x{u:X4})"
            : $"{string.Join(" | ", bits)} (0x{u:X4})";
    }

    /// <summary>
    /// Format <see cref="Attributes.RolePermissions"/> / <see cref="Attributes.UserRolePermissions"/>:
    /// an array of <see cref="RolePermissionType"/> structures.  Each entry
    /// is rendered as <c>RoleName(NodeId) = Permission|Permission|…</c> with
    /// the role name resolved via the OPC UA well-known role IDs (Anonymous,
    /// Observer, …).  Unknown roles fall back to the raw NodeId.
    /// </summary>
    private static string FormatRolePermissions(Variant v)
    {
        // The Variant either decodes to RolePermissionType[] (preferred)
        // or to ExtensionObject[] (when the type isn't pre-registered).
        IList<RolePermissionType>? list = null;
        object? boxed = v.AsBoxedObject();
        if (boxed is RolePermissionType[] arr)
        {
            list = arr;
        }
        else if (boxed is IList<RolePermissionType> typed)
        {
            list = typed;
        }
        else if (boxed is ExtensionObject[] eos)
        {
            var parsed = new List<RolePermissionType>(eos.Length);
            foreach (ExtensionObject eo in eos)
            {
                if (eo.TryGetValue(out RolePermissionType? rpt) && rpt is not null)
                {
                    parsed.Add(rpt);
                }
            }
            list = parsed;
        }
        if (list is null || list.Count == 0)
        {
            return "(none)";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        int n = Math.Min(list.Count, 16);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append("; ");
            }

            RolePermissionType rpt = list[i];
            string role = RoleName(rpt.RoleId);
            string perms = PermissionsBits(rpt.Permissions);
            sb.Append(role).Append(" = ").Append(perms);
        }
        if (list.Count > n)
        {
            sb.Append("; …");
        }

        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Render a <see cref="PermissionType"/> bit mask as OR-joined names.</summary>
    private static string PermissionsBits(uint permissions)
    {
        if (permissions == 0)
        {
            return "None";
        }

        var bits = new List<string>();
        if ((permissions & (uint)PermissionType.Browse) != 0)
        {
            bits.Add("Browse");
        }

        if ((permissions & (uint)PermissionType.ReadRolePermissions) != 0)
        {
            bits.Add("ReadRolePermissions");
        }

        if ((permissions & (uint)PermissionType.WriteAttribute) != 0)
        {
            bits.Add("WriteAttribute");
        }

        if ((permissions & (uint)PermissionType.WriteRolePermissions) != 0)
        {
            bits.Add("WriteRolePermissions");
        }

        if ((permissions & (uint)PermissionType.WriteHistorizing) != 0)
        {
            bits.Add("WriteHistorizing");
        }

        if ((permissions & (uint)PermissionType.Read) != 0)
        {
            bits.Add("Read");
        }

        if ((permissions & (uint)PermissionType.Write) != 0)
        {
            bits.Add("Write");
        }

        if ((permissions & (uint)PermissionType.ReadHistory) != 0)
        {
            bits.Add("ReadHistory");
        }

        if ((permissions & (uint)PermissionType.InsertHistory) != 0)
        {
            bits.Add("InsertHistory");
        }

        if ((permissions & (uint)PermissionType.ModifyHistory) != 0)
        {
            bits.Add("ModifyHistory");
        }

        if ((permissions & (uint)PermissionType.DeleteHistory) != 0)
        {
            bits.Add("DeleteHistory");
        }

        if ((permissions & (uint)PermissionType.ReceiveEvents) != 0)
        {
            bits.Add("ReceiveEvents");
        }

        if ((permissions & (uint)PermissionType.Call) != 0)
        {
            bits.Add("Call");
        }

        if ((permissions & (uint)PermissionType.AddReference) != 0)
        {
            bits.Add("AddReference");
        }

        if ((permissions & (uint)PermissionType.RemoveReference) != 0)
        {
            bits.Add("RemoveReference");
        }

        if ((permissions & (uint)PermissionType.DeleteNode) != 0)
        {
            bits.Add("DeleteNode");
        }

        if ((permissions & (uint)PermissionType.AddNode) != 0)
        {
            bits.Add("AddNode");
        }

        return string.Join("|", bits);
    }

    /// <summary>
    /// Map a role NodeId to its well-known name (Anonymous, Observer, …).
    /// Returns the NodeId text for unknown roles.
    /// </summary>
    private static string RoleName(NodeId roleId)
    {
        if (roleId.IsNull)
        {
            return "(null)";
        }

        if (roleId == ObjectIds.WellKnownRole_Anonymous)
        {
            return "Anonymous";
        }

        if (roleId == ObjectIds.WellKnownRole_AuthenticatedUser)
        {
            return "AuthenticatedUser";
        }

        if (roleId == ObjectIds.WellKnownRole_Observer)
        {
            return "Observer";
        }

        if (roleId == ObjectIds.WellKnownRole_Operator)
        {
            return "Operator";
        }

        if (roleId == ObjectIds.WellKnownRole_Engineer)
        {
            return "Engineer";
        }

        if (roleId == ObjectIds.WellKnownRole_Supervisor)
        {
            return "Supervisor";
        }

        if (roleId == ObjectIds.WellKnownRole_ConfigureAdmin)
        {
            return "ConfigureAdmin";
        }

        if (roleId == ObjectIds.WellKnownRole_SecurityAdmin)
        {
            return "SecurityAdmin";
        }

        if (roleId == ObjectIds.WellKnownRole_TrustedApplication)
        {
            return "TrustedApplication";
        }

        return roleId.ToString() ?? "(unnamed)";
    }

    private static string FormatGeneric(Variant v)
    {
        object? boxed = v.AsBoxedObject();
        return boxed switch
        {
            null => "(null)",
            string s => s,
            LocalizedText l => l.Text ?? string.Empty,
            QualifiedName q => q.ToString() ?? string.Empty,
            Array a => FormatArray(a),
            _ => boxed.ToString() ?? string.Empty
        };
    }

    private static string FormatArray(Array a)
    {
        const int kMax = 16;
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        int n = Math.Min(a.Length, kMax);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(a.GetValue(i)?.ToString() ?? "null");
        }
        if (a.Length > kMax)
        {
            sb.Append(", …");
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static string Glyph(NodeClass cls) => cls switch
    {
        NodeClass.Object => "\u25C9",
        NodeClass.ObjectType => "\u25C7",
        NodeClass.Variable => "\u25CB",
        NodeClass.VariableType => "\u25CE",
        NodeClass.Method => "\u25B6",
        NodeClass.ReferenceType => "\u25E6",
        NodeClass.DataType => "\u25A1",
        NodeClass.View => "\u25A3",
        _ => "?"
    };

    public void Dispose()
    {
        m_cts?.Cancel();
        m_cts?.Dispose();
        m_cts = null;
    }
}

internal sealed record AttributeRow(string Name, string Value);
