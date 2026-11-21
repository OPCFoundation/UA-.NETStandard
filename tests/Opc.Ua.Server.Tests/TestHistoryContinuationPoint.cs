// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A history continuation point that carries nothing but its identity, and records
    /// whether the session disposed it.
    /// </summary>
    internal sealed class TestHistoryContinuationPoint : IHistoryContinuationPoint
    {
        /// <summary>
        /// Creates a point with the given identity, or a fresh one.
        /// </summary>
        /// <param name="id">The identity to use.</param>
        public TestHistoryContinuationPoint(Guid? id = null)
        {
            Id = id ?? Guid.NewGuid();
        }

        /// <inheritdoc/>
        public Guid Id { get; }

        /// <summary>
        /// Whether the owner disposed this point.
        /// </summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Disposed = true;
        }
    }
}
