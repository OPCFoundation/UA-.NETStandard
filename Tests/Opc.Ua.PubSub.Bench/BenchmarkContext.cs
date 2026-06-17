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
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Bench
{
    /// <summary>
    /// Shared helpers used by the benchmark fixtures. Mirrors the
    /// helpers in <c>Tests/Opc.Ua.PubSub.Tests/Encoding/*</c> but
    /// strips the test-framework dependencies so the benchmark binary
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
            FieldMetaData[] fmd = new FieldMetaData[fields.Count];
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
