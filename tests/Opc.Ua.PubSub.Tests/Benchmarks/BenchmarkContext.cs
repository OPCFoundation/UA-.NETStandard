// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.Benchmarks
{
    /// <summary>
    /// Shared helpers used by the benchmark fixtures. Mirrors the
    /// helpers in <c>tests/Opc.Ua.PubSub.Tests/Encoding/*</c> but
    /// strips the test-framework dependencies so the benchmark host
    /// stays small.
    /// </summary>
    internal static class BenchmarkContext
    {
        private static readonly DataSetMetaDataRegistry s_registry = new();

        public static PubSubNetworkMessageContext NewContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                s_registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        public static IDataSetMetaDataRegistry Registry => s_registry;

        public static DataSetMetaDataType BuildScalarMetaData(
            string name,
            IReadOnlyList<(string FieldName, BuiltInType Type)> fields,
            uint majorVersion = 1U,
            uint minorVersion = 0U)
        {
            var fmd = new FieldMetaData[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                fmd[i] = new FieldMetaData
                {
                    Name = fields[i].FieldName,
                    BuiltInType = (byte)fields[i].Type,
                    ValueRank = ValueRanks.Scalar
                };
            }
            return new DataSetMetaDataType
            {
                Name = name,
                Fields = new ArrayOf<FieldMetaData>(fmd.AsMemory()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                }
            };
        }

        public static DataSetMetaDataType BuildArrayMetaData(
            string name,
            string fieldName,
            BuiltInType type,
            int length,
            uint majorVersion = 1U,
            uint minorVersion = 0U)
        {
            FieldMetaData[] fmd =
            [
                new FieldMetaData
                {
                    Name = fieldName,
                    BuiltInType = (byte)type,
                    ValueRank = ValueRanks.OneDimension,
                    ArrayDimensions = new ArrayOf<uint>(new uint[] { (uint)length })
                }
            ];
            return new DataSetMetaDataType
            {
                Name = name,
                Fields = new ArrayOf<FieldMetaData>(fmd.AsMemory()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                }
            };
        }
    }
}
