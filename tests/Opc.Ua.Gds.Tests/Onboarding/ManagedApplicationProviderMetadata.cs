/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Models a provider-owned endpoint whose virtual clone deliberately retains its owner.
    /// </summary>
    internal sealed class SelfCloningEndpointDescription : EndpointDescription
    {
        /// <inheritdoc/>
        public override object Clone()
        {
            return this;
        }
    }

    /// <summary>
    /// Models nested provider application metadata whose clone retains its owner.
    /// </summary>
    internal sealed class SelfCloningApplicationDescription : ApplicationDescription
    {
        /// <inheritdoc/>
        public override object Clone()
        {
            return this;
        }
    }

    /// <summary>
    /// Models nested provider token metadata whose clone retains its owner.
    /// </summary>
    internal sealed class SelfCloningUserTokenPolicy : UserTokenPolicy
    {
        /// <inheritdoc/>
        public override object Clone()
        {
            return this;
        }
    }

    /// <summary>
    /// Models provider quota metadata whose clone retains its owner.
    /// </summary>
    internal sealed class SelfCloningEndpointConfiguration : EndpointConfiguration
    {
        /// <inheritdoc/>
        public override object Clone()
        {
            return this;
        }
    }
}
