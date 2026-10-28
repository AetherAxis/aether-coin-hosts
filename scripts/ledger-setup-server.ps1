# ── Aether 虚拟币链·服务器一次性准备脚本（管理员、Windows 服务器上手动跑）──────────────
# 链节点 = 资产层（独立于交易所存活）：AetherLedgerNode 服务（TCP JSON-RPC 127.0.0.1:17805——只绑回环，
# 公网零暴露；交易所/钱包 CLI 同机连它，远程机器走 SSH 隧道）。
# 此后每次部署 = 本仓 .github/workflows/deploy-ledger.yml（workflow_dispatch 手动触发——2026-10-27 起链三件套
# 由 ⑧ aether-ctp-hosts 构建、依赖 ① aether-ledger 源码，旧 monorepo workflow 已停用其 push 触发）。
# 2026-10-27 自 monorepo scripts/ 迁入（服务器已于 10-20 按本脚本准备过——保留用于重建/灾备）。
# 部署目录三件套：LedgerNode（节点）/ LedgerWallet（钱包 CLI——运维发币/用户提币）/ LedgerVerify（独立验证器）。
# 幂等：重复运行安全。兼容 Windows PowerShell 5.1。与 CTP 仿真栈共用同一台服务器与 SSH 通道（同 Secrets）。

[CmdletBinding()]
param(
    [string]$NodeAddr = '127.0.0.1:17805',   # 绑定端点（回环=安全默认；'0.0.0.0:17805'=公网直连——自动放行防火墙，见 runbook §2.D）
    [string]$NodeService = 'AetherLedgerNode',
    [string]$NodeDir = 'C:\Aether\LedgerNode',
    [string]$WalletDir = 'C:\Aether\LedgerWallet',
    [string]$VerifyDir = 'C:\Aether\LedgerVerify',
    [string]$DataDir = 'C:\Aether\ledger-data',
    [string]$StagingDir = 'C:\Aether\ledger-tmp',
    [string]$ProducerSeed = ''   # 出块种子（64 位 hex）。⚠ 重启已有链必需——不传且注册表里没有 ⇒ 服务一重启就崩溃循环，见 §3
)

$ErrorActionPreference = 'Stop'

function Fail([string]$msg) {
    [Console]::Error.WriteLine("[LEDGER-SETUP] $msg")
    exit 1
}
function Info([string]$msg) { Write-Host "[LEDGER-SETUP] $msg" }

# 0) 管理员守卫
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail '必须以管理员运行（服务/注册表写入需要管理员权限）'
}

# 1) 目录（账本/日志在 DataDir——部署目录之外，重部署不动链数据）
foreach ($d in @($NodeDir, $WalletDir, $VerifyDir, $DataDir, $StagingDir)) {
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}
Info "目录就绪：$NodeDir / $WalletDir / $VerifyDir（部署三件套）| $DataDir（链文件+节点日志）| $StagingDir（部署中转）"

# 2) 链节点服务（幂等；exe 待首次部署复制）
if (-not (Get-Service -Name $NodeService -ErrorAction SilentlyContinue)) {
    New-Service -Name $NodeService `
        -BinaryPathName (Join-Path $NodeDir 'Aether.Ledger.Node.exe') `
        -DisplayName 'Aether Ledger 链节点（虚拟币资产层）' `
        -Description 'Aether 虚拟币链节点（JSON-RPC 127.0.0.1:17805 仅本机——交易所桥/钱包 CLI 的连接口；链文件 = 余额真相，独立于任何活进程）' `
        -StartupType Automatic | Out-Null
    Info "服务 $NodeService 已创建（exe 待首次部署复制）"
}
else {
    Info "服务 $NodeService 已存在"
}

# 3) 服务级 env（注册表 REG_MULTI_SZ，幂等重写；改动需重启服务生效）。
#    17805 只绑回环 ⇒ 无防火墙规则、无公网暴露（远程机器经 SSH 隧道：ssh -L 17805:127.0.0.1:17805）。
#
#    ⚠ AETHER_LEDGER_PRODUCER_SEED 是「重启能不能活」的唯一开关（2026-10-28 修）：
#      · 新链只在**首次启动**随机生成 producer 种子，打印进日志；链文件只存公钥，不存种子。
#      · 加载已有链时种子**只能**从 env 来，没有就抛「重启已有链须设 AETHER_LEDGER_PRODUCER_SEED」。
#      · 而服务配了崩溃自恢复 restart/5000 ⇒ 没这个 env 的效果是：首次部署成功，之后任何一次重启
#        （手动、重启机器、崩溃恢复）都变成无限崩溃循环，端口 5000ms 一次连不上。
#    所以本脚本：带 -ProducerSeed 就写它；不带就**沿用注册表里已有的值**（重跑不会把已有种子抹掉）。
$nodeKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$NodeService"
$existingEnv = @((Get-ItemProperty -Path $nodeKey -Name Environment -ErrorAction SilentlyContinue).Environment)
$existingProducer = ''
foreach ($line in $existingEnv) {
    if ($line -like 'AETHER_LEDGER_PRODUCER_SEED=*') { $existingProducer = ($line -split '=', 2)[1] }
}

