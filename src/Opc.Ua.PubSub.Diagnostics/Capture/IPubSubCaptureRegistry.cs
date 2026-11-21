// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Shared coordination point that holds the currently-active
    /// <see cref="IPubSubCaptureObserver"/>. PubSub transports read
    /// <see cref="CurrentObserver"/> on their hot send / receive path; a
    /// diagnostics capture session installs and removes the observer.
    /// </summary>
    /// <remarks>
    /// A single registry instance is shared (typically as a DI singleton)
    /// between the transports and the capture tooling. Reads on the
    /// transport path are lock-free; at most one observer is active at a
    /// time.
    /// </remarks>
    public interface IPubSubCaptureRegistry
    {
        /// <summary>
        /// The observer to notify of sent / received frames, or
        /// <see langword="null"/> when capture is not active. Implementations
        /// expose this as a lock-free volatile read.
        /// </summary>
        IPubSubCaptureObserver? CurrentObserver { get; }

        /// <summary>
        /// Installs <paramref name="observer"/> as the active observer,
        /// replacing any previous one.
        /// </summary>
        /// <param name="observer">The observer to install.</param>
        void SetObserver(IPubSubCaptureObserver observer);

        /// <summary>
        /// Clears the active observer if it is the same instance as
        /// <paramref name="observer"/>.
        /// </summary>
        /// <param name="observer">The observer expected to be active.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="observer"/> was active
        /// and has been cleared; otherwise <see langword="false"/>.
        /// </returns>
        bool TryClearObserver(IPubSubCaptureObserver observer);
    }
}
