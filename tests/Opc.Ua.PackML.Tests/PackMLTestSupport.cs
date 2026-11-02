/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PackML.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PackML.Tests
{
    internal static class PackMLTestSupport
    {
        /// <summary>
        /// Requires a materialized generated child before inspecting its value.
        /// </summary>
        /// <typeparam name="T">The required generated node type.</typeparam>
        public static T Required<T>(this T? node) where T : NodeState
        {
            return node ?? throw new AssertionException("A required PackML child was not materialized.");
        }

        public static SystemContext CreateContext(int precedingNamespaces = 0)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
            for (int i = 0; i < precedingNamespaces; i++)
            {
                context.NamespaceUris.GetIndexOrAppend("urn:packml:test:padding:" + i);
            }
            ushort index = context.NamespaceUris.GetIndexOrAppend("urn:packml:test:units");
            context.NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Numeric, index);
            _ = new NodeStateCollection().AddOpcUaPackML(context);
            return context;
        }

        public static QualifiedName Name(ISystemContext context, string name = "Unit") =>
            new(name, checked((ushort)context.NamespaceUris.GetIndex("urn:packml:test:units")));

        public static TaskCompletionSource<bool> Signal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class EquipmentProvider : IPackMLEquipmentProvider
    {
        public PackMLEquipmentSnapshot Snapshot { get; set; } = new()
        {
            CurrentMode = 1,
            CurrentMachineSpeed = 12.5f,
            SourceTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            StatusCode = StatusCodes.Good
        };

        public ServiceResult Authorization { get; set; } = ServiceResult.Good;
        public ServiceResult Acceptance { get; set; } = ServiceResult.Good;
        public List<PackMLRequest> Authorized { get; } = [];
        public List<PackMLRequest> Executed { get; } = [];
        public Func<PackMLRequest, CancellationToken, ValueTask<ServiceResult>>? Execute { get; set; }
        public Func<CancellationToken, ValueTask>? Callback { get; set; }
        public int CallbackPhase { get; set; }

        public async ValueTask<ServiceResult> AuthorizeAsync(
            ISystemContext context, PackMLRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorized.Add(request);
            if (CallbackPhase == 0 && Callback is { } callback)
            {
                await callback(cancellationToken);
            }
            return Authorization;
        }

        public async ValueTask<ServiceResult> ExecuteAsync(
            PackMLRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executed.Add(request);
            if (CallbackPhase == 1 && Callback is { } callback)
            {
                await callback(cancellationToken);
            }
            return Execute is { } execute ? await execute(request, cancellationToken) : Acceptance;
        }

        public async ValueTask<PackMLEquipmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CallbackPhase == 2 && Callback is { } callback)
            {
                await callback(cancellationToken);
            }
            return Snapshot;
        }
    }

    internal sealed class ManualTimeProvider : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => m_ticks;
        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(m_ticks);
        public void Advance(TimeSpan amount) => m_ticks += amount.Ticks;
        private long m_ticks;
    }

    internal sealed class MemoryAdministrationStore : IPackMLAdministrationStore
    {
        public PackMLAdministrationSnapshot? Snapshot { get; set; }
        public int Loads { get; private set; }
        public int Saves { get; private set; }
        public Exception? SaveException { get; set; }
        public ValueTask<PackMLAdministrationSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Loads++;
            return new(Snapshot);
        }
        public ValueTask SaveAsync(PackMLAdministrationSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SaveException is { } exception)
            {
                throw exception;
            }
            Saves++;
            Snapshot = snapshot;
            return default;
        }
    }
}
