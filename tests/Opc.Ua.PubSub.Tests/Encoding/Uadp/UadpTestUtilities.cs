// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Time.Testing;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Shared helpers for UADP encoder/decoder tests.
    /// </summary>
    internal static class UadpTestUtilities
    {
        public static PubSubNetworkMessageContext NewContext(
            IDataSetMetaDataRegistry? registry = null,
            IPubSubDiagnostics? diagnostics = null,
            TimeProvider? timeProvider = null,
            PubSubFieldEncoding uadpActionFieldEncoding = PubSubFieldEncoding.Variant)
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry ?? new DataSetMetaDataRegistry(),
                diagnostics ?? new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                timeProvider ??
                new FakeTimeProvider(
                    new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)),
                uadpActionFieldEncoding);
        }
    }
}
