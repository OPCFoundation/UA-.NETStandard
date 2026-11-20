/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The base interface for client proxies.
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface IChannelBase : IDisposable;

    /// <summary>
    /// Obsolete, use transport channel instead
    /// </summary>
    [Obsolete("Use ITransportChannel instead.")]
    public interface ISessionChannel : IChannelBase;

    /// <summary>
    /// A base class for UA channel objects used access UA interfaces
    /// </summary>
    /// <typeparam name="TChannel"></typeparam>
    [Obsolete("Use UAChannelBase.")]
    public partial class UaChannelBase<TChannel> : IChannelBase
        where TChannel : class, IChannelBase
    {
        /// <summary>
        /// This must be set by the derived class to initialize the telemtry system
        /// </summary>
        public required ITelemetryContext Telemetry
        {
            get => m_telemetry;
            init
            {
                m_telemetry = value;
                m_logger = value.CreateLogger(this);
            }
        }

        /// <summary>
        /// Initializes the object with the specified binding and endpoint address.
        /// </summary>
        protected UaChannelBase(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Channel?.Dispose();
                Channel = null;
            }
        }

        /// <summary>
        /// Gets the inner channel.
        /// </summary>
        /// <value>The channel.</value>
        protected TChannel? Channel { get; private set; }

        /// <summary>
        /// Logger to be used by the concrete channel implementation. Shall
        /// not be used outside of the channel inheritance hierarchy. Create
        /// new logger from telemetry context.
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; private set; } = LoggerUtils.Null.Logger;
#pragma warning restore IDE1006 // Naming Styles

        private readonly ITelemetryContext m_telemetry = null!;
    }

    public partial class UaChannelBase<TChannel> : IChannelBase
        where TChannel : class, IChannelBase
    {
        /// <summary>
        /// An async result object that wraps the UA channel.
        /// Satisfies the model compiler generating channel base classes.
        /// When removing also remove the entire Opc.Ua.Channels file.
        /// </summary>
        [Obsolete("WCF channels are not supported anymore.")]
        protected class UaChannelAsyncResult : AsyncResultBase
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="UaChannelAsyncResult"/> class.
            /// </summary>
            /// <param name="channel">The channel.</param>
            /// <param name="callback">The callback.</param>
            /// <param name="callbackData">The callback data.</param>
            /// <param name="logger">A contextual logger to log to</param>
            public UaChannelAsyncResult(
                TChannel channel,
                AsyncCallback? callback,
                object? callbackData,
                ILogger? logger = null)
                : base(callback, callbackData, 0, logger)
            {
                Channel = channel;
            }

            /// <summary>
            /// Gets the wrapped channel.
            /// </summary>
            /// <value>The wrapped channel.</value>
            public TChannel Channel { get; }

            /// <summary>
            /// Called when asynchronous operation completes.
            /// </summary>
            /// <param name="ar">The asynchronous result object.</param>
            public void OnOperationCompleted(IAsyncResult ar)
            {
                try
                {
                    // check if the begin operation has had a chance to complete.
                    lock (Lock)
                    {
                        InnerResult ??= ar;
                    }

                    // signal that the operation is complete.
                    OperationCompleted();
                }
                catch (Exception e)
                {
                    m_logger.ChannelBaseObsoleteLogMessage0(e);
                }
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The oject that </returns>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="ServiceResultException"></exception>
            public static new UaChannelAsyncResult WaitForComplete(IAsyncResult ar)
            {
                if (ar is not UaChannelAsyncResult asyncResult)
                {
                    throw new ArgumentException(
                        "End called with an invalid IAsyncResult object.",
                        nameof(ar));
                }

                if (!asyncResult.WaitForComplete())
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }

                return asyncResult;
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for ChannelBaseObsolete.
    /// </summary>
    internal static partial class ChannelBaseObsoleteLog
    {
        [LoggerMessage(EventId = CoreEventIds.ChannelBaseObsolete + 0, Level = LogLevel.Error,
            Message = "Unexpected exception invoking UaChannelAsyncResult callback function.")]
        public static partial void ChannelBaseObsoleteLogMessage0(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
