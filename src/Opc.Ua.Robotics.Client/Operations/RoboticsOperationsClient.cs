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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Robotics.Operations;

namespace Opc.Ua.Robotics.Client.Operations
{
    /// <summary>
    /// Invokes opt-in, explicitly non-normative Robotics operation convention methods.
    /// </summary>
    public sealed class RoboticsOperationsClient
    {
        /// <summary>
        /// Creates a convention operation client for one operations object.
        /// </summary>
        public RoboticsOperationsClient(ISession session, NodeId operationsNodeId, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            OperationsNodeId = operationsNodeId.IsNull
                ? throw new ArgumentException("An operations NodeId is required.", nameof(operationsNodeId))
                : operationsNodeId;
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the operations object NodeId.
        /// </summary>
        public NodeId OperationsNodeId { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Invokes MoveTo by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> MoveToAsync(
            MoveToRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("MoveTo", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes MoveJ by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> MoveJAsync(
            JointMoveRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("MoveJ", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes MoveL by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> MoveLAsync(
            LinearMoveRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("MoveL", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes Grasp by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> GraspAsync(
            GraspRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("Grasp", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes Release by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> ReleaseAsync(
            ReleaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("Release", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes PickFrom by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> PickFromAsync(
            PickPlaceRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("PickFrom", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes PlaceAt by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> PlaceAtAsync(
            PickPlaceRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("PlaceAt", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes SwapTool by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> SwapToolAsync(
            ToolChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("SwapTool", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes SetOutput by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> SetOutputAsync(
            OutputRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("SetOutput", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes CallProgram by BrowseName.
        /// </summary>
        public Task<RoboticsOperationResult> CallProgramAsync(
            ProgramCallRequest request,
            CancellationToken cancellationToken = default)
        {
            return InvokeStandardAsync("CallProgram", ToArguments(request), cancellationToken);
        }

        /// <summary>
        /// Invokes an application-specific operation by BrowseName.
        /// </summary>
        /// <typeparam name="TRequest">
        /// The request CLR type.
        /// </typeparam>
        /// <typeparam name="TResponse">
        /// The response CLR type.
        /// </typeparam>
        public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string name,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<Variant> output = await CallAsync(
                name,
                [new Variant(ToGenericArguments(request))],
                cancellationToken).ConfigureAwait(false);

            RoboticsOperationResult result = FromOutput(output);
            if (typeof(TResponse) == typeof(RoboticsOperationResult))
            {
                return Unsafe.As<RoboticsOperationResult, TResponse>(ref result);
            }
            if (typeof(TResponse) == typeof(ArrayOf<Variant>))
            {
                ArrayOf<Variant> values = result.Outputs ?? [];
                return Unsafe.As<ArrayOf<Variant>, TResponse>(ref values);
            }
            if (typeof(TResponse) == typeof(Variant))
            {
                ArrayOf<Variant> values = result.Outputs ?? [];
                Variant value = values.Count == 0 ? Variant.Null : values[0];
                return Unsafe.As<Variant, TResponse>(ref value);
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private async Task<RoboticsOperationResult> InvokeStandardAsync(
            string name,
            ArrayOf<Variant> inputArguments,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> output = await CallAsync(name, inputArguments, cancellationToken)
                .ConfigureAwait(false);
            return FromOutput(output);
        }

        private async Task<ArrayOf<Variant>> CallAsync(
            string name,
            ArrayOf<Variant> inputArguments,
            CancellationToken cancellationToken)
        {
            NodeId methodId = await ResolveMethodAsync(name, cancellationToken).ConfigureAwait(false);
            Variant[] arguments = new Variant[inputArguments.Count];
            for (int ii = 0; ii < inputArguments.Count; ii++)
            {
                arguments[ii] = inputArguments[ii];
            }
            return await Session.CallAsync(
                OperationsNodeId,
                methodId,
                cancellationToken,
                arguments).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveMethodAsync(string name, CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeToBrowse: OperationsNodeId,
                maxResultsToReturn: 0,
                browseDirection: BrowseDirection.Forward,
                referenceTypeId: Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                includeSubtypes: true,
                nodeClassMask: (uint)NodeClass.Method,
                ct: cancellationToken).ConfigureAwait(false);

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                if (string.Equals(reference.BrowseName.Name, name, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNodeIdUnknown,
                "Operation method '{0}' was not found below '{1}'.",
                name,
                OperationsNodeId);
        }

        private static RoboticsOperationResult FromOutput(ArrayOf<Variant> output)
        {
            if (output.Count == 0 || !output[0].TryGetValue(out StatusCode statusCode))
            {
                return new RoboticsOperationResult(new ServiceResult(StatusCodes.BadUnexpectedError));
            }
            string? message = null;
            if (output.Count > 1 && !output[1].IsNull && output[1].TryGetValue(out string text))
            {
                message = text;
            }
            ArrayOf<Variant>? values = null;
            if (output.Count > 2 && !output[2].IsNull && output[2].TryGetValue(out ArrayOf<Variant> outputs))
            {
                values = outputs;
            }
            return new RoboticsOperationResult(new ServiceResult(statusCode), message, values);
        }

        private static ArrayOf<Variant> ToArguments(MoveToRequest request)
        {
            return [
                Structure(request.TargetFrame),
                Optional(request.SpeedFraction),
                Optional(request.BlendRadius),
                OptionalStructure(request.BlendRadiusUnits)
            ];
        }

        private static ArrayOf<Variant> ToArguments(JointMoveRequest request)
        {
            return [
                new Variant(request.JointTargets),
                Structure(request.JointUnits),
                Optional(request.SpeedFraction)
            ];
        }

        private static ArrayOf<Variant> ToArguments(LinearMoveRequest request)
        {
            return [
                Structure(request.TargetFrame),
                new Variant(request.LinearSpeed),
                Structure(request.LinearSpeedUnits),
                Optional(request.Acceleration),
                OptionalStructure(request.AccelerationUnits)
            ];
        }

        private static ArrayOf<Variant> ToArguments(GraspRequest request)
        {
            return [
                Optional(request.ForceNewtons),
                Optional(request.Width),
                OptionalStructure(request.WidthUnits),
                new Variant((int)request.Approach)
            ];
        }

        private static ArrayOf<Variant> ToArguments(ReleaseRequest request)
        {
            return [
                new Variant((int)request.Mode),
                OptionalStructure(request.TargetFrame)
            ];
        }

        private static ArrayOf<Variant> ToArguments(PickPlaceRequest request)
        {
            return [
                new Variant(request.StationOrLocationIdentifier),
                new Variant(request.ObjectClass),
                request.Attributes.Count == 0 ? Variant.Null : new Variant(ToExtensionObjects(request.Attributes)),
                Optional(request.ForceNewtons)
            ];
        }

        private static ArrayOf<Variant> ToArguments(ToolChangeRequest request)
        {
            return [
                new Variant(request.ToolIdentifier),
                request.DockStation == null ? Variant.Null : new Variant(request.DockStation)
            ];
        }

        private static ArrayOf<Variant> ToArguments(OutputRequest request)
        {
            return [new Variant(request.OutputLineIdentifier), request.Value];
        }

        private static ArrayOf<Variant> ToArguments(ProgramCallRequest request)
        {
            return [new Variant(request.ProgramName), new Variant(request.Arguments)];
        }

        private static ArrayOf<Variant> ToGenericArguments<TRequest>(TRequest request)
        {
            if (request is ArrayOf<Variant> arguments)
            {
                return arguments;
            }
            if (request is Variant variant)
            {
                return [variant];
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static Variant Structure(IEncodeable value)
        {
            return new Variant(new ExtensionObject(value));
        }

        private static Variant Optional(double? value)
        {
            return value.HasValue ? new Variant(value.Value) : Variant.Null;
        }

        private static Variant OptionalStructure(IEncodeable? value)
        {
            return value == null ? Variant.Null : Structure(value);
        }

        private static ArrayOf<ExtensionObject> ToExtensionObjects(ArrayOf<KeyValuePair> values)
        {
            var result = new ExtensionObject[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                result[ii] = new ExtensionObject(values[ii]);
            }
            return result.ToArrayOf();
        }
    }
}
