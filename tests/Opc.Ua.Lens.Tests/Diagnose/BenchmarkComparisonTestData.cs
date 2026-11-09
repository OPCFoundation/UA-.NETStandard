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
 * WHETHER IN AN ACTION OF CONTRACT, TORT OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

internal static class BenchmarkComparisonTestData
{
    public static BenchmarkConfiguration Configuration()
    {
        return new BenchmarkConfiguration
        {
            Mode = BenchmarkMode.Write,
            Generator = ValueGenerator.Fixed,
            TargetRate = 100,
            DurationSeconds = 10,
            MaxConcurrency = 4,
            TargetNodeId = "nsu=urn:ualens:comparison;s=Value",
            TargetType = BuiltInType.Double,
            TargetValueRank = ValueRanks.Scalar,
            InputArguments = ArrayOf<BenchmarkArgumentConfiguration>.Empty,
            EndpointUrl = "opc.tcp://localhost:4840/",
            ServerApplicationUri = "urn:ualens:comparison:server",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserTokenType = UserTokenType.Anonymous
        };
    }

    public static BenchmarkRun Captured(int sequence = 0, BenchmarkConfiguration? configuration = null)
    {
        var histogram = new LatencyHistogram();
        histogram.Record(1);
        histogram.Record(1);
        histogram.Record(2);
        histogram.Record(8);
        return BenchmarkRun.Create(configuration ?? Configuration(), histogram.Capture(), 1,
            TimeSpan.FromSeconds(2), Epoch.AddSeconds(sequence), BenchmarkCompletion.Completed, "Captured run");
    }

    public static BenchmarkRun Aggregate(int sequence = 0)
    {
        return new BenchmarkRun
        {
            TimestampUtc = Epoch.AddSeconds(sequence),
            TargetRate = 100,
            AchievedRate = 100,
            TotalOps = 200,
            MeanLatencyMs = 2,
            P50Ms = 1,
            P90Ms = 5,
            P99Ms = 10,
            ErrorCount = 2,
            Notes = "mode=Write; gen=Fixed; target=Value"
        };
    }

    public static readonly DateTime Epoch = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
}
