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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Scales;
using UaLens.Connection;
using DataTypeIds = Opc.Ua.DataTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class WeighingCompanionProvider
    {
        private static Command FindCommand(ScaleEntry scale, string id)
        {
            foreach (Command command in s_commands)
            {
                if (command.Id == id && command.Applies(scale))
                {
                    return command;
                }
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "This command is not offered for the selected real scale kind.");
        }

        private async ValueTask<WeighingSimulationGrant> AuthorizeSimulationAsync(
            CompanionContext context, CompanionTarget target, string operationId, CancellationToken cancellationToken)
        {
            EndpointDescription endpoint = context.Session.Endpoint;
            if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrWhiteSpace(endpoint.SecurityPolicyUri) ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new UnauthorizedAccessException("Weighing commands require a signed and encrypted channel.");
            }
            if (m_simulationPolicy is null)
            {
                throw new UnauthorizedAccessException(
                    "No verified owned-simulation policy is configured. Physical scale operations are excluded.");
            }
            WeighingSimulationGrant grant = await m_simulationPolicy.AuthorizeAsync(
                context, target, operationId, cancellationToken).ConfigureAwait(false);
            if (grant is null ||
                grant.RunId == Guid.Empty ||
                string.IsNullOrWhiteSpace(grant.Revision) ||
                grant.Revision.Length > 128)
            {
                throw new UnauthorizedAccessException("The owned-simulation policy returned no valid run evidence.");
            }
            RequireUnexpiredGrant(grant, m_timeProvider);
            return grant;
        }

        private static void RequireUnexpiredGrant(WeighingSimulationGrant grant, TimeProvider timeProvider)
        {
            if (timeProvider.GetUtcNow() >= grant.ExpiresAt)
            {
                throw new UnauthorizedAccessException(
                    "The verified owned-simulation grant has expired. Prepare a newly authorized request.");
            }
        }

        private static async ValueTask<MethodAvailability> ReadAvailabilityAsync(
            CompanionContext context, NodeId scale, Command command, CancellationToken cancellationToken)
        {
            NodeId owner = command.ParentName is { } parent
                ? await ResolveAsync(context, scale, parent, cancellationToken).ConfigureAwait(false)
                : scale;
            if (owner.IsNull)
            {
                return new MethodAvailability(owner, default, StatusCodes.BadNotFound, "Not exposed", false);
            }
            NodeId method = await ResolveAsync(context, owner, command.MethodName, cancellationToken)
                .ConfigureAwait(false);
            if (method.IsNull)
            {
                return new MethodAvailability(owner, method, StatusCodes.BadNotFound, "Not exposed", false);
            }
            ArrayOf<DataValue> permissions = await ReadAsync(context,
                [
                    new ReadValueId { NodeId = method, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.Executable },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.UserExecutable }
                ], cancellationToken).ConfigureAwait(false);
            foreach (DataValue permission in permissions)
            {
                if (!StatusCode.IsGood(permission.StatusCode))
                {
                    return new MethodAvailability(owner, method, permission.StatusCode,
                        "Permission read: " + permission.StatusCode, false);
                }
            }
            if (!permissions[0].WrappedValue.TryGetValue(out int nodeClass) ||
                nodeClass != (int)NodeClass.Method ||
                !permissions[1].WrappedValue.TryGetValue(out bool executable) ||
                !permissions[2].WrappedValue.TryGetValue(out bool userExecutable))
            {
                return new MethodAvailability(owner, method, StatusCodes.BadTypeMismatch,
                    "Invalid method or permission attribute type", false);
            }
            return !userExecutable
                ? new MethodAvailability(owner, method, StatusCodes.BadUserAccessDenied, "Not executable by this user",
                    false)
                : !executable
                    ? new MethodAvailability(owner, method, StatusCodes.BadInvalidState, "Not executable in this state",
                        false)
                    : new MethodAvailability(
                        owner, method, StatusCodes.Good, "Executable; owned simulation only", true);
        }

        private static async ValueTask<MethodBinding> ReadBindingAsync(
            CompanionContext context, NodeId scale, Command command, CancellationToken cancellationToken)
        {
            MethodAvailability availability = await ReadAvailabilityAsync(
                context, scale, command, cancellationToken).ConfigureAwait(false);
            if (!availability.CanExecute)
            {
                throw new ServiceResultException(
                    availability.Status == StatusCodes.BadNotFound ? StatusCodes.BadNotSupported : availability.Status,
                    availability.Reason);
            }
            NodeId ownerType = await ReadTypeAsync(context, availability.Owner, cancellationToken)
                .ConfigureAwait(false);
            ExpandedNodeId expected = command.Family switch
            {
                CommandFamily.Product => Scales.ObjectTypeIds.ProductionPresetType,
                CommandFamily.RecipeManagement => Scales.ObjectTypeIds.RecipeManagementType,
                _ => default
            };
            if (!expected.IsNull)
            {
                await RequireObjectTypeAsync(context, availability.Owner, expected, cancellationToken)
                    .ConfigureAwait(false);
            }
            string inputs = await ReadSignatureAsync(context, availability.Method, Opc.Ua.BrowseNames.InputArguments,
                command.ArgumentTypes, cancellationToken).ConfigureAwait(false);
            string outputs = await ReadSignatureAsync(context, availability.Method, Opc.Ua.BrowseNames.OutputArguments,
                command.ReturnsNode ? [DataTypeIds.NodeId] : [], cancellationToken).ConfigureAwait(false);
            return new MethodBinding(availability.Owner, ownerType, availability.Method, inputs, outputs);
        }

        private static async ValueTask<string> ReadSignatureAsync(
            CompanionContext context, NodeId method, string name, ArrayOf<NodeId> expected,
            CancellationToken cancellationToken)
        {
            NodeId node = await ResolveAsync(context, method, name, cancellationToken, Opc.Ua.Namespaces.OpcUa)
                .ConfigureAwait(false);
            if (node.IsNull)
            {
                return expected.Count == 0 ? "Absent" :
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Missing method argument metadata.");
            }
            ArrayOf<DataValue> read = await ReadAsync(context,
                [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }], cancellationToken)
                .ConfigureAwait(false);
            RequireGood(read[0].StatusCode, "Method argument metadata");
            if (!read[0].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> arguments) ||
                arguments.IsNull ||
                arguments.Count != expected.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The typed method signature changed.");
            }
            var signature = new StringBuilder(node.ToString());
            for (int index = 0; index < arguments.Count; index++)
            {
                if (!Variant.From(arguments[index]).TryGetValue<Argument>(
                    out Argument? argument, context.Session.MessageContext) ||
                    argument is null ||
                    argument.DataType != expected[index] ||
                    argument.ValueRank != ValueRanks.Scalar ||
                    argument.ArrayDimensions.Count != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch, "The typed method signature changed.");
                }
                RequireText(argument.Name, 256, "Method argument name");
                signature.Append('|').Append(argument.Name).Append(':').Append(argument.DataType);
            }
            return signature.ToString();
        }

        private static Payload ParseInput(Command command, ArrayOf<CompanionValue> inputs)
        {
            if (inputs.Count != command.Inputs.Count)
            {
                throw new ArgumentException("Supply the exact weighing form fields.", nameof(inputs));
            }
            string identifier = string.Empty;
            string name = string.Empty;
            LocalizedText recipeName = default;
            string unitNamespace = string.Empty;
            int unitId = -1;
            double number = 0;
            uint count = 0;
            for (int index = 0; index < inputs.Count; index++)
            {
                CompanionValue input = inputs[index];
                CompanionInputDefinition definition = command.Inputs[index];
                if (input.Name != definition.Name ||
                    !StatusCode.IsGood(input.Quality) ||
                    !input.Value.TypeInfo.IsScalar ||
                    input.Value.TypeInfo.BuiltInType != definition.DataType)
                {
                    throw new ArgumentException("Weighing form fields have incorrect names or types.", nameof(inputs));
                }
                switch (input.Name)
                {
                    case "productId":
                    case "recipeId":
                    case "vehicleId":
                        if (!input.Value.TryGetValue(out identifier))
                        {
                            throw new ArgumentException("An identifier string is required.", nameof(inputs));
                        }
                        RequireText(identifier, 128, "Weighing identifier");
                        break;
                    case "productName":
                        input.Value.TryGetValue(out name);
                        RequireText(name, 256, "Product name");
                        break;
                    case "recipeName":
                        input.Value.TryGetValue(out recipeName);
                        RequireText(recipeName.Text, 256, "Recipe name");
                        RequireText(recipeName.Locale, 32, "Recipe locale", allowEmpty: true);
                        break;
                    case "unitNamespace":
                        input.Value.TryGetValue(out unitNamespace);
                        RequireText(unitNamespace, 2048, "Unit namespace");
                        if (!Uri.TryCreate(unitNamespace, UriKind.Absolute, out Uri? uri) ||
                            !string.IsNullOrEmpty(uri.UserInfo))
                        {
                            throw new ArgumentException("An absolute engineering-unit namespace is required.");
                        }
                        break;
                    case "unitId":
                        input.Value.TryGetValue(out unitId);
                        if (unitId < 0)
                        {
                            throw new ArgumentException("A known nonnegative unit identifier is required.");
                        }
                        break;
                    case "presetTare":
                        input.Value.TryGetValue(out number);
                        if (!double.IsFinite(number) || number < 0)
                        {
                            throw new ArgumentException("Preset tare must be finite and nonnegative.");
                        }
                        break;
                    case "pieceWeight":
                    case "count":
                        input.Value.TryGetValue(out count);
                        if (count is 0 or > 1_000_000)
                        {
                            throw new ArgumentException("The reference value must be in 1–1000000.");
                        }
                        break;
                }
            }
            return new Payload(identifier, name, recipeName, number, count, unitNamespace, unitId);
        }

        private static void AddEvidence(
            CompanionContext context, List<StateValue> evidence, string name, Field field)
        {
            CellCompanionSupport.CheckCount(evidence.Count + 1, context.MaxFields, "Weighing preparation fields");
            evidence.Add(new StateValue(name, field.NodeId, field.Status,
                DataValueCodec.Snapshot(field.Value, context.Session.MessageContext)));
        }

        private static CompanionInputDefinition TextInput(string name, string label)
        {
            return new CompanionInputDefinition(name, label, BuiltInType.String, "1–128 characters.");
        }

        private enum CommandFamily
        {
            Weight,
            Product,
            RecipeManagement,
            RecipeRun,
            Pieces,
            Vehicle,
            System
        }

        private sealed record Command(
            string Id,
            string MethodName,
            ExpandedNodeId DeclarationId,
            CommandFamily Family,
            ArrayOf<CompanionInputDefinition> Inputs,
            ArrayOf<NodeId> ArgumentTypes,
            bool ReturnsNode = false)
        {
            public string? ParentName => Family switch
            {
                CommandFamily.Product => Scales.BrowseNames.ProductionPreset,
                CommandFamily.RecipeManagement => Scales.BrowseNames.Recipes,
                _ => null
            };

            public CompanionOperation Operation => new(
                Id, MethodName + " (owned simulation)", CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = Inputs.IsNull ? [] : Inputs
            };

            public bool Applies(ScaleEntry scale)
            {
                return Family switch
                {
                    CommandFamily.Product => true,
                    CommandFamily.System => scale.IsScaleSystem,
                    CommandFamily.RecipeManagement or CommandFamily.RecipeRun => scale.Kind == ScaleKind.Recipe,
                    CommandFamily.Pieces => scale.Kind == ScaleKind.PieceCounting,
                    CommandFamily.Vehicle => scale.Kind == ScaleKind.Vehicle,
                    _ => !scale.IsScaleSystem
                };
            }
        }

        private sealed record MethodAvailability(
            NodeId Owner, NodeId Method, StatusCode Status, string Reason, bool CanExecute);

        private sealed record MethodBinding(
            NodeId Owner, NodeId OwnerType, NodeId Method, string InputSignature, string OutputSignature);

        private sealed record Payload(
            string Identifier, string Name, LocalizedText RecipeName, double Number, uint Count,
            string UnitNamespace, int UnitId)
        {
            public string Review => string.Create(CultureInfo.InvariantCulture,
                $"Identifier: {Identifier}; name: {Name}{RecipeName.Text}; value: {Number}; " +
                $"reference value: {Count}; unit: {UnitNamespace} ({UnitId}).");
        }

        private sealed record UnitValue(string NamespaceUri, int UnitId, LocalizedText Name, LocalizedText Description)
        {
            public EUInformation ToValue()
            {
                return new EUInformation
                {
                    NamespaceUri = NamespaceUri,
                    UnitId = UnitId,
                    DisplayName = Name,
                    Description = Description
                };
            }
        }

        private sealed record StateValue(string Name, NodeId Node, StatusCode Quality, Variant Value);

        private sealed record CommandEvidence(
            NodeId ScaleType, MethodBinding Binding, NodeId Subject, UnitValue? Unit, ArrayOf<StateValue> Values)
        {
            public bool Matches(CommandEvidence other)
            {
                if (ScaleType != other.ScaleType ||
                    Binding != other.Binding ||
                    Subject != other.Subject ||
                    Unit != other.Unit ||
                    Values.Count != other.Values.Count)
                {
                    return false;
                }
                for (int index = 0; index < Values.Count; index++)
                {
                    if (Values[index] != other.Values[index])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private sealed class SessionStamp
        {
            public SessionStamp(CompanionContext context)
            {
                m_session = context.Session;
                m_sessionId = context.Session.SessionId;
                m_identity = context.Session.Identity;
                m_namespaces = context.Session.NamespaceUris.ToArray();
                m_messageContext = context.Session.MessageContext;
                EndpointDescription endpoint = context.Session.Endpoint;
                m_endpoint = endpoint.EndpointUrl ??
                    throw new InvalidOperationException("The weighing session has no endpoint identity.");
                m_applicationUri = endpoint.Server?.ApplicationUri;
                m_securityMode = endpoint.SecurityMode;
                m_securityPolicy = endpoint.SecurityPolicyUri;
                m_certificate = endpoint.ServerCertificate.Copy();
            }

            public void RequireMatch(CompanionContext context)
            {
                EndpointDescription endpoint = context.Session.Endpoint;
                if (!ReferenceEquals(context.Session, m_session) ||
                    !context.Session.Connected ||
                    context.Session.SessionId != m_sessionId ||
                    !ReferenceEquals(context.Session.Identity, m_identity) ||
                    !ReferenceEquals(context.Session.MessageContext, m_messageContext) ||
                    !m_namespaces.Span.SequenceEqual(context.Session.NamespaceUris.ToArray()) ||
                    endpoint.EndpointUrl != m_endpoint ||
                    endpoint.Server?.ApplicationUri != m_applicationUri ||
                    endpoint.SecurityMode != m_securityMode ||
                    endpoint.SecurityPolicyUri != m_securityPolicy ||
                    endpoint.ServerCertificate != m_certificate)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The weighing session, identity or namespace metadata changed. Prepare again.");
                }
            }

            private readonly ISession m_session;
            private readonly NodeId m_sessionId;
            private readonly IUserIdentity m_identity;
            private readonly IServiceMessageContext m_messageContext;
            private readonly ArrayOf<string> m_namespaces;
            private readonly string m_endpoint;
            private readonly string? m_applicationUri;
            private readonly MessageSecurityMode m_securityMode;
            private readonly string? m_securityPolicy;
            private readonly ByteString m_certificate;
        }

        private static readonly CompanionInputDefinition s_productId = TextInput("productId", "Product ID");
        private static readonly CompanionInputDefinition s_recipeId = TextInput("recipeId", "Recipe ID");
        private static readonly CompanionInputDefinition s_vehicleId = TextInput("vehicleId", "Vehicle ID");

        private static readonly CompanionInputDefinition s_count = new(
            "count", "Reference pieces", BuiltInType.UInt32, "1–1000000; no implicit sampling or calibration.");

        private static readonly ArrayOf<CompanionInputDefinition> s_unitInputs =
        [
            new("unitNamespace", "Engineering-unit namespace", BuiltInType.String,
                "Copy the current weight's engineering-unit namespace; no conversion is performed."),
            new("unitId", "Engineering-unit ID", BuiltInType.Int32, "Copy the current weight's unit ID.")
        ];

        private static readonly ArrayOf<Command> s_commands =
        [
            new("set-zero", Scales.BrowseNames.SetZero, Scales.MethodIds.ScaleDeviceType_SetZero,
                CommandFamily.Weight, [], []),
            new("set-tare", Scales.BrowseNames.SetTare, Scales.MethodIds.ScaleDeviceType_SetTare,
                CommandFamily.Weight, [], []),
            new("clear-tare", Scales.BrowseNames.ClearTare, Scales.MethodIds.ScaleDeviceType_ClearTare,
                CommandFamily.Weight, [], []),
            new("preset-tare", Scales.BrowseNames.SetPresetTare, Scales.MethodIds.ScaleDeviceType_SetPresetTare,
                CommandFamily.Weight,
                [new("presetTare", "Preset tare", BuiltInType.Double,
                    "Finite, nonnegative, within the server's range."),
                    .. s_unitInputs],
                [DataTypeIds.Double, DataTypeIds.EUInformation]),
            new("register-weight", Scales.BrowseNames.RegisterWeight, Scales.MethodIds.ScaleDeviceType_RegisterWeight,
                CommandFamily.Weight, [], []),
            new("select-product", Scales.BrowseNames.SelectProduct, Scales.MethodIds.ProductionPresetType_SelectProduct,
                CommandFamily.Product, [s_productId], [DataTypeIds.String]),
            new("deselect-product", Scales.BrowseNames.DeselectProduct,
                Scales.MethodIds.ProductionPresetType_DeselectProduct,
                CommandFamily.Product, [s_productId], [DataTypeIds.String]),
            new("switch-product", Scales.BrowseNames.SwitchProduct, Scales.MethodIds.ProductionPresetType_SwitchProduct,
                CommandFamily.Product, [s_productId], [DataTypeIds.String]),
            new("remove-product", Scales.BrowseNames.RemoveProduct, Scales.MethodIds.ProductionPresetType_RemoveProduct,
                CommandFamily.Product, [s_productId], [DataTypeIds.String]),
            new("add-product", Scales.BrowseNames.AddProduct, Scales.MethodIds.ProductionPresetType_AddProduct,
                CommandFamily.Product, [TextInput("productName", "Product name"), s_productId],
                [DataTypeIds.String, DataTypeIds.String, DataTypeIds.NodeId], true),
            new("add-recipe", Scales.BrowseNames.AddRecipe, Scales.MethodIds.RecipeManagementType_AddRecipe,
                CommandFamily.RecipeManagement,
                [s_recipeId, new("recipeName", "Recipe name", BuiltInType.LocalizedText, "1–256 characters.")],
                [DataTypeIds.String, DataTypeIds.LocalizedText], true),
            new("remove-recipe", Scales.BrowseNames.RemoveRecipe, Scales.MethodIds.RecipeManagementType_RemoveRecipe,
                CommandFamily.RecipeManagement, [s_recipeId], [DataTypeIds.String]),
            new("start-recipe", Scales.BrowseNames.StartRecipe, Scales.MethodIds.RecipeScaleType_StartRecipe,
                CommandFamily.RecipeRun, [s_recipeId], [DataTypeIds.NodeId]),
            new("stop-recipe", Scales.BrowseNames.StopRecipe, Scales.MethodIds.RecipeScaleType_StopRecipe,
                CommandFamily.RecipeRun, [s_recipeId], [DataTypeIds.NodeId]),
            new("continue-recipe", Scales.BrowseNames.ContinueRecipe, Scales.MethodIds.RecipeScaleType_ContinueRecipe,
                CommandFamily.RecipeRun, [s_recipeId], [DataTypeIds.NodeId]),
            new("abort-recipe", Scales.BrowseNames.AbortRecipe, Scales.MethodIds.RecipeScaleType_AbortRecipe,
                CommandFamily.RecipeRun, [s_recipeId], [DataTypeIds.NodeId]),
            new("skip-recipe-element", Scales.BrowseNames.SkipCurrentRecipeElement,
                Scales.MethodIds.RecipeScaleType_SkipCurrentRecipeElement,
                CommandFamily.RecipeRun, [s_recipeId], [DataTypeIds.NodeId]),
            new("reference-piece-weight", Scales.BrowseNames.SetReferencePieceWeight,
                Scales.MethodIds.PieceCountingScaleType_SetReferencePieceWeight,
                CommandFamily.Pieces,
                [new("pieceWeight", "Reference piece weight", BuiltInType.UInt32, "1–1000000."), .. s_unitInputs],
                [DataTypeIds.UInt32, DataTypeIds.EUInformation]),
            new("reference-piece-count", Scales.BrowseNames.SetNumberOfReferencePieces,
                Scales.MethodIds.PieceCountingScaleType_SetNumberOfReferencePieces,
                CommandFamily.Pieces, [s_count], [DataTypeIds.UInt32]),
            new("start-reference", Scales.BrowseNames.StartReference,
                Scales.MethodIds.PieceCountingScaleType_StartReference,
                CommandFamily.Pieces, [s_count], [DataTypeIds.UInt32]),
            new("inbound-weighing", Scales.BrowseNames.InboundWeighing,
                Scales.MethodIds.VehicleScaleType_InboundWeighing,
                CommandFamily.Vehicle, [s_vehicleId], [DataTypeIds.String]),
            new("outbound-weighing", Scales.BrowseNames.OutboundWeighing,
                Scales.MethodIds.VehicleScaleType_OutboundWeighing,
                CommandFamily.Vehicle, [s_vehicleId], [DataTypeIds.String]),
            new("one-pass-weighing", Scales.BrowseNames.OnePassWeighing,
                Scales.MethodIds.VehicleScaleType_OnePassWeighing,
                CommandFamily.Vehicle, [s_vehicleId], [DataTypeIds.String]),
            new("reset-statistics", Scales.BrowseNames.ResetGlobalStatistics,
                Scales.MethodIds.ScaleSystemType_ResetGlobalStatistics, CommandFamily.System, [], [])
        ];
    }
}
