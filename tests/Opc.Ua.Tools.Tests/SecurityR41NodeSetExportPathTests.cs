// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.SecurityRegression
{
    /// <summary>
    /// Covers the managed export writer's containment and exclusive-create behavior.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SecurityR41NodeSetExportPathTests
    {
        [Test]
        public void SecurityR41ExportWriterCreatesContainedPrivateFilesWithoutReplacingExistingFiles()
        {
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"security-r41-{Guid.NewGuid():N}");
            string destination = Path.Combine(root, "nested", "address-space.xml");
            const string expected = "<UANodeSet />";
            try
            {
                using (FileStream output = NodeSetExportFileWriter.CreateNew(root, destination))
                {
                    byte[] content = Encoding.UTF8.GetBytes(expected);
                    output.Write(content);
                }

                Assert.That(File.ReadAllText(destination), Is.EqualTo(expected));
                Assert.That(
                    () => NodeSetExportFileWriter.CreateNew(root, destination),
                    Throws.TypeOf<IOException>());
                Assert.That(File.ReadAllText(destination), Is.EqualTo(expected),
                    "An existing export must remain byte-for-byte unchanged.");

                string escaped = Path.GetFullPath(Path.Combine(root, "..", $"outside-{Guid.NewGuid():N}.xml"));
                Assert.That(
                    () => NodeSetExportFileWriter.CreateNew(root, escaped),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(File.Exists(escaped), Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
#endif
