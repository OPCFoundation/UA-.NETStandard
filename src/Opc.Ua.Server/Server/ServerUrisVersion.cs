/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
    /// Before the server accepts requests (<see cref="IServerInternal.IsRunning"/>) no Client can read the value,
    /// so the changes made while it starts are folded into the start time instead of each
    /// adding one: the startup burst of namespace registrations does not move the value
    /// ahead of the clock. Once running, a change sets the larger of the previous value
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
        /// Raises the UrisVersion when a table changed since the last call. The change masks
        /// of the Property are updated but not cleared; the caller reports the change.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <returns>The Property when its value was raised; otherwise <c>null</c>.</returns>
        public static PropertyState<uint>? Advance(IServerInternal server)
        {
            ServerObjectState? serverObject =
                server?.DiagnosticsNodeManager?.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            PropertyState<uint>? urisVersion = serverObject?.UrisVersion;
            if (urisVersion == null)
            {
                return null;
            }

            Tracker tracker = s_trackers.GetValue(urisVersion, _ => new Tracker());
            lock (tracker.Gate)
            {
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
                if (server.IsRunning)
                {
                    uint next = previous == uint.MaxValue ? 1 : previous + 1;
                    value = Math.Max(next, versionTime);
                }
                else
                {
                    // not readable yet: fold startup changes into the start time.
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
            public long NamespaceVersion;
            public long ServerVersion;
        }

        private static readonly DateTime s_versionTimeBase = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly ConditionalWeakTable<PropertyState<uint>, Tracker> s_trackers = new();
    }
}
