// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
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
