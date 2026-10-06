/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

package org.opcfoundation.interop.milo;

import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.uint;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyStore;
import java.security.PrivateKey;
import java.security.Security;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.function.Function;
import org.bouncycastle.jce.provider.BouncyCastleProvider;
import org.eclipse.milo.opcua.sdk.client.OpcUaClient;
import org.eclipse.milo.opcua.sdk.client.identity.AnonymousProvider;
import org.eclipse.milo.opcua.sdk.client.identity.IdentityProvider;
import org.eclipse.milo.opcua.sdk.client.identity.UsernameProvider;
import org.eclipse.milo.opcua.sdk.server.EndpointConfig;
import org.eclipse.milo.opcua.sdk.server.OpcUaServer;
import org.eclipse.milo.opcua.sdk.server.OpcUaServerConfig;
import org.eclipse.milo.opcua.sdk.server.OpcUaServerConfigLimits;
import org.eclipse.milo.opcua.sdk.server.identity.AnonymousIdentityValidator;
import org.eclipse.milo.opcua.sdk.server.identity.CompositeValidator;
import org.eclipse.milo.opcua.sdk.server.identity.UsernameIdentityValidator;
import org.eclipse.milo.opcua.sdk.server.identity.X509IdentityValidator;
import org.eclipse.milo.opcua.stack.core.NodeIds;
import org.eclipse.milo.opcua.stack.core.Stack;
import org.eclipse.milo.opcua.stack.core.channel.EncodingLimits;
import org.eclipse.milo.opcua.stack.core.security.CertificateValidator;
import org.eclipse.milo.opcua.stack.core.security.DefaultApplicationGroup;
import org.eclipse.milo.opcua.stack.core.security.DefaultCertificateManager;
import org.eclipse.milo.opcua.stack.core.security.DefaultClientCertificateValidator;
import org.eclipse.milo.opcua.stack.core.security.DefaultServerCertificateValidator;
import org.eclipse.milo.opcua.stack.core.security.FileBasedCertificateQuarantine;
import org.eclipse.milo.opcua.stack.core.security.FileBasedTrustListManager;
import org.eclipse.milo.opcua.stack.core.security.KeyStoreCertificateStore;
import org.eclipse.milo.opcua.stack.core.security.RsaSha256CertificateFactory;
import org.eclipse.milo.opcua.stack.core.security.SecurityPolicy;
import org.eclipse.milo.opcua.stack.core.transport.TransportProfile;
import org.eclipse.milo.opcua.stack.core.types.builtin.DateTime;
import org.eclipse.milo.opcua.stack.core.types.builtin.LocalizedText;
import org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.UInteger;
import org.eclipse.milo.opcua.stack.core.types.enumerated.MessageSecurityMode;
import org.eclipse.milo.opcua.stack.core.types.structured.BuildInfo;
import org.eclipse.milo.opcua.stack.core.types.structured.EndpointDescription;
import org.eclipse.milo.opcua.stack.core.util.SelfSignedCertificateBuilder;
import org.eclipse.milo.opcua.stack.core.util.SelfSignedCertificateGenerator;
import org.eclipse.milo.opcua.stack.transport.server.tcp.OpcTcpServerTransport;
import org.eclipse.milo.opcua.stack.transport.server.tcp.OpcTcpServerTransportConfig;

/**
 * Eclipse Milo interop peer: the same CLI and stdout contract as
 * tests/Opc.Ua.Interop.LegacyPeer, so the 2.0 interop tests can drive it.
 *
 * <pre>
 * server --port &lt;p&gt; --pki &lt;dir&gt; [--kind interop] [--autoaccept true|false] [--init-only true]
 * client --url &lt;u&gt; --pki &lt;dir&gt; [--policy &lt;uri&gt;] [--mode &lt;m&gt;] [--user u --password p]
 *        [--checks a,b] [--expect-connect-error A,B] [--token-lifetime ms]
 *        [--token-test-seconds s] [--timeout-seconds s]
 * </pre>
 */
public final class Peer {

  static final int EXIT_SUCCESS = 0;
  static final int EXIT_CHECKS_FAILED = 1;
  static final int EXIT_USAGE = 2;
  static final int EXIT_FATAL = 3;

