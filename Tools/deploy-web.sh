#!/usr/bin/env bash
# Publish the web build (Builds/Web) to aqua3d.umolab.ru from this computer (WSL or Git Bash).
# Build it first: Aquarium > Build Web Player, or
#   Unity.exe -batchmode -projectPath <project> -buildTarget WebGL -executeMethod Aquarium.EditorTools.PlayerBuild.Web -quit
# KEY=~/.ssh/aqua3d_deploy (a key locked to the site by rrsync, target "./") or, with a key that has a
# shell on the server, TARGET=/var/www/fastuser/data/www/aqua3d.umolab.ru/
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f Builds/Web/index.html ] || { echo "no web build in Builds/Web" >&2; exit 1; }
KEY=${KEY:-}
TARGET=${TARGET:-./}
SSH="ssh -o BatchMode=yes${KEY:+ -i $KEY -o IdentitiesOnly=yes}"
rsync -rz --delete --omit-dir-times --chmod=D2775,F664 -e "$SSH" Builds/Web/ "user1@fp.cloud28.umolab.ru:$TARGET"
echo "published: https://aqua3d.umolab.ru"
