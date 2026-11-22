// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Fans every sink call out to an ordered set of inner sinks. Used when the connector
    /// must both persist a text override layer and drive a live viewport from the same
    /// subscription, so the on-disk artefact and the rendered stage never diverge.
    /// </summary>
    /// <remarks>
    /// Inner sinks are invoked in construction order and every one is invoked even if an
    /// earlier one throws; the first failure is rethrown once all have been given the
    /// value. A slow or broken renderer therefore cannot silently stop the file layer
    /// from being written.
    /// </remarks>
    public sealed class CompositeUsdSink : IUsdSink
    {
        private readonly IUsdSink[] m_sinks;

        /// <summary>
        /// Creates a sink that forwards to <paramref name="sinks"/> in order.
        /// </summary>
        /// <param name="sinks">The inner sinks. Must contain at least one non-null sink.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sinks"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="sinks"/> is empty or contains a <c>null</c> entry.
        /// </exception>
        public CompositeUsdSink(params IUsdSink[] sinks)
        {
            if (sinks is null)
            {
                throw new ArgumentNullException(nameof(sinks));
            }
            if (sinks.Length == 0)
            {
                throw new ArgumentException("At least one sink is required.", nameof(sinks));
            }
            foreach (IUsdSink sink in sinks)
            {
                if (sink is null)
                {
                    throw new ArgumentException("A sink must not be null.", nameof(sinks));
                }
            }
            m_sinks = (IUsdSink[])sinks.Clone();
        }

        /// <inheritdoc/>
        public void SetAttribute(string primPath, string propertyName, Variant value)
        {
            Exception? failure = null;
            foreach (IUsdSink sink in m_sinks)
            {
                try
                {
                    sink.SetAttribute(primPath, propertyName, value);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            Rethrow(failure);
        }

        /// <inheritdoc/>
        public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value)
        {
            Exception? failure = null;
            foreach (IUsdSink sink in m_sinks)
            {
                try
                {
                    sink.SetTimeSample(primPath, propertyName, time, value);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            Rethrow(failure);
        }

        /// <inheritdoc/>
        public void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active)
        {
            Exception? failure = null;
            foreach (IUsdSink sink in m_sinks)
            {
                try
                {
                    sink.ComposePrim(primPath, arc, assetReference, active);
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            Rethrow(failure);
        }

        /// <inheritdoc/>
        public IDisposable BeginBatch()
        {
            var scopes = new List<IDisposable>(m_sinks.Length);
            try
            {
                foreach (IUsdSink sink in m_sinks)
                {
                    scopes.Add(sink.BeginBatch());
                }
            }
            catch
            {
                new CompositeBatchScope(scopes).Dispose();
                throw;
            }
            return new CompositeBatchScope(scopes);
        }

        private static void Rethrow(Exception? failure)
        {
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Disposes every inner batch scope, in reverse order, even when one throws.
        /// </summary>
        private sealed class CompositeBatchScope : IDisposable
        {
            private readonly List<IDisposable> m_scopes;
            private bool m_disposed;

            public CompositeBatchScope(List<IDisposable> scopes)
            {
                m_scopes = scopes;
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                Exception? failure = null;
                for (int i = m_scopes.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        m_scopes[i].Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                    }
                }
                Rethrow(failure);
            }
        }
    }
}
