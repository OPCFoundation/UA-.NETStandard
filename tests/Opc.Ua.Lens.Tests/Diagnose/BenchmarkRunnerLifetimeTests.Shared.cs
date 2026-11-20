// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose;

public sealed partial class BenchmarkRunnerLifetimeTests
{

    private static BenchmarkTarget Target(BenchmarkMode mode) => new(
        mode, new NodeId("target", 2), new NodeId("owner", 2), BuiltInType.Int32, ValueRanks.Scalar, [], "Target");

    private static Mock<ISession> Session(Func<Task> operation)
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RequestHeader? _, ArrayOf<WriteValue> _, CancellationToken _) =>
            {
                await operation().ConfigureAwait(false);
                return new WriteResponse { Results = [StatusCodes.Good] };
            });
        session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RequestHeader? _, ArrayOf<CallMethodRequest> _, CancellationToken _) =>
            {
                await operation().ConfigureAwait(false);
                return new CallResponse { Results = [new CallMethodResult { StatusCode = StatusCodes.Good }] };
            });
        return session;
    }
}
