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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// In-memory <see cref="ISecurityKeyService"/> double used by the
    /// <see cref="PullSecurityKeyProvider"/> fixture. Counts calls,
    /// can be flipped to throw, and produces sequential token-id keys
    /// using the supplied policy.
    /// </summary>
    internal sealed class FakeSecurityKeyService : ISecurityKeyService
    {
        private readonly IPubSubSecurityPolicy m_policy;
        private readonly TimeSpan m_keyLifetime;
        private uint m_nextTokenId;
        private int m_callCount;
        private bool m_failNext;
        private OpcUaSksException? m_failureException;

        public FakeSecurityKeyService(IPubSubSecurityPolicy policy, TimeSpan keyLifetime)
        {
            m_policy = policy;
            m_keyLifetime = keyLifetime;
            m_nextTokenId = 1u;
        }

        public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;

        public int CallCount => Volatile.Read(ref m_callCount);

        public IList<SksKeyRequest> Requests { get; } = [];

        public void FailOnce(OpcUaSksException exception)
        {
            m_failNext = true;
            m_failureException = exception;
        }

        public ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            SksKeyRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_callCount);
            Requests.Add(request);
            if (m_failNext)
            {
                m_failNext = false;
                OpcUaSksException ex = m_failureException
                    ?? new OpcUaSksException(StatusCodes.BadCommunicationError, "Injected failure.");
                AvailabilityChanged?.Invoke(
                    this,
                    new SksAvailabilityChangedEventArgs(false, ex.Status, ex.Message));
                throw ex;
            }

            uint count = Math.Max(1u, request.RequestedKeyCount);
            uint startToken = request.StartingTokenId == 0u ? m_nextTokenId : request.StartingTokenId;
            var packed = new List<byte[]>((int)count);
            DateTimeUtc now = DateTimeUtc.From(DateTime.UtcNow);
            for (uint i = 0; i < count; i++)
            {
                PubSubSecurityKey key = SksKeyGenerator.Generate(
                    m_policy,
                    unchecked(startToken + i),
                    now,
                    m_keyLifetime);
                packed.Add(SksKeyGenerator.Pack(key));
            }
            m_nextTokenId = unchecked(startToken + count);
            AvailabilityChanged?.Invoke(
                this,
                new SksAvailabilityChangedEventArgs(true, StatusCodes.Good, null));
            return new ValueTask<SksKeyResponse>(new SksKeyResponse(
                m_policy.PolicyUri,
                startToken,
                packed,
                TimeSpan.Zero,
                m_keyLifetime));
        }
    }
}
