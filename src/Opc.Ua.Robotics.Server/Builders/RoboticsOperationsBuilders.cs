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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Robotics.Operations;
using UaBrowseNames = global::Opc.Ua.BrowseNames;
using UaDataTypeIds = global::Opc.Ua.DataTypeIds;
using UaObjectTypeIds = global::Opc.Ua.ObjectTypeIds;
using UaReferenceTypeIds = global::Opc.Ua.ReferenceTypeIds;
using UaVariableTypeIds = global::Opc.Ua.VariableTypeIds;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Builds an opt-in, non-normative Robotics operation convention object.
    /// </summary>
    public interface IRoboticsOperationsBuilder
    {
        /// <summary>
        /// Gets the operation object state after it is materialized.
        /// </summary>
        BaseObjectState? State { get; }

        /// <summary>
        /// Supplies the dynamic UserExecutable decision for every convention method.
        /// </summary>
        IRoboticsOperationsBuilder WithUserExecutable(
            Func<ISystemContext, MethodState, bool> isUserExecutable);

        /// <summary>
        /// Registers the non-normative MoveTo convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnMoveTo(
            Func<MoveToRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative MoveJ convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnMoveJ(
            Func<JointMoveRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative MoveL convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnMoveL(
            Func<LinearMoveRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative Grasp convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnGrasp(
            Func<GraspRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative Release convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnRelease(
            Func<ReleaseRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative PickFrom convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnPickFrom(
            Func<PickPlaceRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative PlaceAt convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnPlaceAt(
            Func<PickPlaceRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative SwapTool convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnSwapTool(
            Func<ToolChangeRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative SetOutput convention handler.
        /// </summary>
        IRoboticsOperationsBuilder OnSetOutput(
            Func<OutputRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Registers the non-normative CallProgram fallback handler.
        /// </summary>
        /// <remarks>
        /// Prefer the standard OPC UA Programs plus OPC 40010 TaskControl route for program loading and
        /// execution. Use this operation only as an application-specific fallback when that standard model
        /// cannot represent the target program invocation.
        /// </remarks>
        IRoboticsOperationsBuilder OnCallProgram(
            Func<ProgramCallRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler);

        /// <summary>
        /// Adds an application-specific operation outside the industrial convention subset.
        /// </summary>
        /// <typeparam name="TRequest">
        /// The request CLR type.
        /// </typeparam>
        /// <typeparam name="TResponse">
        /// The response CLR type.
        /// </typeparam>
        IRoboticsOperationsBuilder AddOperation<TRequest, TResponse>(
            string name,
            Func<TRequest, CancellationToken, ValueTask<TResponse>> handler);
    }

    internal sealed class RoboticsOperationsBuilder : IRoboticsOperationsBuilder
    {
        public RoboticsOperationsBuilder(
            RoboticsBuildScope scope,
            MotionDeviceState owner,
            string browseName,
            ushort applicationNamespaceIndex)
        {
            m_scope = scope ?? throw new ArgumentNullException(nameof(scope));
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_browseName = string.IsNullOrWhiteSpace(browseName)
                ? throw new ArgumentException("A non-empty browse name is required.", nameof(browseName))
                : browseName;
            m_applicationNamespaceIndex = applicationNamespaceIndex;
            ValidateApplicationNamespace(scope, applicationNamespaceIndex);
            scope.PostRegistrationActions.Add(MaterializeAsync);
        }

        public BaseObjectState? State { get; private set; }

        public IRoboticsOperationsBuilder WithUserExecutable(
            Func<ISystemContext, MethodState, bool> isUserExecutable)
        {
            m_scope.EnsureMutable();
            m_isUserExecutable = isUserExecutable ?? throw new ArgumentNullException(nameof(isUserExecutable));
            return this;
        }

        public IRoboticsOperationsBuilder OnMoveTo(
            Func<MoveToRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("MoveTo", handler, CreateMoveTo, MoveToArguments());
        }

        public IRoboticsOperationsBuilder OnMoveJ(
            Func<JointMoveRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("MoveJ", handler, CreateMoveJ, MoveJArguments());
        }

        public IRoboticsOperationsBuilder OnMoveL(
            Func<LinearMoveRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("MoveL", handler, CreateMoveL, MoveLArguments());
        }

        public IRoboticsOperationsBuilder OnGrasp(
            Func<GraspRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("Grasp", handler, CreateGrasp, GraspArguments());
        }

        public IRoboticsOperationsBuilder OnRelease(
            Func<ReleaseRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("Release", handler, CreateRelease, ReleaseArguments());
        }

        public IRoboticsOperationsBuilder OnPickFrom(
            Func<PickPlaceRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("PickFrom", handler, CreatePickPlace, PickPlaceArguments());
        }

        public IRoboticsOperationsBuilder OnPlaceAt(
            Func<PickPlaceRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("PlaceAt", handler, CreatePickPlace, PickPlaceArguments());
        }

        public IRoboticsOperationsBuilder OnSwapTool(
            Func<ToolChangeRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("SwapTool", handler, CreateToolChange, ToolChangeArguments());
        }

        public IRoboticsOperationsBuilder OnSetOutput(
            Func<OutputRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("SetOutput", handler, CreateOutput, OutputArguments());
        }

        public IRoboticsOperationsBuilder OnCallProgram(
            Func<ProgramCallRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler)
        {
            return AddStandardOperation("CallProgram", handler, CreateProgramCall, ProgramCallArguments());
        }

        public IRoboticsOperationsBuilder AddOperation<TRequest, TResponse>(
            string name,
            Func<TRequest, CancellationToken, ValueTask<TResponse>> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A non-empty operation name is required.", nameof(name));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_scope.EnsureMutable();
            AddRegistration(new GenericOperationRegistration<TRequest, TResponse>(name, handler));
            return this;
        }

        private RoboticsOperationsBuilder AddStandardOperation<TRequest>(
            string name,
            Func<TRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler,
            Func<ArrayOf<Variant>, TRequest> createRequest,
            ArrayOf<Argument> inputArguments)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_scope.EnsureMutable();
            AddRegistration(new StandardOperationRegistration<TRequest>(
                name,
                handler,
                createRequest,
                inputArguments));
            return this;
        }

        private void AddRegistration(OperationRegistration registration)
        {
            for (int ii = 0; ii < m_registrations.Count; ii++)
            {
                if (string.Equals(m_registrations[ii].Name, registration.Name, StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "Operation '{0}' is already registered.",
                        registration.Name);
                }
            }
            m_registrations.Add(registration);
        }

        private async ValueTask MaterializeAsync(CancellationToken cancellationToken)
        {
            if (m_registrations.Count == 0)
            {
                return;
            }

            BaseObjectState operations = CreateOperationsObject();
            State = operations;
            m_owner.AddChild(operations);
            for (int ii = 0; ii < m_registrations.Count; ii++)
            {
                CreateMethod(operations, m_registrations[ii]);
            }
            await m_scope.BuildContext.Manager
                .AddPredefinedNodeAsync(operations, cancellationToken)
                .ConfigureAwait(false);
        }

        private BaseObjectState CreateOperationsObject()
        {
            var browseName = new QualifiedName(m_browseName, m_applicationNamespaceIndex);
            var state = new BaseObjectState(m_owner)
            {
                BrowseName = browseName,
                DisplayName = new LocalizedText(m_browseName),
                Description = new LocalizedText(
                    "Opt-in, explicitly non-normative industrial operation conventions. " +
                    "This object is application-owned and is not part of OPC 40010."),
                NodeId = ChildNodeId(m_owner.NodeId, m_browseName),
                ReferenceTypeId = UaReferenceTypeIds.HasComponent,
                SymbolicName = m_browseName,
                TypeDefinitionId = UaObjectTypeIds.BaseObjectType
            };
            state.AddReference(UaReferenceTypeIds.HasComponent, true, m_owner.NodeId);
            return state;
        }

        private void CreateMethod(BaseObjectState parent, OperationRegistration registration)
        {
            var method = new MethodState(parent)
            {
                BrowseName = new QualifiedName(registration.Name, m_applicationNamespaceIndex),
                DisplayName = new LocalizedText(registration.Name),
                Description = new LocalizedText(
                    "Opt-in, explicitly non-normative Robotics operation convention method. " +
                    "This is not an OPC 40010 method."),
                Executable = true,
                NodeId = ChildNodeId(parent.NodeId, registration.Name),
                ReferenceTypeId = UaReferenceTypeIds.HasComponent,
                SymbolicName = registration.Name,
                UserExecutable = true
            };
            method.AddReference(UaReferenceTypeIds.HasComponent, true, parent.NodeId);
            method.OnReadUserExecutable = OnReadUserExecutable;
            method.OnCallMethod2Async = registration.InvokeAsync;
            parent.AddChild(method);
            AddArgumentProperty(method, UaBrowseNames.InputArguments, "InputArguments", registration.InputArguments);
            AddArgumentProperty(method, UaBrowseNames.OutputArguments, "OutputArguments", registration.OutputArguments);
        }

        private ServiceResult OnReadUserExecutable(ISystemContext context, NodeState node, ref bool value)
        {
            if (node is MethodState method && m_isUserExecutable != null)
            {
                value = m_isUserExecutable(context, method);
            }
            return ServiceResult.Good;
        }

        private void AddArgumentProperty(
            MethodState method,
            string browseName,
            string suffix,
            ArrayOf<Argument> arguments)
        {
            var property = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            property.BrowseName = new QualifiedName(browseName);
            property.DataType = UaDataTypeIds.Argument;
            property.DisplayName = new LocalizedText(browseName);
            property.NodeId = ChildNodeId(method.NodeId, suffix);
            property.ReferenceTypeId = UaReferenceTypeIds.HasProperty;
            property.TypeDefinitionId = UaVariableTypeIds.PropertyType;
            property.Value = arguments;
            property.ValueRank = ValueRanks.OneDimension;
            if (browseName == UaBrowseNames.InputArguments)
            {
                method.InputArguments = property;
            }
            else
            {
                method.OutputArguments = property;
            }
            method.AddChild(property);
        }

        private NodeId ChildNodeId(NodeId parentNodeId, string name)
        {
            return new NodeId(
                $"{parentNodeId.IdentifierAsString}_{name}",
                m_applicationNamespaceIndex);
        }

        private static void ValidateApplicationNamespace(
            RoboticsBuildScope scope,
            ushort applicationNamespaceIndex)
        {
            string? namespaceUri = scope.Context.NamespaceUris.GetString(applicationNamespaceIndex);
            if (namespaceUri == null ||
                applicationNamespaceIndex == 0 ||
                namespaceUri == Opc.Ua.Di.Namespaces.OpcUaDi ||
                namespaceUri == Opc.Ua.IA.Namespaces.IA ||
                namespaceUri == Opc.Ua.Robotics.Namespaces.Robotics)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Robotics operation conventions must be created in an application-owned namespace.");
            }
        }

        private static Argument Argument(string name, NodeId dataType, int valueRank = ValueRanks.Scalar)
        {
            return new Argument
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private static ArrayOf<Argument> ResultArguments()
        {
            return [
                Argument("StatusCode", UaDataTypeIds.StatusCode),
                Argument("Message", UaDataTypeIds.String),
                Argument("Outputs", UaDataTypeIds.BaseDataType, ValueRanks.OneDimension)
            ];
        }

        private static ArrayOf<Argument> MoveToArguments()
        {
            return [
                Argument("TargetFrame", UaDataTypeIds.Structure),
                Argument("SpeedFraction", UaDataTypeIds.Double),
                Argument("BlendRadius", UaDataTypeIds.Double),
                Argument("BlendRadiusUnits", UaDataTypeIds.EUInformation)
            ];
        }

        private static ArrayOf<Argument> MoveJArguments()
        {
            return [
                Argument("JointTargets", UaDataTypeIds.Double, ValueRanks.OneDimension),
                Argument("JointUnits", UaDataTypeIds.EUInformation),
                Argument("SpeedFraction", UaDataTypeIds.Double)
            ];
        }

        private static ArrayOf<Argument> MoveLArguments()
        {
            return [
                Argument("TargetFrame", UaDataTypeIds.Structure),
                Argument("LinearSpeed", UaDataTypeIds.Double),
                Argument("LinearSpeedUnits", UaDataTypeIds.EUInformation),
                Argument("Acceleration", UaDataTypeIds.Double),
                Argument("AccelerationUnits", UaDataTypeIds.EUInformation)
            ];
        }

        private static ArrayOf<Argument> GraspArguments()
        {
            return [
                Argument("ForceNewtons", UaDataTypeIds.Double),
                Argument("Width", UaDataTypeIds.Double),
                Argument("WidthUnits", UaDataTypeIds.EUInformation),
                Argument("Approach", UaDataTypeIds.Int32)
            ];
        }

        private static ArrayOf<Argument> ReleaseArguments()
        {
            return [
                Argument("Mode", UaDataTypeIds.Int32),
                Argument("TargetFrame", UaDataTypeIds.Structure)
            ];
        }

        private static ArrayOf<Argument> PickPlaceArguments()
        {
            return [
                Argument("StationOrLocationIdentifier", UaDataTypeIds.String),
                Argument("ObjectClass", UaDataTypeIds.String),
                Argument("Attributes", UaDataTypeIds.Structure, ValueRanks.OneDimension),
                Argument("ForceNewtons", UaDataTypeIds.Double)
            ];
        }

        private static ArrayOf<Argument> ToolChangeArguments()
        {
            return [
                Argument("ToolIdentifier", UaDataTypeIds.String),
                Argument("DockStation", UaDataTypeIds.String)
            ];
        }

        private static ArrayOf<Argument> OutputArguments()
        {
            return [
                Argument("OutputLineIdentifier", UaDataTypeIds.String),
                Argument("Value", UaDataTypeIds.BaseDataType)
            ];
        }

        private static ArrayOf<Argument> ProgramCallArguments()
        {
            return [
                Argument("ProgramName", UaDataTypeIds.String),
                Argument("Arguments", UaDataTypeIds.BaseDataType, ValueRanks.OneDimension)
            ];
        }

        private static MoveToRequest CreateMoveTo(ArrayOf<Variant> input)
        {
            return new MoveToRequest(
                RequiredStructure<ThreeDFrame>(input, 0),
                OptionalDouble(input, 1),
                OptionalDouble(input, 2),
                OptionalStructure<EUInformation>(input, 3));
        }

        private static JointMoveRequest CreateMoveJ(ArrayOf<Variant> input)
        {
            return new JointMoveRequest(
                RequiredDoubleArray(input, 0),
                RequiredStructure<EUInformation>(input, 1),
                OptionalDouble(input, 2));
        }

        private static LinearMoveRequest CreateMoveL(ArrayOf<Variant> input)
        {
            return new LinearMoveRequest(
                RequiredStructure<ThreeDFrame>(input, 0),
                RequiredDouble(input, 1),
                RequiredStructure<EUInformation>(input, 2),
                OptionalDouble(input, 3),
                OptionalStructure<EUInformation>(input, 4));
        }

        private static GraspRequest CreateGrasp(ArrayOf<Variant> input)
        {
            return new GraspRequest(
                OptionalDouble(input, 0),
                OptionalDouble(input, 1),
                OptionalStructure<EUInformation>(input, 2),
                (RoboticsApproach)RequiredInt32(input, 3));
        }

        private static ReleaseRequest CreateRelease(ArrayOf<Variant> input)
        {
            return new ReleaseRequest(
                (RoboticsReleaseMode)RequiredInt32(input, 0),
                OptionalStructure<ThreeDFrame>(input, 1));
        }

        private static PickPlaceRequest CreatePickPlace(ArrayOf<Variant> input)
        {
            return new PickPlaceRequest(
                RequiredString(input, 0),
                RequiredString(input, 1),
                OptionalKeyValueArray(input, 2) ?? [],
                OptionalDouble(input, 3));
        }

        private static ToolChangeRequest CreateToolChange(ArrayOf<Variant> input)
        {
            return new ToolChangeRequest(RequiredString(input, 0), OptionalString(input, 1));
        }

        private static OutputRequest CreateOutput(ArrayOf<Variant> input)
        {
            return new OutputRequest(RequiredString(input, 0), input[1]);
        }

        private static ProgramCallRequest CreateProgramCall(ArrayOf<Variant> input)
        {
            return new ProgramCallRequest(RequiredString(input, 0), OptionalVariantArray(input, 1) ?? []);
        }

        private static T RequiredStructure<T>(ArrayOf<Variant> input, int index)
            where T : class, IEncodeable
        {
#pragma warning disable CS8600 // TODO: update when Variant.TryGetStructure carries class nullability.
            if (input[index].TryGetStructure(out T value))
#pragma warning restore CS8600
            {
                return value!;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static T? OptionalStructure<T>(ArrayOf<Variant> input, int index)
            where T : class, IEncodeable
        {
            if (input[index].IsNull)
            {
                return null;
            }
            return RequiredStructure<T>(input, index);
        }

        private static ArrayOf<double> RequiredDoubleArray(ArrayOf<Variant> input, int index)
        {
            if (input[index].TryGetValue(out ArrayOf<double> value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static ArrayOf<KeyValuePair>? OptionalKeyValueArray(ArrayOf<Variant> input, int index)
        {
            if (input[index].IsNull)
            {
                return null;
            }
            if (input[index].TryGetValue(out ArrayOf<KeyValuePair> value, null))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static ArrayOf<Variant>? OptionalVariantArray(ArrayOf<Variant> input, int index)
        {
            if (input[index].IsNull)
            {
                return null;
            }
            if (input[index].TryGetValue(out ArrayOf<Variant> value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static ArrayOf<Variant> RequiredVariantArray(ArrayOf<Variant> input, int index)
        {
            if (input[index].TryGetValue(out ArrayOf<Variant> value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static double RequiredDouble(ArrayOf<Variant> input, int index)
        {
            if (input[index].TryGetValue(out double value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static double? OptionalDouble(ArrayOf<Variant> input, int index)
        {
            if (input[index].IsNull)
            {
                return null;
            }
            return RequiredDouble(input, index);
        }

        private static int RequiredInt32(ArrayOf<Variant> input, int index)
        {
            if (input[index].TryGetValue(out int value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static string RequiredString(ArrayOf<Variant> input, int index)
        {
            if (input[index].TryGetValue(out string value))
            {
                return value;
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static string? OptionalString(ArrayOf<Variant> input, int index)
        {
            if (input[index].IsNull)
            {
                return null;
            }
            return RequiredString(input, index);
        }

        private readonly ushort m_applicationNamespaceIndex;
        private readonly string m_browseName;
        private readonly MotionDeviceState m_owner;
        private readonly List<OperationRegistration> m_registrations = [];
        private readonly RoboticsBuildScope m_scope;
        private Func<ISystemContext, MethodState, bool>? m_isUserExecutable;

        private abstract class OperationRegistration
        {
            protected OperationRegistration(string name, ArrayOf<Argument> inputArguments)
            {
                Name = name;
                InputArguments = inputArguments;
            }

            public string Name { get; }

            public ArrayOf<Argument> InputArguments { get; }

            public ArrayOf<Argument> OutputArguments => ResultArguments();

            public abstract ValueTask<ServiceResult> InvokeAsync(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default);
        }

        private sealed class StandardOperationRegistration<TRequest> : OperationRegistration
        {
            public StandardOperationRegistration(
                string name,
                Func<TRequest, CancellationToken, ValueTask<RoboticsOperationResult>> handler,
                Func<ArrayOf<Variant>, TRequest> createRequest,
                ArrayOf<Argument> inputArguments)
                : base(name, inputArguments)
            {
                m_handler = handler;
                m_createRequest = createRequest;
            }

            public override async ValueTask<ServiceResult> InvokeAsync(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                TRequest request = m_createRequest(inputArguments);
                RoboticsOperationResult result = await m_handler(request, cancellationToken)
                    .ConfigureAwait(false);
                AddResultOutputs(result, outputArguments);
                return result.ServiceResult;
            }

            private readonly Func<ArrayOf<Variant>, TRequest> m_createRequest;
            private readonly Func<TRequest, CancellationToken, ValueTask<RoboticsOperationResult>> m_handler;
        }

        private sealed class GenericOperationRegistration<TRequest, TResponse> : OperationRegistration
        {
            public GenericOperationRegistration(
                string name,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> handler)
                : base(name, [Argument("Arguments", UaDataTypeIds.BaseDataType, ValueRanks.OneDimension)])
            {
                m_handler = handler;
            }

            public override async ValueTask<ServiceResult> InvokeAsync(
                ISystemContext context,
                MethodState method,
                NodeId objectId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                TRequest request = CreateGenericRequest(inputArguments);
                TResponse response = await m_handler(request, cancellationToken).ConfigureAwait(false);
                if (response is RoboticsOperationResult operationResult)
                {
                    AddResultOutputs(operationResult, outputArguments);
                    return operationResult.ServiceResult;
                }
                if (response is ArrayOf<Variant> variants)
                {
                    var result = new RoboticsOperationResult(ServiceResult.Good, Outputs: variants);
                    AddResultOutputs(result, outputArguments);
                    return ServiceResult.Good;
                }
                outputArguments[0] = new Variant(StatusCodes.Good);
                outputArguments[1] = Variant.Null;
                outputArguments[2] = Variant.Null;
                return ServiceResult.Good;
            }

            private static TRequest CreateGenericRequest(ArrayOf<Variant> inputArguments)
            {
                if (typeof(TRequest) == typeof(ArrayOf<Variant>))
                {
                    ArrayOf<Variant> arguments = RequiredVariantArray(inputArguments, 0);
                    return Unsafe.As<ArrayOf<Variant>, TRequest>(ref arguments);
                }
                if (typeof(TRequest) == typeof(Variant))
                {
                    ArrayOf<Variant> arguments = RequiredVariantArray(inputArguments, 0);
                    Variant value = arguments.Count == 0 ? Variant.Null : arguments[0];
                    return Unsafe.As<Variant, TRequest>(ref value);
                }
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }

            private readonly Func<TRequest, CancellationToken, ValueTask<TResponse>> m_handler;
        }

        private static void AddResultOutputs(
            RoboticsOperationResult result,
            List<Variant> outputArguments)
        {
            outputArguments[0] = new Variant(result.ServiceResult.StatusCode);
            outputArguments[1] = result.Message == null ? Variant.Null : new Variant(result.Message);
            ArrayOf<Variant>? outputs = result.Outputs;
            if (outputs == null)
            {
                outputArguments[2] = Variant.Null;
            }
            else
            {
                ArrayOf<Variant> values = outputs.Value;
                outputArguments[2] = new Variant(values);
            }
        }
    }
}
