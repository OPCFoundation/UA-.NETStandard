# open62541 interop peer for the Opc.Ua.Interop.Tests suite.
#
# Build open62541 (with UA_ENABLE_ENCRYPTION=OPENSSL, UA_NAMESPACE_ZERO=FULL,
# UA_ENABLE_DISCOVERY=ON and UA_ENABLE_XML_ENCODING=ON; without XML encoding
# the generated namespace 0 has no InputArguments values, so every namespace 0
# method with arguments, e.g. Server.GetMonitoredItems, fails with
# Bad_InternalError) and install it, then:
#   cmake -S . -B build -DCMAKE_PREFIX_PATH=<open62541 install> && cmake --build build
#
# OpenSSL (3.x) is the crypto backend CI uses: with it the peer also offers the
# ECC_nistP256_AesGcm, ECC_nistP256_ChaChaPoly, ECC_curve25519 and ECC_curve448
# policies, which open62541 1.5 implements only with OpenSSL. An open62541
# built with UA_ENABLE_ENCRYPTION=MBEDTLS works as well, with the RSA policies
# and the four ECC policies of OPC UA 1.05 only. The peer reports the policies
# of its build in the "policies" of its PEER-INFO line. On Windows, put the
# OpenSSL DLLs (libcrypto-3-x64.dll, libssl-3-x64.dll) next to the executable.
cmake_minimum_required(VERSION 3.20)
project(open62541-peer C)

find_package(open62541 1.5 REQUIRED)

add_executable(open62541-peer peer.c checks.h)
target_link_libraries(open62541-peer PRIVATE open62541::open62541)
# C11 for <stdatomic.h> (the stop flag is shared with the stdin thread).
set_target_properties(open62541-peer PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON)
if(MSVC)
  target_compile_options(open62541-peer PRIVATE /experimental:c11atomics)
endif()
