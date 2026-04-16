using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The names of additional parameters used in security-related operations.
    /// </summary>
    public static class AdditionalParameterNames
    {
        /// <summary>
        /// The algorith to use for the ephemeral key used to encrypt user identity tokens.
        /// </summary>
        public const string ECDHPolicyUri = "ECDHPolicyUri";

        /// <summary>
        /// An ephemeral key used to encrypt user identity tokens.
        /// </summary>
        public const string ECDHKey = "ECDHKey";

        /// <summary>
        /// Padding bytes added to randomize the length of messages.
        /// </summary>
        public const string Padding = "Padding";
    }
}
