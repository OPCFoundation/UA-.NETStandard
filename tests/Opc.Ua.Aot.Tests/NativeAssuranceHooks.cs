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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Opt-in native launch handshake; ordinary test runs do not emit assurance records.
    /// </summary>
    public static class NativeAssuranceHooks
    {
        /// <summary>
        /// Reports the runtime and waits for the launcher's independent image observation.
        /// </summary>
        [Before(Assembly)]
        public static async Task BeforeAssemblyAsync(AssemblyHookContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string directory = Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_NATIVE_DIRECTORY");
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }
            string nonce = GetNonce();
            await WriteReportAsync(directory, nonce, "started").ConfigureAwait(false);
            string acknowledgement = Path.Combine(directory, "launcher.ack");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(acknowledgement))
            {
                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }
            string actual = await File.ReadAllTextAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
            if (!string.Equals(actual, nonce, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Native assurance launch acknowledgement does not match.");
            }
        }

        /// <summary>
        /// Records hook completion, which must also be corroborated by MTP results and process exit.
        /// </summary>
        [After(Assembly)]
        public static async Task AfterAssemblyAsync(AssemblyHookContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string directory = Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_NATIVE_DIRECTORY");
            if (!string.IsNullOrEmpty(directory))
            {
                await WriteReportAsync(directory, GetNonce(), "completed").ConfigureAwait(false);
            }
        }

        private static string GetNonce()
        {
            string nonce = Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_NATIVE_NONCE");
            if (!Guid.TryParseExact(nonce, "N", out _))
            {
                throw new InvalidOperationException("Native assurance invocation nonce is missing or invalid.");
            }
            return nonce;
        }

        private static async Task WriteReportAsync(string directory, string nonce, string phase)
        {
            string image = Environment.ProcessPath ??
                throw new InvalidOperationException("Native assurance process image is unavailable.");
            await using var input = new FileStream(
                image, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(input).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("phase", phase);
                writer.WriteString("nonce", nonce);
                writer.WriteNumber("processId", Environment.ProcessId);
                writer.WriteString("imagePath", Path.GetFullPath(image));
                writer.WriteString("imageDigest", "sha256:" + Convert.ToHexString(hash).ToLowerInvariant());
                writer.WriteString("framework", RuntimeInformation.FrameworkDescription);
                writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
                writer.WriteBoolean("isDynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
                writer.WriteBoolean("isDynamicCodeCompiled", RuntimeFeature.IsDynamicCodeCompiled);
                writer.WriteString("sourceSha", Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_SOURCE_SHA"));
                writer.WriteString("runId", Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_RUN_ID"));
                writer.WriteString("attempt", Environment.GetEnvironmentVariable("OPCUA_ASSURANCE_ATTEMPT"));
                writer.WriteString("observedAt", DateTimeOffset.UtcNow);
                writer.WriteEndObject();
            }
            string destination = Path.Combine(directory, $"runtime.{phase}.json");
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 8192, useAsync: true);
            buffer.Position = 0;
            await buffer.CopyToAsync(output).ConfigureAwait(false);
        }
    }
}
