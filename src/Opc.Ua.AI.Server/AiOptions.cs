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
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Server
{
    /// <summary>
    /// How the sample is configured.
    /// </summary>
    /// <remarks>
    /// There is deliberately no second management API. The specification's own
    /// Methods are the control plane, and everything here is startup configuration -
    /// which backend to reach, which deployments to publish, and how the simulated
    /// scenarios behave.
    /// </remarks>
    public sealed class AIOptions
    {
        /// <summary>Configuration section this binds from.</summary>
        public const string SectionName = "AiModelManagement";

        /// <summary>
        /// Identifier of the primary deployment, as published through
        /// <c>DeploymentType.DeploymentId</c>.
        /// </summary>
        public string PrimaryDeploymentId { get; set; } = "primary";

        /// <summary>
        /// Identifier of the deployment the primary falls back to.
        /// </summary>
        public string FallbackDeploymentId { get; set; } = "fallback";

        /// <summary>
        /// Whether to publish a fallback deployment and wire
        /// <c>FallsBackTo</c> from the primary to it.
        /// </summary>
        /// <remarks>
        /// Worth having as a switch because the fallback path is the one whose
        /// failure is invisible: a fallback that answers without reporting the
        /// substituted model in <c>ModelUsed</c> looks perfectly healthy.
        /// </remarks>
        public bool EnableFallback { get; set; } = true;

        /// <summary>
        /// Whether to publish a catalogue and an import job (scenario 4.4).
        /// </summary>
        public bool EnableCatalogue { get; set; } = true;

        /// <summary>
        /// Whether to publish a learning job (scenario 4.7).
        /// </summary>
        public bool EnableLearningLoop { get; set; } = true;

        /// <summary>
        /// How long an inference transfer survives without completing before the
        /// Server may reclaim it.
        /// </summary>
        public TimeSpan TransferExpiry { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long a simulated asynchronous inference takes, so that a client can
        /// observe the job lifecycle rather than the job completing before it has
        /// subscribed.
        /// </summary>
        public TimeSpan AsyncInferenceDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Largest payload the Server will accept through a chunked transfer.
        /// </summary>
        /// <remarks>
        /// A transfer exists because a payload was too large to pass inline, so the
        /// bound that matters is not the inline one. Without a second bound here
        /// "too large for inline" would mean "unbounded", which is a worse answer
        /// than the limit it was meant to relax.
        /// </remarks>
        public ulong MaxTransferSize { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// How many transfers may be open at once.
        /// </summary>
        public int MaxConcurrentTransfers { get; set; } = 16;

        /// <summary>
        /// How long an inference started through a transfer may run.
        /// </summary>
        public TimeSpan TransferInferenceTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// How many asynchronous jobs the Server keeps before reclaiming the
        /// oldest.
        /// </summary>
        /// <remarks>
        /// A job retains its request and its response, so an uncapped set grows in
        /// bytes as well as in nodes, and any session that can call InvokeAsync can
        /// grow it. Transfers already have both an expiry and a cap; jobs need the
        /// same for the same reason.
        /// </remarks>
        public int MaxRetainedJobs { get; set; } = 64;

        /// <summary>
        /// Identifier of the model source this Server consumes from.
        /// </summary>
        public string SourceId { get; set; } = "model-source";
    }
}
