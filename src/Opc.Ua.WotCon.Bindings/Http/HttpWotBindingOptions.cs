/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Opc.Ua.WotCon.Bindings.Http
{
    /// <summary>
    /// Options for the HTTP WoT binding executor. The client factory is injectable
    /// so callers can supply a pooled, mutually-authenticated or test
    /// <see cref="HttpClient"/>; when none is supplied the executor owns a private
    /// client. Default headers are applied to every request in addition to any
    /// credential the provider resolves and are treated as sensitive by the
    /// redirect policy.
    /// </summary>
    public sealed class HttpWotBindingOptions
    {
        /// <summary>
        /// Gets or sets the factory that supplies the <see cref="HttpClient"/>. When
        /// <c>null</c> the executor creates and owns a private client whose handler
        /// disables automatic redirects and ambient cookie handling, so the executor
        /// can apply a bounded, origin-aware redirect policy that never leaks
        /// credentials across origins. A supplied client is treated as caller-owned
        /// and is never disposed or mutated by the executor. Every supplied client
        /// must satisfy
        /// <see cref="CallerClientHandlesRedirectSafety"/>, regardless of the
        /// currently configured headers or form security.
        /// </summary>
        public Func<HttpClient>? ClientFactory { get; set; }

        /// <summary>
        /// Gets or sets default headers applied to requests on the original origin
        /// and any same-origin redirect. All configured headers are treated as
        /// sensitive and are stripped from cross-origin redirects. The collection is
        /// copied into immutable per-channel state during activation; later changes
        /// to this property or its original collection do not affect an active
        /// channel.
        /// </summary>
        public IReadOnlyDictionary<string, string>? DefaultHeaders { get; set; }

        /// <summary>
        /// Gets or sets the poll interval used for observe / event operations.
        /// </summary>
        public TimeSpan ObserveInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the retry policy applied after consecutive unhealthy polls during an
        /// observe operation. Defaults to the stack's exponential backoff
        /// (<see cref="ExponentialBackoffChannelReconnectPolicy"/>, 500 ms doubling to 30 s), so an
        /// asset that has gone offline is not polled once per interval. The backoff never polls
        /// faster than <see cref="ObserveInterval"/>.
        /// </summary>
        public IChannelReconnectPolicy? RetryPolicy { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of redirects the executor-owned client
        /// follows for a single request. The default is <c>5</c>; <c>0</c> disables
        /// redirect following entirely. Default headers and custom header / query
        /// credentials are stripped whenever a redirect crosses to a different
        /// origin.
        /// </summary>
        public int MaxAutomaticRedirects { get; set; } = 5;

        /// <summary>
        /// Gets or sets whether the executor-owned client may follow a redirect that
        /// downgrades the scheme from <c>https</c> to <c>http</c>. The default is
        /// <c>false</c>: an insecure downgrade is refused.
        /// </summary>
        public bool AllowInsecureRedirectDowngrade { get; set; }

        /// <summary>
        /// Gets or sets whether a caller-supplied <see cref="HttpClient"/> is trusted
        /// to handle redirects safely. The default is <c>false</c>: every
        /// caller-supplied client fails closed because the executor cannot inspect or
        /// control its redirect handler, automatic cookie handling and
        /// <see cref="System.Net.CookieContainer"/>, mutable
        /// <see cref="HttpClient.DefaultRequestHeaders"/>, or later caller
        /// mutations. Set to <c>true</c> only when the supplied client is known to
        /// disable automatic redirects, or to follow them without forwarding
        /// credentials to a different origin.
        /// </summary>
        public bool CallerClientHandlesRedirectSafety { get; set; }
    }
}
