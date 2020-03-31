/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#ifndef _OpcUa_ErrorHandling_H_
#define _OpcUa_ErrorHandling_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * OpcUa_ReferenceParameter
 *===========================================================================*/
 /** @brief use this to eliminate "unused parameter" warnings */
#define OpcUa_ReferenceParameter(xParameter) (void)(xParameter)


/*============================================================================
 * OpcUa_StatusCodeString
 *===========================================================================*/
typedef struct _OpcUa_StatusCodeString
{
    OpcUa_StatusCode    uStatus;
    OpcUa_StringA       sString;
} OpcUa_StatusCodeString;

/*============================================================================
 * Error Handling
 *===========================================================================*/
/*  */
#if OPCUA_TRACE_ERROR_MACROS
#define OpcUa_DeclareErrorTraceModule(xModule) OpcUa_UInt32 uModule = xModule; OpcUa_ReferenceParameter(uModule);
#else /* OPCUA_TRACE_ERROR_MACROS */
#define OpcUa_DeclareErrorTraceModule(xModule)
#endif /* OPCUA_TRACE_ERROR_MACROS */

/* general modules */
#define OpcUa_Module_NoModule           0x00000000L
#define OpcUa_Module_TestModule         0x00000001L
#define OpcUa_Module_Stream             0x00000002L
#define OpcUa_Module_Buffer             0x00000003L
#define OpcUa_Module_Connection         0x00000004L
#define OpcUa_Module_Crypto             0x00000005L 
#define OpcUa_Module_Listener           0x00000006L
#define OpcUa_Module_MemoryStream       0x00000007L
#define OpcUa_Module_PkiProvider        0x00000008L

/* transport modules */
#define OpcUa_Module_TcpListener        0x00000101L
#define OpcUa_Module_HttpListener       0x00000102L
#define OpcUa_Module_TcpConnection      0x00000103L
#define OpcUa_Module_TcpStream          0x00000104L
#define OpcUa_Module_SecureConnection   0x00000105L
#define OpcUa_Module_SecureListener     0x00000106L
#define OpcUa_Module_SecureStream       0x00000107L
#define OpcUa_Module_SecureChannel      0x00000108L
#define OpcUa_Module_HttpStream         0x00000109L
#define OpcUa_Module_HttpConnection     0x00000110L

/* core modules */
#define OpcUa_Module_Mutex              0x00000201L
#define OpcUa_Module_Thread             0x00000202L
#define OpcUa_Module_Socket             0x00000203L
#define OpcUa_Module_Memory             0x00000204L
#define OpcUa_Module_Condition          0x00000205L
#define OpcUa_Module_List               0x00000206L
#define OpcUa_Module_Utilities          0x00000207L
#define OpcUa_Module_DateTime           0x00000208L
#define OpcUa_Module_Guid               0x00000209L
#define OpcUa_Module_Semaphore          0x0000020AL
#define OpcUa_Module_String             0x0000020BL
#define OpcUa_Module_Trace              0x0000020CL
#define OpcUa_Module_ThreadPool         0x0000020DL
#define OpcUa_Module_XmlReader          0x0000020EL
#define OpcUa_Module_XmlWriter          0x0000020FL

/* proxy stub modules */
#define OpcUa_Module_Session            0x00000301L
#define OpcUa_Module_Endpoint           0x00000302L
#define OpcUa_Module_AsyncCallState     0x00000303L
#define OpcUa_Module_Serializer         0x00000304L
#define OpcUa_Module_BinarySerializer   0x00000305L
#define OpcUa_Module_XmlSerializer      0x00000306L
#define OpcUa_Module_Channel            0x00000307L
#define OpcUa_Module_ProxyStub          0x00000308L
#define OpcUa_Module_ServiceTable       0x00000309L

/* application modules */
#define OpcUa_Module_Server             0x00000401L
#define OpcUa_Module_Client             0x00000402L

/*============================================================================
 * Everything is as expected.
 *===========================================================================*/
#define OpcUa_Good 0x00000000

/*============================================================================
 * Something unexpected occurred but the results still maybe useable.
 *===========================================================================*/
#define OpcUa_Uncertain 0x40000000

/*============================================================================
 * An error occurred.
 *===========================================================================*/
#define OpcUa_Bad 0x80000000

/* tests if a status code is good. */
#define OpcUa_IsGood(xCode) (((xCode) & 0xC0000000) == 0x00000000)

/* tests if a status code is not good. */
#define OpcUa_IsNotGood(xCode) (((xCode) & 0xC0000000) != 0x00000000)

