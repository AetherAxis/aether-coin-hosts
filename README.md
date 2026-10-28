# Aether.Coin

**虚拟币线的宿主仓**——引擎是 ① `aether-ledger`（纯库），本仓只做一件事：把引擎变成可发布的宿主——**服务端节点**、**客户端钱包**（CLI + 图形界面）、**离线验证器**。

和 ⑧ `aether-ctp-hosts` 同一套纪律：**只引不产**，目录名 = 程序集名 = 发布单元名。区别是本仓只装虚拟币这一条线，CTP 宿主留在 ⑧。

## 组成

| 目录 | 形态 | 是什么 | 引 |
|---|---|---|---|
| `Aether.Ledger.Node` | **服务端** | 链节点：TCP JSON-RPC `:17805`（默认仅回环）+ 出块循环 + 链文件持久化 | ① |
| `Aether.Ledger.Wallet` | **客户端（CLI）** | 钱包 CLI：`new` / `info` / `balance` / `transfer` / `deposit` / `withdraw` | ① ④ |
| `Aether.Ledger.Verify` | 工具 | 离线验证链文件（全量重放 + 五条规则），退出码 `0`=有效 / `2`=无效 | ① |
| `Aether.Ledger.Wallet.App` | **客户端（UI 核心）** | 钱包图形界面的 View / ViewModel / 服务层，三端共享 | ① |
| `Aether.Ledger.Wallet.Desktop` | 客户端（桌面 head） | Avalonia 桌面窗口，`WinExe`；含 `--selftest` 无头自检 | App |
| `Aether.Ledger.Wallet.Android` | 客户端（手机 head） | `net10.0-android`；TFM 与接线就位，种子安全存储待第一批 | App |
| `Aether.Ledger.Wallet.iOS` | 客户端（手机 head） | `net10.0-ios`；同上 | App |

节点/CLI/验证器三个是**同样地位的外部客户端**视角：钱包和交易所桥（⑧ `LedgerCashRail`）走同一条 RPC、持各自的种子、自己签名。节点不认识它们中的任何一个。

钱包 UI 是**一个核三个头**（2026-10-28 落地）：`Wallet.App` 里没有一行平台 API，种子能力全走 `ISeedStore`——
桌面 head 注入 `FileSeedStore`（明文 + 0600 权限，运维级托管），移动 head 目前**故意不注入**，注入的是
`MissingSeedStore`，界面上直接显示「本平台还没有接种子安全存储」，而不是偷偷写明文。种子存储落 Keychain /
Keystore 之前，手机端不参与真实资金。

## 链文件是唯一真相

```
Aether.Ledger.Node  ←── 链文件（JSON lines：首行创世 + 每行一块）
      │                     ▲
      │ RPC                 │ 全量重放
      ▼                     │
Wallet / 交易所桥 / agent 工具   Aether.Ledger.Verify（不需要任何活进程）
```

- 节点重启 = 读取 + 全量重放，**内存态不落盘、也不需要落盘**；崩溃窗口不存在「账本对不上」的问题。
- 验证器独立于节点：交易所死了、节点死了，**链文件 + 它 = 余额真相**。
- ⚠ 链文件只存公钥：**重启已有链必须设 `AETHER_LEDGER_PRODUCER_SEED`**，否则节点直接抛异常并落进 SCM 崩溃重启循环（现象：日志反复「链已加载」，永无「链节点已就绪」）。

## 部署（服务端 = 链节点）

一条命令链：**服务器准备一次（管理员跑脚本）→ 配 Secrets → 手动 Run workflow → 抄种子 → 把 producer 种子写回 env**。

```bash
# ① 服务器一次性准备（Windows，管理员 PowerShell，只需一次；重建/灾备时可重跑，幂等）
.\scripts\ledger-setup-server.ps1                     # 建三套目录 + AetherLedgerNode 服务 + env + 崩溃自恢复
#    首次部署时链文件还不存在 ⇒ producer 种子由节点自己生成，脚本这一步无法预写（见 ④）

# ② 本仓 Settings → Secrets and variables → Actions（新仓，必须在「本仓」配）：
#    CTPSIM_SSH_HOST / CTPSIM_SSH_USER / CTPSIM_SSH_KEY（+ 可选 CTPSIM_SSH_PORT）

# ③ 触发部署：Actions → deploy-ledger → Run workflow（workflow_dispatch）
#    三件套 win-x64 自包含发布 → SSH 到服务器：停服务 → 换目录 → 起服务 → RPC 探活

# ④ 首启后立刻抄种子（新链只有这一次机会）
#    服务器上：Select-String -Path C:\Aether\ledger-data\node.log -Pattern '种子'
#    ⚠⚠ treasury 种子 = 全部未发行供给；producer 种子 = 出块权，且「重启能不能活」全靠它

# ⑤ 把 producer 种子写进服务 env（不做 = 节点第一次重启就进崩溃循环，restart/5000 无限重试）
#    重跑脚本带上就行（幂等，会识别已有链文件，并沿用注册表里已有的值）：
.\scripts\ledger-setup-server.ps1 -ProducerSeed <日志里的 producer 种子>
#    或手写注册表：$k='HKLM:\SYSTEM\CurrentControlSet\Services\AetherLedgerNode'
#                $e=@((Get-ItemProperty $k).Environment)+@("AETHER_LEDGER_PRODUCER_SEED=<hex>")
#                Set-ItemProperty $k -Name Environment -Type MultiString -Value $e; Restart-Service AetherLedgerNode
```

