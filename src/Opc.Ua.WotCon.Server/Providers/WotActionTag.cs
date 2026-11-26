// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Describes a single WoT action surfaced as an OPC UA
    /// <c>MethodNode</c> on an <c>IWoTAssetType</c> instance per
    /// OPC 10100-1 §6.3.9.
    /// </summary>
    public sealed class WotActionTag
    {
        /// <summary>
        /// Initialises a new <see cref="WotActionTag"/>.
        /// </summary>
        public WotActionTag(
            string name,
            NodeId nodeId,
            IReadOnlyList<Argument> inputArguments,
            IReadOnlyList<Argument> outputArguments,
            JsonElement? form)
            : this(name, nodeId, inputArguments, outputArguments, form, null, null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="WotActionTag"/>.
        /// </summary>
        public WotActionTag(
            string name,
            NodeId nodeId,
            IReadOnlyList<Argument> inputArguments,
            IReadOnlyList<Argument> outputArguments,
            JsonElement? form,
            string? conditionAction,
            string? actsOn)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NodeId = nodeId;
            InputArguments = inputArguments ?? throw new ArgumentNullException(nameof(inputArguments));
            OutputArguments = outputArguments ?? throw new ArgumentNullException(nameof(outputArguments));
            Form = form;
            ConditionAction = conditionAction;
            ActsOn = actsOn;
        }

        /// <summary>
        /// The WoT action name (used as <c>BrowseName</c>).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The <c>NodeId</c> of the materialised OPC UA method.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Input arguments derived from the action's <c>input</c> JSON schema.
        /// </summary>
        public IReadOnlyList<Argument> InputArguments { get; }

        /// <summary>
        /// Output arguments derived from the action's <c>output</c> JSON schema.
        /// </summary>
        public IReadOnlyList<Argument> OutputArguments { get; }

        /// <summary>
        /// Optional OPC UA Condition Method named by <c>uav:conditionAction</c>.
        /// </summary>
        public string? ConditionAction { get; }

        /// <summary>
        /// Optional event affordance named by <c>uav:actsOn</c>.
        /// </summary>
        public string? ActsOn { get; }

        /// <summary>
        /// Raw protocol-binding form (as parsed JSON). Providers cast or
        /// re-parse this element to read their specific metadata.
        /// </summary>
        public JsonElement? Form { get; }
    }
}
