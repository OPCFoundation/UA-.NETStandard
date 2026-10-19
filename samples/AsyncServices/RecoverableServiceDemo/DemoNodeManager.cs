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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using RecoverableServiceDemo.Model;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Binds the generated sample Method to one owned, observable execution.
    /// </summary>
    [NodeManager(NamespaceUri = DemoWork.NamespaceUri)]
    public sealed partial class DemoNodeManager
    {
        /// <summary>
        /// Gets or sets the work supplied by the host's node-manager factory.
        /// </summary>
        internal DemoWork Work { get; init; } = new();

        partial void Configure(IDemoNodeManagerBuilder builder)
        {
            builder.DemoDevice.SlowIncrement.OnCall(Work.IncrementAsync);
        }
    }

    /// <summary>
    /// Supplies a bounded delay and a counter that exposes accidental replay.
    /// </summary>
    internal sealed class DemoWork
    {
        /// <summary>
        /// Gets the signal raised only after the actual Server Method starts.
        /// </summary>
        public Task Entered => m_entered.Task;

        /// <summary>
        /// Gets the signal raised after the actual Server mutation.
        /// </summary>
        public Task Completed => m_completed.Task;

        /// <summary>
        /// Gets the number of actual Method invocations.
        /// </summary>
        public int Invocations => Volatile.Read(ref m_invocations);

        /// <summary>
        /// Gets the current counter value.
        /// </summary>
        public int Value => Volatile.Read(ref m_value);

        /// <summary>
        /// Allows the already admitted Method to begin its simulated work.
        /// </summary>
        public void Release()
        {
            m_release.TrySetResult();
        }

        /// <summary>
        /// Runs for five seconds and applies one increment, independently of the wrapper wait.
        /// </summary>
        public async ValueTask<int> IncrementAsync(int amount, CancellationToken ct)
        {
            Interlocked.Increment(ref m_invocations);
            m_entered.TrySetResult();
            await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            int value = Interlocked.Add(ref m_value, amount);
            m_completed.TrySetResult();
            return value;
        }

        /// <summary>The sample's application namespace, not a protocol namespace.</summary>
        internal const string NamespaceUri = "urn:opcfoundation:samples:recoverable-services";

        private readonly TaskCompletionSource m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_invocations;
        private int m_value;
    }

    /// <summary>
    /// Injects the host's execution state through the existing node-manager factory interface.
    /// </summary>
    internal sealed class DemoWorkNodeManagerFactory(DemoWork work) : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [DemoWork.NamespaceUri];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The Server owns the returned manager. TODO: Remove when CA2000 models ValueTask factory ownership.
#pragma warning disable CA2000
            IAsyncNodeManager manager = new DemoNodeManager(server, configuration) { Work = work };
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
