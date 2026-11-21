// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Shared helpers for PubSub fuzz targets.
    /// </summary>
    public static partial class FuzzableCode
    {
        internal static PublisherId SeedPublisherId => PublisherId.FromUInt16(300);

        internal static Uuid SeedDataSetClassId => new("aabbccdd-1122-3344-5566-778899aabbcc");

        internal static DateTimeOffset SeedTime => new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Prints information about the fuzzer target.
        /// </summary>
        public static void FuzzInfo()
        {
            Console.WriteLine("OPC UA PubSub fuzzer for UADP, UADP chunk reassembly and JSON decode.");
        }

        internal static PubSubNetworkMessageContext NewContext(TimeProvider? timeProvider = null)
        {
            TimeProvider clock = timeProvider ?? new FixedTimeProvider();
            DataSetMetaDataType metaData = CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(SeedPublisherId, 0, 1, SeedDataSetClassId, 1),
                metaData);
            registry.Register(
                new DataSetMetaDataKey(SeedPublisherId, 1, 1, SeedDataSetClassId, 1),
                metaData);
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock),
                clock);
        }

        internal static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "RetainedFuzzDataSet",
                DataSetClassId = SeedDataSetClassId,
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 2
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Running",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Count",
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Label",
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        internal static byte[] CopyCapped(ReadOnlySpan<byte> input)
        {
            return input.Length <= kMaxFuzzInputBytes
                ? input.ToArray()
                : input[..kMaxFuzzInputBytes].ToArray();
        }

        internal static byte[] ReadCapped(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            byte[] buffer = new byte[4096];
            int remaining = kMaxFuzzInputBytes;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    break;
                }

                memoryStream.Write(buffer, 0, read);
                remaining -= read;
            }

            return memoryStream.ToArray();
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return SeedTime;
            }
        }

        private const int kMaxFuzzInputBytes = 1024 * 1024;
    }
}