$producer = ''
if ($ProducerSeed) {
    if ($ProducerSeed -notmatch '^[0-9a-fA-F]{64}$') {
        Fail '-ProducerSeed 必须是 64 位 hex（取节点日志里「producer 种子（hex…）：」那一行的值）'
    }
    $producer = $ProducerSeed.ToLowerInvariant()
    Info 'PRODUCER_SEED 来自 -ProducerSeed'
}
elseif ($existingProducer) {
    $producer = $existingProducer
    Info 'PRODUCER_SEED 沿用注册表里已有的值（本次未传 -ProducerSeed）'
}

$nodeEnv = @(
    "AETHER_LEDGER_ADDR=$NodeAddr",
    "AETHER_LEDGER_CHAIN_FILE=$DataDir\ledger.chain",
    "AETHER_LEDGER_LOG=$DataDir\node.log"
)
if ($producer) { $nodeEnv += "AETHER_LEDGER_PRODUCER_SEED=$producer" }
Set-ItemProperty -Path $nodeKey -Name Environment -Type MultiString -Value $nodeEnv
Info "链节点 env 已写入注册表（ADDR=$NodeAddr / 链文件 / 日志 / PRODUCER_SEED=$(if ($producer) { '已设' } else { '未设⚠' }））"

# 防火墙：公网直连形态（非回环绑定）自动放行节点端口（幂等）
if ($NodeAddr -notmatch '^127\.0\.0\.1|^localhost') {
    $nodePort = ($NodeAddr -split ':')[-1]
    if (-not (Get-NetFirewallRule -DisplayName "Aether Ledger Node ($nodePort)" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "Aether Ledger Node ($nodePort)" -Direction Inbound -Protocol TCP -LocalPort $nodePort -Action Allow | Out-Null
    }
    Info "公网直连形态：防火墙已放行 $nodePort（runbook §2.D）"
}

# 4) 崩溃自恢复
sc.exe failure $NodeService reset= 86400 actions= restart/5000 | Out-Null
Info '链节点崩溃自恢复已配置（restart/5000）'

# 5) 收尾清单
$chainFile = Join-Path $DataDir 'ledger.chain'
if (-not (Test-Path $chainFile)) {
    Info '⚠⚠⚠ 首次启动会生成新链——treasury/producer 种子会打印进日志（节点起后从日志抄录，丢失 = 丢失全部未发行供给/无法出块）：'
    Info "   Select-String -Path $DataDir\node.log -Pattern '种子' -Context 0,0"
    if ($producer) {
        Info '   注意：链文件还不存在 ⇒ 这是新链，producer 由节点自己随机生成，本次写入的 PRODUCER_SEED 会被忽略（且与创世公钥不符）。'
        Info '   首启后必须把日志里的 producer 种子写回 env（下面的第 3 步），否则链建好后就再也起不来。'
    }
}
Info '── 完成。还差四步：'
Info '1) 触发 deploy-ledger workflow（push 或手动）完成首次部署（三件套二进制一起发布）'
Info '2) ⚠ 从节点日志抄录 treasury/producer 种子并离线备份（种子 = 财富与出块权，丢了没人能找回）'
if (-not $producer) {
    Info '3) ⚠⚠ 把 producer 种子写进服务 env —— 不做这一步，节点第一次重启就进崩溃循环（restart/5000 无限重试）：'
    Info '   $s = ''<日志里的 producer 种子，64 位 hex>'''
    Info "   $k = 'HKLM:\SYSTEM\CurrentControlSet\Services\$NodeService'"
    Info '   $e = @((Get-ItemProperty $k).Environment) + @("AETHER_LEDGER_PRODUCER_SEED=$s")'
    Info '   Set-ItemProperty $k -Name Environment -Type MultiString -Value $e'
    Info "   Restart-Service $NodeService"
    Info "   或者直接重跑本脚本并带上 -ProducerSeed <种子>（幂等，能识别已有链文件）。"
}
else {
    Info '3) producer 种子已写入服务 env（重启安全）。'
}
Info '4) （可选，启用交易所桥现货）交易所服务加 custody env + BOARD 加 ax001 后重启：'
Info '   $seed = ''<openssl rand -hex 32 生成>''   # custody 托管种子——丢失 = 托管币不可提'
Info '   $k = ''HKLM:\SYSTEM\CurrentControlSet\Services\AetherCtpExchange'''
Info '   $envs = (Get-ItemProperty $k).Environment + @("AETHER_LEDGER_CUSTODY_SEED=$seed")'
Info '   Set-ItemProperty $k -Name Environment -Type MultiString -Value $envs'
Info "   # BOARD 行改 AETHER_CTP_SIM_BOARD=zx001,ax001（同法追加/改写）；Restart-Service AetherCtpExchange"
Info "   # custody 地址（桌面 agent 工具要配 AETHER_LEDGER_CUSTODY_ADDRESS）：$WalletDir\Aether.Ledger.Wallet.exe balance --seed <种子>"
Info '提示：链节点可独立重启（交易所桥轮询自动重连——交易所不用重启）；链文件 + LedgerVerify = 不需要任何活进程的余额真相'
Info "       独立验证：$VerifyDir\Aether.Ledger.Verify.exe $chainFile"
