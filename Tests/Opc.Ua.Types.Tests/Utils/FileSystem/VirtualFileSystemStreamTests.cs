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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils.FileSystem
{
#pragma warning disable CA2022 // Avoid inexact read with 'Stream.Read'
    /// <summary>
    /// Tests for VirtualFileSystem stream operations and edge cases
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VirtualFileSystemStreamTests
    {
        [Test]
        public void Stream_SeekOperations_WorkCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "seektest.txt";
            byte[] content = "0123456789ABCDEF"u8.ToArray();
            vfs.Add(filePath, content);

            // Act & Assert
            using Stream stream = vfs.OpenRead(filePath);

            // Test seeking from beginning
            long position = stream.Seek(5, SeekOrigin.Begin);
            Assert.That(position, Is.EqualTo(5));

            byte[] buffer = new byte[1];
            stream.Read(buffer, 0, 1);
            Assert.That(buffer[0], Is.EqualTo((byte)'5'));

            // Test seeking from current position
            position = stream.Seek(3, SeekOrigin.Current);
            Assert.That(position, Is.EqualTo(9));

            stream.Read(buffer, 0, 1);
            Assert.That(buffer[0], Is.EqualTo((byte)'9'));

            // Test seeking from end
            position = stream.Seek(-2, SeekOrigin.End);
            Assert.That(position, Is.EqualTo(content.Length - 2));

            stream.Read(buffer, 0, 1);
            Assert.That(buffer[0], Is.EqualTo((byte)'E'));
        }

        [Test]
        public void WriteStream_SeekOperations_ThrowWhenNotSupported()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "writetest.txt";
            vfs.Add(filePath, []);

            // Act & Assert
            using Stream stream = vfs.OpenRead(filePath);

            // Test set length throws exception
            InvalidOperationException lengthException = Assert.Throws<InvalidOperationException>(() => stream.SetLength(100));
            Assert.That(lengthException.Message, Contains.Substring("Cannot set a length when opened in read mode"));

            stream.SetLength(stream.Length); // Should not throw
        }

        [Test]
        public void Stream_ReadBeyondLength_ReturnsCorrectAmount()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "shortfile.txt";
            byte[] content = "Hello"u8.ToArray(); // 5 bytes
            vfs.Add(filePath, content);

            // Act
            using Stream stream = vfs.OpenRead(filePath);

            // Try to read more than available
            byte[] buffer = new byte[10];
            int bytesRead = stream.Read(buffer, 0, 10);

            // Assert
            Assert.That(bytesRead, Is.EqualTo(5)); // Only 5 bytes available
            Assert.That(buffer.Take(5), Is.EqualTo(content));
            Assert.That(stream.Position, Is.EqualTo(5));
        }

        [Test]
        public void Stream_ReadAtExactLength_ReturnsZero()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "exactlength.txt";
            byte[] content = "Hello"u8.ToArray();
            vfs.Add(filePath, content);

            // Act
            using Stream stream = vfs.OpenRead(filePath);

            // Read all content first
            byte[] buffer1 = new byte[5];
            stream.Read(buffer1, 0, 5);

            // Try to read more at end of file
            byte[] buffer2 = new byte[5];
            int bytesRead = stream.Read(buffer2, 0, 5);

            // Assert
            Assert.That(bytesRead, Is.EqualTo(0));
            Assert.That(stream.Position, Is.EqualTo(5));
        }

        [Test]
        public void Stream_MultipleWrites_UpdatePositionCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "multiwrite.txt";
            byte[] part1 = "Hello "u8.ToArray();
            byte[] part2 = "World"u8.ToArray();

            // Act
            using (Stream stream = vfs.OpenWrite(filePath))
            {
                stream.Write(part1, 0, part1.Length);
                Assert.That(stream.Position, Is.EqualTo(part1.Length));

                stream.Write(part2, 0, part2.Length);
                Assert.That(stream.Position, Is.EqualTo(part1.Length + part2.Length));

                stream.Flush();
            }

            // Assert
            byte[] expectedContent = "Hello World"u8.ToArray();
            Assert.That(vfs.Get(filePath), Is.EqualTo(expectedContent));
        }

        [Test]
        public void Stream_Properties_ReturnCorrectValues()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "properties.txt";
            byte[] content = "Hello World"u8.ToArray();
            vfs.Add(filePath, content);

            // Act & Assert - Read stream
            using (Stream readStream = vfs.OpenRead(filePath))
            {
                Assert.That(readStream.CanRead, Is.True);
                Assert.That(readStream.CanWrite, Is.False);
                Assert.That(readStream.CanSeek, Is.True);
                Assert.That(readStream.Length, Is.EqualTo(content.Length));
                Assert.That(readStream.Position, Is.EqualTo(0));
            }

            // Act & Assert - Write stream
            using Stream writeStream = vfs.OpenWrite(filePath);

            Assert.That(writeStream.CanRead, Is.False);
            Assert.That(writeStream.CanWrite, Is.True);
            Assert.That(writeStream.CanSeek, Is.True);
            Assert.That(writeStream.Position, Is.EqualTo(0));
        }

        [Test]
        public void Stream_Disposal_UpdatesFileLengthCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "disposaltest.txt";
            byte[] content = "Hello World"u8.ToArray();

            // Act - Write content and dispose stream
            using (Stream stream = vfs.OpenWrite(filePath))
            {
                stream.Write(content, 0, content.Length);
                // Stream disposal should update the file length
            }

            // Assert - File should have correct content
            Assert.That(vfs.Exists(filePath), Is.True);

            using Stream readStream = vfs.OpenRead(filePath);
            Assert.That(readStream.Length, Is.EqualTo(content.Length));

            byte[] buffer = new byte[content.Length];
            int bytesRead = readStream.Read(buffer, 0, buffer.Length);
            Assert.That(bytesRead, Is.EqualTo(content.Length));
            Assert.That(buffer, Is.EqualTo(content));
        }

        [Test]
        public void WriteAndReadBack_SingleFile_MaintainsConsistency()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "consistency.txt";
            byte[] originalContent = "This is the original content that should remain consistent"u8.ToArray();

            // Act & Assert - Write and read back multiple times
            for (int i = 0; i < 10; i++)
            {
                // Write content
                using (Stream writeStream = vfs.OpenWrite(filePath))
                {
                    writeStream.Write(originalContent, 0, originalContent.Length);
                }

                // Read content back and verify
                using (Stream readStream = vfs.OpenRead(filePath))
                {
                    byte[] buffer = new byte[originalContent.Length];
                    int bytesRead = readStream.Read(buffer, 0, buffer.Length);

                    Assert.That(bytesRead, Is.EqualTo(originalContent.Length));
                    Assert.That(buffer, Is.EqualTo(originalContent));
                }

                // Also verify with Get method
                byte[] retrievedContent = vfs.Get(filePath);
                Assert.That(retrievedContent, Is.EqualTo(originalContent));
            }
        }

        [Test]
        public void WriteAndReadBack_MultipleFiles_MaintainIndividualConsistency()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            var testData = new Dictionary<string, byte[]>
            {
                ["file1.txt"] = "Content for file 1"u8.ToArray(),
                ["file2.txt"] = "Different content for file 2 with more text"u8.ToArray(),
                ["file3.txt"] = "Short"u8.ToArray(),
                ["file4.txt"] = [],
                ["file5.txt"] = new byte[1000] // Large content
            };

            // Initialize large content with pattern
            for (int i = 0; i < testData["file5.txt"].Length; i++)
            {
                testData["file5.txt"][i] = (byte)(i % 256);
            }

            // Act & Assert - Write all files, then read them back multiple times
            foreach (KeyValuePair<string, byte[]> kvp in testData)
            {
                using Stream writeStream = vfs.OpenWrite(kvp.Key);
                writeStream.Write(kvp.Value, 0, kvp.Value.Length);
            }

            // Perform multiple read cycles to ensure consistency
            for (int cycle = 0; cycle < 5; cycle++)
            {
                foreach (KeyValuePair<string, byte[]> kvp in testData)
                {
                    // Test with stream reading
                    using (Stream readStream = vfs.OpenRead(kvp.Key))
                    {
                        byte[] buffer = new byte[kvp.Value.Length];
                        int bytesRead = readStream.Read(buffer, 0, buffer.Length);

                        Assert.That(bytesRead, Is.EqualTo(kvp.Value.Length));
                        Assert.That(buffer, Is.EqualTo(kvp.Value));
                    }

                    // Test with direct Get method
                    byte[] retrievedContent = vfs.Get(kvp.Key);
                    Assert.That(retrievedContent, Is.EqualTo(kvp.Value));
                }
            }
        }

        [Test]
        public void WriteAndReadBack_OverwriteContent_MaintainsNewContent()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "overwrite.txt";
            byte[][] contents =
            [
                "First version of content"u8.ToArray(),
                "Second version with different length and content"u8.ToArray(),
                "Third"u8.ToArray(),
                [],
                "Final version after empty"u8.ToArray()
            ];

            // Act & Assert - Write each version and verify it can be read back correctly
            for (int i = 0; i < contents.Length; i++)
            {
                byte[] currentContent = contents[i];

                // Write new content
                using (Stream writeStream = vfs.OpenWrite(filePath))
                {
                    writeStream.Write(currentContent, 0, currentContent.Length);
                }

                // Read back and verify multiple times to ensure consistency
                for (int readCycle = 0; readCycle < 3; readCycle++)
                {
                    using (Stream readStream = vfs.OpenRead(filePath))
                    {
                        Assert.That(readStream.Length, Is.EqualTo(currentContent.Length));

                        byte[] buffer = new byte[Math.Max(currentContent.Length, 1)]; // Ensure buffer is at least size 1
                        int bytesRead = readStream.Read(buffer, 0, currentContent.Length);

                        Assert.That(bytesRead, Is.EqualTo(currentContent.Length));

                        if (currentContent.Length > 0)
                        {
                            byte[] result = [.. buffer.Take(currentContent.Length)];
                            Assert.That(result, Is.EqualTo(currentContent));
                        }
                    }

                    // Also verify with Get method
                    byte[] retrievedContent = vfs.Get(filePath);
                    Assert.That(retrievedContent, Is.EqualTo(currentContent));
                }
            }
        }

        [Test]
        public void WriteAndReadBack_PartialWrites_BuildsContentCorrectly()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "partial.txt";
            byte[][] parts =
            [
                "Hello"u8.ToArray(),
                " "u8.ToArray(),
                "World"u8.ToArray(),
                "!"u8.ToArray(),
                " This is a longer ending."u8.ToArray()
            ];
            byte[] expectedFinalContent = [.. parts.SelectMany(p => p)];

            // Act - Write content in parts
            using (Stream writeStream = vfs.OpenWrite(filePath))
            {
                foreach (byte[] part in parts)
                {
                    writeStream.Write(part, 0, part.Length);
                }
            }

            // Assert - Read back and verify multiple times
            for (int readCycle = 0; readCycle < 5; readCycle++)
            {
                using (Stream readStream = vfs.OpenRead(filePath))
                {
                    Assert.That(readStream.Length, Is.EqualTo(expectedFinalContent.Length));

                    byte[] buffer = new byte[expectedFinalContent.Length];
                    int bytesRead = readStream.Read(buffer, 0, buffer.Length);

                    Assert.That(bytesRead, Is.EqualTo(expectedFinalContent.Length));
                    Assert.That(buffer, Is.EqualTo(expectedFinalContent));
                }

                // Also verify with Get method
                byte[] retrievedContent = vfs.Get(filePath);
                Assert.That(retrievedContent, Is.EqualTo(expectedFinalContent));
            }
        }

        [Test]
        public void WriteAndReadBack_RandomContent_MaintainsIntegrity()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const string filePath = "random.dat";
            foreach (int size in new[] { 0, 1, 10, 100, 1000, 10000 })
            {
                // Generate random content
                byte[] content = new byte[size];
                UnsecureRandom.Shared.NextBytes(content);

                // Act - Write random content
                using (Stream writeStream = vfs.OpenWrite(filePath))
                {
                    writeStream.Write(content, 0, content.Length);
                }

                // Assert - Read back and verify consistency multiple times
                for (int readAttempt = 0; readAttempt < 3; readAttempt++)
                {
                    using (Stream readStream = vfs.OpenRead(filePath))
                    {
                        Assert.That(readStream.Length, Is.EqualTo(content.Length));

                        if (content.Length > 0)
                        {
                            byte[] buffer = new byte[content.Length];
                            int bytesRead = readStream.Read(buffer, 0, buffer.Length);

                            Assert.That(bytesRead, Is.EqualTo(content.Length));
                            Assert.That(buffer, Is.EqualTo(content));
                        }
                    }

                    // Also verify with Get method
                    byte[] retrievedContent = vfs.Get(filePath);
                    Assert.That(retrievedContent, Is.EqualTo(content));
                }
            }
        }

        [Test]
        public async Task WriteAndReadBack_ConcurrentOperations_MaintainConsistency_Async()
        {
            // Arrange
            using var vfs = new VirtualFileSystem();
            const int fileCount = 10;
            const int cycleCount = 5;
            var contentMap = new Dictionary<string, byte[]>();

            // Generate unique content for each file
            for (int i = 0; i < fileCount; i++)
            {
                string filePath = $"concurrent{i}.txt";
                contentMap[filePath] = System.Text.Encoding.UTF8.GetBytes($"Content for file {i} - {DateTime.UtcNow.Ticks}");
            }

            // Act - Perform concurrent write/read operations
            var tasks = new List<Task>();

            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                foreach (KeyValuePair<string, byte[]> kvp in contentMap)
                {
                    string filePath = kvp.Key;
                    byte[] expectedContent = kvp.Value;

                    tasks.Add(Task.Run(() =>
                    {
                        // Write content
                        using (Stream writeStream = vfs.OpenWrite(filePath))
                        {
                            writeStream.Write(expectedContent, 0, expectedContent.Length);
                        }

                        // Immediately read back and verify
                        Thread.Sleep(1); // Small delay to allow for race conditions

                        using (Stream readStream = vfs.OpenRead(filePath))
                        {
                            byte[] buffer = new byte[expectedContent.Length];
                            int bytesRead = readStream.Read(buffer, 0, buffer.Length);

                            if (bytesRead != expectedContent.Length || !buffer.SequenceEqual(expectedContent))
                            {
                                throw new InvalidOperationException($"Content mismatch for {filePath}");
                            }
                        }

                        // Also verify with Get method
                        byte[] retrievedContent = vfs.Get(filePath);
                        if (!retrievedContent.SequenceEqual(expectedContent))
                        {
                            throw new InvalidOperationException($"Get method content mismatch for {filePath}");
                        }
                    }));
                }
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Final verification - ensure all files have correct content
            foreach (KeyValuePair<string, byte[]> kvp in contentMap)
            {
                Assert.That(vfs.Exists(kvp.Key), Is.True, $"File {kvp.Key} should exist");
                Assert.That(vfs.Get(kvp.Key), Is.EqualTo(kvp.Value));
            }
        }
    }
}
