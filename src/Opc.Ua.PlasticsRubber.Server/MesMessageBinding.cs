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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Commits an MES message for presentation on the actual machine.
    /// </summary>
    public interface IMesMessageProvider
    {
        /// <summary>
        /// Commits a new message; severity is between one and one thousand.
        /// </summary>
        ValueTask SetMessageAsync(
            PlasticsRubberRequestContext context,
            string? id,
            string? message,
            ushort severity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the message on the device before the address-space message is cleared.
        /// </summary>
        ValueTask ClearMessageAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements the exact set/clear semantics of the common MES message capability.
    /// </summary>
    public sealed class MesMessageBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the selected message methods of a generated MachineMESStatus instance.
        /// </summary>
        public MesMessageBinding(
            MachineMESStatusState node,
            ISystemContext context,
            IMesMessageProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
            : this(
                node ?? throw new ArgumentNullException(nameof(node)),
                node.MESMessage ?? throw new ArgumentException("The MES message object is missing.", nameof(node)),
                node.SetMESMessage,
                node.ClearMESMessage,
                context,
                provider,
                accessPolicy)
        {
        }

        /// <summary>
        /// Binds the same message capability when a concrete companion type declares its members directly.
        /// </summary>
        public MesMessageBinding(
            BaseObjectState owner,
            MESMessageState message,
            MethodState? setMessage,
            MethodState? clearMessage,
            ISystemContext context,
            IMesMessageProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = owner ?? throw new ArgumentNullException(nameof(owner));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (message.Id == null || message.Message == null || message.Severity == null)
            {
                throw new ArgumentException("The MES message is missing mandatory fields.", nameof(message));
            }
            m_binding = new PlasticsRubberMethodBinding(owner, context, accessPolicy);
            if (setMessage != null)
            {
                if (setMessage.OnCallMethod2Async != null || setMessage.OnCallMethod2 != null ||
                    setMessage is SetMESMessageMethodState { OnCallAsync: not null } or
                        SetMESMessageMethodState { OnCall: not null })
                {
                    throw new InvalidOperationException("The message method already has a handler.");
                }
                setMessage.OnCallMethod2Async = (call, method, target, input, _, ct) =>
                    m_binding.ExecuteAsync(
                        call, target, method.NodeId,
                        async (request, token) =>
                        {
                            if (input.Count != 3)
                            {
                                throw ServiceResultException.Create(
                                    input.Count < 3 ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments,
                                    "SetMESMessage requires Id, Message and Severity.");
                            }
                            if ((!input[0].IsNull && !input[0].TryGetValue(out string? _)) ||
                                (!input[1].IsNull && !input[1].TryGetValue(out string? _)) ||
                                !input[2].TryGetValue(out ushort severity))
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadTypeMismatch,
                                    "SetMESMessage requires String, String and UInt16 arguments.");
                            }
                            input[0].TryGetValue(out string? id);
                            input[1].TryGetValue(out string? text);
                            if (severity is < 1 or > 1000)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadOutOfRange, "Message severity must be between 1 and 1000.");
                            }
                            await provider.SetMessageAsync(request, id, text, severity, token)
                                .ConfigureAwait(false);
                            message.Id.Value = id;
                            message.Message.Value = text;
                            message.Severity.Value = severity;
                            message.ClearChangeMasks(context, true);
                        },
                        ct);
                m_binding.Track(setMessage);
            }
            if (clearMessage != null)
            {
                m_binding.Bind(clearMessage, async (request, ct) =>
                {
                    await provider.ClearMessageAsync(request, ct).ConfigureAwait(false);
                    message.Id.Value = string.Empty;
                    message.Message.Value = string.Empty;
                    message.Severity.Value = 0;
                    message.ClearChangeMasks(context, true);
                });
            }
        }

        /// <summary>
        /// Gets the concrete companion instance that owns the message methods.
        /// </summary>
        public BaseObjectState Node { get; }

        /// <summary>
        /// Gets the generated common message instance.
        /// </summary>
        public MESMessageState Message { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// The common authorized operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
