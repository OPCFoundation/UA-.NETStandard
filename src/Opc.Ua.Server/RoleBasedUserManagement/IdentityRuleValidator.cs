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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates and normalises <see cref="IdentityMappingRuleType"/> instances
    /// per OPC UA Part 18 §4.4.3.
    /// </summary>
    internal static class IdentityRuleValidator
    {
        /// <summary>
        /// Validates a rule. Returns <c>Good</c> if the rule is acceptable,
        /// <c>Bad_InvalidArgument</c> with a diagnostic message otherwise.
        /// </summary>
        public static ServiceResult Validate(IdentityMappingRuleType rule)
        {
            if (rule == null)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument,
                    new LocalizedText("Rule is null."));
            }

            string criteria = rule.Criteria ?? string.Empty;

            switch (rule.CriteriaType)
            {
                case IdentityCriteriaType.Anonymous:
                case IdentityCriteriaType.AuthenticatedUser:
                case IdentityCriteriaType.TrustedApplication:
                    if (criteria.Length > 0)
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument,
                            new LocalizedText($"Criteria must be empty for {rule.CriteriaType}."));
                    }
                    return ServiceResult.Good;
                case IdentityCriteriaType.UserName:
                case IdentityCriteriaType.Role:
                case IdentityCriteriaType.GroupId:
                case IdentityCriteriaType.Application:
                    if (criteria.Length == 0)
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument,
                            new LocalizedText($"Criteria must be non-empty for {rule.CriteriaType}."));
                    }
                    return ServiceResult.Good;
                case IdentityCriteriaType.Thumbprint:
                    if (!IsValidThumbprint(criteria))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument,
                            new LocalizedText(
                                "Thumbprint must be an upper-case hexadecimal string with no spaces."));
                    }
                    return ServiceResult.Good;
                case IdentityCriteriaType.X509Subject:
                    if (!IsValidX509Subject(criteria))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument,
                            new LocalizedText(
                                "X509 subject must match the format Name=\"Value\"/Name=\"Value\"... per Part 18 §4.4.3."));
                    }
                    return ServiceResult.Good;
                default:
                    return new ServiceResult(StatusCodes.BadInvalidArgument,
                        new LocalizedText($"Unknown criteriaType {rule.CriteriaType}."));
            }
        }

        /// <summary>
        /// Returns <c>true</c> if two rules are equivalent for the purposes of
        /// duplicate-detection (Part 18 §4.4.5 mandates <c>Bad_AlreadyExists</c>
        /// when an equivalent rule already exists).
        /// </summary>
        public static bool AreEquivalent(IdentityMappingRuleType a, IdentityMappingRuleType b)
        {
            if (a.CriteriaType != b.CriteriaType)
            {
                return false;
            }
            string criteriaA = a.Criteria ?? string.Empty;
            string criteriaB = b.Criteria ?? string.Empty;
            StringComparison comparison = a.CriteriaType == IdentityCriteriaType.Thumbprint
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(criteriaA, criteriaB, comparison);
        }

        private static bool IsValidThumbprint(string criteria)
        {
            if (criteria.Length == 0 || criteria.Length % 2 != 0)
            {
                return false;
            }
            foreach (char c in criteria)
            {
                if (c is >= '0' and <= '9')
                {
                    continue;
                }
                if (c is >= 'A' and <= 'F')
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private static bool IsValidX509Subject(string criteria)
        {
            // Grammar: /Name="Value"(/Name="Value")*
            // Names: CN, O, OU, DC, L, S, C, dnQualifier, serialNumber.
            if (criteria.Length == 0)
            {
                return false;
            }

            int index = 0;
            while (index < criteria.Length)
            {
                // Read name.
                int nameStart = index;
                while (index < criteria.Length && criteria[index] != '=')
                {
                    index++;
                }
                if (index == nameStart || index >= criteria.Length)
                {
                    return false;
                }
                string name = criteria[nameStart..index];
                if (!IsKnownSubjectName(name))
                {
                    return false;
                }
                index++; // skip '='
                if (index >= criteria.Length || criteria[index] != '"')
                {
                    return false;
                }
                index++; // skip opening quote
                while (index < criteria.Length && criteria[index] != '"')
                {
                    index++;
                }
                if (index >= criteria.Length)
                {
                    return false;
                }
                index++; // skip closing quote
                if (index < criteria.Length)
                {
                    if (criteria[index] != '/')
                    {
                        return false;
                    }
                    index++; // skip separator
                    if (index >= criteria.Length)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsKnownSubjectName(string name)
        {
            return name switch
            {
                "CN" or "O" or "OU" or "DC" or "L" or "S" or "C"
                    or "dnQualifier" or "serialNumber" => true,
                _ => false
            };
        }

        /// <summary>
        /// Normalises an X.509 subject name read from a certificate into the
        /// canonical Part 18 §4.4.3 form used by the <see cref="IdentityCriteriaType.X509Subject"/>
        /// criteria, so comparisons against rule criteria are deterministic.
        /// Unknown names are skipped per spec.
        /// </summary>
        public static string NormaliseX509Subject(string? subject)
        {
            if (string.IsNullOrEmpty(subject))
            {
                return string.Empty;
            }

            // The .NET X509 subject is comma-separated "Name=Value" pairs in
            // reverse order of the certificate; first split into segments.
            var pairs = new System.Collections.Generic.List<(string Name, string Value)>();
            int i = 0;
            while (i < subject!.Length)
            {
                // Skip whitespace and separators.
                while (i < subject.Length && (subject[i] == ',' || subject[i] == ' '))
                {
                    i++;
                }
                int nameStart = i;
                while (i < subject.Length && subject[i] != '=')
                {
                    i++;
                }
                if (i >= subject.Length)
                {
                    break;
                }
                string name = subject[nameStart..i].Trim();
                i++; // skip '='
                string value;
                if (i < subject.Length && subject[i] == '"')
                {
                    i++; // skip opening quote
                    int vs = i;
                    while (i < subject.Length && subject[i] != '"')
                    {
                        i++;
                    }
                    value = subject[vs..i];
                    if (i < subject.Length)
                    {
                        i++; // skip closing quote
                    }
                }
                else
                {
                    int vs = i;
                    while (i < subject.Length && subject[i] != ',')
                    {
                        i++;
                    }
                    value = subject[vs..i].Trim();
                }
                if (name.Length > 0 && IsKnownSubjectName(name))
                {
                    pairs.Add((name, value));
                }
            }

            // Spec order: CN, O, OU, DC, L, S, C, dnQualifier, serialNumber.
            // For names appearing multiple times preserve relative order of
            // appearance within the certificate.
            string[] order =
            [
                "CN", "O", "OU", "DC", "L", "S", "C", "dnQualifier", "serialNumber"
            ];
            var sb = new StringBuilder();
            foreach (string name in order)
            {
                foreach ((string Name, string Value) in pairs)
                {
                    if (!string.Equals(Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (sb.Length > 0)
                    {
                        sb.Append('/');
                    }
                    sb.Append(Name)
                        .Append('=')
                        .Append('"')
                        .Append(Value)
                        .Append('"');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns an upper-case hex thumbprint with no separators.
        /// </summary>
        public static string NormaliseThumbprint(string? thumbprint)
        {
            if (string.IsNullOrEmpty(thumbprint))
            {
                return string.Empty;
            }
            return thumbprint!.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpper(CultureInfo.InvariantCulture);
        }
    }
}
