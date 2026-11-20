/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils.FileSystem
{
    /// <summary>
    /// Tests for the deny-all <see cref="NullFileSystem"/>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NullFileSystemTests
    {
        [Test]
        public void ExistsAlwaysReturnsFalse()
        {
            Assert.That(NullFileSystem.Instance.Exists("anything", isDirectory: false), Is.False);
            Assert.That(NullFileSystem.Instance.Exists("anything", isDirectory: true), Is.False);
        }

        [Test]
        public void OpenReadThrows()
        {
            Assert.That(
                () => NullFileSystem.Instance.OpenRead(@"\\attacker\share\x"),
                Throws.InstanceOf<FileNotFoundException>());
        }

        [Test]
        public void OpenWriteThrows()
        {
            Assert.That(
                () => NullFileSystem.Instance.OpenWrite("x"),
                Throws.InstanceOf<FileNotFoundException>());
        }

        [Test]
        public void DeleteThrows()
        {
            Assert.That(
                () => NullFileSystem.Instance.Delete("x", isDirectory: false),
                Throws.InstanceOf<FileNotFoundException>());
        }

        [Test]
        public void GetLengthThrows()
        {
            Assert.That(
                () => NullFileSystem.Instance.GetLength("x"),
                Throws.InstanceOf<FileNotFoundException>());
        }

        [Test]
        public void GetLastWriteTimeThrows()
        {
            Assert.That(
                () => NullFileSystem.Instance.GetLastWriteTime("x"),
                Throws.InstanceOf<FileNotFoundException>());
        }
    }
}
