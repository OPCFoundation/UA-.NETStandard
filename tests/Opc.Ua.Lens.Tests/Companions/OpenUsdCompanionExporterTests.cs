// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class OpenUsdCompanionExporterTests
{
    [Test]
    public async Task ExportWritesVerifiedBytesAndTheExistingUsdFileSinkOutputAsync()
    {
        string parent = NewTestDirectory();
        string destination = Path.Combine(parent, "export");
        var exporter = new OpenUsdCompanionExporter();
        const string rootContent = "#usda 1.0\n\ndef Xform \"World\" {}\n";
        try
        {
            await exporter.WriteAsync(
                destination, "robot.usda", "/World/Robot",
                [new OpenUsdVerifiedAsset("robot.usda", new ByteString(Encoding.UTF8.GetBytes(rootContent)))],
                [new OpenUsdCompanionBindingValue("/World/Robot", "temperature", Variant.From(21.5d))],
                CancellationToken.None).ConfigureAwait(false);

            string asset = await File.ReadAllTextAsync(Path.Combine(destination, "robot.usda")).ConfigureAwait(false);
            string snapshot = await File.ReadAllTextAsync(Path.Combine(destination, "ualens-snapshot.usda"))
                .ConfigureAwait(false);
            string stage = await File.ReadAllTextAsync(Path.Combine(destination, "ualens-stage.usda"))
                .ConfigureAwait(false);
            Assert.That(asset, Is.EqualTo(rootContent));
            Assert.That(snapshot, Does.Contain("#usda 1.0").And.Contain("temperature").And.Contain("21.5"));
            Assert.That(stage, Does.Contain("@./robot.usda@").And.Contain("@./ualens-snapshot.usda@"));
            Assert.That(Directory.GetFiles(destination), Has.Length.EqualTo(3));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Test]
    public async Task ExistingDestinationsAreNeverOverwrittenAsync()
    {
        string parent = NewTestDirectory();
        string destination = Path.Combine(parent, "existing");
        Directory.CreateDirectory(destination);
        string marker = Path.Combine(destination, "keep.txt");
        try
        {
            await File.WriteAllTextAsync(marker, "keep existing data").ConfigureAwait(false);
            var exporter = new OpenUsdCompanionExporter();
            Assert.That(
                async () => await exporter.WriteAsync(
                    destination, "robot.usda", "/World/Robot", [], [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<IOException>());
            string saved = await File.ReadAllTextAsync(marker).ConfigureAwait(false);
            Assert.That(saved, Is.EqualTo("keep existing data"));
            Assert.That(Directory.GetFiles(destination), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Test]
    public void CancellationBeforeExportCreatesNoOutput()
    {
        string parent = NewTestDirectory();
        string destination = Path.Combine(parent, "cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            var exporter = new OpenUsdCompanionExporter();
            Assert.That(
                async () => await exporter.WriteAsync(
                    destination, "robot.usda", "/World/Robot", [], [], cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestCase("../outside.usda")]
    [TestCase("/absolute.usda")]
    [TestCase("C:/drive.usda")]
    [TestCase("file@inject.usda")]
    [TestCase("layers//root.usda")]
    [TestCase("NUL")]
    public void InvalidAssetPathsDoNotCreateAnyDirectory(string identifier)
    {
        string parent = NewTestDirectory();
        string destination = Path.Combine(parent, "invalid");
        try
        {
            var exporter = new OpenUsdCompanionExporter();
            Assert.That(
                async () => await exporter.WriteAsync(
                    destination, "robot.usda", "/World/Robot",
                    [new OpenUsdVerifiedAsset(identifier, ByteString.Empty)], [], CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Test]
    public void InvalidPrimAndPropertyNamesAreRejectedRatherThanSilentlyDroppedByTheSink()
    {
        Assert.That(
            () => OpenUsdCompanionExporter.ValidatePrimPath("/World/Robot\"inject"),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(
            () => OpenUsdCompanionExporter.ValidatePropertyName("temperature = 4"),
            Throws.TypeOf<ServiceResultException>());
    }

    private static string NewTestDirectory()
    {
        string path = OpenUsdTestPaths.NewDestination();
        Directory.CreateDirectory(path);
        return path;
    }
}
