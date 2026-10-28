#!/bin/sh
# Starts the API listening on all interfaces at $PORT (Railway injects it).
set -eu

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"

if [ "$(id -u)" = "0" ]; then
  # The volume is mounted root-owned. Create the Data Protection key
  # directory and hand it to the unprivileged runtime user, then drop root.
  if [ -n "${DataProtection__KeysPath:-}" ]; then
    mkdir -p "$DataProtection__KeysPath"
    chown -R app:app "$DataProtection__KeysPath"
    chmod 700 "$DataProtection__KeysPath"
  fi

  exec setpriv --reuid=app --regid=app --init-groups dotnet FieldOps.Api.dll
fi

exec dotnet FieldOps.Api.dll
