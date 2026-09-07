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

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.ViewModels;

/// <summary>
/// One row of the Subscription tab's per-monitored-item status sub-pane.
/// Static columns (Id / BrowseName / NodeId / AttributeId) come straight
/// from the underlying <see cref="UaLens.Subscriptions.MonitoredItemConfig"/>;
/// the dynamic columns (Mode / Sampling / Queue / Samples / Last status /
/// Last value) are refreshed on a 250 ms throttle by
/// <see cref="SubscriptionViewModel"/> from the engine adapter's live stats
/// dictionary.
/// </summary>
internal sealed partial class MonitoredItemStatusRow : ObservableObject
{
    public int Id { get; }
    public string BrowseName { get; }
    public string NodeId { get; }
    public string AttributeName { get; }

    [ObservableProperty]
    private string m_mode;

    [ObservableProperty]
    private string m_sampling;

    [ObservableProperty]
    private string m_queue;

    [ObservableProperty]
    private string m_samples = "—";

    [ObservableProperty]
    private string m_lastStatus = "—";

    [ObservableProperty]
    private string m_lastValue = "—";

    public MonitoredItemStatusRow(UaLens.Subscriptions.MonitoredItemConfig config)
    {
        Id = config.Id;
        BrowseName = config.DisplayName;
        NodeId = config.NodeId.ToString();
        AttributeName = AttributeIdName(config.AttributeId);
        m_mode = config.MonitoringMode.ToString();
        m_sampling = string.Format(CultureInfo.InvariantCulture, "{0:0}ms",
            config.SamplingInterval.TotalMilliseconds);
        m_queue = config.QueueSize.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Map an OPC UA attribute id to its short human-readable name.  The
    /// stack exposes no public <c>Attributes.GetBrowseName(uint)</c>; this
    /// covers the standard attributes a monitored item is ever realistically
    /// configured against (Value plus the common metadata attributes), with
    /// a numeric fallback so the column is never empty.
    /// </summary>
    public static string AttributeIdName(uint attributeId)
    {
        return attributeId switch
        {
            Attributes.NodeId => "NodeId",
            Attributes.NodeClass => "NodeClass",
            Attributes.BrowseName => "BrowseName",
            Attributes.DisplayName => "DisplayName",
            Attributes.Description => "Description",
            Attributes.WriteMask => "WriteMask",
            Attributes.UserWriteMask => "UserWriteMask",
            Attributes.IsAbstract => "IsAbstract",
            Attributes.Symmetric => "Symmetric",
            Attributes.InverseName => "InverseName",
            Attributes.ContainsNoLoops => "ContainsNoLoops",
            Attributes.EventNotifier => "EventNotifier",
            Attributes.Value => "Value",
            Attributes.DataType => "DataType",
            Attributes.ValueRank => "ValueRank",
            Attributes.ArrayDimensions => "ArrayDimensions",
            Attributes.AccessLevel => "AccessLevel",
            Attributes.UserAccessLevel => "UserAccessLevel",
            Attributes.MinimumSamplingInterval => "MinimumSamplingInterval",
            Attributes.Historizing => "Historizing",
            Attributes.Executable => "Executable",
            Attributes.UserExecutable => "UserExecutable",
            Attributes.DataTypeDefinition => "DataTypeDefinition",
            Attributes.RolePermissions => "RolePermissions",
            Attributes.UserRolePermissions => "UserRolePermissions",
            Attributes.AccessRestrictions => "AccessRestrictions",
            Attributes.AccessLevelEx => "AccessLevelEx",
            _ => attributeId.ToString(CultureInfo.InvariantCulture)
        };
    }
}
