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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Runtime context handed to an executor while it activates a compiled form:
    /// the credential provider used to resolve secret-free references and the
    /// safety bounds it must enforce.
    /// </summary>
    public sealed class WotExecutorContext
    {
        /// <summary>Initializes a new executor context.</summary>
        public WotExecutorContext(
            IWotCredentialProvider? credentials = null,
            IWotCodecRegistry? codecs = null,
            WotBindingBounds? bounds = null)
        {
            Credentials = credentials ?? NullWotCredentialProvider.Instance;
            Codecs = codecs ?? WotPayloadCodecRegistry.Default;
            Bounds = bounds ?? WotBindingBounds.Default;
        }

        /// <summary>Gets the credential provider.</summary>
        public IWotCredentialProvider Credentials { get; }

        /// <summary>Gets the codec registry.</summary>
        public IWotCodecRegistry Codecs { get; }

        /// <summary>Gets the enforced safety bounds.</summary>
        public WotBindingBounds Bounds { get; }
    }

    /// <summary>A push notification from an observe / event channel.</summary>
    public sealed class WotNotification
    {
        /// <summary>Initializes a new notification.</summary>
        /// <param name="value">
        /// The notified value. For a property observe this is the reported
        /// <see cref="DataValue"/>. For an event this is a deterministic
        /// projection of the event (see <see cref="EventFields"/> for the full
        /// per-field envelope) carrying the mapped <see cref="StatusCode"/> and
        /// the event's source / receive timestamps.
        /// </param>
        /// <param name="eventFields">
        /// The optional event field envelope: every <c>EventFilter</c>
        /// select-clause field (browse path, '/'-joined for nested paths)
        /// mapped to its own <see cref="DataValue"/>. Empty for a property
        /// observe notification.
        /// </param>
        public WotNotification(
            DataValue value, IReadOnlyDictionary<string, DataValue>? eventFields = null)
        {
            Value = value;
            EventFields = eventFields ?? ImmutableDictionary<string, DataValue>.Empty;
        }

        /// <summary>Gets the notified value together with its status and timestamps.</summary>
        public DataValue Value { get; }

        /// <summary>
        /// Gets the event field envelope, keyed by the select-clause browse
        /// path. Empty for a property observe notification.
        /// </summary>
        public IReadOnlyDictionary<string, DataValue> EventFields { get; }
    }

    /// <summary>The result of a read operation.</summary>
    public sealed class WotReadResult
    {
        /// <summary>Initializes a new read result.</summary>
        public WotReadResult(StatusCode status, DataValue value, string? error = null)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        /// <summary>Gets the mapped status code.</summary>
        public StatusCode Status { get; }

        /// <summary>Gets the read value with status and timestamps.</summary>
        public DataValue Value { get; }

        /// <summary>Gets the error message on failure, if any.</summary>
        public string? Error { get; }

        /// <summary>Gets whether the operation succeeded.</summary>
        public bool Success => StatusCode.IsGood(Status);
    }

    /// <summary>The result of a write operation.</summary>
    public sealed class WotWriteResult
    {
        /// <summary>Initializes a new write result.</summary>
        public WotWriteResult(StatusCode status, string? error = null)
        {
            Status = status;
            Error = error;
        }

        /// <summary>Gets the mapped status code.</summary>
        public StatusCode Status { get; }

        /// <summary>Gets the error message on failure, if any.</summary>
        public string? Error { get; }

        /// <summary>Gets whether the operation succeeded.</summary>
        public bool Success => StatusCode.IsGood(Status);
    }

    /// <summary>The result of an action invocation.</summary>
    public sealed class WotInvokeResult
    {
        /// <summary>Initializes a new invoke result.</summary>
        public WotInvokeResult(StatusCode status, IReadOnlyList<DataValue>? outputs = null, string? error = null)
        {
            Status = status;
            Outputs = outputs ?? Array.Empty<DataValue>();
            Error = error;
        }

        /// <summary>Gets the mapped status code.</summary>
        public StatusCode Status { get; }

        /// <summary>Gets the action outputs in declaration order.</summary>
        public IReadOnlyList<DataValue> Outputs { get; }

        /// <summary>Gets the error message on failure, if any.</summary>
        public string? Error { get; }

        /// <summary>Gets whether the operation succeeded.</summary>
        public bool Success => StatusCode.IsGood(Status);
    }

    /// <summary>A running observe / event subscription. Disposing it stops delivery.</summary>
    public interface IWotSubscription : IAsyncDisposable
    {
        /// <summary>Gets the compiled form the subscription observes.</summary>
        WotCompiledForm Form { get; }
    }

    /// <summary>
    /// A live per-form binding channel opened by an executor. It exposes the
    /// read / write / invoke / observe / event operations the binding supports.
    /// Operations not supported by the channel's compiled operation return a
    /// <see cref="StatusCodes.BadNotSupported"/> result. Disposing the channel
    /// releases the underlying transport resource.
    /// </summary>
    public interface IWotBindingChannel : IAsyncDisposable
    {
        /// <summary>Gets the compiled form the channel binds.</summary>
        WotCompiledForm Form { get; }

        /// <summary>Reads the current value.</summary>
        ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>Writes a value.</summary>
        ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default);

        /// <summary>Invokes an action with ordered inputs.</summary>
        ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default);

        /// <summary>Observes property-value changes.</summary>
        ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default);

        /// <summary>Subscribes to events.</summary>
        ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Executes a compiled binding form against a live transport. Executors are
    /// registered independently from planners so a protocol can be validated
    /// without an executor and executed once one is present.
    /// </summary>
    public interface IWotBindingExecutor
    {
        /// <summary>Gets the identity of the binder this executor serves.</summary>
        WotBindingIdentity Identity { get; }

        /// <summary>Gets whether the executor can run the supplied compiled form.</summary>
        bool CanExecute(WotCompiledForm form);

        /// <summary>Opens a live channel for the supplied compiled form.</summary>
        ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default);
    }
}
