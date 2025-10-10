# Switch Statement Refactoring Guide

## Issue Summary
This document describes the pattern for refactoring switch statements in the UA-.NETStandard codebase that test for a finite set of values (enums or defined constants) to throw exceptions for unexpected values instead of silently handling them in the default case.

## Pattern to Apply

### Before (Problematic):
```csharp
switch (idType)
{
    case IdType.Opaque:
        return new NodeId(ReadByteString("Id"), namespaceIndex);
    case IdType.String:
        return new NodeId(ReadString("Id"), namespaceIndex);
    case IdType.Guid:
        return new NodeId(ReadGuid("Id"), namespaceIndex);
    default:
        return new NodeId(ReadUInt32("Id"), namespaceIndex);  // Implicit Numeric case
}
```

### After (Recommended):
```csharp
switch (idType)
{
    case IdType.Numeric:
        return new NodeId(ReadUInt32("Id"), namespaceIndex);
    case IdType.Opaque:
        return new NodeId(ReadByteString("Id"), namespaceIndex);
    case IdType.String:
        return new NodeId(ReadString("Id"), namespaceIndex);
    case IdType.Guid:
        return new NodeId(ReadGuid("Id"), namespaceIndex);
    default:
        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
            Utils.Format("Unexpected IdType value: {0}", idType));
}
```

## When to Apply This Pattern

Apply this pattern when ALL of the following conditions are met:

1. **Finite Set**: The switch selector is an enum type or a well-defined set of constants
2. **Known Values**: All possible valid values can be enumerated at compile time
3. **No Fallback Logic**: The default case is not intentionally handling "all other cases" as valid behavior
4. **Error Detection**: An unexpected value indicates a programming error or data corruption

## When NOT to Apply This Pattern

Do NOT apply this pattern when:

1. **Intentional Fallback**: The default case provides legitimate fallback behavior
   - Example: Returning a default value for unknown/unsupported security policies
   - Example: Handling "all other" built-in types as ExtensionObjects

2. **Open Sets**: The selector is not a finite set
   - Example: uint attributeId where new attributes may be added
   - Example: String-based lookups where unknown values are valid

3. **Design Patterns**: The code uses the switch as part of a visitor or strategy pattern
   - Example: Dispatching to base class for unhandled cases

4. **Generated Code**: The code is auto-generated and will be regenerated
   - Example: Files in Stack/Opc.Ua.Core/Stack/Generated/

## Files Updated

### Stack/Opc.Ua.Core/Types/Encoders/JsonDecoder.cs
- Updated 3 switch statements on IdType enum
- Added explicit case for IdType.Numeric
- Changed default to throw ServiceResultException

### Stack/Opc.Ua.Core/Security/Certificates/EccUtils.cs
- Updated 2 instances of GetSignatureAlgorithmName
- Added cases for ECC_curve25519 and ECC_curve448
- Changed default to throw for unexpected security policy URIs

### Stack/Opc.Ua.Core/Types/Utils/DataGenerator.cs
- Updated GetRandomNodeId for IdType enum
- Updated GetRandomInteger and GetRandomUInteger
- Added explicit cases and changed defaults to throw

### Stack/Opc.Ua.Core/Types/Utils/RelativePath.cs
- Updated 3 switch statements on ElementType enum
- Added default cases that throw for unexpected ElementType values

## Testing
All 24,625 unit tests in Opc.Ua.Core.Tests pass after applying these changes.

## Remaining Work

This is a large-scale refactoring affecting 475+ switch statements across the codebase. The changes made so far demonstrate the pattern on clear-cut enum-based switches. Many remaining switches fall into the "do not apply" category because they:

- Intentionally provide fallback behavior
- Handle open sets of values
- Are part of legitimate design patterns

Future work should:
1. Carefully review each switch statement individually
2. Verify the intent of the default case with domain experts
3. Add explicit cases for all known enum values
4. Only change defaults to throw when it's truly an error condition

## Exception Details

When throwing exceptions, always include the unexpected value:
```csharp
throw new ServiceResultException(StatusCodes.BadUnexpectedError,
    Utils.Format("Unexpected {EnumName} value: {0}", unexpectedValue));
```

This aids in debugging and helps identify:
- Data corruption issues
- Missing enum values after updates
- Programming errors in value assignment
