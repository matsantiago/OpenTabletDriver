#!/usr/bin/env bash

[ "${BUILD}" != "true" ] && exit_with_error "Must build to package MacOS"

pkg_script_root="$(readlink -f $(dirname "${BASH_SOURCE[0]}"))"

echo -e "\nPreparing package..."

PKG_FILE="${OTD_NAME}-${OTD_VERSION}_${NET_RUNTIME}.tar.gz"

pkg_root="${OUTPUT}/${OTD_NAME}.app"

move_to_nested "${OUTPUT}" "${pkg_root}/Contents/MacOS"

echo "Copying MacOS assets..."
mkdir -p "${pkg_root}/Contents/Resources"
cp "${pkg_script_root}/Icon.icns" "${pkg_root}/Contents/Resources/"
cp "${pkg_script_root}/Info.plist" "${pkg_root}/Contents/"

if [ "${SIGNED}" == "true" ]; then
  echo "Signing app bundle..."
  signing_identity="${OTD_CODESIGN_IDENTITY:-}"
  signing_identity_file="${HOME}/Library/Application Support/OpenTabletDriver/Signing/identity.txt"
  if [ -z "${signing_identity}" ] && [ -f "${signing_identity_file}" ]; then
    signing_identity="$(cat "${signing_identity_file}")"
  fi

  if [ -n "${signing_identity}" ]; then
    # Sign helpers first, then the enclosing bundle. Keep codesign's default
    # certificate-based requirements so future builds retain the same identity.
    # A configured identity must fail the build if unavailable, never fall back
    # to ad-hoc signing and silently invalidate the user's privacy permissions.
    for helper in OpenTabletDriver.Console OpenTabletDriver.Daemon; do
      codesign --force --sign "${signing_identity}" --identifier "${helper}" \
        "${pkg_root}/Contents/MacOS/${helper}"
    done
    codesign --force --sign "${signing_identity}" --identifier net.opentabletdriver "${pkg_root}"
    codesign --verify --deep --strict "${pkg_root}"
  elif hash rcodesign 2>/dev/null; then
    rcodesign sign "${pkg_root}"
  elif hash codesign 2>/dev/null; then
    codesign --deep --force --sign - "${pkg_root}"
  else
    echo "Warning: neither rcodesign nor codesign found, skipping signing"
  fi
fi

echo "Creating tarball..."
create_binary_tarball "${pkg_root}" "${OUTPUT}/${PKG_FILE}"

echo -e "\nPackaging finished! Package created at '${OUTPUT}/${PKG_FILE}'"
