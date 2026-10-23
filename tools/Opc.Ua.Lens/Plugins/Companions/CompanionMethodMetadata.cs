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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Validates live instance-method metadata before a prepared typed operation.
    /// </summary>
    internal static class CompanionMethodMetadata
    {
        /// <summary>
        /// Resolves one Method and requires Good permissions and its declared argument shapes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<NodeId> RequireMethodAsync(
            CompanionContext context, NodeId owner, string namespaceUri, string name,
            ArrayOf<NodeId> inputs, ArrayOf<NodeId> outputs, CancellationToken cancellationToken,
            int inputValueRank = ValueRanks.Scalar)
        {
            NodeId method = await IndustrialCompanionAccess.ResolveChildAsync(
                context, owner, namespaceUri, name, false, cancellationToken).ConfigureAwait(false);
            DataValue nodeClass = await ReadOneAsync(context, method, Attributes.NodeClass, cancellationToken)
                .ConfigureAwait(false);
            if (!nodeClass.WrappedValue.TryGetValue(out int actualClass) || actualClass != (int)NodeClass.Method)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The task target is not a Method.");
            }
            DataValue executable = await ReadOneAsync(context, method, Attributes.Executable, cancellationToken)
                .ConfigureAwait(false);
            DataValue allowed = await ReadOneAsync(context, method, Attributes.UserExecutable, cancellationToken)
                .ConfigureAwait(false);
            if (!executable.WrappedValue.TryGetValue(out bool canExecute) ||
                !allowed.WrappedValue.TryGetValue(out bool canUse))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Method permissions must be Boolean.");
            }
            if (!canExecute || !canUse)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The method is not currently executable.");
            }
            await RequireArgumentsAsync(
                context, method, BrowseNames.InputArguments, inputs, inputValueRank, cancellationToken).ConfigureAwait(false);
            await RequireArgumentsAsync(
                context, method, BrowseNames.OutputArguments, outputs, ValueRanks.Scalar, cancellationToken)
                .ConfigureAwait(false);
            return method;
        }

        /// <summary>
        /// Requires a string variable's actual data type and both write-access flags.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task RequireWritableStringAsync(
            CompanionContext context, NodeId node, CancellationToken cancellationToken)
        {
            DataValue access = await ReadOneAsync(context, node, Attributes.AccessLevel, cancellationToken)
                .ConfigureAwait(false);
            DataValue user = await ReadOneAsync(context, node, Attributes.UserAccessLevel, cancellationToken)
                .ConfigureAwait(false);
            DataValue type = await ReadOneAsync(context, node, Attributes.DataType, cancellationToken).ConfigureAwait(false);
            if (!access.WrappedValue.TryGetValue(out byte level) ||
                !user.WrappedValue.TryGetValue(out byte userLevel) ||
                !type.WrappedValue.TryGetValue(out NodeId dataType) ||
                (dataType != DataTypeIds.String && dataType != DataTypeIds.UriString))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Writable metadata must be a string variable.");
            }
            if ((level & userLevel & AccessLevels.CurrentWrite) == 0)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The metadata is not writable.");
            }
        }

        /// <summary>
        /// Returns one Good attribute or propagates the exact failed service/value status.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<DataValue> ReadOneAsync(
            CompanionContext context, NodeId node, uint attribute, CancellationToken cancellationToken)
        {
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Required metadata is not exposed.");
            }
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, [new ReadValueId { NodeId = node, AttributeId = attribute }],
                cancellationToken).ConfigureAwait(false);
            if (response.ResponseHeader is null || response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The metadata read is incomplete.");
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            DataValue value = response.Results[0];
            if (!StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode, "Required metadata did not have Good quality.");
            }
            return value;
        }

        private static async Task RequireArgumentsAsync(
            CompanionContext context, NodeId method, string name, ArrayOf<NodeId> expected,
            int rank, CancellationToken cancellationToken)
        {
            NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                context, method, Namespaces.OpcUa, name, true, cancellationToken).ConfigureAwait(false);
            if (node.IsNull && expected.Count == 0)
            {
                return;
            }
            DataValue reading = await ReadOneAsync(context, node, Attributes.Value, cancellationToken).ConfigureAwait(false);
            if (!reading.WrappedValue.TryGetValue(out ArrayOf<Argument> arguments, context.Session.MessageContext) ||
                arguments.IsNull ||
                arguments.Count != expected.Count)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Method argument metadata does not match.");
            }
            for (int index = 0; index < arguments.Count; index++)
            {
                Argument argument = arguments[index];
                bool typeMatches = argument.DataType == expected[index] ||
                    (expected[index] == DataTypeIds.String && argument.DataType == DataTypeIds.UriString);
                if (!typeMatches ||
                    argument.ValueRank != rank ||
                    (rank == ValueRanks.Scalar && argument.ArrayDimensions.Count != 0) ||
                    (rank == ValueRanks.OneDimension &&
                        (argument.ArrayDimensions.Count > 1 ||
                            (argument.ArrayDimensions.Count == 1 && argument.ArrayDimensions[0] != 0))))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Method argument shape changed.");
                }
            }
        }
    }
}