/* tests if a status code is uncertain. */
#define OpcUa_IsUncertain(xCode) (((xCode) & 0xC0000000) == 0x40000000)

/* tests if a status code is not uncertain. */
#define OpcUa_IsNotUncertain(xCode) (((xCode) & 0xC0000000) != 0x40000000)

/* tests if a status code is bad. */
#define OpcUa_IsBad(xCode) (((xCode) & 0x80000000) != 0)

/* tests if a status code is not bad. */
#define OpcUa_IsNotBad(xCode) (((xCode) & 0x80000000) == 0)

/* tests if a status code is not equal the specified code. */
#define OpcUa_IsNotEqual(xCode) (((xCode) & 0xFFFF0000) != uStatus)

/* tests if a status code is equal the specified code. */
#define OpcUa_IsEqual(xCode) (((xCode) & 0xFFFF0000) == uStatus)

/* get info bits portion of a status code. */
#define OpcUa_GetInfoBits(xCode) (((xCode) & 0x000003FF))

/* get severity and sub code portion of a status code. */
#define OpcUa_GetCode(xCode) (((xCode) & 0xFFFF0000))

/* get severity and sub code portion of a status code. */
#define OpcUa_GetFlags(xCode) (((xCode) & 0xFFFF0000))

/* set overflow bit for status code associated with a data value. */
#define OpcUa_StatusCode_SetOverflowBits(xCode)             xCode = ((xCode | OpcUa_StatusCode_InfoType_DataValue | OpcUa_StatusCode_InfoBit_Overflow))

/* status code info type flag */
#define OpcUa_StatusCode_InfoType_DataValue                 0x00000400 

/* StructureChanged and SemanticChanged flags of a status code. */
#define OpcUa_StatusCode_StructureChanged                   0x00008000
#define OpcUa_StatusCode_SemanticsChanged                   0x00004000

/* status code info bits. */
#define OpcUa_StatusCode_InfoBit_Limit_None                 0x00000000
#define OpcUa_StatusCode_InfoBit_Limit_Low                  0x00000100
#define OpcUa_StatusCode_InfoBit_Limit_High                 0x00000200
#define OpcUa_StatusCode_InfoBit_Limit_Constant             0x00000300

#define OpcUa_StatusCode_InfoBit_Overflow                   0x00000080

#define OpcUa_StatusCode_InfoBit_Historian_Raw              0x00000000
#define OpcUa_StatusCode_InfoBit_Historian_Calculated       0x00000001
#define OpcUa_StatusCode_InfoBit_Historian_Interpolated     0x00000002
#define OpcUa_StatusCode_InfoBit_Historian_Partial          0x00000004
#define OpcUa_StatusCode_InfoBit_Historian_ExtraData        0x00000008
#define OpcUa_StatusCode_InfoBit_Historian_MultiValue       0x00000010

