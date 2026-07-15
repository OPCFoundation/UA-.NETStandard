/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Configures the Optional members of the OPC 10000-12 §7.10.3
    /// <c>ServerConfigurationType</c> surface that the
    /// <see cref="ConfigurationNodeManager"/> can expose in addition to the
    /// mandatory Push Certificate Management members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every member here is opt-in: when a value is <see langword="null"/> (or
    /// a provider is not set) the corresponding Optional address-space node is
    /// suppressed, preserving the "expose only when configured/known"
    /// behaviour required by the specification. The identity Properties
    /// (<c>ApplicationUri</c>, <c>ProductUri</c>, <c>ApplicationType</c>,
    /// <c>ApplicationNames</c>) are always known from the
    /// <see cref="ApplicationConfiguration"/> and are therefore exposed
    /// independently of these options.
    /// </para>
    /// <para>
    /// The type doubles as the DI options object (registered via
    /// <c>AddOptions&lt;ServerConfigurationOptions&gt;()</c>) and as the direct
    /// carrier threaded through <see cref="MainNodeManagerFactory"/> for the
    /// non-DI construction path.
    /// </para>
    /// </remarks>
    public sealed class ServerConfigurationOptions
    {
        /// <summary>
        /// When set, exposes the Optional <c>HasSecureElement</c> Property and
        /// seeds it with this value (§7.10.3): <see langword="true"/> if the
        /// application has access to hardware-based secure storage for the
        /// private keys of its Certificates. When <see langword="null"/> the
        /// Property is suppressed.
        /// </summary>
        public bool? HasSecureElement { get; set; }

        /// <summary>
        /// When set, exposes the Optional <c>InApplicationSetup</c> Property and
        /// seeds it with this value (§7.10.3, §G.2): <see langword="true"/> if
        /// the application is in the application-setup state. When
        /// <see langword="null"/> the Property is suppressed. Hosts that track a
        /// dynamic setup state can write the node's value at runtime once it is
        /// exposed.
        /// </summary>
        public bool? InApplicationSetup { get; set; }

        /// <summary>
        /// When set, exposes the Optional <c>ResetToServerDefaults</c> Method
        /// (§7.10.13) and delegates the actual reset to this provider. When
        /// <see langword="null"/> the Method is suppressed.
        /// </summary>
        public IServerConfigurationResetProvider? ResetProvider { get; set; }

        /// <summary>
        /// When set, exposes the Optional <c>ConfigurationFile</c> Object
        /// (§7.10.20) and backs its read/update flow with this provider. When
        /// <see langword="null"/> the Object is suppressed.
        /// </summary>
        public IApplicationConfigurationFileProvider? ConfigurationFileProvider { get; set; }

        /// <summary>
        /// The grace period the server advertises via <c>SecondsTillShutdown</c>
        /// and waits after the <c>ResetToServerDefaults</c> Method response is
        /// returned before invoking <see cref="ResetProvider"/> and shutting
        /// down (§7.10.13). Gives the calling Client a chance to receive the
        /// Method response. Defaults to 10&#160;seconds.
        /// </summary>
        public TimeSpan ResetShutdownDelay { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The <c>ActivityTimeout</c> (§7.8.5.1) seeded on the
        /// <c>ConfigurationFile</c> Object, in milliseconds: the maximum
        /// elapsed time between Method calls after <c>Open</c> before the server
        /// automatically closes the file and discards any changes. Defaults to
        /// 60&#160;000&#160;ms (1&#160;minute).
        /// </summary>
        public double ConfigurationFileActivityTimeout { get; set; } = 60000.0;

        /// <summary>
        /// The resource-protection safety ceiling, in bytes, that bounds the
        /// TrustList size the server actually enforces and advertises through
        /// <c>ServerConfiguration.MaxTrustListSize</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 10000-12 §8.4.5 defines <c>MaxTrustListSize</c> as the maximum
        /// size, in bytes, of a TrustList a Client may write, where <c>0</c>
        /// means "no limit" (unlimited). Enforcing a truly unbounded write would
        /// expose the server to unbounded allocation, so this ceiling caps the
        /// <em>actually-enforced</em> limit regardless of the advertised value.
        /// The server advertises the honest effective limit (never <c>0</c>
        /// while a finite cap is in force), computed as:
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// advertised <c>MaxTrustListSize</c> is <c>0</c> (unlimited) → the
        /// effective limit is this ceiling;
        /// </item>
        /// <item>
        /// advertised <c>MaxTrustListSize</c> is above this ceiling → the
        /// effective limit is this ceiling;
        /// </item>
        /// <item>
        /// advertised <c>MaxTrustListSize</c> is finite and at or below this
        /// ceiling → the effective limit is the advertised value.
        /// </item>
        /// </list>
        /// <para>
        /// Defaults to 1&#160;MiB — the value the server historically enforced
        /// as a hidden cap. Raise it for servers that must accept larger
        /// TrustLists (for example large CRLs); a non-positive value falls back
        /// to the 1&#160;MiB default.
        /// </para>
        /// </remarks>
        public int MaxTrustListSizeSafetyCeiling { get; set; } = 1 * 1024 * 1024;
    }
}
