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

using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Bounded short-lived local sessions with a separate anti-CSRF token. No browser-selected UA identity.
/// </summary>
internal sealed class ConsoleSessions
{
    /// <summary>
    /// Loads an externally provisioned high-entropy launch credential.
    /// </summary>
    internal ConsoleSessions(ConsoleOptions options, TimeProvider? timeProvider = null)
    {
        m_timeProvider = timeProvider ?? TimeProvider.System;
        string token = File.ReadAllText(options.BootstrapKeyFile).Trim();
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Bootstrap key file must contain 32 random bytes as 64 hex digits.");
        }
        m_keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        m_role = options.Role;
    }

    /// <summary>
    /// Authenticates a local launch and creates one of at most sixteen sessions.
    /// </summary>
    internal SessionDto? Bootstrap(HttpContext context, string token)
    {
        if (token.Length != 64 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)), m_keyHash))
        {
            return null;
        }
        lock (m_gate)
        {
            foreach (string id in m_sessions.Where(pair =>
                pair.Value.ExpiresUtc <= m_timeProvider.GetUtcNow().UtcDateTime)
                .Select(static pair => pair.Key).ToArray())
            {
                m_sessions.Remove(id);
            }
            if (m_sessions.Count >= 16)
            {
                return null;
            }
            string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var session = new SessionDto(m_role,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                m_timeProvider.GetUtcNow().UtcDateTime.AddMinutes(30));
            m_sessions.Add(sessionId, session);
            context.Response.Cookies.Append("recycling-console", sessionId, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/",
                Expires = new DateTimeOffset(session.ExpiresUtc),
                IsEssential = true
            });
            return session;
        }
    }

    /// <summary>
    /// Resolves an unexpired session without extending its lifetime.
    /// </summary>
    internal SessionDto? Get(HttpContext context)
    {
        string? id = context.Request.Cookies["recycling-console"];
        lock (m_gate)
        {
            return id != null && m_sessions.TryGetValue(id, out SessionDto? session) &&
                session.ExpiresUtc > m_timeProvider.GetUtcNow().UtcDateTime ? session : null;
        }
    }

    /// <summary>
    /// Requires both explicit same-origin browser evidence and a per-session anti-CSRF credential.
    /// </summary>
    internal static bool CanMutate(HttpContext context, SessionDto session, ConsoleOptions options)
    {
        string token = context.Request.Headers["X-CSRF-Token"].ToString();
        return context.Request.Headers.Origin == options.Origin &&
            token.Length == session.CsrfToken.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(session.CsrfToken));
    }

    /// <summary>Serializes session issuance and lookup.</summary>
    private readonly System.Threading.Lock m_gate = new();
    /// <summary>At most sixteen unexpired browser sessions.</summary>
    private readonly Dictionary<string, SessionDto> m_sessions = new(StringComparer.Ordinal);
    /// <summary>One-way hash of the external launch credential.</summary>
    private readonly byte[] m_keyHash;
    /// <summary>Immutable local role ceiling.</summary>
    private readonly string m_role;
    /// <summary>Injectable session wall clock and expiry timer source.</summary>
    private readonly TimeProvider m_timeProvider;
}