/*============================================================================
 * OpcUa_ReturnErrorIfNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
     #define OpcUa_ReturnErrorIfNull(xArg, xStatus) \
    if ((xArg) == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_DEBUG, "<-- ReturnError: Value " #xArg " is OpcUa_Null!\n"); \
        return xStatus; \
    }
#else
    #define OpcUa_ReturnErrorIfNull(xArg, xStatus) \
    if ((xArg) == OpcUa_Null) \
    { \
        return xStatus; \
    }
#endif

/*============================================================================
 * OpcUa_ReturnErrorIfArgumentNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfArgumentNull(xArg) \
    if ((xArg) == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: Argument " #xArg " is OpcUa_Null!\n"); \
        return OpcUa_BadInvalidArgument; \
    }
#else
    #define OpcUa_ReturnErrorIfArgumentNull(xArg) \
    if ((xArg) == OpcUa_Null) \
    { \
        return OpcUa_BadInvalidArgument; \
    }
#endif

/*============================================================================
 * OpcUa_ReturnErrorIfArrayArgumentNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfArrayArgumentNull(xCount, xArg) \
    if ((xCount) > 0 && (xArg) == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: Argument " #xArg " is OpcUa_Null!\n"); \
        return OpcUa_BadInvalidArgument; \
    }
#else
    #define OpcUa_ReturnErrorIfArrayArgumentNull(xCount, xArg) \
    if ((xCount) > 0 && (xArg) == OpcUa_Null) \
    { \
        return OpcUa_BadInvalidArgument; \
    }
#endif

/*============================================================================
 * OpcUa_ReturnErrorIfAllocFailed
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfAllocFailed(xArg) \
    if ((xArg) == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError:  Allocation failed!\n"); \
        return OpcUa_BadOutOfMemory; \
    }
#else
    #define OpcUa_ReturnErrorIfAllocFailed(xArg) \
    if ((xArg) == OpcUa_Null) \
    { \
        return OpcUa_BadOutOfMemory; \
    }
#endif
/*============================================================================
 * OpcUa_ReturnErrorIfTrue
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfTrue(xCondition, xStatus) \
    if (xCondition) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: " #xCondition " evaluated to true! Returning 0x%08X\n", xStatus); \
        return xStatus; \
    }
#else
    #define OpcUa_ReturnErrorIfTrue(xCondition, xStatus) \
    if (xCondition) \
    { \
        return xStatus; \
    }
#endif
/*============================================================================
 * OpcUa_ReturnErrorIfBad
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfBad(xStatus) \
    if (OpcUa_IsBad(xStatus)) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: 0x%08X\n", xStatus); \
        return xStatus; \
    }
#else
    #define OpcUa_ReturnErrorIfBad(xStatus) \
    if (OpcUa_IsBad(xStatus)) \
    { \
        return xStatus; \
    }
#endif

/*============================================================================
 * OpcUa_GotoErrorIfNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfNull(xArg, xStatus) \
    if (xArg == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: " #xArg " is OpcUa_Null!\n");\
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfNull(xArg, xStatus) \
    if (xArg == OpcUa_Null) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        goto Error; \
    }
#endif
/*============================================================================
 * OpcUa_GotoErrorIfNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfNotNull(xArg, xStatus) \
    if (xArg != OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: " #xArg " is not OpcUa_Null!\n");\
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfNotNull(xArg, xStatus) \
    if (xArg != OpcUa_Null) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        goto Error; \
    }
#endif
/*============================================================================
 * OpcUa_GotoErrorIfArgumentNull
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfArgumentNull(xArg) \
    if (xArg == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: Argument " #xArg " is OpcUa_Null!\n");\
        uStatus = (uStatus & 0x0000FFFF) | OpcUa_BadInvalidArgument; \
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfArgumentNull(xArg) \
    if (xArg == OpcUa_Null) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | OpcUa_BadInvalidArgument; \
        goto Error; \
    }
#endif /* OPCUA_TRACE_ERROR_MACROS */
/*============================================================================
 * OpcUa_GotoErrorIfAllocFailed
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfAllocFailed(xArg) \
    if (xArg == OpcUa_Null) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError:  Allocation failed!\n"); \
        uStatus = (uStatus & 0x0000FFFF) | OpcUa_BadOutOfMemory; \
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfAllocFailed(xArg) \
    if (xArg == OpcUa_Null) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | OpcUa_BadOutOfMemory; \
        goto Error; \
    }
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_GotoErrorIfTrue
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfTrue(xCondition, xStatus) \
    if (xCondition) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: Expression " #xCondition " is true!\n");\
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfTrue(xCondition, xStatus) \
    if (xCondition) \
    { \
        uStatus = (uStatus & 0x0000FFFF) | xStatus; \
        goto Error; \
    }
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_GotoErrorIfBad
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorIfBad(xStatus) \
    if (OpcUa_IsBad(xStatus)) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: Bad statuscode 0x%08X\n", xStatus); \
        goto Error; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorIfBad(xStatus) \
    if (OpcUa_IsBad(xStatus)) \
    { \
        goto Error; \
    }
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_GotoError
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_GotoErrorWithStatus(xStatus) \
    uStatus = xStatus; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "--- GotoError: " #xStatus ". Statuscode 0x%08X\n", xStatus); \
    goto Error;
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_GotoErrorWithStatus(xStatus) \
    uStatus = xStatus; \
    goto Error;
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_ReturnErrorIfInvalidObject
 *===========================================================================*/
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnErrorIfInvalidObject(xType, xObject, xMethod) \
    if (((xType*)(xObject)->Handle)->SanityCheck != xType##_SanityCheck || (xObject)->xMethod != xType##_##xMethod) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: Invalid object type for " #xType " and object " #xObject " at method " #xMethod"!\n"); \
        return OpcUa_BadInvalidArgument; \
    }
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_ReturnErrorIfInvalidObject(xType, xObject, xMethod) \
    if (((xType*)(xObject)->Handle)->SanityCheck != xType##_SanityCheck || (xObject)->xMethod != xType##_##xMethod) \
    { \
        return OpcUa_BadInvalidArgument; \
    }
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_ReturnError
 *===========================================================================*/
/** 
 * @brief Returns the given error.
 */
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnError(xStatus)  \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- ReturnError: 0x%08X\n", xStatus); \
    return xStatus;
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_ReturnError(xStatus) return xStatus;
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_GotoError
 *===========================================================================*/
/** 
 * @brief Jumps to the beginning of the error handling block.
 */
#define OpcUa_GotoError goto Error;

/*============================================================================
 * OpcUa_InitializeStatus
 *===========================================================================*/
/** 
 * @brief Marks the beginning of a error handling block.
 */
#if !OPCUA_ERRORHANDLING_OMIT_METHODNAME
    #if OPCUA_TRACE_ERROR_MACROS
        #define OpcUa_InitializeStatus(xModule, xMethod)      \
        OpcUa_StatusCode    uStatus              = OpcUa_Good; \
        OpcUa_UInt32        uModule              = xModule; \
        OpcUa_CharA         const uStatusMethod[]= xMethod;   \
        OpcUa_ReferenceParameter(uStatusMethod); \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_DEBUG, "--> " #xModule "::" #xMethod " (0x%08X)\n", (xModule&0x0000FFFFL));\
        if (uStatus != OpcUa_Good) goto Error; OpcUa_ReferenceParameter(uModule);
    #else /* OPCUA_TRACE_ERROR_MACROS */
        #define OpcUa_InitializeStatus(xModule, xMethod)     \
        OpcUa_StatusCode    uStatus              = OpcUa_Good; \
        OpcUa_UInt32        uModule              = xModule; \
        OpcUa_CharA         const uStatusMethod[]= xMethod;  \
        OpcUa_ReferenceParameter(uStatusMethod); \
        OpcUa_ReferenceParameter(uModule); \
        OpcUa_GotoErrorIfBad(uStatus);
    #endif /* OPCUA_TRACE_ERROR_MACROS */
#else /* OPCUA_ERRORHANDLING_OMIT_METHODNAME */
    #if OPCUA_TRACE_ERROR_MACROS
        #error Tracing only with active method names
    #else /* OPCUA_TRACE_ERROR_MACROS */
        #define OpcUa_InitializeStatus(xModule, xMethod)     \
        OpcUa_StatusCode    uStatus              = OpcUa_Good; \
        OpcUa_UInt32        uModule              = xModule; \
        OpcUa_ReferenceParameter(uModule); \
        OpcUa_GotoErrorIfBad(uStatus);
    #endif /* OPCUA_TRACE_ERROR_MACROS */
#endif /* OPCUA_ERRORHANDLING_OMIT_METHODNAME */

/*============================================================================
 * OpcUa_ReturnStatusCode
 *===========================================================================*/
/** 
 * @brief Marks the beginning of an error handling block.
 */
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_ReturnStatusCode \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_DEBUG, "<-- \"%s\" = 0x%08X (%s).\n", uStatusMethod, uStatus, OpcUa_IsGood(uStatus)?"GOOD":"BAD");\
    return uStatus&0xFFFF0000L; 
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_ReturnStatusCode \
    return uStatus&0xFFFF0000L; 
