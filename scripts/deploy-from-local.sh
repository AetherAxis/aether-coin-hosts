#!/usr/bin/env bash
# ── 本机直传部署（虚拟币线：链节点 + 钱包 CLI + 独立验证器）──────────────────────────
# 为什么需要它：GitHub 托管 runner 到这台服务器的上传带宽极差——2026-10-28 实测 runner 传 54MB
#   23 分钟未完（job 被硬超时杀掉），本机同一条命令 34 秒。CI 的 deploy-ledger.yml 保留作构建契约
#   （deploy-dryrun.yml 每 push 验），真上线走本脚本。
#
# 用法：scripts/deploy-from-local.sh
# 环境变量（都有默认值）：
#   AETHER_DEPLOY_HOST  默认 117.72.186.128
#   AETHER_DEPLOY_USER  默认 administrator
#   AETHER_DEPLOY_KEY   默认 ~/.ssh/ctpsim_deploy
#   DOTNET              默认 dotnet
#
# ⚠ 停服影响：链节点会重启 = 资产层停约 10-20s（链文件越大重放越久）；交易所的结算桥会自动重连，
#   交易所本身不用动。重启已有链要求服务 env 里有 AETHER_LEDGER_PRODUCER_SEED（缺了就崩溃循环，
#   见 doc/ledger-runbook.md §4）。
set -euo pipefail

HOST=${AETHER_DEPLOY_HOST:-117.72.186.128}
DEPLOY_USER=${AETHER_DEPLOY_USER:-administrator}
KEY=${AETHER_DEPLOY_KEY:-$HOME/.ssh/ctpsim_deploy}
DOTNET=${DOTNET:-dotnet}

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
WS=$(dirname "$ROOT")
for d in aether-ledger aether-exchange; do
    [ -d "$WS/$d" ] || { echo "缺兄弟仓 $d（本机工作区须 16 仓并排）" >&2; exit 1; }
done

SSH_OPTS=(-i "$KEY" -o BatchMode=yes -o StrictHostKeyChecking=accept-new
          -o ConnectTimeout=20 -o ServerAliveInterval=15 -o ServerAliveCountMax=4)
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT

# VERSION 带两个仓的 sha：链节点行为由 ① 决定、宿主由本仓决定——只写一个追不了「改动上没上线」
APP_SHA=$(git -C "$ROOT" rev-parse --short HEAD)
LEDGER_SHA=$(git -C "$WS/aether-ledger" rev-parse --short HEAD)
VERSION="${APP_SHA}+${LEDGER_SHA}"

echo "[deploy] 构建 win-x64 自包含（版本 $VERSION）"
for p in Aether.Ledger.Node:node Aether.Ledger.Wallet:wallet Aether.Ledger.Verify:verify; do
    proj=${p%%:*}; out=${p##*:}
    ( cd "$ROOT" && "$DOTNET" publish "$proj" -c Release --self-contained true --runtime win-x64 -o "$TMP/$out" >/dev/null )
    printf '%s\n' "$VERSION" > "$TMP/$out/VERSION"
    echo "  publish $proj → $out"
done

tar -czf "$TMP/ledger.tar.gz" -C "$TMP" node wallet verify
echo "[deploy] 上传 → $HOST:C:/Aether/ledger-tmp（$(du -h "$TMP/ledger.tar.gz" | cut -f1)）"
t0=$(date +%s)
scp "${SSH_OPTS[@]}" "$TMP/ledger.tar.gz" "$ROOT/scripts/ledger-remote-deploy.ps1" \
    "$DEPLOY_USER@$HOST:C:/Aether/ledger-tmp/" >/dev/null
echo "  上传耗时 $(( $(date +%s) - t0 ))s"

echo "[deploy] 远端执行 ledger-remote-deploy.ps1（链节点将重启，资产层停 10-20s）"
ssh "${SSH_OPTS[@]}" "$DEPLOY_USER@$HOST" \
    "powershell -NoProfile -ExecutionPolicy Bypass -File C:/Aether/ledger-tmp/ledger-remote-deploy.ps1"
