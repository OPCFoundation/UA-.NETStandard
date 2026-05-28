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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default <see cref="ISoftwareUpdateBuilder"/> implementation.
    /// Captures the chosen Loading mode + callback overrides; the
    /// actual address-space wiring is delegated to
    /// <see cref="SoftwareUpdateFacetWiring"/> at
    /// <see cref="DeviceBuilderSoftwareUpdateExtensions.WithSoftwareUpdate{TDevice}(IDeviceBuilder{TDevice}, ISoftwarePackageStore, Action{ISoftwareUpdateBuilder})"/>
    /// time.
    /// </summary>
    internal sealed class SoftwareUpdateBuilder : ISoftwareUpdateBuilder
    {
        public SoftwareLoadingMode LoadingMode { get; private set; } =
            SoftwareLoadingMode.Package;

        public ISoftwareFolder? Folder { get; private set; }

        public Func<ISoftwareUpdateContext, CancellationToken, ValueTask>?
            PrepareHandler { get; private set; }

        public Func<ISoftwareUpdateContext, SoftwarePackage, CancellationToken, ValueTask>?
            InstallHandler { get; private set; }

        public Func<ISoftwareUpdateContext, bool, CancellationToken, ValueTask>?
            ConfirmHandler { get; private set; }

        public Func<ISoftwareUpdateContext, CancellationToken, ValueTask>?
            UninstallHandler { get; private set; }

        public ISoftwareUpdateBuilder UsePackageLoading()
        {
            LoadingMode = SoftwareLoadingMode.Package;
            return this;
        }

        public ISoftwareUpdateBuilder UseDirectLoading()
        {
            LoadingMode = SoftwareLoadingMode.Direct;
            return this;
        }

        public ISoftwareUpdateBuilder UseCachedLoading()
        {
            LoadingMode = SoftwareLoadingMode.Cached;
            return this;
        }

        public ISoftwareUpdateBuilder WithSoftwareFolder(ISoftwareFolder folder)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            return this;
        }

        public ISoftwareUpdateBuilder OnPrepare(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler)
        {
            PrepareHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnInstall(
            Func<ISoftwareUpdateContext, SoftwarePackage, CancellationToken, ValueTask> handler)
        {
            InstallHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnConfirm(
            Func<ISoftwareUpdateContext, bool, CancellationToken, ValueTask> handler)
        {
            ConfirmHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnUninstall(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler)
        {
            UninstallHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }
    }
}
