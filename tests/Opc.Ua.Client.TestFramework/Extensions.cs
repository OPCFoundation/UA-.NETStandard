/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client.TestFramework
{
    public static class Extensions
    {
        /// <summary>
        /// Test helper that returns the session's V2 subscription manager,
        /// throwing when the session does not expose one (classic engine).
        /// Replaces the removed ManagedSession.SubscriptionManager property in
        /// tests that require the V2 engine (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static Subscriptions.ISubscriptionManager RequireSubscriptionManager(
            this ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (!session.TryGetSubscriptionManager(
                    out Subscriptions.ISubscriptionManager? manager))
            {
                throw new InvalidOperationException(
                    "The session does not expose a V2 subscription manager.");
            }
            return manager;
        }

        public static bool HasArgsOfType(
            this ArrayOf<CallMethodRequest> requests,
            params Type[] argTypes)
        {
            if (requests.Count != 1 || requests[0].InputArguments.Count != argTypes.Length)
            {
                return false;
            }
            for (int i = 0; i < argTypes.Length; i++)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (requests[0].InputArguments[i].Value!.GetType() != argTypes[i])
                {
                    return false;
                }
#pragma warning restore CS0618 // Type or member is obsolete
            }
            return true;
        }

        public static CallResponse ToResponse(
            this ArrayOf<Variant> outputArguments,
            StatusCode response = default,
            StatusCode result = default)
        {
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = response
                },
                Results =
                [
                    new CallMethodResult
                    {
                        StatusCode = result,
                        OutputArguments = outputArguments
                    }
                ]
            };
        }
    }
}
