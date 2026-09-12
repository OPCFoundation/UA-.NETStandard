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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Client
{
    internal static class WotProvisioningContract
    {
        public static ValueTask VerifyGroupAsync(
            ISession session,
            NodeId receiver,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            var output = new List<ArgumentContract>
            {
                new("GroupNodeId", Ua.DataTypeIds.NodeId),
                new("AssignedGroupId", Ua.DataTypeIds.String)
            };
            if (getOrCreate)
            {
                output.Add(new("Created", Ua.DataTypeIds.Boolean));
            }
            return VerifyAsync(
                session, receiver, getOrCreate ? "GetOrCreateDocumentGroup" : "CreateDocumentGroup",
                [
                    new("Kind", ExpandedNodeId.ToNodeId(DataTypeIds.WoTDocumentKindEnum, session.NamespaceUris)),
                    new("CatalogUri", Ua.DataTypeIds.UriString)
                ],
                output.ToArrayOf(), cancellationToken);
        }

        public static ValueTask VerifyResourceAsync(
            ISession session,
            NodeId receiver,
            WoTDocumentKindEnum kind,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            bool model = kind == WoTDocumentKindEnum.ThingModel;
            var output = new List<ArgumentContract>
            {
                new("LogicalResourceNodeId", Ua.DataTypeIds.NodeId),
                new("VersionNodeId", Ua.DataTypeIds.NodeId),
                new("AssignedResourceId", Ua.DataTypeIds.String),
                new("AssignedVersionId", Ua.DataTypeIds.String),
                new("FileHandle", Ua.DataTypeIds.UInt32)
            };
            if (getOrCreate)
            {
                output.Add(new("CreatedResource", Ua.DataTypeIds.Boolean));
                output.Add(new("CreatedVersion", Ua.DataTypeIds.Boolean));
            }
            return VerifyAsync(
                session, receiver,
                (getOrCreate ? "GetOrCreate" : "Create") + (model ? "ThingModelResource" : "ThingDescriptionResource"),
                [
                    new(model ? "ModelId" : "ThingId", Ua.DataTypeIds.UriString),
                    new("VersionId", Ua.DataTypeIds.String),
                    new("RequestFileOpen", Ua.DataTypeIds.Boolean)
                ],
                output.ToArrayOf(), cancellationToken);
        }

        public static async ValueTask<string> ReadAssignedIdentifierAsync(
            ISession session,
            NodeId receiver,
            string name,
            CancellationToken cancellationToken)
        {
            NodeId property = await WotConBrowsePathResolver.ResolveChildAsync(
                session, receiver, Ua.ReferenceTypeIds.HasProperty,
                session.NamespaceUris.GetIndexOrAppend(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri),
                name, StatusCodes.BadNotSupported, "The assigned identifier Property is unavailable.",
                cancellationToken, requireUnique: true).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(property, cancellationToken).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out string assigned) || string.IsNullOrEmpty(assigned))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The assigned identifier is invalid.");
            }
            return assigned;
        }

        private static async ValueTask VerifyAsync(
            ISession session,
            NodeId receiver,
            string methodName,
            ArrayOf<ArgumentContract> inputs,
            ArrayOf<ArgumentContract> outputs,
            CancellationToken cancellationToken)
        {
            NodeId method = await WotConBrowsePathResolver.ResolveChildAsync(
                session, receiver, Ua.ReferenceTypeIds.HasComponent,
                session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon), methodName,
                StatusCodes.BadNotSupported, $"The receiver does not expose {methodName}.",
                cancellationToken, requireUnique: true).ConfigureAwait(false);
            NodeId inputArguments = await WotConBrowsePathResolver.ResolveChildAsync(
                session, method, Ua.ReferenceTypeIds.HasProperty, 0, "InputArguments",
                StatusCodes.BadNotSupported, "The typed Method input contract is unavailable.",
                cancellationToken, requireUnique: true).ConfigureAwait(false);
            NodeId outputArguments = await WotConBrowsePathResolver.ResolveChildAsync(
                session, method, Ua.ReferenceTypeIds.HasProperty, 0, "OutputArguments",
                StatusCodes.BadNotSupported, "The typed Method output contract is unavailable.",
                cancellationToken, requireUnique: true).ConfigureAwait(false);
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = method, AttributeId = Attributes.Executable },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.UserExecutable },
                    new ReadValueId { NodeId = inputArguments, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = outputArguments, AttributeId = Attributes.Value }
                ], cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "The Method contract Read is incomplete.");
            }
            foreach (DataValue value in response.Results)
            {
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode);
                }
            }
            if (!response.Results[0].WrappedValue.TryGetValue(out bool executable) ||
                !response.Results[1].WrappedValue.TryGetValue(out bool userExecutable))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The executable attributes are invalid.");
            }
            if (!executable || !userExecutable)
            {
                throw new ServiceResultException(
                    executable ? StatusCodes.BadUserAccessDenied : StatusCodes.BadNotExecutable);
            }
            VerifyArguments(response.Results[2], inputs);
            VerifyArguments(response.Results[3], outputs);
        }

        private static void VerifyArguments(in DataValue value, ArrayOf<ArgumentContract> expected)
        {
            if (!value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> arguments) ||
                arguments.Count != expected.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The typed Method argument count differs.");
            }
            for (int index = 0; index < expected.Count; index++)
            {
                if (!arguments[index].TryGetValue(out IEncodeable? encodeable) ||
                    encodeable is not Argument argument ||
                    !string.Equals(argument.Name, expected[index].Name, StringComparison.Ordinal) ||
                    argument.DataType != expected[index].DataType ||
                    argument.ValueRank != ValueRanks.Scalar ||
                    argument.ArrayDimensions.Count != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch, "The typed Method scalar argument contract differs.");
                }
            }
        }

        private readonly record struct ArgumentContract(string Name, NodeId DataType);
    }
}
