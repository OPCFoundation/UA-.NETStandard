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

#if NET10_0
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
