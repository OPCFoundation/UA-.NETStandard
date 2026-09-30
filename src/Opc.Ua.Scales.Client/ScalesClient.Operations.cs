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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.PackML;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using PackMLBrowseNames = Opc.Ua.PackML.BrowseNames;

namespace Opc.Ua.Scales.Client
{
    public sealed partial class ScalesClient
    {
        /// <summary>
        /// Reads the products of a scale's or scale system's production preset.
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<ScaleProductInfo>> ReadProductsAsync(
            NodeId owner,
            CancellationToken cancellationToken = default)
        {
            NodeId products = (await ResolvePathsAsync(
                owner,
                [[ScalesName(BrowseNames.ProductionPreset), ScalesName(BrowseNames.Products)]],
                cancellationToken).ConfigureAwait(false))[0];
            var result = new List<ScaleProductInfo>();
            await foreach (ReferenceDescription reference in BrowseChildrenAsync(products, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                var product = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                ArrayOf<NodeId> ids = await ResolvePathsAsync(
                    product,
                    [
                        [ScalesName(BrowseNames.ProductId)],
                        [ScalesName(BrowseNames.ProductName)],
                        [ScalesName(BrowseNames.ProductMode)]
                    ],
                    cancellationToken).ConfigureAwait(false);
                if (ids[0].IsNull)
                {
                    continue;
                }
                Variant[] v = await ReadValuesAsync(ids, cancellationToken).ConfigureAwait(false);
                result.Add(new ScaleProductInfo(
                    product,
                    AsString(v[0]) ?? string.Empty,
                    AsText(v[1]),
                    ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris),
                    v[2].TryGetValue(out bool processing) ? processing : null));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Reads the ids of the products in processing (<c>CurrentProducts</c>).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<string>> ReadCurrentProductsAsync(
            NodeId owner,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> ids = await ResolvePathsAsync(
                owner,
                [[ScalesName(BrowseNames.ProductionPreset), ScalesName(BrowseNames.CurrentProducts)]],
                cancellationToken).ConfigureAwait(false);
            Variant[] v = await ReadValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            return v[0].TryGetValue(out ArrayOf<string> current) && !current.IsNull ? current : [];
        }

        /// <summary>
        /// Calls <c>SelectProduct</c> (§7.7.6).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="productId">The product id.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask SelectProductAsync(NodeId owner, string productId, CancellationToken cancellationToken = default)
        {
            return CallPresetAsync(owner, BrowseNames.SelectProduct, productId, cancellationToken);
        }

        /// <summary>
        /// Calls <c>DeselectProduct</c> (§7.7.7).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="productId">The product id.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask DeselectProductAsync(NodeId owner, string productId, CancellationToken cancellationToken = default)
        {
            return CallPresetAsync(owner, BrowseNames.DeselectProduct, productId, cancellationToken);
        }

        /// <summary>
        /// Calls <c>SwitchProduct</c> (§7.7.8).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="productId">The product id to switch to.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask SwitchProductAsync(NodeId owner, string productId, CancellationToken cancellationToken = default)
        {
            return CallPresetAsync(owner, BrowseNames.SwitchProduct, productId, cancellationToken);
        }

        /// <summary>
        /// Calls <c>RemoveProduct</c> (§7.7.5).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="productId">The product id.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask RemoveProductAsync(NodeId owner, string productId, CancellationToken cancellationToken = default)
        {
            return CallPresetAsync(owner, BrowseNames.RemoveProduct, productId, cancellationToken);
        }

        /// <summary>
        /// Calls <c>AddProduct</c> (§7.7.4).
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="productName">The product name.</param>
        /// <param name="productId">The unique product id.</param>
        /// <param name="productType">
        /// The product type, or null for the owner's own product type.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The NodeId of the new product.</returns>
        public async ValueTask<NodeId> AddProductAsync(
            NodeId owner,
            string productName,
            string productId,
            NodeId productType = default,
            CancellationToken cancellationToken = default)
        {
            NodeId preset = await ResolveChildAsync(owner, ScalesName(BrowseNames.ProductionPreset), cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<Variant> outputs = await CallAsync(
                preset,
                ScalesName(BrowseNames.AddProduct),
                cancellationToken,
                Variant.From(productName),
                Variant.From(productId),
                Variant.From(productType)).ConfigureAwait(false);
            return outputs.Count > 0 && outputs[0].TryGetValue(out NodeId id) ? id : NodeId.Null;
        }

        private async ValueTask CallPresetAsync(NodeId owner, string method, string productId, CancellationToken ct)
        {
            NodeId preset = await ResolveChildAsync(owner, ScalesName(BrowseNames.ProductionPreset), ct).ConfigureAwait(false);
            if (preset.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "'{0}' has no production preset.", owner);
            }
            await CallAsync(preset, ScalesName(method), ct, Variant.From(productId)).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the <see cref="PackMLStateNumbers"/> value of the innermost
        /// active PackML state of a scale (<c>State</c>) or scale system
        /// (<c>SystemState</c>), or null when it publishes none.
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<uint?> ReadPackMLStateAsync(NodeId owner, CancellationToken cancellationToken = default)
        {
            QualifiedName root = await PackMLRootAsync(owner, cancellationToken).ConfigureAwait(false);
            QualifiedName currentState = new(Opc.Ua.BrowseNames.CurrentState);
            QualifiedName id = new(Opc.Ua.BrowseNames.Id);
            QualifiedName machine = PackMLName(PackMLBrowseNames.MachineState);
            QualifiedName execute = PackMLName(PackMLBrowseNames.ExecuteState);
            ArrayOf<NodeId> ids = await ResolvePathsAsync(
                owner,
                [
                    [root, machine, execute, currentState, id],
                    [root, machine, currentState, id],
                    [root, currentState, id]
                ],
                cancellationToken).ConfigureAwait(false);
            if (ids[2].IsNull)
            {
                return null;
            }
            Variant[] v = await ReadValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            foreach (Variant value in v)
            {
                if (value.TryGetValue(out NodeId stateId) && !stateId.IsNull && stateId.TryGetValue(out uint numeric))
                {
                    uint number = PackMLStateNumbers.FromStateId(numeric);
                    if (number != 0)
                    {
                        return number;
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// Invokes a PackML command on a scale's or scale system's state
        /// machine, calling the method on the machine level it belongs to.
        /// </summary>
        /// <param name="owner">The scale or scale system.</param>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask ExecutePackMLCommandAsync(
            NodeId owner,
            PackMLCommand command,
            CancellationToken cancellationToken = default)
        {
            QualifiedName root = await PackMLRootAsync(owner, cancellationToken).ConfigureAwait(false);
            QualifiedName machine = PackMLName(PackMLBrowseNames.MachineState);
            QualifiedName execute = PackMLName(PackMLBrowseNames.ExecuteState);
            QualifiedName[] path = command switch
            {
                PackMLCommand.Abort or PackMLCommand.Clear => [root],
                PackMLCommand.Stop => [root, machine],
                PackMLCommand.Reset => await ReadPackMLStateAsync(owner, cancellationToken).ConfigureAwait(false)
                    == PackMLStateNumbers.Complete
                        ? [root, machine, execute]
                        : [root, machine],
                _ => [root, machine, execute]
            };
            NodeId target = (await ResolvePathsAsync(owner, [path], cancellationToken).ConfigureAwait(false))[0];
            if (target.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "'{0}' has no PackML state machine.", owner);
            }
            Variant[] inputs = command == PackMLCommand.Start
                ? [Variant.From(new ArrayOf<ExtensionObject>())]
                : [];
            await CallAsync(target, PackMLName(command.ToString()), cancellationToken, inputs).ConfigureAwait(false);
        }

        private async ValueTask<QualifiedName> PackMLRootAsync(NodeId owner, CancellationToken ct)
        {
            NodeId state = await ResolveChildAsync(owner, ScalesName(BrowseNames.State), ct).ConfigureAwait(false);
            return state.IsNull ? ScalesName(BrowseNames.SystemState) : ScalesName(BrowseNames.State);
        }

        /// <summary>
        /// Calls an argument-less OPC 40200 method on a node: the loss-in-weight
        /// <c>DischargeStart</c>/<c>DischargeStop</c>/<c>RefillStart</c>/<c>RefillStop</c>,
        /// the laboratory <c>StartLeveling</c>/<c>StartCalibration</c>/<c>StartIonisator</c>/<c>StopIonisator</c>,
        /// a totalizer's <c>ResetTotalizer</c> or a system's
        /// <c>ResetGlobalStatistics</c>.
        /// </summary>
        /// <param name="target">The object that owns the method.</param>
        /// <param name="method">A generated <see cref="BrowseNames"/> constant.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask InvokeAsync(NodeId target, string method, CancellationToken cancellationToken = default)
        {
            await CallAsync(target, ScalesName(method), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>SetReferencePieceWeight</c> (§7.27.4).
        /// </summary>
        /// <param name="scale">The piece-counting scale.</param>
        /// <param name="referencePieceWeight">The weight of one reference piece.</param>
        /// <param name="engineeringUnits">The weight's unit.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetReferencePieceWeightAsync(
            NodeId scale,
            uint referencePieceWeight,
            EUInformation engineeringUnits,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                scale,
                ScalesName(BrowseNames.SetReferencePieceWeight),
                cancellationToken,
                Variant.From(referencePieceWeight),
                Variant.From(new ExtensionObject(engineeringUnits))).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>SetNumberOfReferencePieces</c> (§7.27.5).
        /// </summary>
        /// <param name="scale">The piece-counting scale.</param>
        /// <param name="numberOfReferencePieces">The number of reference pieces.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetNumberOfReferencePiecesAsync(
            NodeId scale,
            uint numberOfReferencePieces,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                scale,
                ScalesName(BrowseNames.SetNumberOfReferencePieces),
                cancellationToken,
                Variant.From(numberOfReferencePieces)).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>StartReference</c> (§7.27.6).
        /// </summary>
        /// <param name="scale">The piece-counting scale.</param>
        /// <param name="numberOfReferencePieces">The number of pieces on the scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask StartReferenceAsync(
            NodeId scale,
            uint numberOfReferencePieces,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                scale,
                ScalesName(BrowseNames.StartReference),
                cancellationToken,
                Variant.From(numberOfReferencePieces)).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>OpenDraftShields</c> or <c>CloseDraftShields</c>.
        /// </summary>
        /// <param name="scale">The laboratory scale.</param>
        /// <param name="shield">The shield(s).</param>
        /// <param name="close">Whether to close rather than open.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetDraftShieldsAsync(
            NodeId scale,
            DraftShieldType shield,
            bool close,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                scale,
                ScalesName(close ? BrowseNames.CloseDraftShields : BrowseNames.OpenDraftShields),
                cancellationToken,
                Variant.From((int)shield)).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls one of the vehicle weighing methods
        /// (<c>InboundWeighing</c>, <c>OutboundWeighing</c>,
        /// <c>OnePassWeighing</c>, §7.48.4 - §7.48.6).
        /// </summary>
        /// <param name="scale">The vehicle scale.</param>
        /// <param name="method">The method's generated <see cref="BrowseNames"/> constant.</param>
        /// <param name="vehicleId">The vehicle id.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask WeighVehicleAsync(
            NodeId scale,
            string method,
            string vehicleId,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(method), cancellationToken, Variant.From(vehicleId)).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>SetFeederSpeed</c> on a feeder module (§7.1.4).
        /// </summary>
        /// <param name="feeder">The feeder module.</param>
        /// <param name="feederSpeed">The target speed.</param>
        /// <param name="engineeringUnits">The speed's unit.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetFeederSpeedAsync(
            NodeId feeder,
            float feederSpeed,
            EUInformation engineeringUnits,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                feeder,
                ScalesName(BrowseNames.SetFeederSpeed),
                cancellationToken,
                Variant.From(feederSpeed),
                Variant.From(new ExtensionObject(engineeringUnits))).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls one of the recipe processing methods of a recipe scale
        /// (<c>StartRecipe</c>, <c>StopRecipe</c>, <c>ContinueRecipe</c>,
        /// <c>SkipCurrentRecipeElement</c>, <c>AbortRecipe</c>).
        /// </summary>
        /// <param name="scale">The recipe scale.</param>
        /// <param name="method">The method's generated <see cref="BrowseNames"/> constant.</param>
        /// <param name="recipe">The recipe node.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask ProcessRecipeAsync(
            NodeId scale,
            string method,
            NodeId recipe,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(method), cancellationToken, Variant.From(recipe)).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>AddRecipe</c> on a recipe scale's recipe management.
        /// </summary>
        /// <param name="scale">The recipe scale.</param>
        /// <param name="recipeId">The unique recipe id.</param>
        /// <param name="recipeName">The recipe name.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The NodeId of the new recipe.</returns>
        public async ValueTask<NodeId> AddRecipeAsync(
            NodeId scale,
            string recipeId,
            LocalizedText recipeName,
            CancellationToken cancellationToken = default)
        {
            NodeId recipes = await ResolveChildAsync(scale, ScalesName(BrowseNames.Recipes), cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<Variant> outputs = await CallAsync(
                recipes,
                ScalesName(BrowseNames.AddRecipe),
                cancellationToken,
                Variant.From(recipeId),
                Variant.From(recipeName)).ConfigureAwait(false);
            return outputs.Count > 0 && outputs[0].TryGetValue(out NodeId id) ? id : NodeId.Null;
        }

        /// <summary>
        /// Calls <c>AddRecipeElement</c> on a recipe (§7.31.4).
        /// </summary>
        /// <param name="recipe">The recipe.</param>
        /// <param name="elementType">The element type.</param>
        /// <param name="elementName">The element name.</param>
        /// <param name="previousElements">The predecessors; the recipe itself for a start element.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The NodeId of the new element.</returns>
        public async ValueTask<NodeId> AddRecipeElementAsync(
            NodeId recipe,
            NodeId elementType,
            string elementName,
            IReadOnlyList<NodeId> previousElements,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<Variant> outputs = await CallAsync(
                recipe,
                ScalesName(BrowseNames.AddRecipeElement),
                cancellationToken,
                Variant.From(elementType),
                Variant.From(elementName),
                Variant.From(previousElements.ToArray().ToArrayOf())).ConfigureAwait(false);
            return outputs.Count > 0 && outputs[0].TryGetValue(out NodeId id) ? id : NodeId.Null;
        }

        /// <summary>
        /// Builds the event filter that selects OPC 40200 scale events and
        /// alarms with the fields <see cref="Decode"/> reads.
        /// </summary>
        public EventFilter CreateNotificationFilter()
        {
            NodeId baseEventType = Opc.Ua.ObjectTypeIds.BaseEventType;
            SimpleAttributeOperand Field(params QualifiedName[] path) => new()
            {
                TypeDefinitionId = baseEventType,
                BrowsePath = path.ToArrayOf(),
                AttributeId = Attributes.Value,
                IndexRange = null
            };
            var filter = new EventFilter
            {
                SelectClauses = new[]
                {
                    Field(new QualifiedName(Opc.Ua.BrowseNames.SourceNode)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.SourceName)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.Time)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.Severity)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.Message)),
                    Field(ScalesName(BrowseNames.NotificationCategory)),
                    Field(ScalesName(BrowseNames.NotificationId)),
                    Field(ScalesName(BrowseNames.VendorNotificationId)),
                    Field(ScalesName(BrowseNames.AuxParameters)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.EventType)),
                    Field(new QualifiedName(Opc.Ua.BrowseNames.ActiveState), new QualifiedName(Opc.Ua.BrowseNames.Id))
                }.ToArrayOf()
            };
            // Push inserts at the front and renumbers the element operands, so
            // the Or pushed last becomes the root the server evaluates.
            var where = new ContentFilter();
            ContentFilterElement ofEvent = where.Push(
                FilterOperator.OfType,
                Variant.From(ScalesTypeId(ObjectTypes.ScaleEventType)));
            ContentFilterElement ofAlarm = where.Push(
                FilterOperator.OfType,
                Variant.From(ScalesTypeId(ObjectTypes.ScaleAlarmType)));
            where.Push(FilterOperator.Or, Variant.FromStructure(ofEvent), Variant.FromStructure(ofAlarm));
            filter.WhereClause = where;
            return filter;
        }

        /// <summary>
        /// Decodes the fields of a notification selected with
        /// <see cref="CreateNotificationFilter"/>.
        /// </summary>
        /// <param name="fields">The event fields.</param>
        public ScaleNotificationInfo Decode(ArrayOf<Variant> fields)
        {
            Variant Get(int ii) => ii < fields.Count ? fields[ii] : Variant.Null;
            NodeId eventType = Get(9).TryGetValue(out NodeId type) ? type : NodeId.Null;
            bool isAlarm = eventType == ScalesTypeId(ObjectTypes.ScaleAlarmType) ||
                Get(10).TryGetValue(out bool _);
            return new ScaleNotificationInfo
            {
                SourceNode = Get(0).TryGetValue(out NodeId source) ? source : NodeId.Null,
                SourceName = AsString(Get(1)),
                Time = Get(2).TryGetValue(out DateTimeUtc time) ? (DateTime)time : DateTime.MinValue,
                Severity = Get(3).TryGetValue(out ushort severity) ? severity : (ushort)0,
                Message = AsText(Get(4)),
                Category = Get(5).TryGetValue(out uint category) ? (ScaleNotificationCategory)category : ScaleNotificationCategory.Others,
                NotificationId = Get(6).TryGetValue(out uint id) ? id : 0,
                VendorNotificationId = AsString(Get(7)),
                AuxParameters = Get(8).TryGetValue(out ArrayOf<string> aux) && !aux.IsNull ? aux : [],
                IsAlarm = isAlarm,
                Active = isAlarm ? Get(10).TryGetValue(out bool active) && active : null
            };
        }

        /// <summary>
        /// Streams the OPC 40200 events and alarms a scale, scale system or
        /// module reports.
        /// </summary>
        /// <param name="source">The notifier: a scale, scale system or module.</param>
        /// <param name="streaming">
        /// The streaming subscription, or null for the session's default
        /// (which requires a <see cref="ManagedSession"/>).
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<ScaleNotificationInfo> ObserveNotificationsAsync(
            NodeId source,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EventFilter filter = CreateNotificationFilter();
            await foreach (EventNotification notification in (streaming ?? GetDefaultStreaming(Session))
                .SubscribeEventsAsync(source, filter, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return Decode(notification.Fields);
            }
        }

        /// <summary>
        /// Reads everything a client typically wants to know about a scale in
        /// one pass.
        /// </summary>
        /// <param name="scale">
        /// The scale. A scale system publishes no weight of its own; read its
        /// scales from <see cref="EnumerateSystemScalesAsync"/> instead.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ArgumentException">The entry is a scale system.</exception>
        public async ValueTask<ScaleSnapshot> ReadSnapshotAsync(ScaleEntry scale, CancellationToken cancellationToken = default)
        {
            if (scale == null)
            {
                throw new ArgumentNullException(nameof(scale));
            }
            if (scale.IsScaleSystem)
            {
                throw new ArgumentException(
                    "A scale system publishes no weight of its own; read the snapshots of its scales.",
                    nameof(scale));
            }
            return new ScaleSnapshot
            {
                Scale = scale,
                Identification = await ReadIdentificationAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                CurrentWeight = await ReadCurrentWeightAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                RegisteredWeight = await ReadRegisteredWeightAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                WeighingRanges = await ReadWeighingRangesAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                AllowedEngineeringUnits = await ReadAllowedEngineeringUnitsAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                Products = await ReadProductsAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                CurrentProducts = await ReadCurrentProductsAsync(scale.NodeId, cancellationToken).ConfigureAwait(false),
                PackMLState = await ReadPackMLStateAsync(scale.NodeId, cancellationToken).ConfigureAwait(false)
            };
        }
    }
}
