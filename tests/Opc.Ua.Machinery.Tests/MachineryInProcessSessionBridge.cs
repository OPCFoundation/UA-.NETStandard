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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Builds a Moq-backed <see cref="ISession"/> that dispatches browse,
    /// read, browse-path translation and method calls straight into a
    /// <see cref="MachineryServerFixture"/>'s in-process address space, so the
    /// client surface can be exercised end to end without a TCP server.
    /// </summary>
    internal static class MachineryInProcessSessionBridge
    {
        public static Mock<ISession> Build(MachineryServerFixture fixture)
        {
            var mock = new Mock<ISession>();
            NamespaceTable namespaceUris = fixture.Manager.Server.NamespaceUris;
            mock.SetupGet(session => session.NamespaceUris).Returns(namespaceUris);

            ServiceMessageContext context = ServiceMessageContext.Create(
                NUnitTelemetryContext.Create());
            context.NamespaceUris = namespaceUris;
            mock.SetupGet(session => session.MessageContext).Returns(context);

            // Browser (used by MachineryClient's own browse helper, and by
            // BrowseNext-aware fixes to it) reads these three properties the
            // moment a Browser is attached to a session, regardless of
            // whether pagination is ever exercised. ISession declares
            // OperationLimits and ServerCapabilities non-nullable, so a real
            // session always has them; this bridge must too.
            // Type checks go through the node cache; answer them from the
            // server's own type tree.
            TypeTable typeTree = fixture.Manager.Server.TypeTree;
            var nodeCache = new Mock<INodeCache>();
            nodeCache
                .Setup(cache => cache.IsTypeOfAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NodeId subType, NodeId superType, CancellationToken _) =>
                    new ValueTask<bool>(typeTree.IsTypeOf(subType, superType)));
            mock.SetupGet(session => session.NodeCache).Returns(nodeCache.Object);

            mock.SetupGet(session => session.OperationLimits).Returns(new OperationLimits());
            mock.SetupGet(session => session.ServerCapabilities).Returns(new ServerCapabilities());
            mock.SetupGet(session => session.ContinuationPointPolicy)
                .Returns(ContinuationPointPolicy.Default);

            mock.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    var results = new BrowsePathResult[paths.Count];
                    for (int ii = 0; ii < paths.Count; ii++)
                    {
                        results[ii] = ResolveBrowsePath(fixture, paths[ii]);
                    }
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });

            mock.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    RequestHeader? _,
                    double _,
                    TimestampsToReturn _,
                    ArrayOf<ReadValueId> nodesToRead,
                    CancellationToken _) =>
                {
                    var results = new DataValue[nodesToRead.Count];
                    for (int ii = 0; ii < nodesToRead.Count; ii++)
                    {
                        results[ii] = ReadValue(fixture, nodesToRead[ii]);
                    }
                    return new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });

            mock.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    RequestHeader? _,
                    ViewDescription? _,
                    uint _,
                    ArrayOf<BrowseDescription> nodesToBrowse,
                    CancellationToken _) =>
                {
                    var results = new BrowseResult[nodesToBrowse.Count];
                    for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                    {
                        results[ii] = Browse(fixture, nodesToBrowse[ii]);
                    }
                    return new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });

            mock.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    RequestHeader? _,
                    ArrayOf<CallMethodRequest> calls,
                    CancellationToken _) =>
                {
                    var results = new CallMethodResult[calls.Count];
                    for (int ii = 0; ii < calls.Count; ii++)
                    {
                        results[ii] = InvokeCall(fixture, calls[ii]);
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
            MachineryServerFixture fixture,
            BrowsePath path)
        {
            NodeState? current = fixture.Manager.FindPredefinedNode(path.StartingNode);
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
                    fixture.Manager.SystemContext,
                    element.TargetName);
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

        private static DataValue ReadValue(
            MachineryServerFixture fixture,
            ReadValueId nodeToRead)
        {
            NodeState? node = fixture.Manager.FindPredefinedNode(nodeToRead.NodeId);
            return node is BaseVariableState variable
                ? new DataValue(variable.WrappedValue, StatusCodes.Good)
                : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
        }

        private static BrowseResult Browse(
            MachineryServerFixture fixture,
            BrowseDescription description)
        {
            NodeState? node = fixture.Manager.FindPredefinedNode(description.NodeId);
            if (node is null)
            {
                return new BrowseResult
                {
                    StatusCode = StatusCodes.BadNodeIdUnknown,
                    References = []
                };
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(fixture.Manager.SystemContext, children);
            var references = new List<ReferenceDescription>();
            foreach (BaseInstanceState child in children)
            {
                if (description.NodeClassMask != 0 &&
                    (description.NodeClassMask & (uint)child.NodeClass) == 0)
                {
                    continue;
                }
                references.Add(new ReferenceDescription
                {
                    NodeId = (ExpandedNodeId)child.NodeId,
                    BrowseName = child.BrowseName,
                    DisplayName = child.DisplayName,
                    NodeClass = child.NodeClass,
                    ReferenceTypeId = child.ReferenceTypeId,
                    IsForward = true,
                    TypeDefinition = child is BaseObjectState objectState
                        ? (ExpandedNodeId)objectState.TypeDefinitionId
                        : ExpandedNodeId.Null
                });
            }

            return new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = ArrayOf.Wrapped(references.ToArray())
            };
        }

        private static CallMethodResult InvokeCall(
            MachineryServerFixture fixture,
            CallMethodRequest call)
        {
            NodeState? node = fixture.Manager.FindPredefinedNode(call.MethodId);
            if (node is not MethodState method)
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
            var argumentErrors = new List<ServiceResult>();
            ServiceResult status = method
                .CallAsync(
                    fixture.Manager.SystemContext,
                    call.ObjectId,
                    call.InputArguments,
                    argumentErrors,
                    outputs)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            var inputResults = new StatusCode[argumentErrors.Count];
            for (int ii = 0; ii < argumentErrors.Count; ii++)
            {
                inputResults[ii] = argumentErrors[ii].StatusCode;
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
