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
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class PerformanceStateTests
{
    private static PerformanceRestoredState RoundTrip(PerformanceStateDto dto)
    {
        JsonElement element = JsonSerializer.SerializeToElement(
            dto, PerformanceStateJsonContext.Default.PerformanceStateDto);
        PerformanceStateDto restoredDto = element.Deserialize(
            PerformanceStateJsonContext.Default.PerformanceStateDto)!;
        return PerformanceState.Validate(restoredDto);
    }

    [Test]
    public void RoundTripsAWriteTargetAndWorkloadSettings()
    {
        var target = new BenchmarkTarget(
            BenchmarkMode.Write, new NodeId("Temp", 3), NodeId.Null, BuiltInType.Double, -1, null, "Temperature");

        PerformanceStateDto dto = PerformanceState.CreateDto(
            BenchmarkMode.Write, 150, unboundedBurst: true, durationValue: 3,
            DurationUnit.Hours, ValueGenerator.Random, compareLast3: false, target);
        PerformanceRestoredState restored = RoundTrip(dto);

        Assert.That(restored.Mode, Is.EqualTo(BenchmarkMode.Write));
        Assert.That(restored.TargetRate, Is.EqualTo(150));
        Assert.That(restored.UnboundedBurst, Is.True);
        Assert.That(restored.DurationValue, Is.EqualTo(3));
        Assert.That(restored.DurationUnit, Is.EqualTo(DurationUnit.Hours));
        Assert.That(restored.Generator, Is.EqualTo(ValueGenerator.Random));
        Assert.That(restored.Target, Is.Not.Null);
        Assert.That(restored.Target!.NodeId, Is.EqualTo(new NodeId("Temp", 3)));
        Assert.That(restored.Target.BuiltInType, Is.EqualTo(BuiltInType.Double));
        Assert.That(restored.Target.ObjectId.IsNull, Is.True);
    }

    [Test]
    public void RoundTripsACallTargetPreservingArgumentBuiltInTypes()
    {
        var arguments = new[]
        {
            new Argument { Name = "count", DataType = new NodeId((uint)BuiltInType.UInt32), ValueRank = -1 }
        };
        var target = new BenchmarkTarget(
            BenchmarkMode.Call, new NodeId(1234u), new NodeId(1u), BuiltInType.Int32, -1, arguments, "DoWork");

        PerformanceStateDto dto = PerformanceState.CreateDto(
            BenchmarkMode.Call, 300, unboundedBurst: false, durationValue: 2,
            DurationUnit.Minutes, ValueGenerator.Sequential, compareLast3: true, target);
        PerformanceRestoredState restored = RoundTrip(dto);

        Assert.That(restored.CompareLast3, Is.True);
        Assert.That(restored.Target, Is.Not.Null);
        Assert.That(restored.Target!.Mode, Is.EqualTo(BenchmarkMode.Call));
        Assert.That(restored.Target.ObjectId.IsNull, Is.False);
        Assert.That(restored.Target.InputArguments, Is.Not.Null);
        Assert.That(restored.Target.InputArguments, Has.Length.EqualTo(1));
        Assert.That(ValueFactory.BuiltInForArgument(restored.Target.InputArguments[0]),
            Is.EqualTo(BuiltInType.UInt32));
    }

    [Test]
    public void ConfigurationWithoutATargetRoundTrips()
    {
        PerformanceStateDto dto = PerformanceState.CreateDto(
            BenchmarkMode.Write, 200, false, 1, DurationUnit.Seconds, ValueGenerator.Fixed, false, target: null);
        PerformanceRestoredState restored = RoundTrip(dto);

        Assert.That(restored.Target, Is.Null);
        Assert.That(restored.Generator, Is.EqualTo(ValueGenerator.Fixed));
    }

    [Test]
    public void UnknownVersionIsRejected()
    {
        var dto = new PerformanceStateDto { Version = 42 };
        Assert.That(() => PerformanceState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void InvalidTargetNodeIdIsRejected()
    {
        var dto = new PerformanceStateDto { Target = new PerformanceTargetDto { NodeId = "not-a-node-id" } };
        Assert.That(() => PerformanceState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void OutOfRangeDurationIsRejected()
    {
        var dto = new PerformanceStateDto { DurationValue = 0 };
        Assert.That(() => PerformanceState.Validate(dto), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void InvalidModeIsRejected()
    {
        var dto = new PerformanceStateDto { Mode = 999 };
        Assert.That(() => PerformanceState.Validate(dto), Throws.InstanceOf<FormatException>());
    }
}
