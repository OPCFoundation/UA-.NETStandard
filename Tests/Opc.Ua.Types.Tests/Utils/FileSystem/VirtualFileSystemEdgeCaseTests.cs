/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils.FileSystem
{
#pragma warning disable CA2022 // Avoid inexact read with 'Stream.Read'
    /// <summary>
    /// Tests for VirtualFileSystem error conditions and edge cases
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VirtualFileSystemEdgeCaseTests
    {
        [Test]
        public void FilePath_WithSpecialCharacters_WorksCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            string[] filePaths =
            [
                "file with spaces.txt",
                "file-with-dashes.txt",
                "file_with_underscores.txt",
                "file.with.dots.txt",
                "file@with#symbols$.txt"
            ];
            byte[] content = "test content"u8.ToArray();

            // Act & Assert
            foreach (string filePath in filePaths)
            {
                vfs.Add(filePath, content);
                Assert.That(vfs.Exists(filePath), Is.True);
                Assert.That(vfs.Get(filePath), Is.EqualTo(content));
            }

            Assert.That(vfs.Files.Count(), Is.EqualTo(filePaths.Length));
        }

        [Test]
        public void FilePath_VeryLong_WorksCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string longPath = "very/long/path/with/many/nested/directories/and/a/very/long/filename/that/exceeds/normal/expectations/test.txt";
            byte[] content = "test content"u8.ToArray();

            // Act
            vfs.Add(longPath, content);

            // Assert
            Assert.That(vfs.Exists(longPath), Is.True);
            Assert.That(vfs.Get(longPath), Is.EqualTo(content));
        }

        [Test]
        public void Add_EmptyPath_HandlesGracefully()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            byte[] content = "test content"u8.ToArray();

            // Act & Assert - Empty string
            vfs.Add(string.Empty, content);
            Assert.That(vfs.Exists(string.Empty), Is.True);
            Assert.That(vfs.Get(string.Empty), Is.EqualTo(content));
        }

        [Test]
        public void Add_NullContent_ThrowsException()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "test.txt";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => vfs.Add(filePath, null!));
        }

        [Test]
        public void ManyFiles_WorksCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const int fileCount = 1000;
            byte[] content = "test content"u8.ToArray();

            // Act
            for (int i = 0; i < fileCount; i++)
            {
                vfs.Add($"file{i:D4}.txt", content);
            }

            // Assert
            Assert.That(vfs.Files.Count(), Is.EqualTo(fileCount));

            // Verify a few random files
            var random = new Random(42); // Fixed seed for reproducible tests
            for (int i = 0; i < 10; i++)
            {
                int index = random.Next(fileCount);
                string filePath = $"file{index:D4}.txt";

                Assert.That(vfs.Exists(filePath), Is.True);
                Assert.That(vfs.Get(filePath), Is.EqualTo(content));
            }
        }

        [Test]
        public void FileOperations_UnicodePaths_WorkCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            string[] unicodePaths =
            [
                "файл.txt", // Russian
                "文件.txt", // Chinese
                "ファイル.txt", // Japanese
                "파일.txt", // Korean
                "αρχείο.txt", // Greek
                "файл🎉.txt" // With emoji
            ];
            byte[] content = "Unicode test content"u8.ToArray();

            // Act & Assert
            foreach (string filePath in unicodePaths)
            {
                vfs.Add(filePath, content);
                Assert.That(vfs.Exists(filePath), Is.True);
                Assert.That(vfs.Get(filePath), Is.EqualTo(content));
            }
        }

        [Test]
        public void MemoryPressure_LargeFiles_HandledGracefully()
        {
            // Arrange
            var rnd = new Random();
            using var vfs = new VirtualFileSystem();

            // Act - Create several moderately large files
            for (int i = 0; i < 5; i++)
            {
                string filePath = $"large{i}.dat";
                byte[] largeContent = new byte[1024 * 1024]; // 1MB each
                rnd.NextBytes(largeContent);

                vfs.Add(filePath, largeContent);
            }

            // Assert - All files should be accessible
            Assert.That(vfs.Files.Count(), Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                string filePath = $"large{i}.dat";
                Assert.That(vfs.Exists(filePath), Is.True);

                byte[] content = vfs.Get(filePath);
                Assert.That(content.Length, Is.EqualTo(1024 * 1024));
            }
        }

        [Test]
        public void RapidAddDelete_WorksCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "rapidtest.txt";
            byte[] content = "test content"u8.ToArray();

            // Act & Assert - Rapid add/delete cycles
            for (int i = 0; i < 100; i++)
            {
                vfs.Add(filePath, content);
                Assert.That(vfs.Exists(filePath), Is.True);

                vfs.Delete(filePath);
                Assert.That(vfs.Exists(filePath), Is.False);
            }

            Assert.That(vfs.Files, Is.Empty);
        }

        [Test]
        public void FileOperations_AfterPartialDisposal_StillWork()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath1 = "file1.txt";
            const string filePath2 = "file2.txt";
            byte[] content = "test content"u8.ToArray();

            vfs.Add(filePath1, content);
            vfs.Add(filePath2, content);

            // Act - Delete one file
            vfs.Delete(filePath1);

            // Assert - Other file should still work
            Assert.That(vfs.Exists(filePath1), Is.False);
            Assert.That(vfs.Exists(filePath2), Is.True);
            Assert.That(vfs.Get(filePath2), Is.EqualTo(content));
            Assert.That(vfs.Files.Count(), Is.EqualTo(1));
        }

        [Test]
        public void StreamOperations_ZeroLengthBuffers_WorkCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "zerobuffer.txt";
            byte[] content = "test content"u8.ToArray();
            vfs.Add(filePath, content);

            // Act & Assert - Reading with zero-length buffer
            using (Stream stream = vfs.OpenRead(filePath))
            {
                byte[] buffer = [];
                int bytesRead = stream.Read(buffer, 0, 0);
                Assert.That(bytesRead, Is.EqualTo(0));
                Assert.That(stream.Position, Is.EqualTo(0));
            }

            // Act & Assert - Writing with zero-length buffer
            using (Stream stream = vfs.OpenWrite("empty.txt"))
            {
                byte[] buffer = [];
                stream.Write(buffer, 0, 0);
                Assert.That(stream.Position, Is.EqualTo(0));
            }
        }

        [Test]
        public void FilePaths_WithDirectorySeparators_WorkCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            string[] filePaths =
            [
                "folder/file.txt",
                "folder\\file2.txt",
                "deep/nested/folder/file.txt",
                "deep\\nested\\folder\\file2.txt"
            ];
            byte[] content = "nested content"u8.ToArray();

            // Act & Assert
            foreach (string filePath in filePaths)
            {
                vfs.Add(filePath, content);
                Assert.That(vfs.Exists(filePath), Is.True);
                Assert.That(vfs.Get(filePath), Is.EqualTo(content));
            }

            // Different separators should be treated as different paths
            Assert.That(vfs.Files.Count(), Is.EqualTo(filePaths.Length));
        }

        [Test]
        public void GetLastWriteTime_NonExistentPhysicalFile_FallsBackGracefully()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string nonExistentPath = "this/file/does/not/exist/anywhere.txt";

            // Act & Assert - Should not throw, but return default time from FileInfo
            DateTime result = vfs.GetLastWriteTime(nonExistentPath);

            // The result will be whatever FileInfo returns for non-existent files
            // This is implementation detail, but should not throw
            Assert.That(result, Is.TypeOf<DateTime>());
        }

        [Test]
        public void StreamPosition_PartialReads_WorksCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "partialread.txt";
            byte[] content = "0123456789"u8.ToArray(); // 10 bytes
            vfs.Add(filePath, content);

            // Act & Assert
            using Stream stream = vfs.OpenRead(filePath);
            byte[] buffer = new byte[15]; // Larger than content

            // First read - should read all available
            int firstRead = stream.Read(buffer, 0, 15);
            Assert.That(firstRead, Is.EqualTo(10));
            Assert.That(stream.Position, Is.EqualTo(10));

            // Second read - should return 0 (end of file)
            int secondRead = stream.Read(buffer, 0, 15);
            Assert.That(secondRead, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(10));
        }

        [Test]
        public void Add_OverwriteExistingFile_UpdatesContent()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "overwrite.txt";
            byte[] originalContent = "Original content"u8.ToArray();
            byte[] newContent = "New content that is different"u8.ToArray();

            // Act
            vfs.Add(filePath, originalContent);
            Assert.That(vfs.Get(filePath), Is.EqualTo(originalContent));

            vfs.Add(filePath, newContent);

            // Assert
            Assert.That(vfs.Get(filePath), Is.EqualTo(newContent));
            Assert.That(vfs.Files.Count, Is.EqualTo(1)); // Should still be only one file
        }

        [Test]
        public void OpenRead_NonExistentFile_ThrowsException()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => vfs.OpenRead("nonexistent.txt"));
        }

        [DatapointSource]
        internal static string[] Paths =
        [
            string.Empty,
            "   ",
            "\t",
            "\n"
        ];

        [Theory]
        public void FilePath_WithWhitespace_HandlesCorrectly(string filePath)
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            byte[] content = "test content"u8.ToArray();

            // Act
            vfs.Add(filePath, content);

            // Assert
            Assert.That(vfs.Exists(filePath), Is.True);
            Assert.That(vfs.Get(filePath), Is.EqualTo(content));
        }
    }
}
