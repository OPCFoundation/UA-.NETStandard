// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using System.Collections.Generic;

    /// <summary>
    /// Result set
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Results"></param>
    /// <param name="Errors"></param>
    internal record struct ResultSet<T>(IReadOnlyList<T> Results,
        IReadOnlyList<ServiceResult> Errors);

    /// <summary>
    /// Result set helpers
    /// </summary>
    internal static class ResultSet
    {
        /// <summary>
        /// Empty result set
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ResultSet<T> Empty<T>()
        {
            return new([], []);
        }
    }
}
