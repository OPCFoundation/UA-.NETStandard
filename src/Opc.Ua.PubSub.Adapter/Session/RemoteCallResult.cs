// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// The result of an OPC UA method call issued through an
    /// <see cref="IServerSession"/>.
    /// </summary>
    public readonly record struct RemoteCallResult
    {
        /// <summary>
        /// Initializes a new <see cref="RemoteCallResult"/>.
        /// </summary>
        /// <param name="status">
        /// The status code returned by the server for the method call.
        /// </param>
        /// <param name="outputArguments">
        /// The output arguments returned by the method.
        /// </param>
        public RemoteCallResult(StatusCode status, ArrayOf<Variant> outputArguments)
        {
            Status = status;
            OutputArguments = outputArguments;
        }

        /// <summary>
        /// The status code returned by the server for the method call.
        /// </summary>
        public StatusCode Status { get; init; }

        /// <summary>
        /// The output arguments returned by the method. Empty when the call
        /// produced no outputs or failed.
        /// </summary>
        public ArrayOf<Variant> OutputArguments { get; init; }
    }
}