  static final String INTEROP_NAMESPACE = "urn:opcfoundation.org:interop:legacy";
  static final String USER_NAME = "interop";
  static final String PASSWORD = "interop-password";
  private static final char[] KEYSTORE_PASSWORD = "interop".toCharArray();

  static {
    // Aes256_Sha256_RsaPss needs the Bouncy Castle provider.
    Security.addProvider(new BouncyCastleProvider());
  }

  private Peer() {}

  public static void main(String[] args) {
    int exitCode;
    try {
      if (args.length == 0) {
        System.err.println("error: missing command");
        exitCode = EXIT_USAGE;
      } else {
        Map<String, String> options = parseOptions(Arrays.copyOfRange(args, 1, args.length));
        exitCode =
            switch (args[0]) {
              case "server" -> runServer(options);
              case "client" -> ClientChecks.run(options);
              default -> {
                System.err.println("error: unknown command " + args[0]);
                yield EXIT_USAGE;
              }
            };
      }
    } catch (IllegalArgumentException e) {
      System.err.println("error: " + e.getMessage());
      exitCode = EXIT_USAGE;
    } catch (Throwable t) {
      System.err.println("FATAL " + t);
      t.printStackTrace();
      exitCode = EXIT_FATAL;
    }
    System.out.flush();
    Stack.releaseSharedResources();
    System.exit(exitCode);
  }

  static Map<String, String> parseOptions(String[] args) {
    var options = new HashMap<String, String>();
    for (int ii = 0; ii < args.length; ii += 2) {
      if (!args[ii].startsWith("--") || ii + 1 >= args.length) {
        throw new IllegalArgumentException("expected --name value, got " + args[ii]);
      }
      options.put(args[ii].substring(2).toLowerCase(), args[ii + 1]);
    }
    return options;
  }

  static boolean flag(Map<String, String> options, String name, boolean defaultValue) {
    String value = options.get(name);
    return value == null ? defaultValue : Boolean.parseBoolean(value);
  }

  // ------------------------------------------------------------------ PKI

  /**
   * The application certificate in the .NET layout the tests provision: own/certs/NAME.der
   * (read by InteropPki) and own/private/NAME.pfx.
   */
  record Identity(KeyPair keyPair, X509Certificate certificate, String applicationUri) {}

  static Identity loadOrCreateIdentity(Path pki, String name, String applicationUri)
      throws Exception {
    Path certs = Files.createDirectories(pki.resolve("own").resolve("certs"));
    Path privateDir = Files.createDirectories(pki.resolve("own").resolve("private"));
    Path pfx = privateDir.resolve(name + ".pfx");
    KeyStore keyStore = KeyStore.getInstance("PKCS12");
    if (Files.exists(pfx)) {
      try (var in = Files.newInputStream(pfx)) {
        keyStore.load(in, KEYSTORE_PASSWORD);
      }
    } else {
      KeyPair keyPair = SelfSignedCertificateGenerator.generateRsaKeyPair(2048);
      var builder =
          new SelfSignedCertificateBuilder(keyPair)
              .setCommonName(name)
              .setOrganization("OPC Foundation")
              .setApplicationUri(applicationUri)
              .addDnsName("localhost")
              .addDnsName(InetAddress.getLocalHost().getHostName())
              .addIpAddress("127.0.0.1");
      X509Certificate certificate = builder.build();
      keyStore.load(null, KEYSTORE_PASSWORD);
      keyStore.setKeyEntry(
          name, keyPair.getPrivate(), KEYSTORE_PASSWORD, new X509Certificate[] {certificate});
      try (var out = Files.newOutputStream(pfx)) {
        keyStore.store(out, KEYSTORE_PASSWORD);
      }
      Files.write(certs.resolve(name + ".der"), certificate.getEncoded());
    }
    var certificate = (X509Certificate) keyStore.getCertificate(name);
    var key = (PrivateKey) keyStore.getKey(name, KEYSTORE_PASSWORD);
    return new Identity(new KeyPair(certificate.getPublicKey(), key), certificate, applicationUri);
  }

