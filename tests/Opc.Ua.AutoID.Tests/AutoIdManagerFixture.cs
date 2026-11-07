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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Bounds gated test invocations and cleanup without replacing an earlier assertion failure.
    /// </summary>
    internal static class AutoIdGatedTest
    {
        /// <summary>
        /// Bounds two serialized operations, releases their gate and drains each before disposing the reader.
        /// </summary>
        public static async Task RunSerializedAsync<TFirst, TSecond>(
            AutoIdReader reader,
            Func<Task<TFirst>> first,
            Func<Task<TSecond>> second,
            Action release,
            Func<Task<Task<TFirst>>, Func<Task<Task<TSecond>>>, Task> assertPending,
            Func<TFirst, TSecond, Task> assertCompleted)
        {
            var failures = new List<Exception>();
            Task<Task<TFirst>> invocation = Task.Factory.StartNew(
                first, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            Task<Exception?> observer = ObserveAsync(async () =>
            {
                Task<TFirst> admitted = await invocation.ConfigureAwait(false);
                await admitted.ConfigureAwait(false);
            });
            Task<Task<TSecond>>? next = null;
            Task<Exception?>? nextObserver = null;

            Task<Task<TSecond>> StartSecond()
            {
                if (next == null)
                {
                    Task<Task<TSecond>> scheduled = Task.Factory.StartNew(
                        second, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                    next = scheduled;
                    nextObserver = ObserveAsync(async () =>
                    {
                        Task<TSecond> admitted = await scheduled.ConfigureAwait(false);
                        await admitted.ConfigureAwait(false);
                    });
                }
                return next;
            }

            try
            {
                await assertPending(invocation, StartSecond).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
            finally
            {
                await CaptureAsync(
                    () =>
                    {
                        release();
                        return Task.CompletedTask;
                    }, "Releasing the serialization gate", failures).ConfigureAwait(false);
            }

            TFirst firstResult = default!;
            TSecond secondResult = default!;
            bool firstCompleted = false;
            bool secondCompleted = false;
            await CaptureAsync(async () =>
            {
                firstResult = await DrainAsync(invocation, observer).ConfigureAwait(false);
                firstCompleted = true;
            }, "Draining the first operation", failures).ConfigureAwait(false);
            if (next != null && nextObserver != null)
            {
                await CaptureAsync(async () =>
                {
                    secondResult = await DrainAsync(next, nextObserver).ConfigureAwait(false);
                    secondCompleted = true;
                }, "Draining the second operation", failures).ConfigureAwait(false);
            }
            if (firstCompleted && secondCompleted)
            {
                Task<Task<bool>> assertions = Task.Factory.StartNew(async () =>
                {
                    await assertCompleted(firstResult, secondResult).ConfigureAwait(false);
                    return true;
                }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                Task<Exception?> assertionsObserver = ObserveAsync(async () =>
                {
                    Task<bool> admitted = await assertions.ConfigureAwait(false);
                    await admitted.ConfigureAwait(false);
                });
                await CaptureAsync(async () =>
                {
                    await DrainAsync(assertions, assertionsObserver).ConfigureAwait(false);
                }, "Checking serialized results", failures).ConfigureAwait(false);
            }

            Task<Task<bool>> disposal = Task.Factory.StartNew(async () =>
            {
                await reader.DisposeAsync().ConfigureAwait(false);
                return true;
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            Task<Exception?> disposalObserver = ObserveAsync(async () =>
            {
                Task<bool> admitted = await disposal.ConfigureAwait(false);
                await admitted.ConfigureAwait(false);
            });
            await CaptureAsync(async () =>
            {
                await DrainAsync(disposal, disposalObserver).ConfigureAwait(false);
            }, "Draining reader disposal", failures).ConfigureAwait(false);
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("Serialized test and cleanup failures.", failures);
            }
        }

        /// <summary>
        /// Releases the gate and drains the operation and reader disposal independently after the assertions.
        /// </summary>
        public static async Task RunAsync<T>(
            AutoIdReader reader,
            Func<Task<T>> operation,
            Action release,
            Func<Task<Task<T>>, Func<Task<Task>>, Task> assertPending,
            Action<T>? assertCompleted = null)
        {
            var failures = new List<Exception>();
            Task<Task<T>>? invocation = null;
            Task<Task>? disposal = null;
            Task<Exception?>? operationObserver = null;
            Task<Exception?>? disposalObserver = null;

            Task<Task> StartDisposal()
            {
                if (disposal == null)
                {
                    Task<Task> scheduled = Task.Factory.StartNew(
                        () => reader.DisposeAsync().AsTask(),
                        CancellationToken.None,
                        TaskCreationOptions.DenyChildAttach,
                        TaskScheduler.Default);
                    disposal = scheduled;
                    disposalObserver = ObserveAsync(
                        async () =>
                        {
                            Task admitted = await scheduled.ConfigureAwait(false);
                            await admitted.ConfigureAwait(false);
                        });
                }
                return disposal;
            }

            try
            {
                Task<Task<T>> scheduled = Task.Factory.StartNew(
                    operation, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                invocation = scheduled;
                operationObserver = ObserveAsync(
                    async () =>
                    {
                        Task<T> admitted = await scheduled.ConfigureAwait(false);
                        await admitted.ConfigureAwait(false);
                    });
                await assertPending(invocation, StartDisposal).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
            finally
            {
                try
                {
                    release();
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException("Releasing the test gate failed.", ex));
                }
            }

            await CaptureAsync(
                () =>
                {
                    StartDisposal();
                    return Task.CompletedTask;
                },
                "Scheduling reader disposal",
                failures).ConfigureAwait(false);
            if (invocation != null && operationObserver != null)
            {
                await CaptureAsync(
                    async () =>
                    {
                        Task<T> admitted = await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        Exception? failure = await operationObserver.WaitAsync(TimeSpan.FromSeconds(5))
                            .ConfigureAwait(false);
                        if (failure != null)
                        {
                            ExceptionDispatchInfo.Capture(failure).Throw();
                        }
                        T result = await admitted.ConfigureAwait(false);
                        assertCompleted?.Invoke(result);
                    },
                    "Draining the operation or checking its result",
                    failures).ConfigureAwait(false);
            }
            if (disposal != null && disposalObserver != null)
            {
                await CaptureAsync(
                    async () =>
                    {
                        await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        Exception? failure = await disposalObserver.WaitAsync(TimeSpan.FromSeconds(5))
                            .ConfigureAwait(false);
                        if (failure != null)
                        {
                            ExceptionDispatchInfo.Capture(failure).Throw();
                        }
                    },
                    "Draining reader disposal",
                    failures).ConfigureAwait(false);
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("Gated test and cleanup failures.", failures);
            }
        }

        /// <summary>
        /// Bounds the invocation and its retained observer before reading a successful result.
        /// </summary>
        private static async Task<T> DrainAsync<T>(Task<Task<T>> invocation, Task<Exception?> observer)
        {
            Task<T> admitted = await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Exception? failure = await observer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return await admitted.ConfigureAwait(false);
        }

        /// <summary>
        /// Consumes eventual outer and inner faults and returns them for bounded cleanup to inspect.
        /// </summary>
        private static async Task<Exception?> ObserveAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Records a distinct cleanup failure and lets subsequent cleanup steps run.
        /// </summary>
        private static async Task CaptureAsync(Func<Task> action, string step, List<Exception> failures)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!failures.Exists(failure =>
                    ReferenceEquals(failure, ex) || ReferenceEquals(failure.InnerException, ex)))
                {
                    failures.Add(new InvalidOperationException(step + " failed.", ex));
                }
            }
        }
    }

    /// <summary>
    /// Starts a server with an <see cref="AutoIdNodeManager"/> whose address space is created
    /// without going through the hosting pipeline, so tests can call the method handlers directly.
    /// </summary>
    internal sealed class AutoIdManagerFixture : IAsyncDisposable
    {
        public AutoIdNodeManager Manager { get; private set; } = null!;

        public AutoIdRuntime Runtime => Manager.Runtime;

        public ISystemContext Context => Manager.SystemContext;

        public async Task StartAsync(
            AutoIdServerOptions? options = null, Opc.Ua.Server.FileSystem.IFileTypeBinder? fileBinder = null)
        {
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Manager = new AutoIdNodeManager(server.CurrentInstance, m_fixture.Config, null,
                new Microsoft.Extensions.Options.OptionsWrapper<AutoIdServerOptions>(
                    options ?? new AutoIdServerOptions()), default, fileBinder);
            await Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
        }

        public QualifiedName Name(string name)
        {
            return new QualifiedName(name, Manager.InstanceNamespaceIndex);
        }

        public async ValueTask DisposeAsync()
        {
            if (Manager != null)
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private ServerFixture<StandardServer>? m_fixture;
    }

    /// <summary>
    /// A scriptable provider that records the calls the runtime makes.
    /// </summary>
    internal sealed class FakeProvider : IAutoIdScanProvider, IAutoIdAsyncScanProvider, IAutoIdRfidProvider,
        IAutoIdRfidAccessEventPolicy,
        IAsyncDisposable
    {
        public AutoIdReaderKind Kind { get; set; } = AutoIdReaderKind.Rfid;

        public AutoIdRfidOperations SupportedOperations { get; set; } = AutoIdRfidOperations.All;

        /// <inheritdoc/>
        public bool CanPublishData(string codeType, ushort region, uint offset, uint length)
        {
            return codeType == "EPC" && region is >= 1 and <= 3;
        }

        public IAutoIdReaderSink? Sink { get; private set; }

        public int AttachCalls { get; private set; }

        public int DetachCalls { get; private set; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? AttachException { get; set; }

        public Func<ScanSettings, CancellationToken, ValueTask<AutoIdScanOutcome>> OnScan { get; set; } =
            (settings, ct) => new ValueTask<AutoIdScanOutcome>(
                new AutoIdScanOutcome(default, AutoIdOperationStatusEnumeration.NO_IDENTIFIER));

        public Func<ScanSettings, CancellationToken, ValueTask<AutoIdOperationStatusEnumeration>> OnStart { get; set; } =
            (settings, ct) => new ValueTask<AutoIdOperationStatusEnumeration>(
                AutoIdOperationStatusEnumeration.SUCCESS);

        public Func<CancellationToken, ValueTask> OnStop { get; set; } = ct => default;

        /// <summary>
        /// Runs when the provider is detached.
        /// </summary>
        public Func<CancellationToken, ValueTask> OnDetach { get; set; } = ct => default;

        /// <summary>
        /// Runs when the provider is disposed.
        /// </summary>
        public Action? OnDispose { get; set; }

        public Func<CancellationToken, ValueTask<AutoIdTagReadOutcome>> OnRead { get; set; } =
            ct => new ValueTask<AutoIdTagReadOutcome>(
                new AutoIdTagReadOutcome(ByteString.From([1, 2]), AutoIdOperationStatusEnumeration.SUCCESS));

        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            AttachCalls++;
            Sink = sink;
            if (AttachException != null)
            {
                throw AttachException;
            }
            return default;
        }

        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            DetachCalls++;
            return OnDetach(cancellationToken);
        }

        public ValueTask<AutoIdScanOutcome> ScanAsync(ScanSettings settings, CancellationToken cancellationToken = default)
        {
            return OnScan(settings, cancellationToken);
        }

        public ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return OnStart(settings, cancellationToken);
        }

        public ValueTask StopScanAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return OnStop(cancellationToken);
        }

        public ValueTask<AutoIdTagReadOutcome> ReadTagAsync(
            ScanData identifier,
            string codeType,
            ushort region,
            uint offset,
            uint length,
            ByteString password,
            CancellationToken cancellationToken = default)
        {
            return OnRead(cancellationToken);
        }

        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagAsync(
            ScanData identifier, string codeType, ushort region, uint offset, ByteString data,
            ByteString password, CancellationToken cancellationToken = default)
        {
            return Success();
        }

        public ValueTask<AutoIdOperationStatusEnumeration> WriteTagIdAsync(
            ScanData identifier, string codeType, ByteString newUId, byte afi, bool toggle,
            ByteString password, CancellationToken cancellationToken = default)
        {
            return Success();
        }

        public ValueTask<AutoIdOperationStatusEnumeration> KillTagAsync(
            ScanData identifier, string codeType, ByteString killPassword,
            CancellationToken cancellationToken = default)
        {
            return Success();
        }

        public ValueTask<AutoIdOperationStatusEnumeration> LockTagAsync(
            ScanData identifier, string codeType, ByteString password, RfidLockRegionEnumeration region,
            RfidLockOperationEnumeration lockOperation, uint offset, uint length,
            CancellationToken cancellationToken = default)
        {
            return Success();
        }

        public ValueTask<AutoIdOperationStatusEnumeration> SetTagPasswordAsync(
            ScanData identifier, string codeType, RfidPasswordTypeEnumeration passwordType,
            ByteString accessPassword, ByteString newPassword, CancellationToken cancellationToken = default)
        {
            return Success();
        }

        public ValueTask DisposeAsync()
        {
            OnDispose?.Invoke();
            Disposed = true;
            return default;
        }

        private static ValueTask<AutoIdOperationStatusEnumeration> Success()
        {
            return new ValueTask<AutoIdOperationStatusEnumeration>(AutoIdOperationStatusEnumeration.SUCCESS);
        }
    }

    /// <summary>
    /// Builders for scan data and settings shared by the tests.
    /// </summary>
    internal static class AutoIdTestData
    {
        public static ScanSettings Settings(double duration = 0, int cycles = 0, bool dataAvailable = false)
        {
            return new ScanSettings { Duration = duration, Cycles = cycles, DataAvailable = dataAvailable };
        }

        public static ScanData Epc(ByteString epc)
        {
            return new ScanData
            {
                SwitchField = ScanDataFields.Epc,
                Epc = new ScanDataEpc { UId = epc }
            };
        }

        public static ByteString Bytes(params byte[] bytes)
        {
            return ByteString.From(bytes);
        }

        public static RfidScanResult RfidResult(ByteString epc)
        {
            return new RfidScanResult
            {
                CodeType = "EPC",
                ScanData = Epc(epc),
                Timestamp = DateTimeUtc.Now
            };
        }
    }
}
