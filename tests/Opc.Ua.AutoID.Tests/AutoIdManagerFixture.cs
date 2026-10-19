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
using Opc.Ua.AutoID.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Starts a server with an <see cref="AutoIdNodeManager"/> whose address space is created
    /// without going through the hosting pipeline, so tests can call the method handlers directly.
    /// </summary>
    internal sealed class AutoIdManagerFixture : IAsyncDisposable
    {
        public AutoIdNodeManager Manager { get; private set; } = null!;

        public AutoIdRuntime Runtime => Manager.Runtime;

        public ISystemContext Context => Manager.SystemContext;

        public async Task StartAsync(AutoIdServerOptions? options = null)
        {
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Manager = options == null
                ? new AutoIdNodeManager(server.CurrentInstance, m_fixture.Config)
                : new AutoIdNodeManager(server.CurrentInstance, m_fixture.Config, options);
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
        IAsyncDisposable
    {
        public AutoIdReaderKind Kind { get; set; } = AutoIdReaderKind.Rfid;

        public AutoIdRfidOperations SupportedOperations { get; set; } = AutoIdRfidOperations.All;

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
            return default;
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