  /** The trust list over the .NET layout: trusted/{certs,crl} and issuer/{certs,crl}. */
  static FileBasedTrustListManager trustList(Path pki) throws Exception {
    Path issuerCerts = Files.createDirectories(pki.resolve("issuer").resolve("certs"));
    Path issuerCrl = Files.createDirectories(pki.resolve("issuer").resolve("crl"));
    Path trustedCerts = Files.createDirectories(pki.resolve("trusted").resolve("certs"));
    Path trustedCrl = Files.createDirectories(pki.resolve("trusted").resolve("crl"));
    var trustList = new FileBasedTrustListManager(issuerCerts, issuerCrl, trustedCerts, trustedCrl);
    trustList.initialize();
    return trustList;
  }

  static FileBasedCertificateQuarantine quarantine(Path pki) throws Exception {
    return FileBasedCertificateQuarantine.create(
        Files.createDirectories(pki.resolve("rejected").resolve("certs")));
  }

  /** Accepts every peer certificate when auto-accepting, else delegates. */
  static CertificateValidator autoAccepting(boolean autoAccept, CertificateValidator validator) {
    return (chain, applicationUri, hostnames) -> {
      if (!autoAccept) {
        validator.validateCertificateChain(chain, applicationUri, hostnames);
      }
    };
  }

  // ------------------------------------------------------------------ server

