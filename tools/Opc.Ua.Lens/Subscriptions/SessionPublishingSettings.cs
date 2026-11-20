// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;

namespace UaLens.Subscriptions
{
    /// <summary>
    /// Session-wide publish pipeline limits, shared by every subscription.
    /// </summary>
    internal sealed record SessionPublishingSettings
    {
        public SessionPublishingSettings(int minimumRequests, int maximumRequests)
        {
            if (minimumRequests < 1 || maximumRequests < minimumRequests)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumRequests),
                    "The minimum must be positive and no greater than the maximum.");
            }
            MinimumRequests = minimumRequests;
            MaximumRequests = maximumRequests;
        }

        public int MinimumRequests { get; }

        public int MaximumRequests { get; }

        /// <summary>
        /// Applies both limits, restoring the previous pair if the session rejects either value.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException">
        /// The update failed and the previous limits could not be restored.
        /// </exception>
        public void Apply(ISession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            int previousMinimum = session.MinPublishRequestCount;
            int previousMaximum = session.MaxPublishRequestCount;
            try
            {
                ApplyLimits(session, MinimumRequests, MaximumRequests);
            }
            catch (Exception applyError)
            {
                try
                {
                    ApplyLimits(session, previousMinimum, previousMaximum);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        "The previous publishing limits could not be restored. " +
                        "Reconnect before changing the pipeline again.",
                        new AggregateException(applyError, rollbackError));
                }
                throw;
            }
        }

        /// <summary>
        /// Orders both normal writes and restoration without creating an invalid intermediate range.
        /// </summary>
        private static void ApplyLimits(ISession session, int minimum, int maximum)
        {
            if (maximum >= session.MaxPublishRequestCount)
            {
                session.MaxPublishRequestCount = maximum;
                session.MinPublishRequestCount = minimum;
            }
            else
            {
                session.MinPublishRequestCount = minimum;
                session.MaxPublishRequestCount = maximum;
            }
        }
    }
}
