// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class PubSubConfigurationFileTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ImportsVersionedAndPlainDocumentsWithOptionalBomWithoutOwningTheStreamAsync(
        bool versioned, bool bom)
    {
        PubSubConfiguration expected = PubSubTestRuntime.Configuration with
        {
            Fields = [new() { Name = "Temperature", Type = BuiltInType.Double }]
        };
        string json = versioned
            ? PubSubStateCodec.Capture(expected).GetRawText()
            : PubSubStateCodec.Format(expected);
        byte[] data = Encoding.UTF8.GetBytes((bom ? "\uFEFF" : string.Empty) + json);
        using var stream = new MemoryStream(data);
        PubSubConfiguration actual = await PubSubConfigurationFiles.ReadAsync(stream).ConfigureAwait(false);

        Assert.That(actual.Endpoint, Is.EqualTo(expected.Endpoint));
        Assert.That(actual.Fields[0].Name, Is.EqualTo("Temperature"));
        Assert.That(actual.Fields[0].Type, Is.EqualTo(BuiltInType.Double));
        Assert.That(actual.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
        Assert.That(actual.Publication, Is.EqualTo(PubSubPublication.Disabled));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(data.Length));
    }

    [TestCase(65536, true)]
    [TestCase(65537, false)]
    public async Task CharacterBudgetHasAnInclusiveExactBoundaryAsync(int length, bool accepted)
    {
        string json = PubSubStateCodec.Format(new PubSubConfiguration()).PadRight(length);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (accepted)
        {
            PubSubConfiguration configuration = await PubSubConfigurationFiles.ReadAsync(stream).ConfigureAwait(false);
            Assert.That(configuration.Endpoint, Is.Empty);
            Assert.That(configuration.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }
        else
        {
            await Assert.ThatAsync(() => PubSubConfigurationFiles.ReadAsync(stream),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
        }
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public async Task ByteBudgetStopsReadingBeforeAnUnboundedStreamCanBeConsumedAsync()
    {
        byte[] bytes = new byte[PubSubConfigurationFiles.MaxConfigurationBytes + 2048];
        using var stream = new MemoryStream(bytes);
        await Assert.ThatAsync(() => PubSubConfigurationFiles.ReadAsync(stream),
            Throws.TypeOf<JsonException>().With.Message.Contains("byte limit")).ConfigureAwait(false);
        Assert.That(stream.Position, Is.EqualTo(PubSubConfigurationFiles.MaxConfigurationBytes + 1));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase("")]
    [TestCase("[]")]
    [TestCase("{\"version\":2,\"configuration\":{}}")]
    [TestCase("{\"fields\":[{\"name\":\"A\",\"name\":\"B\"}]}")]
    [TestCase("{\"securityKey\":\"not-a-real-key\"}")]
    public async Task MalformedUnsupportedAndActiveConfigurationFailsExplicitlyAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await Assert.ThatAsync(() => PubSubConfigurationFiles.ReadAsync(stream),
            Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public async Task InvalidUtf8AndPreCanceledReadsAreNotEmptySuccessesAsync()
    {
        using var invalid = new MemoryStream(s_invalidUtf8);
        await Assert.ThatAsync(() => PubSubConfigurationFiles.ReadAsync(invalid),
            Throws.TypeOf<DecoderFallbackException>()).ConfigureAwait(false);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        await Assert.ThatAsync(() => PubSubConfigurationFiles.ReadAsync(content, new CancellationToken(true)),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(content.Position, Is.Zero);
        Assert.That(content.CanRead, Is.True);
    }

    [Test]
    public async Task ExportReplacesTheCompleteDocumentAndRejectedOrCanceledExportRetainsTheOldFileAsync()
    {
        string file = Path.Combine(Path.GetTempPath(),
            "ualens-pubsub-configuration-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, "old file that must be replaced").ConfigureAwait(false);
            PubSubConfiguration expected = PubSubTestRuntime.Configuration with { DataSetWriterId = 321 };
            await PubSubConfigurationFiles.WriteAsync(file, expected).ConfigureAwait(false);
            byte[] saved = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            using var stream = new MemoryStream(saved);
            PubSubConfiguration actual = await PubSubConfigurationFiles.ReadAsync(stream).ConfigureAwait(false);
            Assert.That(actual.DataSetWriterId, Is.EqualTo(321));
            Assert.That(actual.Endpoint, Is.EqualTo(expected.Endpoint));
            Assert.That(Encoding.UTF8.GetString(saved), Does.Not.Contain("allowUnsecured").And.Not.Contain("password"));

            await Assert.ThatAsync(() => PubSubConfigurationFiles.WriteAsync(
                file, expected with { Fields = [] }), Throws.ArgumentException).ConfigureAwait(false);
            Assert.That(await File.ReadAllBytesAsync(file).ConfigureAwait(false), Is.EqualTo(saved));
            await Assert.ThatAsync(() => PubSubConfigurationFiles.WriteAsync(file,
                expected with { DataSetWriterId = 123 }, new CancellationToken(true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(await File.ReadAllBytesAsync(file).ConfigureAwait(false), Is.EqualTo(saved));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static readonly byte[] s_invalidUtf8 = [0xC3, 0x28];
}
