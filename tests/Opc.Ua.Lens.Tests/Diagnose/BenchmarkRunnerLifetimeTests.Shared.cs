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
