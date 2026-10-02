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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.StateMachine;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub;

internal sealed class PubSubTestRuntime
{
    public PubSubTestRuntime()
    {
        Application.SetupGet(app => app.ApplicationId).Returns("document-test");
        Application.SetupGet(app => app.Connections).Returns(Array.Empty<IPubSubConnection>());
        Application.SetupGet(app => app.MetaDataRegistry).Returns(Metadata);
        Application.SetupGet(app => app.Diagnostics).Returns(Diagnostics);
        Application.SetupGet(app => app.State).Returns(State);
        Application.Setup(app => app.StartAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                State.TryEnable();
                State.TryMarkOperational();
                Started.TrySetResult();
                return ValueTask.CompletedTask;
            });
        Application.Setup(app => app.StopAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                State.TryDisable();
                Stopped.TrySetResult();
                return ValueTask.CompletedTask;
            });
        Application.Setup(app => app.DisposeAsync()).Returns(() =>
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        });
        Application.Setup(app => app.GetConfiguration()).Returns(new PubSubConfigurationDataType());
        Factory.Setup(factory => factory.Inspect(It.IsAny<PubSubConfiguration>(), It.IsAny<bool>()))
            .Returns(ArrayOf<PubSubPrerequisite>.Empty);
        Factory.Setup(factory => factory.UsesPrimarySession(It.IsAny<PubSubConfiguration>())).Returns(false);
        Factory.Setup(factory => factory.CreateAsync(
            It.IsAny<PubSubConfiguration>(),
            It.IsAny<PubSubStartAuthorization>(),
            It.IsAny<ISession>(),
            It.IsAny<PubSubObservationStore>(),
            It.IsAny<CancellationToken>()))
            .Returns((PubSubConfiguration _, PubSubStartAuthorization _, ISession? _, PubSubObservationStore store,
                CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Store = store;
                return ValueTask.FromResult(new PubSubRuntimeHandle(Application.Object, releasePrimaryBindings: () =>
                {
                    PrimaryReleased.TrySetResult();
                    return ValueTask.CompletedTask;
                }));
            });
    }

    public Mock<IPubSubApplication> Application { get; } = new(MockBehavior.Strict);

    public Mock<IPubSubRuntimeFactory> Factory { get; } = new(MockBehavior.Strict);

    public DataSetMetaDataRegistry Metadata { get; } = new();

    public PubSubDiagnostics Diagnostics { get; } = new(PubSubDiagnosticsLevel.High);

    public PubSubStateMachine State { get; } = new(
        "Test application", PubSubComponentKind.Application, NullLogger.Instance);

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource PrimaryReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PubSubObservationStore? Store { get; private set; }

    public static PubSubConfiguration Configuration => new()
    {
        Endpoint = "opc.udp://239.0.0.1:49331",
        NetworkInterface = "127.0.0.1",
        SecurityMode = MessageSecurityMode.None
    };

    public static PubSubStartAuthorization ReceiveAuthorization => new(AllowUnsecured: true);

    public static DataSetField Field(int value, string name = "Value", int fieldIndex = -1)
    {
        return new DataSetField
        {
            Name = name,
            Value = new Variant(value),
            FieldIndex = fieldIndex,
            StatusCode = StatusCodes.Good,
            SourceTimestamp = DateTimeUtc.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        };
    }

    public PubSubWorkspace CreateWorkspace(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        return new PubSubWorkspace(Factory.Object, DefaultTelemetry.Create(static _ => { }), delay: delay);
    }
}

internal sealed class PubSubTestClock : TimeProvider
{
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        return s_epoch.AddTicks(Interlocked.Read(ref m_ticks));
    }

    public override long GetTimestamp()
    {
        return Interlocked.Read(ref m_ticks);
    }

    public void Advance(TimeSpan duration)
    {
        Interlocked.Add(ref m_ticks, duration.Ticks);
    }

    private static readonly DateTimeOffset s_epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long m_ticks;
}
