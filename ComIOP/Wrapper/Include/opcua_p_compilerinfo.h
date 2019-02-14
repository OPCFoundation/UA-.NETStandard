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

#ifndef _OpcUa_P_CompilerInfo_H_
#define _OpcUa_P_CompilerInfo_H_ 1

/* compiler information constants */
#define OPCUA_P_COMPILERNAME_UNKNOWN    "Unknown Compiler"
#define OPCUA_P_COMPILERNAME_MSVC       "Microsoft Visual C/C++"
#define OPCUA_P_COMPILERNAME_MINGNUW    "GNU C++/MINGW"
#define OPCUA_P_COMPILERNAME_GNU        "GNU C++"
#define OPCUA_P_COMPILERNAME_INTEL      "Intel C++"

/* check for known compilers */
#if defined(_MSC_VER)

  /* compiler name */
# if defined(__INTEL_COMPILER)
#  define OPCUA_P_COMPILERNAME OPCUA_P_COMPILERNAME_INTEL
# else
#  define OPCUA_P_COMPILERNAME OPCUA_P_COMPILERNAME_MSVC
# endif
  /* compiler version */
# define OPCUA_P_COMPILERVERSION OPCUA_TOSTRING(_MSC_VER)

#elif defined(__GNUC__)

  /* compiler name */
# if defined(__MINGW32__)
#  define OPCUA_P_COMPILERNAME OPCUA_P_COMPILERNAME_MINGNUW
# elif defined(__INTEL_COMPILER)
#  define OPCUA_P_COMPILERNAME OPCUA_P_COMPILERNAME_INTEL
# else
#  define OPCUA_P_COMPILERNAME OPCUA_P_COMPILERNAME_GNU
# endif
  /* compiler version */
# define OPCUA_P_COMPILERVERSION OPCUA_TOSTRING(__GNUC__)"."OPCUA_TOSTRING(__GNUC_MINOR__)

#else /* compiler */

/* compiler unknown */
# define OPCUA_P_COMPILERNAME       OPCUA_P_COMPILERNAME_UNKNOWN
# define OPCUA_P_COMPILERVERSION    "0"

#endif /* compiler */

/* create defines used by the stack */
#define OPCUA_P_COMPILERINFO OPCUA_P_COMPILERNAME " " OPCUA_P_COMPILERVERSION

#endif /* _OpcUa_P_CompilerInfo_H_ */
