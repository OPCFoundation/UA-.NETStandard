// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.ViewModels
{
    /// <summary>
    /// One row of the Subscription tab's per-monitored-item status sub-pane.
    /// Static columns (Id / BrowseName / NodeId / AttributeId) come straight
    /// from the underlying <see cref="Subscriptions.MonitoredItemConfig"/>;
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
        public partial string Mode { get; set; }

        [ObservableProperty]
        public partial string Sampling { get; set; }

        [ObservableProperty]
        public partial string Queue { get; set; }

        [ObservableProperty]
        public partial string Samples { get; set; } = "—";

        [ObservableProperty]
        public partial string LastStatus { get; set; } = "—";

        [ObservableProperty]
        public partial string LastValue { get; set; } = "—";

        [ObservableProperty]
        public partial string SourceTimestamp { get; set; } = "--";

        [ObservableProperty]
        public partial string ServerTimestamp { get; set; } = "--";

        public MonitoredItemStatusRow(Subscriptions.MonitoredItemConfig config)
        {
            Id = config.Id;
            BrowseName = config.DisplayName;
            NodeId = config.NodeId.ToString();
            AttributeName = AttributeIdName(config.AttributeId);
            Mode = config.MonitoringMode.ToString();
            Sampling = string.Format(CultureInfo.InvariantCulture, "{0:0}ms",
                config.SamplingInterval.TotalMilliseconds);
            Queue = config.QueueSize.ToString(CultureInfo.InvariantCulture);
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
}