#endif /* OPCUA_TRACE_ERROR_MACROS */


/*============================================================================
 * OpcUa_BeginErrorHandling
 *===========================================================================*/
/** 
 * @brief Marks the beginning of an error handling block.
 */
#define OpcUa_BeginErrorHandling Error:  


/*============================================================================
 * OpcUa_FinishErrorHandling
 *===========================================================================*/
/** 
 * @brief Marks the end of an error handling block.
 */
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_FinishErrorHandling                               \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "<-- \"%s\" = 0x%08X (%s).\n", uStatusMethod, uStatus, OpcUa_IsGood(uStatus)?"GOOD":"BAD");\
    return uStatus;
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_FinishErrorHandling return uStatus;
#endif /* OPCUA_TRACE_ERROR_MACROS */

/*============================================================================
 * OpcUa_SurpressError
 *===========================================================================*/
/** 
 * @brief Writes a trace statement indicating that an error was intentionally ignored.
 */
#if OPCUA_TRACE_ERROR_MACROS
    #define OpcUa_SuppressError(xStatus) \
    uStatus = xStatus; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_WARNING, "=== \"%s\" = 0x%08X (%s).\n\n", uStatusMethod, uStatus, OpcUa_IsGood(uStatus)?"GOOD":"BAD");
#else /* OPCUA_TRACE_ERROR_MACROS */
    #define OpcUa_SuppressError(xStatus) uStatus = xStatus;
#endif /* OPCUA_TRACE_ERROR_MACROS */


OPCUA_END_EXTERN_C
#endif /* _OpcUa_ErrorHandling_H_ */