**验收**：`Get-Service AetherLedgerNode` = Running；日志里有「链节点已就绪」；`wallet info`（服务器上
`Aether.Ledger.Wallet.exe info`）能拿到高度且在推进；`Aether.Ledger.Verify.exe C:\Aether\ledger-data\ledger.chain`
退出码 0。

| 服务器路径 | 内容 |
|---|---|
| `C:\Aether\LedgerNode` / `LedgerWallet` / `LedgerVerify` | 三件套二进制 |
| `C:\Aether\ledger-data` | `ledger.chain` + `node.log`——**在部署目录之外，重部署不动链数据** |
| `C:\Aether\ledger-tmp` | 部署中转 |

- 服务名 `AetherLedgerNode`，`sc.exe failure` 崩溃自恢复 `restart/5000`。
- `:17805` 默认只绑回环（无防火墙规则）；远端访问走 SSH 隧道 `ssh -L 17805:127.0.0.1:17805`。
- **依赖仓（① ④）push 不会自动重新部署**——须手动 Run workflow。
- ⚠ **本仓是新仓（2026-10-27 自 ⑧ 拆出）**：`CTPSIM_SSH_HOST` / `CTPSIM_SSH_USER` / `CTPSIM_SSH_KEY`（+ 可选 `CTPSIM_SSH_PORT`）要在**本仓** Settings → Secrets 里重新配一遍，否则 workflow 直接失败。服务器端路径/服务名/staging 未变。
- 首次部署会生成新链，treasury / producer 种子打印进 `node.log`，**必须立刻抄录并离线备份**（种子 = 全部未发行供给 + 出块权）。
- ⚠ **producer 种子必须写回服务 env**（上面第 ⑤ 步）：链文件只存公钥，服务重启时种子只能从
  `AETHER_LEDGER_PRODUCER_SEED` 取——缺它 + `restart/5000` 自恢复 = 无限崩溃循环（日志反复「链已加载」，永无「链节点已就绪」）。

（可选，接交易所现货）再给交易所服务加 `AETHER_LEDGER_CUSTODY_SEED` + `AETHER_LEDGER_NODE_ADDR`、
BOARD 加 `ax001`，重启交易所——桥起来后充值/提币才有归账方。见 `doc/funding-runbook.md`。

服务器初始化、服务 env、种子保管、充值/提币操作见 [`doc/funding-runbook.md`](doc/funding-runbook.md) 与 [`scripts/ledger-setup-server.ps1`](scripts/ledger-setup-server.ps1)。

## 构建与测试

```bash
# 服务端/CLI/验证器 + 桌面钱包 UI（CI 就是这条线；移动 head 不在 CI 里，见下）
dotnet build Aether.Ledger.Wallet.Desktop/Aether.Ledger.Wallet.Desktop.csproj -c Release
dotnet test  tests/Aether.Ledger.Wallet.Tests/Aether.Ledger.Wallet.Tests.csproj -c Release

# 钱包 UI 无头自检：进程内起真节点 → 从日志读 treasury 种子 → 走 VM 命令建钱包/转账 → 查余额（6 项断言，退出码 0 = 通过）
dotnet run --project Aether.Ledger.Wallet.Desktop -c Release -- --selftest

# 跑桌面界面（需要图形环境）
dotnet run --project Aether.Ledger.Wallet.Desktop -c Release
```

移动 head 只在 macOS/Linux 本机构建，不进 CI（Linux runner 没有 android/ios workload）：

```bash
dotnet build Aether.Ledger.Wallet.Android/Aether.Ledger.Wallet.Android.csproj -c Release
dotnet build Aether.Ledger.Wallet.iOS/Aether.Ledger.Wallet.iOS.csproj -c Release -p:RuntimeIdentifier=iossimulator-arm64
```

实测（2026-10-28，macOS）：Android 0 警告 0 错误；iOS 0 错误、41 个警告全部是 IL2xxx trim 警告
（Avalonia / BouncyCastle 的反射裁剪提示），Release 全量 AOT 约 18 分钟——首次构建别以为是卡死。

`slnx` 里放全 8 个项目只为 IDE 可见；**CI 不 build 整个 slnx**（会把 android/ios TFM 一起拽进来）。
`--selftest` 与 ⑮ 的 `--smoke` 同一个理由：没有显示器的环境也要能验「XAML 绑定的属性名/命令真的通」。

10 例回归的口径：**进程内起真链节点（临时端口 + 临时链文件）→ 同进程跑 `WalletCli.RunAsync`**。这个 CLI 是资金操作面，最容易错的地方就是「引了哪份账本库 / 引了哪份交易所 RPC」——用例跑真节点，引错立刻红。
`withdraw` 不在单测覆盖内（要起交易所宿主含 custody 桥），见 `doc/funding-runbook.md` §4 的本地 E2E。

## 与 CTP 线的关系

**只有一条出边**：本仓不引 ③⑤⑥⑦ 的任何类型；钱包 `withdraw` 引 ④ `Aether.Exchange.Rpc`（JSON-RPC 客户端）——除此之外与 CTP 线无任何编译期关系。
交易所要接链，是在 **⑧ 的交易所宿主**上加 `AETHER_SETTLEMENT_RAIL=ledger-aq` + `AETHER_LEDGER_CUSTODY_SEED` + `AETHER_LEDGER_NODE_ADDR`——本仓不需要知道交易所存在。

## 来历

2026-10-27 自 ⑧ `aether-ctp-hosts` 拆出（同日 ⑧ 先按「组合根」收编过一轮）。拆分理由：它有自己的服务端、客户端、部署和故障域，却和 CTP 宿主共享同一个构建与 CI 上下文——虚拟币的部署路径被 CTP 的构建链绑住了。
