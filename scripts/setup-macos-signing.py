#!/usr/bin/env python3
"""Create and reuse a code-signing identity for this Mac's personal OTD builds."""

import hashlib
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import tempfile


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def output(*args):
    return run(*args, capture_output=True, text=True).stdout


def main():
    if sys.platform != "darwin":
        sys.exit("This setup requires macOS.")

    os.umask(0o077)
    signing_dir = Path.home() / "Library/Application Support/OpenTabletDriver/Signing"
    signing_dir.mkdir(parents=True, exist_ok=True)
    certificate = signing_dir / "certificate.pem"
    identity_file = signing_dir / "identity.txt"
    keychain = output("security", "default-keychain", "-d", "user").strip().strip('"')

    if not certificate.exists():
        # Never silently replace a configured identity: that would invalidate
        # existing TCC grants. The private key lives only in a temporary directory
        # until imported into Keychain, with access restricted to codesign.
        if identity_file.exists():
            sys.exit("Signing is already configured but certificate.pem is missing; restore it instead of creating a new identity.")

        with tempfile.TemporaryDirectory(prefix="otd-signing-") as temporary:
            temporary = Path(temporary)
            key = temporary / "private-key.pem"
            cert = temporary / "certificate.pem"
            identity = temporary / "identity.p12"
            config = temporary / "openssl.cnf"
            config.write_text("""[req]
prompt = no
distinguished_name = subject
x509_extensions = extensions
[subject]
CN = OpenTabletDriver Personal Signing
[extensions]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
subjectKeyIdentifier = hash
""")
            run("/usr/bin/openssl", "req", "-new", "-x509", "-newkey", "rsa:3072",
                "-sha256", "-nodes", "-days", "3650", "-config", str(config),
                "-keyout", str(key), "-out", str(cert),
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            # This disposable password protects the temporary transport file;
            # it is never saved as configuration or printed in errors.
            password = secrets.token_urlsafe(32)
            environment = dict(os.environ, OTD_TEMP_IMPORT_PASSWORD=password)
            run("/usr/bin/openssl", "pkcs12", "-export", "-inkey", str(key),
                "-in", str(cert), "-out", str(identity), "-passout",
                "env:OTD_TEMP_IMPORT_PASSWORD", env=environment)
            imported = subprocess.run([
                "security", "import", str(identity), "-k", keychain, "-f", "pkcs12",
                "-P", password, "-x", "-T", "/usr/bin/codesign",
            ])
            if imported.returncode:
                sys.exit("Keychain import failed; no signing configuration was saved.")
            shutil.copyfile(cert, certificate)

    der = run("/usr/bin/openssl", "x509", "-in", str(certificate), "-outform", "DER",
              capture_output=True).stdout
    # codesign selects certificates by SHA-1 fingerprint; this is an identifier,
    # not the algorithm used to sign the certificate or executable.
    fingerprint = hashlib.sha1(der).hexdigest().upper()
    identities = output("security", "find-identity", "-p", "codesigning", keychain)
    if fingerprint not in identities:
        sys.exit("The saved certificate has no matching private key in the default keychain. Restore the identity; do not regenerate it.")

    valid = output("security", "find-identity", "-v", "-p", "codesigning", keychain)
    if fingerprint not in valid:
        print("Authorize the macOS prompt to trust this certificate for code signing in your user account.", flush=True)
        run("security", "add-trusted-cert", "-r", "trustRoot", "-p", "codeSign",
            "-k", keychain, str(certificate))

    valid = output("security", "find-identity", "-v", "-p", "codesigning", keychain)
    if fingerprint not in valid:
        sys.exit("The signing identity is not valid; no build configuration was changed.")
    if identity_file.exists() and identity_file.read_text().strip() != fingerprint:
        sys.exit("A different signing identity is already configured; refusing to replace it.")
    assert re.fullmatch(r"[0-9A-F]{40}", fingerprint)
    identity_file.write_text(fingerprint + "\n")
    print(f"Persistent signing configured: {identity_file}")
    print("Keep this certificate and its Keychain private key for future updates.")


if __name__ == "__main__":
    main()
