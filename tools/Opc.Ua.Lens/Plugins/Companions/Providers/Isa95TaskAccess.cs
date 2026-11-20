// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed record Isa95TaskMethod(NodeId NodeId, StatusCode Status)
    {
        public void RequireAvailable()
        {
            if (!StatusCode.IsGood(Status))
            {
                throw new ServiceResultException(Status, "The ISA-95 method is absent or not permitted.");
            }
        }
    }

    /// <summary>
    /// Fresh instance-method evidence and typed ISA-95 result validation.
    /// </summary>
    internal static class Isa95TaskAccess
    {
        public static string NamespaceUri(bool version2)
        {
            return version2 ? V2.Namespaces.ISA95JobControlV2 : V1.Namespaces.ISA95JobControlV1;
        }

        public static async ValueTask<Isa95TaskMethod> ReadMethodAsync(
            CompanionContext context,
            NodeId endpoint,
            bool version2,
            string name,
            CancellationToken cancellationToken)
        {
            NodeId method = await IndustrialCompanionAccess.ResolveChildAsync(
                context, endpoint, NamespaceUri(version2), name, true, cancellationToken).ConfigureAwait(false);
            if (method.IsNull)
            {
                return new Isa95TaskMethod(NodeId.Null, StatusCodes.BadMethodInvalid);
            }
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = method, AttributeId = Attributes.NodeClass },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.Executable },
                    new ReadValueId { NodeId = method, AttributeId = Attributes.UserExecutable }
                ], cancellationToken).ConfigureAwait(false);
            RequireGoodHeader(response.ResponseHeader);
            if (response.Results.Count != 3)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "Incomplete ISA-95 method permission attributes.");
            }
            foreach (DataValue value in response.Results)
            {
                if (!StatusCode.IsGood(value.StatusCode))
                {
                    return new Isa95TaskMethod(method, value.StatusCode);
                }
            }
            if (!response.Results[0].WrappedValue.TryGetValue(out int nodeClass) ||
                nodeClass != (int)NodeClass.Method ||
                !response.Results[1].WrappedValue.TryGetValue(out bool executable) ||
                !response.Results[2].WrappedValue.TryGetValue(out bool userExecutable))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "Invalid ISA-95 method permission attributes.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new Isa95TaskMethod(method,
                !userExecutable ? StatusCodes.BadUserAccessDenied :
                !executable ? StatusCodes.BadNotExecutable : StatusCodes.Good);
        }

        public static void CheckJobStatus(ulong status)
        {
            // Annex B.2 defines success as bit 0; zero and additional bits do not prove success.
            if (status != 1UL)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, $"ISA-95 returned job status {status}.");
            }
        }

        public static void RequireGoodHeader(ResponseHeader? header)
        {
            if (header is null)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The service header is missing.");
            }
            RequireGood(header.ServiceResult);
        }

        public static void RequireGood(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status);
            }
        }
    }
}
