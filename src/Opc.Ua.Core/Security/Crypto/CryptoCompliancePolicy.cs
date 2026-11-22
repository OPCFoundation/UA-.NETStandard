// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// How strictly the validation status of a crypto provider is enforced.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="Permissive"/>, which is exactly the behaviour
    /// the stack had before the provider model existed: nothing is filtered and
    /// nothing is warned about. Making a stricter mode the default would drop
    /// endpoints that work today, so tightening is an explicit choice.
    /// </remarks>
    public enum CryptoCompliancePolicy
    {
        /// <summary>
        /// Use whatever is configured and say nothing. Existing behaviour.
        /// </summary>
        Permissive = 0,

        /// <summary>
        /// Use whatever is configured, but report every provider that carries no
        /// validation so the choice is visible in logs, metrics and the address
        /// space.
        /// </summary>
        WarnOnUncertified = 1,

        /// <summary>
        /// Refuse providers that carry no validation, and withhold the security
        /// policies that depend on them.
        /// </summary>
        /// <remarks>
        /// This does not make the stack validated. It restricts it to platform
        /// cryptography and to algorithms that a validated module can perform;
        /// whether the module is actually running in a validated mode remains a
        /// property of how the machine is configured.
        /// </remarks>
        FipsOnly = 2
    }
}
