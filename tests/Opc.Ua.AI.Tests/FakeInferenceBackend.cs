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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// A backend that answers without a network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a test double, not a third configurable provider. The distinction
    /// matters: a fake that shipped as a supported option would let the sample look
    /// healthy while never having reached a model, and the useful thing about the
    /// sample is precisely that it does.
    /// </para>
    /// <para>
    /// It records what it was asked, because several of the claims worth testing are
    /// about which backend was called rather than what came back.
    /// </para>
    /// </remarks>
    internal sealed class FakeInferenceBackend : IInferenceBackend
    {
        private readonly List<InferenceRequest> m_requests = [];

        /// <summary>
        /// Creates a fake.
        /// </summary>
        /// <param name="name">
        /// Names this instance in the answers it produces, so a test can tell which
        /// of two fakes actually replied.
        /// </param>
        public FakeInferenceBackend(string name)
        {
            Name = name;
        }

        /// <summary>Identifies this fake in its own answers.</summary>
        public string Name { get; }

        /// <inheritdoc/>
        public InferenceSite Site { get; set; } = InferenceSite.OnServer;

        /// <summary>Whether calls succeed.</summary>
        public bool Healthy { get; set; } = true;

        /// <summary>Whether the probe reports the backend as reachable.</summary>
        public bool Reachable { get; set; } = true;

        /// <summary>
        /// How a failing call fails. Defaults to an ordinary error; set to
        /// <see cref="InferenceFinish.Filtered"/> to exercise a safety refusal,
        /// which must not be retried anywhere else.
        /// </summary>
        public InferenceFinish FailureKind { get; set; } = InferenceFinish.Error;

        /// <summary>Models this fake offers.</summary>
        public List<BackendModel> Models { get; } = [];

        /// <summary>Everything this fake was asked, in order.</summary>
        public IReadOnlyList<InferenceRequest> Requests => m_requests;

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
            string? filter,
            uint maxResults,
            CancellationToken ct)
        {
            IReadOnlyList<BackendModel> models = Models;
            return ValueTask.FromResult(models);
        }

        /// <inheritdoc/>
        public ValueTask<InferenceResult> InvokeAsync(
            InferenceRequest request,
            CancellationToken ct)
        {
            lock (m_requests)
            {
                m_requests.Add(request);
            }

            if (!Healthy)
            {
                return ValueTask.FromResult(new InferenceResult
                {
                    Ok = false,
                    Finish = FailureKind,
                    Message = Name + " is unhealthy."
                });
            }

            // The answer names the fake that produced it, which is what lets a test
            // distinguish "the fallback answered" from "the primary recovered".
            byte[] payload = Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{{\"answeredBy\":\"{Name}\"}}"));

            return ValueTask.FromResult(new InferenceResult
            {
                Ok = true,
                Payload = payload,
                ContentType = "application/json",
                ModelUsed = request.Model,
                UsageUnit = "tokens",
                InputUnits = 1,
                OutputUnits = 2,
                TotalUnits = 3,
                Finish = InferenceFinish.Stop
            });
        }

        /// <inheritdoc/>
        public ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
        {
            return ValueTask.FromResult(new BackendProbe
            {
                Reachable = Reachable,
                Detail = Name
            });
        }
    }
}
