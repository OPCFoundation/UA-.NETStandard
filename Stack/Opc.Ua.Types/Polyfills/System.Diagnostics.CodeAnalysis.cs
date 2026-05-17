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

// Polyfills for trim analysis attributes that are not available
// in .NET Standard 2.0/2.1 or .NET Framework.

#if !NET5_0_OR_GREATER

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Indicates that the specified method requires dynamic access
    /// to code that is not referenced statically.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method |
        AttributeTargets.Constructor |
        AttributeTargets.Class,
        Inherited = false)]
    public sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="RequiresUnreferencedCodeAttribute"/> class.
        /// </summary>
        public RequiresUnreferencedCodeAttribute(string message)
        {
            Message = message;
        }

        /// <summary>
        /// Gets a message that contains information about the
        /// usage of unreferenced code.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets or sets an optional URL with more information
        /// about the method.
        /// </summary>
        public string? Url { get; set; }
    }

    /// <summary>
    /// Suppresses reporting of a specific rule violation, allowing
    /// multiple suppressions on a single code artifact.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.All,
        Inherited = false,
        AllowMultiple = true)]
    public sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="UnconditionalSuppressMessageAttribute"/> class.
        /// </summary>
        public UnconditionalSuppressMessageAttribute(
            string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        /// <summary>
        /// Gets the category.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Gets the check identifier.
        /// </summary>
        public string CheckId { get; }

        /// <summary>
        /// Gets or sets the justification for suppressing the
        /// code analysis message.
        /// </summary>
        public string? Justification { get; set; }

        /// <summary>
        /// Gets or sets the scope.
        /// </summary>
        public string? Scope { get; set; }

        /// <summary>
        /// Gets or sets the target.
        /// </summary>
        public string? Target { get; set; }

        /// <summary>
        /// Gets or sets the message id.
        /// </summary>
        public string? MessageId { get; set; }
    }

    /// <summary>
    /// Specifies the types of members that are dynamically accessed.
    /// </summary>
    [Flags]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
    public enum DynamicallyAccessedMemberTypes
#pragma warning restore CA2217 // Do not mark enums with FlagsAttribute
    {
        /// <summary>
        /// Specifies no members.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies the default, parameterless public constructor.
        /// </summary>
        PublicParameterlessConstructor = 0x0001,

        /// <summary>
        /// Specifies all public constructors.
        /// </summary>
#pragma warning disable RCS1157 // Composite enum value contains undefined flag
        PublicConstructors = 0x0003,
#pragma warning restore RCS1157 // Composite enum value contains undefined flag

        /// <summary>
        /// Specifies all non-public constructors.
        /// </summary>
        NonPublicConstructors = 0x0004,

        /// <summary>
        /// Specifies all public methods.
        /// </summary>
        PublicMethods = 0x0008,

        /// <summary>
        /// Specifies all non-public methods.
        /// </summary>
        NonPublicMethods = 0x0010,

        /// <summary>
        /// Specifies all public fields.
        /// </summary>
        PublicFields = 0x0020,

        /// <summary>
        /// Specifies all non-public fields.
        /// </summary>
        NonPublicFields = 0x0040,

        /// <summary>
        /// Specifies all public nested types.
        /// </summary>
        PublicNestedTypes = 0x0080,

        /// <summary>
        /// Specifies all non-public nested types.
        /// </summary>
        NonPublicNestedTypes = 0x0100,

        /// <summary>
        /// Specifies all public properties.
        /// </summary>
        PublicProperties = 0x0200,

        /// <summary>
        /// Specifies all non-public properties.
        /// </summary>
        NonPublicProperties = 0x0400,

        /// <summary>
        /// Specifies all public events.
        /// </summary>
        PublicEvents = 0x0800,

        /// <summary>
        /// Specifies all non-public events.
        /// </summary>
        NonPublicEvents = 0x1000,

        /// <summary>
        /// Specifies all interfaces.
        /// </summary>
        Interfaces = 0x2000,

        /// <summary>
        /// Specifies all members.
        /// </summary>
        All = ~None
    }

    /// <summary>
    /// Indicates that certain members on a specified
    /// <see cref="Type"/> are accessed dynamically.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Field |
        AttributeTargets.ReturnValue |
        AttributeTargets.GenericParameter |
        AttributeTargets.Parameter |
        AttributeTargets.Property |
        AttributeTargets.Method,
        Inherited = false,
        AllowMultiple = true)]
    public sealed class DynamicallyAccessedMembersAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="DynamicallyAccessedMembersAttribute"/> class.
        /// </summary>
        public DynamicallyAccessedMembersAttribute(
            DynamicallyAccessedMemberTypes memberTypes)
        {
            MemberTypes = memberTypes;
        }

        /// <summary>
        /// Gets the <see cref="DynamicallyAccessedMemberTypes"/>
        /// that specifies the type of dynamically accessed members.
        /// </summary>
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }

    /// <summary>
    /// Indicates that the specified method requires the ability to
    /// generate new code at runtime.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method |
        AttributeTargets.Constructor |
        AttributeTargets.Class,
        Inherited = false)]
    public sealed class RequiresDynamicCodeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="RequiresDynamicCodeAttribute"/> class.
        /// </summary>
        public RequiresDynamicCodeAttribute(string message)
        {
            Message = message;
        }

        /// <summary>
        /// Gets a message that contains information about the
        /// usage of dynamic code.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets or sets an optional URL with more information
        /// about the method.
        /// </summary>
        public string? Url { get; set; }
    }
}

#endif
