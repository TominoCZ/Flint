#!/usr/bin/env bash
# Builds Flint (Release: optimised, no debug symbols) and launches it. Arguments go to the app.
# Logs: ~/.local/share/Flint/logs/ (also echoed here), or $FLINT_LOG_DIR.
set -euo pipefail
cd "$(dirname "$0")"

DOTNET="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
"$DOTNET" build ./src/Flint.App/Flint.App.csproj -c Release -v q -nologo
exec "$DOTNET" ./src/Flint.App/bin/Release/net10.0/Flint.dll "$@"
