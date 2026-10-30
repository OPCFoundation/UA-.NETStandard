// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Owns the asynchronous lifetime of the actual ordered streams for one authenticated flow.
    /// All protocol state remains serialized by its context.
    /// </summary>
    internal sealed class FlowDirectLane
    {
        /// <summary>
        /// Gets the cancellation source shared by admission, reading and writing.
        /// </summary>
        internal CancellationTokenSource Shutdown { get; } = new();

        /// <summary>
        /// Gets the signal set only at the local READY boundary.
        /// </summary>
        internal TaskCompletionSource<bool> Ready { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets the asynchronously admitted local sending stream.
        /// </summary>
        internal TaskCompletionSource<Stream> Sender { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets or sets the receive-admission and parser lifetime.
        /// </summary>
        internal Task ReceiveTask { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Gets or sets the local send-admission lifetime.
        /// </summary>
        internal Task SendTask { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Gets or sets the actual admitted receiving stream.
        /// </summary>
        internal Stream? Receiver { get; set; }

        /// <summary>
        /// Gets or sets whether the opener has received ACCEPT.
        /// </summary>
        internal bool Accepted { get; set; }

        /// <summary>
        /// Gets or sets whether READY has already been queued.
        /// </summary>
        internal bool ReadyQueued { get; set; }
    }
}
