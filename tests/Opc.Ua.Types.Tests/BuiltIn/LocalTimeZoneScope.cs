/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Makes <see cref="TimeZoneInfo.Local"/> a zone with a non-zero UTC offset for the
    /// lifetime of the scope, so tests of local/UTC conversions also fail on UTC hosts
    /// (CI runners default to UTC). The local zone is process wide: use it only from
    /// <see cref="NonParallelizableAttribute"/> tests.
    /// </summary>
    internal sealed class LocalTimeZoneScope : IDisposable
    {
        /// <summary>
        /// The offset of the substituted local zone (no daylight saving time).
        /// </summary>
        public static readonly TimeSpan Offset = new(5, 30, 0);

        private LocalTimeZoneScope()
        {
        }

        /// <summary>
        /// Substitutes the local zone. If the runtime does not allow that, the test is
        /// still meaningful on a host whose own zone is not UTC; otherwise it is
        /// reported as inconclusive.
        /// </summary>
        public static LocalTimeZoneScope Create()
        {
            TimeZoneInfo.ClearCachedData();

            if (!TrySetLocal(TimeZoneInfo.CreateCustomTimeZone(
                "Opc.Ua.Tests.Local",
                Offset,
                "Test local time",
                "Test local time")))
            {
                Assume.That(
                    TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    Is.Not.EqualTo(TimeSpan.Zero),
                    "The local time zone cannot be substituted and the host runs in UTC.");
            }

            return new LocalTimeZoneScope();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            TimeZoneInfo.ClearCachedData();
        }

        private static bool TrySetLocal(TimeZoneInfo zone)
        {
            // .NET caches the local zone in TimeZoneInfo.s_cachedData._localTimeZone
            // (.NET Framework: m_localTimeZone).
            object? cachedData = typeof(TimeZoneInfo)
                .GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);

            if (cachedData == null)
            {
                return false;
            }

            FieldInfo? local =
                cachedData.GetType().GetField("_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance) ??
                cachedData.GetType().GetField("m_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance);

            if (local == null)
            {
                return false;
            }

            try
            {
                local.SetValue(cachedData, zone);
            }
            catch (FieldAccessException)
            {
                return false;
            }

            return TimeZoneInfo.Local.BaseUtcOffset == Offset;
        }
    }
}
