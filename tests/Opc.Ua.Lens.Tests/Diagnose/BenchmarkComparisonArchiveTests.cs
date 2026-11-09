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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class BenchmarkComparisonArchiveTests
{
    [Test]
    public void JsonRoundTripPreservesSelectedBaselineConfigurationAndActualBuckets()
    {
        BenchmarkRun baseline = BenchmarkComparisonTestData.Captured();
        BenchmarkRun selected = BenchmarkComparisonTestData.Captured(1) with { Notes = "comma, quote \" and\nnewline" };
        var archive = new BenchmarkArchive([baseline, selected], baseline.Id, selected.Id);
        BenchmarkArchive restored = BenchmarkArchiveCodec.Parse(BenchmarkArchiveCodec.Serialize(archive));

        Assert.That(restored.IsLegacyCsv, Is.False);
        Assert.That(restored.BaselineId, Is.EqualTo(baseline.Id));
        Assert.That(restored.SelectedId, Is.EqualTo(selected.Id));
        Assert.That(restored.Runs.Count, Is.EqualTo(2));
        Assert.That(restored.Runs[1].Id, Is.EqualTo(selected.Id));
        Assert.That(restored.Runs[1].Configuration, Is.EqualTo(selected.Configuration));
        Assert.That(restored.Runs[1].ElapsedSeconds, Is.EqualTo(2));
        Assert.That(restored.Runs[1].Notes, Is.EqualTo(selected.Notes));
        Assert.That(restored.Runs[1].Distribution!.BucketCounts,
            Is.EqualTo(selected.Distribution!.BucketCounts));
        Assert.That(restored.Runs[1].Distribution!.SampleCount, Is.EqualTo(4));
        Assert.That(restored.Runs[1].MeanLatencyMs, Is.EqualTo(3));
        Assert.That(new BenchmarkComparison(restored.Runs[0], restored.Runs[1]).IsComparable, Is.True);
    }

    [Test]
    public void LegacyCsvRetainsQuotedMultilineNotesWithoutInventingConfigurationOrDistributions()
    {
        BenchmarkRun original = BenchmarkComparisonTestData.Aggregate() with { Notes = "first,\"second\"\r\nthird" };
        string csv = BenchmarkArchiveCodec.SerializeCsv(new BenchmarkArchive([original], original.Id, original.Id));
        BenchmarkArchive restored = BenchmarkArchiveCodec.Parse(csv);

        Assert.That(restored.IsLegacyCsv, Is.True);
        Assert.That(restored.Runs.Count, Is.EqualTo(1));
        Assert.That(restored.Runs[0].Notes, Is.EqualTo(original.Notes));
        Assert.That(restored.Runs[0].TotalOps, Is.EqualTo(200));
        Assert.That(restored.Runs[0].ErrorCount, Is.EqualTo(2));
        Assert.That(restored.Runs[0].Configuration, Is.Null);
        Assert.That(restored.Runs[0].Distribution, Is.Null);
        Assert.That(restored.Runs[0].ElapsedSeconds, Is.Null);
        Assert.That(restored.BaselineId, Is.Null);
        Assert.That(restored.SelectedId, Is.Null);
        Assert.That(restored.Runs[0].Completion, Is.EqualTo(BenchmarkCompletion.Unknown));
    }

    [Test]
    public void MalformedCsvAfterAValidRowCannotProducePartialHistory()
    {
        string valid = BenchmarkArchiveCodec.SerializeCsv(
            new BenchmarkArchive([BenchmarkComparisonTestData.Aggregate()]));
        Assert.That(() => BenchmarkArchiveCodec.Parse(valid + "malformed,row"),
            Throws.TypeOf<FormatException>().With.Message.Contains("no history"));
        Assert.That(() => BenchmarkArchiveCodec.Parse(valid + "\"unterminated"),
            Throws.TypeOf<FormatException>().With.Message.Contains("unterminated"));
    }

    [TestCase("version")]
    [TestCase("distribution-version")]
    [TestCase("negative-bucket")]
    [TestCase("aggregate-count")]
    [TestCase("throughput")]
    [TestCase("duplicate-id")]
    [TestCase("unknown-baseline")]
    public void CorruptJsonCannotMasqueradeAsComparableEvidence(string corruption)
    {
        BenchmarkRun original = BenchmarkComparisonTestData.Captured();
        JsonNode root = JsonNode.Parse(BenchmarkArchiveCodec.Serialize(new BenchmarkArchive([original])))!;
        JsonNode run = root["runs"]![0]!;
        switch (corruption)
        {
            case "version":
                root["version"] = 99;
                break;
            case "distribution-version":
                run["distribution"]!["schemaVersion"] = 99;
                break;
            case "negative-bucket":
                run["distribution"]!["counts"]![0] = -1;
                break;
            case "aggregate-count":
                run["totalOps"] = 5;
                break;
            case "throughput":
                run["achievedRate"] = 12345;
                break;
            case "duplicate-id":
                root["runs"]!.AsArray().Add(run.DeepClone());
                break;
            case "unknown-baseline":
                root["baselineId"] = Guid.NewGuid();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        Assert.That(() => BenchmarkArchiveCodec.Parse(root.ToJsonString()), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void UnknownJsonPropertiesAreRejectedRatherThanSilentlyDropped()
    {
        string json = BenchmarkArchiveCodec.Serialize(new BenchmarkArchive(ArrayOf<BenchmarkRun>.Empty));
        JsonNode root = JsonNode.Parse(json)!;
        root["startAutomatically"] = true;
        Assert.That(() => BenchmarkArchiveCodec.Parse(root.ToJsonString()), Throws.TypeOf<JsonException>());
    }

    [Test]
    public void CharacterLimitAllowsTheExactBoundaryAndRejectsOneMore()
    {
        string json = BenchmarkArchiveCodec.Serialize(new BenchmarkArchive(ArrayOf<BenchmarkRun>.Empty));
        string atLimit = json.PadRight(BenchmarkArchiveCodec.MaxImportCharacters);
        Assert.That(BenchmarkArchiveCodec.Parse(atLimit).Runs.IsEmpty, Is.True);
        Assert.That(() => BenchmarkArchiveCodec.Parse(atLimit + " "), Throws.TypeOf<FormatException>());
        Assert.That(() => BenchmarkArchiveCodec.Parse(" \r\n"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void ImportCountLimitAndEmptyArchiveHaveExplicitBoundaries()
    {
        var runs = new BenchmarkRun[BenchmarkArchiveCodec.MaxImportedRuns + 1];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = BenchmarkComparisonTestData.Aggregate(i);
        }
        new BenchmarkArchive(new ArrayOf<BenchmarkRun>(runs.AsMemory(0, runs.Length - 1))).Validate();
        Assert.That(() => new BenchmarkArchive(runs).Validate(), Throws.TypeOf<FormatException>());
        Assert.That(() => new BenchmarkArchive(ArrayOf<BenchmarkRun>.Null).Validate(),
            Throws.TypeOf<FormatException>());
        Assert.That(BenchmarkArchiveCodec.Parse(BenchmarkArchiveCodec.Serialize(
            new BenchmarkArchive(ArrayOf<BenchmarkRun>.Empty))).Runs.IsNull, Is.False);
    }

    [Test]
    public async Task StreamReadHonorsCancellationAndDoesNotOwnTheInputStreamAsync()
    {
        BenchmarkRun run = BenchmarkComparisonTestData.Captured();
        string json = BenchmarkArchiveCodec.Serialize(new BenchmarkArchive([run]));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => BenchmarkArchiveCodec.ReadAsync(stream, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(stream.CanRead, Is.True);
        stream.Position = 0;
        BenchmarkArchive result = await BenchmarkArchiveCodec.ReadAsync(stream).ConfigureAwait(false);
        Assert.That(result.Runs[0].Id, Is.EqualTo(run.Id));
        Assert.That(result.Runs[0].Distribution!.BucketCounts, Is.EqualTo(run.Distribution!.BucketCounts));
        Assert.That(stream.CanRead, Is.True);
    }
}
