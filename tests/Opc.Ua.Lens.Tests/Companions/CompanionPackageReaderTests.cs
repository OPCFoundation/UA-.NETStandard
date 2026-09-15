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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class CompanionPackageReaderTests
{
    [TestCase(0, 1, false)]
    [TestCase(1, 1, true)]
    [TestCase(2, 1, false)]
    [TestCase(0, 4, false)]
    [TestCase(1, 4, true)]
    [TestCase(3, 4, true)]
    [TestCase(4, 4, true)]
    [TestCase(5, 4, false)]
    public async Task LocalFileLengthMustBeWithinTheInclusiveNonemptyLimitAsync(
        int length, int maximum, bool accepted)
    {
        using var file = new CompanionTemporaryPackage();
        byte[] contents = [.. Enumerable.Range(1, length).Select(value => (byte)value)];
        await File.WriteAllBytesAsync(file.Path, contents).ConfigureAwait(false);
        var reader = new CompanionPackageReader();

        if (accepted)
        {
            ByteString snapshot = await reader.ReadAsync(file.Path, maximum, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(snapshot.IsNull, Is.False);
            Assert.That(snapshot.Length, Is.EqualTo(length));
            Assert.That(snapshot.Span.ToArray(), Is.EqualTo(contents));
        }
        else
        {
            await Assert.ThatAsync(
                () => reader.ReadAsync(file.Path, maximum, CancellationToken.None).AsTask(),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("path")).ConfigureAwait(false);
        }

        byte[] remaining = await File.ReadAllBytesAsync(file.Path).ConfigureAwait(false);
        Assert.That(remaining, Is.EqualTo(contents));
        Assert.That(new FileInfo(file.Path).Length, Is.EqualTo(length));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    public async Task EmptyPathIsRejectedBeforeAnyFileAccessAsync(string? path)
    {
        var reader = new CompanionPackageReader();

        await Assert.ThatAsync(
            () => reader.ReadAsync(path!, 1, CancellationToken.None).AsTask(),
            Throws.InstanceOf<ArgumentException>().With.Property("ParamName").EqualTo("path")).ConfigureAwait(false);
    }

    [TestCase("package.bin")]
    [TestCase(@".\package.bin")]
    [TestCase(@"..\package.bin")]
    [TestCase(@"C:package.bin")]
    [TestCase(@"\package.bin")]
    [TestCase(@"\\server.invalid\share\package.bin")]
    [TestCase(@"\\localhost\share\package.bin")]
    [TestCase(@"\\?\UNC\server.invalid\share\package.bin")]
    public async Task RelativeDriveRelativeAndNetworkSharePathsAreRejectedWithoutOpeningThemAsync(string path)
    {
        var reader = new CompanionPackageReader();

        await Assert.ThatAsync(
            () => reader.ReadAsync(path, 1024, CancellationToken.None).AsTask(),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("path")
                .And.Message.Contains("absolute local package path")).ConfigureAwait(false);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public async Task NonpositiveMaximumIsRejectedBeforeOpeningTheSelectedPathAsync(int maximum)
    {
        using var file = new CompanionTemporaryPackage();
        var reader = new CompanionPackageReader();

        await Assert.ThatAsync(
            () => reader.ReadAsync(file.Path, maximum, CancellationToken.None).AsTask(),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("maximumBytes"))
            .ConfigureAwait(false);

        Assert.That(File.Exists(file.Path), Is.False);
    }

    [Test]
    public async Task AbsolutePathWithALiteralSpaceCanBeReadFromAReadOnlyFileAsync()
    {
        using var file = new CompanionTemporaryPackage();
        await File.WriteAllBytesAsync(file.Path, new byte[] { 0x00, 0x7F, 0x80, 0xFF }).ConfigureAwait(false);
        File.SetAttributes(file.Path, FileAttributes.ReadOnly);
        var reader = new CompanionPackageReader();

        ByteString snapshot = await reader.ReadAsync(file.Path, 4, CancellationToken.None).ConfigureAwait(false);

        Assert.That(System.IO.Path.IsPathFullyQualified(file.Path), Is.True);
        Assert.That(System.IO.Path.GetFileName(file.Path), Does.Contain(" "));
        Assert.That(snapshot.Span.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x7F, 0x80, 0xFF }));
        Assert.That(File.GetAttributes(file.Path).HasFlag(FileAttributes.ReadOnly), Is.True);
        Assert.That(new FileInfo(file.Path).Length, Is.EqualTo(4));
    }

    [Test]
    public async Task ReturnedSnapshotSurvivesFileReplacementAndNoReadHandleIsRetainedAsync()
    {
        using var file = new CompanionTemporaryPackage();
        await File.WriteAllBytesAsync(file.Path, new byte[] { 0x61, 0x62, 0x63 }).ConfigureAwait(false);
        var reader = new CompanionPackageReader();

        ByteString original = await reader.ReadAsync(file.Path, 8, CancellationToken.None).ConfigureAwait(false);
        var replacement = new FileStream(
            file.Path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using (replacement.ConfigureAwait(false))
        {
            await replacement.WriteAsync(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 }).ConfigureAwait(false);
        }
        ByteString current = await reader.ReadAsync(file.Path, 8, CancellationToken.None).ConfigureAwait(false);
        File.Delete(file.Path);

        Assert.That(original.Span.ToArray(), Is.EqualTo(new byte[] { 0x61, 0x62, 0x63 }));
        Assert.That(original.Length, Is.EqualTo(3));
        Assert.That(current.Span.ToArray(), Is.EqualTo(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 }));
        Assert.That(current.Length, Is.EqualTo(5));
        Assert.That(File.Exists(file.Path), Is.False);
    }

    [Test]
    public async Task MissingLocalFilePropagatesTheFileErrorRatherThanReturningAnEmptyPackageAsync()
    {
        using var file = new CompanionTemporaryPackage();
        var reader = new CompanionPackageReader();

        await Assert.ThatAsync(
            () => reader.ReadAsync(file.Path, 1, CancellationToken.None).AsTask(),
            Throws.TypeOf<FileNotFoundException>()).ConfigureAwait(false);

        Assert.That(File.Exists(file.Path), Is.False);
    }

    [Test]
    public async Task AFileHeldForExclusiveWritingCannotBeSnapshottedAsync()
    {
        using var file = new CompanionTemporaryPackage();
        await File.WriteAllBytesAsync(file.Path, new byte[] { 0x11, 0x22 }).ConfigureAwait(false);
        var reader = new CompanionPackageReader();
        var writer = new FileStream(
            file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
        await using (writer.ConfigureAwait(false))
        {
            await Assert.ThatAsync(
                () => reader.ReadAsync(file.Path, 2, CancellationToken.None).AsTask(),
                Throws.InstanceOf<IOException>()).ConfigureAwait(false);
            Assert.That(writer.Position, Is.Zero);
            Assert.That(writer.Length, Is.EqualTo(2));
        }

        ByteString recovered = await reader.ReadAsync(file.Path, 2, CancellationToken.None).ConfigureAwait(false);
        Assert.That(recovered.Span.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AlreadyCanceledReadCannotOpenAFileOrMaskCancellationWithAnIoFailureAsync(bool exists)
    {
        using var file = new CompanionTemporaryPackage();
        if (exists)
        {
            await File.WriteAllBytesAsync(file.Path, new byte[] { 0x41, 0x42 }).ConfigureAwait(false);
        }
        var reader = new CompanionPackageReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThatAsync(
            () => reader.ReadAsync(file.Path, 2, cancellation.Token).AsTask(),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(File.Exists(file.Path), Is.EqualTo(exists));
        if (exists)
        {
            byte[] remaining = await File.ReadAllBytesAsync(file.Path).ConfigureAwait(false);
            Assert.That(remaining, Is.EqualTo(new byte[] { 0x41, 0x42 }));
        }
    }
}

internal sealed class CompanionTemporaryPackage : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "ualens typed package " + Guid.NewGuid().ToString("N") + ".bin");

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.SetAttributes(Path, FileAttributes.Normal);
            File.Delete(Path);
        }
    }
}
