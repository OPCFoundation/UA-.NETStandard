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
using System.Threading.Tasks;

namespace Opc.Ua.Fuzzing.Tests
{
    internal sealed class TestInputDirectory : IDisposable
    {
        public TestInputDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"opcua-fuzz-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }

        public void Dispose()
        {
            if (!m_disposed)
            {
                Directory.Delete(DirectoryPath, recursive: true);
                m_disposed = true;
            }
        }

        internal async Task<string> WriteAsync(string name, byte[] input)
        {
            string file = Path.Combine(DirectoryPath, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using var stream = new FileStream(
                file, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
#if NET8_0_OR_GREATER
            await stream.WriteAsync(input.AsMemory()).ConfigureAwait(false);
#else
            await stream.WriteAsync(input, 0, input.Length).ConfigureAwait(false);
#endif
            return file;
        }

        private bool m_disposed;
    }
}
