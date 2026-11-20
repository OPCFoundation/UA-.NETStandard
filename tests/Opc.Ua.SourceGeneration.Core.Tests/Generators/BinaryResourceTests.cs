/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Unit tests for <see cref = "BinaryResource"/>.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinaryResourceTests
    {
        /// <summary>
        /// Tests GetLength returns zero for an empty byte array.
        /// Input: Empty byte array.
        /// Expected: Returns 0.
        /// </summary>
        [Test]
        public void GetLength_EmptyByteArray_ReturnsZero()
        {
            // Arrange
            byte[] data = [];
            var resource = new BinaryResource("TestResource", data);
            // Act
            long length = resource.GetLength(null!);
            // Assert
            Assert.That(length, Is.Zero);
        }

        /// <summary>
        /// Tests GetLength returns correct length for various byte array sizes.
        /// Input: Byte arrays of different sizes.
        /// Expected: Returns the correct array length.
        /// </summary>
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1024)]
        [TestCase(10000)]
        [TestCase(1048576)]
        public void GetLength_VariousSizes_ReturnsCorrectLength(int size)
        {
            // Arrange
            byte[] data = new byte[size];
            var resource = new BinaryResource("TestResource", data);
            // Act
            long length = resource.GetLength(null!);
            // Assert
            Assert.That(length, Is.EqualTo(size));
        }

        /// <summary>
        /// Tests GetLength does not use the fileSystem parameter and returns correct length.
        /// Input: Valid byte array and mocked IFileSystem.
        /// Expected: Returns correct length without accessing fileSystem.
        /// </summary>
        [Test]
        public void GetLength_WithMockedFileSystem_ReturnsCorrectLengthWithoutUsingParameter()
        {
            // Arrange
            byte[] data = new byte[42];
            var resource = new BinaryResource("TestResource", data);
            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            // Act
            long length = resource.GetLength(mockFileSystem.Object);
            // Assert
            Assert.That(length, Is.EqualTo(42));
            mockFileSystem.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Tests GetLength with null IFileSystem parameter.
        /// Input: Valid byte array and null fileSystem.
        /// Expected: Returns correct length (parameter not used).
        /// </summary>
        [Test]
        public void GetLength_NullFileSystem_ReturnsCorrectLength()
        {
            // Arrange
            byte[] data = new byte[123];
            var resource = new BinaryResource("TestResource", data);
            // Act
            long length = resource.GetLength(null!);
            // Assert
            Assert.That(length, Is.EqualTo(123));
        }

        /// <summary>
        /// Tests GetLength returns long type for large arrays demonstrating implicit int to long conversion.
        /// Input: Large byte array near int.MaxValue boundary.
        /// Expected: Returns correct length as long.
        /// </summary>
        [Test]
        public void GetLength_LargeArray_ReturnsLongType()
        {
            // Arrange
            byte[] data = new byte[int.MaxValue / 1000];
            var resource = new BinaryResource("TestResource", data);
            // Act
            long length = resource.GetLength(null!);
            // Assert
            Assert.That(length, Is.EqualTo(int.MaxValue / 1000));
            Assert.That(length, Is.TypeOf<long>());
        }

        /// <summary>
        /// Tests GetLength throws NullReferenceException when Data is null.
        /// Input: BinaryResource with null Data array.
        /// Expected: Throws NullReferenceException.
        /// </summary>
        [Test]
        public void GetLength_NullData_ThrowsNullReferenceException()
        {
            // Arrange
            var resource = new BinaryResource("TestResource", null!);
            // Act & Assert
            Assert.Throws<NullReferenceException>(() => resource.GetLength(null!));
        }
    }
}
