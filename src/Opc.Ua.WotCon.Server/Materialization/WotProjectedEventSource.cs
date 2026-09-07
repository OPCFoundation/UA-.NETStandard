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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Shares one upstream subscription across local notifier activations.
    /// Closing the last activation stops the subscription, not the generation's
    /// channel or occurrence routes.
    /// </summary>
    internal sealed class WotProjectedEventSource : IAsyncDisposable
    {
        public WotProjectedEventSource(WotCompiledForm form, WotBindingChannelSlot slot)
        {
            Form = form;
            m_slot = slot;
        }

        public WotCompiledForm Form { get; }

        public async ValueTask<IAsyncDisposable> AttachAsync(
            Action<WotNotification> listener, CancellationToken cancellationToken)
        {
            if (!BeginOperation())
            {
                throw new ObjectDisposedException(nameof(WotProjectedEventSource));
            }
            bool acquired = false;
            long id = 0;
            try
            {
                await m_subscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
                if (Volatile.Read(ref m_disposed))
                {
                    throw new ObjectDisposedException(nameof(WotProjectedEventSource));
                }
                lock (m_listenersGate)
                {
                    id = ++m_nextListener;
                    m_listeners.Add(id, listener);
                }
                if (m_subscription is null)
                {
                    IWotBindingChannel channel = await m_slot.GetAsync(cancellationToken).ConfigureAwait(false);
                    m_subscription = await channel.SubscribeEventAsync(Dispatch, cancellationToken)
                        .ConfigureAwait(false);
                }
                return new ListenerLease(this, id);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (m_listenersGate)
                {
                    m_listeners.Remove(id);
                }
                throw;
            }
            finally
            {
                if (acquired)
                {
                    m_subscriptionGate.Release();
                }
                EndOperation();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task operations;
            lock (m_listenersGate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_listeners.Clear();
                operations = m_operationCount == 0 ? Task.CompletedTask : m_operationsDrained!.Task;
            }
            await operations.ConfigureAwait(false);
            try
            {
                IWotSubscription? subscription = m_subscription;
                m_subscription = null;
                if (subscription is not null)
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_subscriptionGate.Dispose();
            }
        }

        public bool CanShare(WotCompiledForm form)
        {
            return SameEndpoint(Form, form) &&
                Form.Addressing.Target == form.Addressing.Target &&
                SameMetadata(Form.Addressing.Metadata, form.Addressing.Metadata) &&
                Form.OperationInfo.Method == form.OperationInfo.Method &&
                SameMetadata(Form.OperationInfo.Metadata, form.OperationInfo.Metadata) &&
                Form.Payload.ContentType == form.Payload.ContentType &&
                Form.Payload.CodecId == form.Payload.CodecId &&
                SameMetadata(Form.Payload.Metadata, form.Payload.Metadata) &&
                (Form.EventSelection ?? WotEventSelection.Default).Clauses.Span.SequenceEqual(
                    (form.EventSelection ?? WotEventSelection.Default).Clauses.Span);
        }

        public static bool SameEndpoint(WotCompiledForm left, WotCompiledForm right)
        {
            return left.Binding.Key == right.Binding.Key &&
                left.Endpoint.BaseUri == right.Endpoint.BaseUri &&
                left.Endpoint.Host == right.Endpoint.Host &&
                left.Endpoint.Port == right.Endpoint.Port &&
                SameMetadata(left.Endpoint.Metadata, right.Endpoint.Metadata) &&
                left.SecurityFloor?.SecurityMode == right.SecurityFloor?.SecurityMode &&
                left.SecurityFloor?.SecurityPolicy == right.SecurityFloor?.SecurityPolicy &&
                left.Security.Length == right.Security.Length &&
                left.Security.Zip(right.Security, static (a, b) =>
                    a.Scheme == b.Scheme && a.SchemeName == b.SchemeName &&
                    a.BindingUri == b.BindingUri && a.Endpoint == b.Endpoint &&
                    a.In == b.In && a.ParameterName == b.ParameterName).All(static equal => equal);
        }

        private static bool SameMetadata(
            ImmutableDictionary<string, string> left, ImmutableDictionary<string, string> right)
        {
            return left.Count == right.Count &&
                left.All(entry => right.TryGetValue(entry.Key, out string? value) && entry.Value == value);
        }

        private void Dispatch(WotNotification notification)
        {
            Action<WotNotification>[] listeners;
            lock (m_listenersGate)
            {
                listeners = [.. m_listeners.Values];
            }
            foreach (Action<WotNotification> listener in listeners)
            {
                listener(notification);
            }
        }

        private async ValueTask DetachAsync(long id)
        {
            if (!BeginOperation())
            {
                return;
            }
            await m_subscriptionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool last;
                lock (m_listenersGate)
                {
                    m_listeners.Remove(id);
                    last = m_listeners.Count == 0;
                }
                if (last && m_subscription is { } subscription)
                {
                    m_subscription = null;
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_subscriptionGate.Release();
                EndOperation();
            }
        }

        private bool BeginOperation()
        {
            lock (m_listenersGate)
            {
                if (m_disposed)
                {
                    return false;
                }
                if (m_operationCount++ == 0)
                {
                    m_operationsDrained = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return true;
            }
        }

        private void EndOperation()
        {
            lock (m_listenersGate)
            {
                if (--m_operationCount == 0)
                {
                    m_operationsDrained!.TrySetResult(true);
                }
            }
        }

        private sealed class ListenerLease(WotProjectedEventSource owner, long id) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                return Interlocked.Exchange(ref m_disposed, 1) == 0 ? owner.DetachAsync(id) : default;
            }

            private int m_disposed;
        }

        private readonly WotBindingChannelSlot m_slot;
        private readonly SemaphoreSlim m_subscriptionGate = new(1, 1);
        private readonly Lock m_listenersGate = new();
        private readonly Dictionary<long, Action<WotNotification>> m_listeners = [];
        private IWotSubscription? m_subscription;
        private long m_nextListener;
        private int m_operationCount;
        private TaskCompletionSource<bool>? m_operationsDrained;
        private bool m_disposed;
    }
}
