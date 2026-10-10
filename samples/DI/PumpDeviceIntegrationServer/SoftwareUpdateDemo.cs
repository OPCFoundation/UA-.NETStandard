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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Pumps
{
    /// <summary>
    /// Opt-in, in-memory software-update simulator. Installation verifies and
    /// copies a staged package; no pump, hardware or operating-system code runs.
    /// </summary>
    public static class SoftwareUpdateDemo
    {
        /// <summary>
        /// Creates a separate DI device with the standard software-update facet.
        /// Call only during explicit repository-sample setup.
        /// </summary>
        public static async ValueTask<NodeId> CreateAsync(
            DiNodeManager manager,
            ISoftwarePackageStore packages,
            CancellationToken cancellationToken = default)
        {
            if (manager is null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (packages is null)
            {
                throw new ArgumentNullException(nameof(packages));
            }
            cancellationToken.ThrowIfCancellationRequested();
            IDeviceBuilder<DeviceState> device = await manager.CreateDeviceAsync(
                new QualifiedName("SoftwareUpdateDemo", manager.InstanceNamespaceIndex),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            device.Device.DisplayName = new LocalizedText("Software update simulator (in-memory only)");
            device.WithSoftwareUpdate(packages, update => update.UsePackageLoading().OnInstall(InstallAsync));
            return device.Device.NodeId;
        }

        private static async ValueTask InstallAsync(
            ISoftwareUpdateContext context,
            SoftwarePackage requested,
            CancellationToken cancellationToken)
        {
            if (requested.Hash.Length != 64)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A SHA-256 package hash is required.");
            }
            SoftwarePackage? staged = null;
            int inspected = 0;
            await foreach (SoftwarePackage candidate in context.PackageStore.ListAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (++inspected > 128)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations, "The sample supports at most 128 staged packages.");
                }
                if (string.Equals(candidate.Hash, requested.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    staged = candidate;
                    break;
                }
            }
            if (staged is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound, "Upload a package with the requested digest before installation.");
            }
            using Stream source = await context.PackageStore.OpenReadAsync(staged.Id, cancellationToken)
                .ConfigureAwait(false);
            using var payload = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while (true)
            {
#if NET6_0_OR_GREATER
                read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
#else
                read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    break;
                }
                if (payload.Length + read > 64L * 1024 * 1024)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "Package exceeds 64 MiB.");
                }
#if NET6_0_OR_GREATER
                await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
#else
                await payload.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
#endif
            }
            payload.Position = 0;
            string actualHash = CoreUtils.ToHexString(SHA256.HashData(payload));
            if (!string.Equals(actualHash, requested.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Staged package digest changed.");
            }
            payload.Position = 0;
            await context.SoftwareFolder.AddVersionAsync(requested, payload, cancellationToken).ConfigureAwait(false);
            await context.SoftwareFolder.SetCurrentVersionAsync(requested.Version, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
