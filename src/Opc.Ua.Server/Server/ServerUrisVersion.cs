// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Maintains the <c>Server.UrisVersion</c> Property (Part 5 §6.3.1): every change of
    /// the server's NamespaceUris or ServerUris table raises it to a greater value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is a <c>VersionTime</c> (Part 4 §7.43), the seconds since
    /// 2000-01-01T00:00Z. Changes are detected with the mutation versions of the two
    /// tables, so the update is idempotent and can be called after any code that may have
    /// appended a namespace or server URI.
    /// </para>
    /// <para>
    /// Until the server has started (<see cref="MarkStarted"/>, or it was seen
    /// <see cref="IServerInternal.IsRunning"/>) no Client can read the value, so the changes
    /// made while it starts are folded into the start time instead of each adding one: the
    /// startup burst of namespace registrations does not move the value ahead of the
    /// clock. After that, including while requests are halted (Suspended, Test, Failed), a
    /// change sets the larger of the previous value
    /// plus one and the current time. The value only runs ahead of the clock when more
    /// changes are made at runtime than seconds pass; a server restarted while it is ahead
    /// can then report an older value than the previous run until the clock catches up.
    /// Nothing is persisted across restarts.
    /// </para>
    /// </remarks>
    internal static class ServerUrisVersion
    {
        /// <summary>
        /// Raises the UrisVersion when a table changed since the last call and reports the
        /// change to the monitored items of the Property.
        /// </summary>
        /// <param name="server">The server.</param>
        public static void Update(IServerInternal server)
        {
            PropertyState<uint>? urisVersion = Advance(server);
            urisVersion?.ClearChangeMasks(server.DefaultSystemContext, false);
        }

        /// <summary>
        /// Records that the server finished starting, so later changes each raise the
        /// UrisVersion, and reports a change made since the last update.
        /// </summary>
        /// <param name="server">The server.</param>
        public static void MarkStarted(IServerInternal server)
        {
            PropertyState<uint>? urisVersion = GetProperty(server);
            if (urisVersion == null)
            {
                return;
            }

            Tracker tracker = s_trackers.GetValue(urisVersion, _ => new Tracker());
            lock (tracker.Gate)
            {
                tracker.Started = true;
            }
            Update(server);
        }

        /// <summary>
        /// Raises the UrisVersion when a table changed since the last call. The change masks
        /// of the Property are updated but not cleared; the caller reports the change.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <returns>The Property when its value was raised; otherwise <c>null</c>.</returns>
        public static PropertyState<uint>? Advance(IServerInternal server)
        {
            PropertyState<uint>? urisVersion = GetProperty(server);
            if (urisVersion == null)
            {
                return null;
            }

            Tracker tracker = s_trackers.GetValue(urisVersion, _ => new Tracker());
            lock (tracker.Gate)
            {
                if (!tracker.Started && server!.IsRunning)
                {
                    tracker.Started = true;
                }

                // read inside the gate, a concurrent caller must not record older versions.
                long namespaceVersion = server!.NamespaceUris.Version;
                long serverVersion = server.ServerUris.Version;
                if (tracker.Initialized &&
                    tracker.NamespaceVersion == namespaceVersion &&
                    tracker.ServerVersion == serverVersion)
                {
                    return null;
                }

                DateTime utcNow = ((server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System)
                    .GetUtcNow()
                    .UtcDateTime;
                uint previous = urisVersion.Value;
                uint versionTime = ToVersionTime(utcNow);
                uint value;
                if (tracker.Started)
                {
                    uint next = previous == uint.MaxValue ? 1 : previous + 1;
                    value = Math.Max(next, versionTime);
                }
                else
                {
                    // not readable before the first start: fold startup changes into the
                    // start time.
                    value = Math.Max(previous, versionTime);
                }

                tracker.NamespaceVersion = namespaceVersion;
                tracker.ServerVersion = serverVersion;
                tracker.Initialized = true;
                if (value == previous)
                {
                    return null;
                }

                urisVersion.Value = value;
                urisVersion.Timestamp = utcNow;
                urisVersion.UpdateChangeMasks(NodeStateChangeMasks.Value);
                return urisVersion;
            }
        }

        private static PropertyState<uint>? GetProperty(IServerInternal? server)
        {
            return server?.DiagnosticsNodeManager?
                .FindPredefinedNode<ServerObjectState>(ObjectIds.Server)?
                .UrisVersion;
        }

        /// <summary>
        /// Converts a UTC time to a <c>VersionTime</c>, the seconds since 2000-01-01T00:00Z.
        /// </summary>
        /// <param name="utcNow">The UTC time.</param>
        /// <returns>The VersionTime; never 0, which means no version information.</returns>
        internal static uint ToVersionTime(DateTime utcNow)
        {
            double seconds = (utcNow - s_versionTimeBase).TotalSeconds;
            if (seconds < 1)
            {
                return 1;
            }
            return seconds >= uint.MaxValue ? uint.MaxValue : (uint)seconds;
        }

        /// <summary>
        /// The table versions the Property value was last raised for.
        /// </summary>
        private sealed class Tracker
        {
            public readonly Lock Gate = new();
            public bool Initialized;
            public bool Started;
            public long NamespaceVersion;
            public long ServerVersion;
        }

        private static readonly DateTime s_versionTimeBase = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly ConditionalWeakTable<PropertyState<uint>, Tracker> s_trackers = new();
    }
}
