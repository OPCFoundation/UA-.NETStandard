/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils.FileSystem
{
    /// <summary>
    /// Tests for file systems that support atomic file replacement.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class AtomicFileReplaceTests
    {
        [SetUp]
        public void SetUp()
        {
            m_testDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AtomicFileReplaceTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_testDirectory))
            {
                Directory.Delete(m_testDirectory, recursive: true);
            }
        }

        [Test]
        public void LocalFileSystemReplaceMovesSourceWhenDestinationDoesNotExist()
        {
            var fileSystem = new LocalFileSystem();
            string sourcePath = GetLocalPath("staged.bin");
            string destinationPath = GetLocalPath("published.bin");
            byte[] sourceContent = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(sourcePath, sourceContent);

            fileSystem.Replace(sourcePath, destinationPath);

            Assert.That(File.Exists(sourcePath), Is.False);
            Assert.That(File.Exists(destinationPath), Is.True);
            Assert.That(File.ReadAllBytes(destinationPath), Is.EqualTo(sourceContent));
        }

        [Test]
        public void LocalFileSystemReplaceCreatesTheDestinationDirectory()
        {
            var fileSystem = new LocalFileSystem();
            string sourcePath = GetLocalPath("staged.bin");
            string destinationDirectory = Path.Combine(m_testDirectory, "published", "nested");
            string destinationPath = Path.Combine(destinationDirectory, "published.bin");
            byte[] sourceContent = "content for a directory that does not exist yet"u8.ToArray();
            File.WriteAllBytes(sourcePath, sourceContent);

            fileSystem.Replace(sourcePath, destinationPath);

            Assert.That(Directory.Exists(destinationDirectory), Is.True);
            Assert.That(File.Exists(sourcePath), Is.False);
            Assert.That(File.ReadAllBytes(destinationPath), Is.EqualTo(sourceContent));
        }

        [Test]
        public void LocalFileSystemReplaceOverwritesExistingDestinationWithCompleteSource()
        {
            var fileSystem = new LocalFileSystem();
            string sourcePath = GetLocalPath("staged.bin");
            string destinationPath = GetLocalPath("published.bin");
            byte[] sourceContent = Enumerable.Range(0, 1024).Select(i => (byte)(255 - (i % 256))).ToArray();
            byte[] destinationContent = "old complete destination"u8.ToArray();
            File.WriteAllBytes(sourcePath, sourceContent);
            File.WriteAllBytes(destinationPath, destinationContent);

            fileSystem.Replace(sourcePath, destinationPath);

            Assert.That(File.Exists(sourcePath), Is.False);
            Assert.That(File.ReadAllBytes(destinationPath), Is.EqualTo(sourceContent));
            Assert.That(File.ReadAllBytes(destinationPath), Is.Not.EqualTo(destinationContent));
        }

        [Test]
        public void LocalFileSystemReplaceMissingSourceThrowsFileNotFoundException()
        {
            var fileSystem = new LocalFileSystem();
            string sourcePath = GetLocalPath("missing.bin");
            string destinationPath = GetLocalPath("published.bin");
            File.WriteAllBytes(destinationPath, "existing destination"u8.ToArray());

            Assert.That(
                () => fileSystem.Replace(sourcePath, destinationPath),
                Throws.TypeOf<FileNotFoundException>());
            Assert.That(File.ReadAllBytes(destinationPath), Is.EqualTo("existing destination"u8.ToArray()));
        }

        [Test]
        public void VirtualFileSystemReplaceMovesSourceWhenDestinationDoesNotExist()
        {
            using var fileSystem = new VirtualFileSystem();
            const string sourcePath = "staged.bin";
            const string destinationPath = "published.bin";
            byte[] sourceContent = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray();
            fileSystem.Add(sourcePath, sourceContent);

            fileSystem.Replace(sourcePath, destinationPath);

            Assert.That(fileSystem.Exists(sourcePath), Is.False);
            Assert.That(fileSystem.Exists(destinationPath), Is.True);
            Assert.That(fileSystem.Get(destinationPath), Is.EqualTo(sourceContent));
        }

        [Test]
        public void VirtualFileSystemReplaceOverwritesExistingDestinationWithCompleteSource()
        {
            using var fileSystem = new VirtualFileSystem();
            const string sourcePath = "staged.bin";
            const string destinationPath = "published.bin";
            byte[] sourceContent = Enumerable.Range(0, 1024).Select(i => (byte)(255 - (i % 256))).ToArray();
            byte[] destinationContent = "old complete destination"u8.ToArray();
            fileSystem.Add(sourcePath, sourceContent);
            fileSystem.Add(destinationPath, destinationContent);
            Assert.That(fileSystem.Get(destinationPath), Is.EqualTo(destinationContent));

            fileSystem.Replace(sourcePath, destinationPath);

            Assert.That(fileSystem.Exists(sourcePath), Is.False);
            Assert.That(fileSystem.Get(destinationPath), Is.EqualTo(sourceContent));
            Assert.That(fileSystem.Get(destinationPath), Is.Not.EqualTo(destinationContent));
        }

        [Test]
        public void VirtualFileSystemReplaceMissingSourceThrowsFileNotFoundException()
        {
            using var fileSystem = new VirtualFileSystem();
            const string sourcePath = "missing.bin";
            const string destinationPath = "published.bin";
            byte[] destinationContent = "existing destination"u8.ToArray();
            fileSystem.Add(destinationPath, destinationContent);

            Assert.That(
                () => fileSystem.Replace(sourcePath, destinationPath),
                Throws.TypeOf<FileNotFoundException>()
                    .With.Property(nameof(FileNotFoundException.FileName)).EqualTo(sourcePath));
            Assert.That(fileSystem.Get(destinationPath), Is.EqualTo(destinationContent));
        }

        [Test]
        public void VirtualFileSystemReplaceConcurrentlyPublishesCompleteSource()
        {
            using var fileSystem = new VirtualFileSystem();
            const string destinationPath = "published.bin";
            byte[] initialContent = "initial content"u8.ToArray();
            byte[][] contents = Enumerable.Range(0, 32)
                .Select(i => Enumerable.Repeat((byte)i, 256 + i).ToArray())
                .ToArray();
            fileSystem.Add(destinationPath, initialContent);

            Task[] tasks = contents.Select((content, index) => Task.Run(() =>
            {
                string sourcePath = "staged" + index + ".bin";
                fileSystem.Add(sourcePath, content);
                fileSystem.Replace(sourcePath, destinationPath);
            })).ToArray();
            Task.WaitAll(tasks);

            byte[] published = fileSystem.Get(destinationPath);
            Assert.That(contents, Has.Some.EqualTo(published));
            for (int ii = 0; ii < contents.Length; ii++)
            {
                Assert.That(fileSystem.Exists("staged" + ii + ".bin"), Is.False);
            }
        }

        [Test]
        public void CombinedFileSystemReplaceUsesSecondaryByDefault()
        {
            using var primary = new VirtualFileSystem();
            using var secondary = new VirtualFileSystem();
            var fileSystem = new CombinedFileSystem(primary, secondary);
            byte[] sourceContent = "secondary content"u8.ToArray();
            secondary.Add("staged.bin", sourceContent);

            fileSystem.Replace("staged.bin", "published.bin");

            Assert.That(primary.Exists("published.bin"), Is.False);
            Assert.That(secondary.Get("published.bin"), Is.EqualTo(sourceContent));
        }

        [Test]
        public void CombinedFileSystemReplaceUsesPrimaryWhenConfiguredForPrimaryWrites()
        {
            using var primary = new VirtualFileSystem();
            using var secondary = new VirtualFileSystem();
            var fileSystem = new CombinedFileSystem(primary, secondary, usePrimaryForWrite: true);
            byte[] sourceContent = "primary content"u8.ToArray();
            primary.Add("staged.bin", sourceContent);

            fileSystem.Replace("staged.bin", "published.bin");

            Assert.That(primary.Get("published.bin"), Is.EqualTo(sourceContent));
            Assert.That(secondary.Exists("published.bin"), Is.False);
        }

        [Test]
        public void NullFileSystemReplaceThrowsFileNotFoundException()
        {
            NullFileSystem fileSystem = NullFileSystem.Instance;

            Assert.That(
                () => fileSystem.Replace("staged.bin", "published.bin"),
                Throws.TypeOf<FileNotFoundException>()
                    .With.Property(nameof(FileNotFoundException.FileName)).EqualTo("staged.bin"));
        }

        [Test]
        public void ResourceFileSystemReplaceThrowsIOException()
        {
            var fileSystem = new ResourceFileSystem(Assembly.GetExecutingAssembly());

            Assert.That(
                () => fileSystem.Replace("staged.bin", "published.bin"),
                Throws.TypeOf<IOException>());
        }

        private string GetLocalPath(string fileName)
        {
            return Path.Combine(m_testDirectory, fileName);
        }

        private string m_testDirectory = string.Empty;
    }
}
