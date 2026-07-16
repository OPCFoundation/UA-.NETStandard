/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Pumps
{
    /// <summary>
    /// Seeds an <see cref="ISoftwarePackageStore"/> with a couple of
    /// demonstration firmware packages so the sample exposes useful
    /// content to clients out of the box.
    /// </summary>
    internal static class SoftwarePackageSeeder
    {
        /// <summary>
        /// Adds two sample firmware payloads to
        /// <paramref name="store"/> with realistic metadata.
        /// </summary>
        public static async Task SeedAsync(ISoftwarePackageStore store)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                "Sample firmware payload (placeholder).");

            await store.AddAsync(
                new SoftwarePackage(
                    Id: "demo-firmware-1.0.0",
                    Version: "1.0.0",
                    Vendor: "Acme Corp",
                    Description: "Demo firmware payload",
                    SizeBytes: 0,
                    CreatedAt: default,
                    Hash: string.Empty),
                new MemoryStream(payload))
                .ConfigureAwait(false);

            await store.AddAsync(
                new SoftwarePackage(
                    Id: "demo-firmware-1.0.1-rc",
                    Version: "1.0.1-rc",
                    Vendor: "Acme Corp",
                    Description: "Demo firmware payload (release candidate)",
                    SizeBytes: 0,
                    CreatedAt: default,
                    Hash: string.Empty),
                new MemoryStream(payload))
                .ConfigureAwait(false);
        }
    }
}
