/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Builds a Moq-backed <see cref="ISession"/> that dispatches
    /// <c>TranslateBrowsePathsToNodeIdsAsync</c> and
    /// <c>CallAsync</c> calls directly into a <see cref="DiServerFixture"/>'s
    /// in-process address space. Lets client-side helpers be exercised
    /// end-to-end without standing up a real TCP server.
    /// </summary>
    internal static class DiInProcessSessionBridge
    {
        public static Mock<ISession> Build(DiServerFixture fixture)
        {
            var mock = new Mock<ISession>();
            NamespaceTable nsTable = fixture.Manager.Server.NamespaceUris;
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            var ctx = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            ctx.NamespaceUris = nsTable;
            mock.SetupGet(s => s.MessageContext).Returns(ctx);

            // ---- TranslateBrowsePathsToNodeIdsAsync ----
            mock.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    var results = new BrowsePathResult[paths.Count];
                    for (int i = 0; i < paths.Count; i++)
                    {
                        results[i] = ResolveBrowsePath(fixture, paths[i]);
                    }
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });

            // ---- CallAsync ----
            mock.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                {
                    var results = new CallMethodResult[calls.Count];
                    for (int i = 0; i < calls.Count; i++)
                    {
                        results[i] = InvokeCall(fixture, calls[i]);
                    }
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });

            return mock;
        }

        private static BrowsePathResult ResolveBrowsePath(
            DiServerFixture fixture, BrowsePath path)
        {
            NodeState? current = fixture.Manager.FindPredefinedNode<NodeState>(
                path.StartingNode);
            if (current is null)
            {
                return new BrowsePathResult
                {
                    StatusCode = StatusCodes.BadNodeIdUnknown,
                    Targets = []
                };
            }
            foreach (RelativePathElement element in path.RelativePath.Elements)
            {
                NodeState? next = current.FindChild(
                    fixture.Manager.SystemContext, element.TargetName);
                if (next is null)
                {
                    return new BrowsePathResult
                    {
                        StatusCode = StatusCodes.BadNoMatch,
                        Targets = []
                    };
                }
                current = next;
            }
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = ArrayOf.Wrapped(
                [
                    new BrowsePathTarget
                    {
                        TargetId = (ExpandedNodeId)current.NodeId,
                        RemainingPathIndex = uint.MaxValue
                    }
                ])
            };
        }

        private static CallMethodResult InvokeCall(
            DiServerFixture fixture, CallMethodRequest call)
        {
            MethodState? method = fixture.Manager.FindPredefinedNode<MethodState>(
                call.MethodId);
            if (method is null)
            {
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.BadMethodInvalid,
                    InputArgumentResults = [],
                    InputArgumentDiagnosticInfos = default,
                    OutputArguments = []
                };
            }

            var outputs = new List<Variant>();
            var argErrors = new List<ServiceResult>();
            ServiceResult status = method.Call(
                fixture.Manager.SystemContext,
                call.ObjectId,
                call.InputArguments,
                argErrors,
                outputs);
            var inputResults = new StatusCode[argErrors.Count];
            for (int i = 0; i < argErrors.Count; i++)
            {
                inputResults[i] = argErrors[i].StatusCode;
            }
            return new CallMethodResult
            {
                StatusCode = status.StatusCode,
                InputArgumentResults = ArrayOf.Wrapped(inputResults),
                InputArgumentDiagnosticInfos = default,
                OutputArguments = ArrayOf.Wrapped(outputs.ToArray())
            };
        }
    }
}
