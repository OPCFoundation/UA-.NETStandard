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
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Default <see cref="IProgramsBuilder"/> implementation. Binding runs
    /// after the completed Robotics tree is registered with the node manager,
    /// so the materialised program files are added to a live address space.
    /// </summary>
    internal sealed class ProgramsBuilder : IProgramsBuilder
    {
        public ProgramsBuilder(RoboticsBuildScope scope, FileDirectoryState directory)
        {
            m_scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <inheritdoc/>
        public FileDirectoryState Directory { get; }

        /// <inheritdoc/>
        public IProgramsBuilder UseFileSystem(IFileSystemProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_providerFactory = _ => provider;
            return this;
        }

        /// <inheritdoc/>
        public IProgramsBuilder UseFileSystem<TProvider>()
            where TProvider : class, IFileSystemProvider
        {
            m_providerFactory = static context => context.GetRequiredService<TProvider>();
            return this;
        }

        /// <inheritdoc/>
        public IProgramsBuilder WithOptions(Action<FileDirectoryBindingOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(m_options);
            return this;
        }

        /// <summary>
        /// Validates the configuration and schedules the binding to run once the
        /// Robotics tree has been registered.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// No file-system provider was configured.
        /// </exception>
        internal void Schedule()
        {
            if (m_providerFactory == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Controller Programs requires a file system: call " +
                    "IProgramsBuilder.UseFileSystem before completing the build.");
            }
            m_scope.PostRegistrationActions.Add(BindAsync);
        }

        private async ValueTask BindAsync(CancellationToken cancellationToken)
        {
            IRoboticsBuildContext context = m_scope.BuildContext;
            IFileSystemProvider provider = m_providerFactory!(context);
            IFileDirectoryBinder binder = context.GetRequiredService<IFileDirectoryBinder>();

            IFileDirectoryBinding binding = await binder.BindAsync(
                Directory,
                provider,
                context.Context,
                m_options,
                (node, ct) => context.Manager.AddPredefinedNodeAsync(node, ct),
                cancellationToken).ConfigureAwait(false);

            m_scope.RegisteredResources.Add(binding);
        }

        private readonly RoboticsBuildScope m_scope;
        private readonly FileDirectoryBindingOptions m_options = new();
        private Func<IRoboticsBuildContext, IFileSystemProvider>? m_providerFactory;
    }
}
