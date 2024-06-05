/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Security.Cryptography;

namespace Opc.Ua.X509StoreExtensions.Internal
{
    /// <summary>
    /// Helper functions to access Crls in a Windows X509 Store
    /// </summary>
    internal static unsafe class X509CrlHelper
    {
        /// <summary>
        /// Gets all crls from the provided X509 Store on Windows
        /// </summary>
        /// <param name="storeHandle">HCERTSTORE Handle to X509 Store</param>
        /// <returns>array of all found crls as byte array</returns>
        public static byte[][] GetCrls(IntPtr storeHandle)
        {
            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }

            var crls = new List<byte[]>();

            CRL_CONTEXT* crlContext = (CRL_CONTEXT*)IntPtr.Zero;
            try
            {
                //read until Pointer to crlContext is NullPtr  (no more crls in store)
                while (true)
                {
                    crlContext = PInvokeHelper.CertEnumCRLsInStore((HCERTSTORE)storeHandle.ToPointer(), crlContext);

                    if (crlContext != null)
                    {
                        byte[] crl = ReadCrlFromCrlContext(crlContext);

                        if (crl != null)
                        {
                            crls.Add(crl);
                        }
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == -2146885628)
                        {
                            //No more crls found in store
                        }
                        else if (error != 0)
                        {
                            Utils.LogError("Error while enumerating Crls from X509Store, Win32Error-Code: {0}", error);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception while enumerating Crls from X509Store");
            }
            return crls.ToArray();
        }

        /// <summary>
        /// gets the crl as byte array from the provided crlcontext
        /// </summary>
        /// <param name="crlContext">crl context as pointer</param>
        /// <returns>crl as byte array</returns>
        private static byte[] ReadCrlFromCrlContext(CRL_CONTEXT* crlContext)
        {
            uint length = crlContext->cbCrlEncoded;
            byte[] crl = new byte[length];

            Marshal.Copy((IntPtr)crlContext->pbCrlEncoded, crl, 0, (int)length);

            return crl;
        }


        /// <summary>
        /// add a crl to to the provided store
        /// </summary>
        /// <param name="storeHandle">HCERTSTORE Handle to X509 Store</param>
        /// <param name="crl">the crl as Asn1 or PKCS7 encoded byte array</param>
        public static void AddCrl(IntPtr storeHandle, byte[] crl)
        {

            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }
            IntPtr crlPointer = Marshal.AllocHGlobal(crl.Length);

            Marshal.Copy(crl, 0, crlPointer, crl.Length);//copy from managed array to unmanaged memory

            try
            {
                /////+-------------------------------------------------------------------------
                // Add certificate/CRL, encoded, context or element disposition values.
                //--------------------------------------------------------------------------
                //#define CERT_STORE_ADD_NEW                                  1
                //#define CERT_STORE_ADD_USE_EXISTING                         2
                //#define CERT_STORE_ADD_REPLACE_EXISTING                     3
                //#define CERT_STORE_ADD_ALWAYS                               4
                //#define CERT_STORE_ADD_REPLACE_EXISTING_INHERIT_PROPERTIES  5
                //#define CERT_STORE_ADD_NEWER                                6
                //#define CERT_STORE_ADD_NEWER_INHERIT_PROPERTIES             7
                if (PInvokeHelper.CertAddEncodedCRLToStore(
                    (HCERTSTORE)storeHandle.ToPointer(),
                    CERT_QUERY_ENCODING_TYPE.X509_ASN_ENCODING | CERT_QUERY_ENCODING_TYPE.PKCS_7_ASN_ENCODING,
                    (byte*)crlPointer,
                    (uint)crl.Length,
                    3,
                    null))
                {
                    //success
                    return;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == -2147024809)
                    {
                        Utils.LogError("Error while adding Crl to X509Store, Win32Error-Code: {0}: ERROR_INVALID_PARAMETER, The parameter is incorrect. ", error);
                    }
                    if (error == -2146881269)
                    {
                        Utils.LogError("Error while adding Crl to X509Store, Win32Error-Code: {0}: CRYPT_E_ASN1_BADTAG, ASN1 bad tag value met. ", error);
                    }
                    if (error == -2147024891)
                    {
                        Utils.LogError("Error while adding Crl to X509Store, Win32Error-Code: {0}: ERROR_ACCESS_DENIED, Access is denied. ", error);
                    }
                    if (error != 0)
                    {
                        Utils.LogError("Error while adding Crl to X509Store, Win32Error-Code: {0}: ", error);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception while adding Crl to X509Store");
            }
            finally
            {
                if (crlPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(crlPointer);
                }
            }
        }

        /// <summary>
        /// deletes a crl from the provided store
        /// </summary>
        /// <param name="storeHandle">HCERTSTORE Handle to X509 Store</param>
        /// <param name="crl">asn1 encoded crl to delete from the store</param>
        /// <returns>true if delete sucessfully, false if failure</returns>
        public static bool DeleteCrl(IntPtr storeHandle, byte[] crl)
        {
            if (!PlatformHelper.IsWindowsWithCrlSupport())
            {
                throw new PlatformNotSupportedException();
            }
            CRL_CONTEXT* crlContext = (CRL_CONTEXT*)IntPtr.Zero;
            try
            {
                //read until Pointer to crlContext is NullPtr (no more crls in store)
                while (true)
                {
                    crlContext = PInvokeHelper.CertEnumCRLsInStore((HCERTSTORE)storeHandle.ToPointer(), crlContext);

                    if (crlContext != null)
                    {
                        byte[] storeCrl = ReadCrlFromCrlContext(crlContext);


                        if (crl != null && crl.SequenceEqual(storeCrl))
                        {
                            if (!PInvokeHelper.CertDeleteCRLFromStore(crlContext))
                            {
                                var error = Marshal.GetLastWin32Error();
                                if (error != 0)
                                {
                                    Utils.LogError("Error while deleting Crl from X509Store, Win32Error-Code: {0}", error);
                                }
                            }
                            else
                            {
                                //was freed by CertDeleteCRLFromStore
                                crlContext = (CRL_CONTEXT*)IntPtr.Zero;
                                return true;
                            }
                            break;
                        }
                    }
                    else
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == -2146885628)
                        {
                            //No more crls found in store
                        }
                        else if (error != 0)
                        {
                            Utils.LogError("Error while deleting Crl from X509Store, Win32Error-Code: {0}", error);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception while deleting Crl from X509Store");
            }
            return false;
        }
    }
}
