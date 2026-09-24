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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Retains bounded replay inputs and details on the runner without writing them to public test logs or attachments.
    /// </summary>
    internal sealed class FuzzReplayDiagnostics
    {
        /// <summary>
        /// Creates a private diagnostic sink whose directory is created only when a finding is recorded.
        /// </summary>
        /// <param name="directory">Runner-local storage outside published test results.</param>
        /// <exception cref="ArgumentNullException"><paramref name="directory"/> is null.</exception>
        internal FuzzReplayDiagnostics(string directory)
        {
            m_directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        }

        /// <summary>
        /// Saves at most sixty inputs of up to 4096 bytes, preserving their exact bytes and bounded diagnostic details.
        /// </summary>
        /// <param name="input">The original failing input.</param>
        /// <param name="details">Restricted exception or child-process output.</param>
        /// <returns>Whether the input fit the diagnostic bounds and was written.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="input"/> or <paramref name="details"/> is null.
        /// </exception>
        /// <exception cref="IOException">The diagnostic files cannot be created or written.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to the diagnostic storage is denied.</exception>
        internal async Task<bool> TryWriteAsync(byte[] input, string details)
        {
            _ = input ?? throw new ArgumentNullException(nameof(input));
            _ = details ?? throw new ArgumentNullException(nameof(details));
            if (m_written >= kMaxReproducers || input.Length > kMaxInputBytes)
            {
                return false;
            }
#if NETFRAMEWORK
            Directory.CreateDirectory(m_directory);
#else
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(m_directory);
            }
            else
            {
                Directory.CreateDirectory(
                    m_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
#endif
            string prefix = m_written.ToString("D2", CultureInfo.InvariantCulture);
            m_written++;
            using (var stream = new FileStream(
                Path.Combine(m_directory, prefix + ".bin"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
#if NETFRAMEWORK
                await stream.WriteAsync(input, 0, input.Length).ConfigureAwait(false);
#else
                await stream.WriteAsync(input.AsMemory()).ConfigureAwait(false);
#endif
            }
            using (var stream = new FileStream(
                Path.Combine(m_directory, prefix + ".txt"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                string boundedDetails = details.Length <= kMaxDetailCharacters
                    ? details
                    : details[..kMaxDetailCharacters];
                await writer.WriteAsync(boundedDetails).ConfigureAwait(false);
                if (details.Length > kMaxDetailCharacters)
                {
                    await writer.WriteAsync("\n[diagnostic details truncated]").ConfigureAwait(false);
                }
                await writer.FlushAsync().ConfigureAwait(false);
            }
            return true;
        }

        private const int kMaxReproducers = 60;
        private const int kMaxInputBytes = 4096;
        private const int kMaxDetailCharacters = 65536;
        private readonly string m_directory;
        private int m_written;
    }
}