  static int runServer(Map<String, String> options) throws Exception {
    String kind = options.getOrDefault("kind", "interop");
    if (!kind.equals("interop")) {
      throw new IllegalArgumentException("--kind " + kind + " is not supported by the Milo peer");
    }
    int port = Integer.parseInt(required(options, "port"));
    Path pki = Path.of(required(options, "pki"));
    String name = "MiloInteropServer";
    String applicationUri = "urn:localhost:opcfoundation.org:" + name;
    Identity identity = loadOrCreateIdentity(pki, name, applicationUri);
    if (flag(options, "init-only", false)) {
      System.out.println("PEER-PKI-READY " + applicationUri);
      return EXIT_SUCCESS;
    }

    FileBasedTrustListManager trustList = trustList(pki);
    var quarantine = quarantine(pki);
    var certificateStore =
        KeyStoreCertificateStore.createAndInitialize(
            new KeyStoreCertificateStore.Settings(
                pki.resolve("own").resolve("private").resolve(name + ".pfx"),
                () -> KEYSTORE_PASSWORD,
                alias -> KEYSTORE_PASSWORD));
    var certificateFactory =
        new RsaSha256CertificateFactory() {
          @Override
          protected KeyPair createRsaSha256KeyPair() {
            return identity.keyPair();
          }

          @Override
          protected X509Certificate[] createRsaSha256CertificateChain(KeyPair keyPair) {
            return new X509Certificate[] {identity.certificate()};
          }
        };
    CertificateValidator validator =
        autoAccepting(
            flag(options, "autoaccept", true),
            new DefaultServerCertificateValidator(trustList, quarantine));
    var group =
        DefaultApplicationGroup.createAndInitialize(
            trustList, certificateStore, certificateFactory, validator);
    var certificateManager = new DefaultCertificateManager(quarantine, group);

    String path = "/" + name;
    Set<EndpointConfig> endpoints = new LinkedHashSet<>();
    var base =
        EndpointConfig.newBuilder()
            .setBindAddress("0.0.0.0")
            .setBindPort(port)
            .setHostname("localhost")
            .setPath(path)
            .setCertificate(identity.certificate())
            .setTransportProfile(TransportProfile.TCP_UASC_UABINARY)
            .addTokenPolicies(
                OpcUaServerConfig.USER_TOKEN_POLICY_ANONYMOUS,
                OpcUaServerConfig.USER_TOKEN_POLICY_USERNAME);
    endpoints.add(
        base.copy()
            .setSecurityPolicy(SecurityPolicy.None)
            .setSecurityMode(MessageSecurityMode.None)
            .build());
    for (SecurityPolicy policy :
        List.of(
            SecurityPolicy.Basic256Sha256,
            SecurityPolicy.Aes128_Sha256_RsaOaep,
            SecurityPolicy.Aes256_Sha256_RsaPss)) {
      for (MessageSecurityMode mode :
          List.of(MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)) {
        // X509 user tokens on the secure endpoints only.
        endpoints.add(
            base.copy()
                .addTokenPolicies(OpcUaServerConfig.USER_TOKEN_POLICY_X509)
                .setSecurityPolicy(policy)
                .setSecurityMode(mode)
                .build());
      }
    }

    String softwareVersion = "Eclipse Milo " + OpcUaServer.SDK_VERSION;
    // Mirrors InteropLimits of the 1.5.378 interop server; the 2.0 tests cross them.
    OpcUaServerConfigLimits limits =
        new OpcUaServerConfigLimits() {
          @Override
          public UInteger getMaxArrayLength() {
            return uint(100_000);
          }

          @Override
          public UInteger getMaxByteStringLength() {
            return uint(1024 * 1024);
          }

          @Override
          public UInteger getMaxStringLength() {
            return uint(1024 * 1024);
          }

          @Override
          public UInteger getMaxNodesPerRead() {
            return uint(100);
          }

          @Override
          public UInteger getMaxNodesPerWrite() {
            return uint(100);
          }

          @Override
          public UInteger getMaxNodesPerBrowse() {
            return uint(100);
          }

          @Override
          public UInteger getMaxNodesPerMethodCall() {
            return uint(100);
          }

          @Override
          public UInteger getMaxMonitoredItemsPerCall() {
            return uint(100);
          }

          @Override
          public UInteger getMaxNodesPerTranslateBrowsePathsToNodeIds() {
            return uint(100);
          }
        };
    EncodingLimits defaults = EncodingLimits.DEFAULT;
    var config =
        OpcUaServerConfig.builder()
            .setApplicationUri(applicationUri)
            .setApplicationName(LocalizedText.english(name))
            .setProductUri("http://opcfoundation.org/UA/Interop/MiloServer")
            .setBuildInfo(
                new BuildInfo(
                    "http://opcfoundation.org/UA/Interop/MiloServer",
                    "Eclipse Milo",
                    "Milo Interop Server",
                    softwareVersion,
                    "0",
                    DateTime.now()))
            .setEndpoints(endpoints)
            .setCertificateManager(certificateManager)
            .setIdentityValidator(
                new CompositeValidator(
                    AnonymousIdentityValidator.INSTANCE,
                    new UsernameIdentityValidator(
                        challenge ->
                            USER_NAME.equals(challenge.getUsername())
                                && PASSWORD.equals(challenge.getPassword())),
                    // Any user certificate is accepted, like the auto-accepted
                    // application certificates.
                    new X509IdentityValidator(certificate -> true)))
            .setLimits(limits)
            .setEncodingLimits(
                new EncodingLimits(
                    defaults.getMaxChunkSize(),
                    defaults.getMaxChunkCount(),
                    4 * 1024 * 1024,
                    defaults.getMaxRecursionDepth()))
            .build();

    var server =
        new OpcUaServer(
            config,
            profile -> new OpcTcpServerTransport(OpcTcpServerTransportConfig.newBuilder().build()));
    var namespace = new InteropNamespace(server);
    namespace.startup();
    server.startup().get();

    String url = "opc.tcp://localhost:" + port + path;
    System.out.println(
        "PEER-INFO "
            + Json.object(
                Map.of(
                    "stack", "Eclipse Milo",
                    "version", OpcUaServer.SDK_VERSION,
                    "applicationUri", applicationUri,
                    "softwareVersion", softwareVersion)));
    System.out.println("PEER-SERVER-READY " + url);
    System.out.println("LEGACY-SERVER-READY " + url);
    System.out.flush();

    try (var in = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8))) {
      String line;
      while ((line = in.readLine()) != null) {
        if (line.trim().equalsIgnoreCase("stop")) {
          break;
        }
      }
    }
    namespace.shutdown();
    server.shutdown().get();
    trustList.close();
    System.out.println("PEER-SERVER-STOPPED");
    return EXIT_SUCCESS;
  }

  static String required(Map<String, String> options, String name) {
    String value = options.get(name);
    if (value == null) {
      throw new IllegalArgumentException("missing --" + name);
    }
    return value;
  }

  // ------------------------------------------------------------------ client connection

  static OpcUaClient createClient(
      Map<String, String> options, Path pki, String policyUri, MessageSecurityMode mode)
      throws Exception {
    return createClient(options, pki, policyUri, mode, null);
  }

  /** A client with the given user identity instead of the one of the options. */
  static OpcUaClient createClient(
      Map<String, String> options,
      Path pki,
      String policyUri,
      MessageSecurityMode mode,
      IdentityProvider identityOverride)
      throws Exception {
    String name = "MiloInteropClient";
    Identity identity = loadOrCreateIdentity(pki, name, "urn:localhost:opcfoundation.org:" + name);
    FileBasedTrustListManager trustList = trustList(pki);
    CertificateValidator validator =
        autoAccepting(
            flag(options, "autoaccept", true),
            new DefaultClientCertificateValidator(trustList, quarantine(pki)));
    String user = options.get("user");
    IdentityProvider identityProvider =
        identityOverride != null
            ? identityOverride
            : user == null
            ? new AnonymousProvider()
            : new UsernameProvider(user, options.getOrDefault("password", ""));
    long tokenLifetime = Long.parseLong(options.getOrDefault("token-lifetime", "0"));
    Function<List<EndpointDescription>, Optional<EndpointDescription>> selectEndpoint =
        endpoints ->
            endpoints.stream()
                .filter(
                    e ->
                        policyUri.equals(e.getSecurityPolicyUri())
                            && e.getSecurityMode() == mode
                            && e.getEndpointUrl().startsWith("opc.tcp"))
                .findFirst();
    return OpcUaClient.create(
        required(options, "url"),
        selectEndpoint,
        transport -> {
          if (tokenLifetime > 0) {
            transport.setChannelLifetime(uint(tokenLifetime));
          }
        },
        client ->
            client
                .setApplicationName(LocalizedText.english(name))
                .setApplicationUri(identity.applicationUri())
                .setKeyPair(identity.keyPair())
                .setCertificate(identity.certificate())
                .setCertificateChain(new X509Certificate[] {identity.certificate()})
                .setCertificateValidator(validator)
                .setIdentityProvider(identityProvider)
                .setRequestTimeout(uint(60_000))
                .setSessionTimeout(uint(120_000))
                // Mirrors the 2.0 reference server fixture: the large-message
                // checks are bounded by the server, not by this client.
                .setEncodingLimits(
                    new EncodingLimits(
                        EncodingLimits.DEFAULT.getMaxChunkSize(),
                        0,
                        16 * 1024 * 1024,
                        EncodingLimits.DEFAULT.getMaxRecursionDepth())));
  }

  /** Minimal JSON writing for the RESULT and PEER-INFO lines. */
  static final class Json {
    private Json() {}

    static String object(Map<String, ?> values) {
      var sb = new StringBuilder("{");
      for (var entry : new LinkedHashMap<>(values).entrySet()) {
        if (sb.length() > 1) {
          sb.append(',');
        }
        sb.append(string(entry.getKey())).append(':');
        Object value = entry.getValue();
        sb.append(value instanceof Number ? value.toString() : string(String.valueOf(value)));
      }
      return sb.append('}').toString();
    }

    static String string(String value) {
      var sb = new StringBuilder("\"");
      for (char c : value.toCharArray()) {
        switch (c) {
          case '"' -> sb.append("\\\"");
          case '\\' -> sb.append("\\\\");
          case '\n' -> sb.append("\\n");
          case '\r' -> sb.append("\\r");
          case '\t' -> sb.append("\\t");
          default -> {
            if (c < 0x20) {
              sb.append(String.format("\\u%04x", (int) c));
            } else {
              sb.append(c);
            }
          }
        }
      }
      return sb.append('"').toString();
    }
  }

  static List<String> split(String value) {
    var items = new ArrayList<String>();
    for (String item : value.split(",")) {
      if (!item.isBlank()) {
        items.add(item.trim());
      }
    }
    return items;
  }
}
