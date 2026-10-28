# Aether.Coin.Hosts

**虚拟币线的宿主仓**——引擎是 ① `aether-ledger`（纯库），本仓只做一件事：把引擎变成可发布的宿主——**服务端节点**、**客户端钱包 CLI**、**离线验证器**。

和 ⑧ `aether-ctp-hosts` 同一套纪律：**只引不产**，目录名 = 程序集名 = 发布单元名。区别是本仓只装虚拟币这一条线，CTP 宿主留在 ⑧。

钱包图形界面（`Wallet.App` + 桌面/Android/iOS 三头）已于 2026-10-28 拆出到 ⑰ `aether-coin-desktop`——本仓与 ⑧ 一样只装宿主。

## 组成

| 目录 | 形态 | 是什么 | 引 |
|---|---|---|---|
| `Aether.Ledger.Node` | **服务端** | 链节点：TCP JSON-RPC `:17805`（默认仅回环）+ 出块循环 + 链文件持久化 | ① |
| `Aether.Ledger.Wallet` | **客户端（CLI）** | 钱包 CLI：`new` / `info` / `balance` / `transfer` / `deposit` / `withdraw` | ① ④ |
| `Aether.Ledger.Verify` | 工具 | 离线验证链文件（全量重放 + 五条规则），退出码 `0`=有效 / `2`=无效 | ① |

节点/CLI/验证器和钱包 UI 是**同样地位的外部客户端**视角：钱包（CLI 与 UI）和交易所桥（⑧ `LedgerCashRail`）走同一条 RPC、持各自的种子、自己签名。节点不认识它们中的任何一个。

## 链文件是唯一真相

```
Aether.Ledger.Node  ←── 链文件（JSON lines：首行创世 + 每行一块）
      │                     ▲
      │ RPC                 │ 全量重放
      ▼                     │
Wallet / 钱包 UI / 交易所桥 / agent 工具   Aether.Ledger.Verify（不需要任何活进程）
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

# ③ 部署（**本机直传，推荐**）：.\scripts\deploy-from-local.sh   # 需 bash；或 CI：Actions → deploy-ledger（上传极慢，见下）
#    2026-10-28 实测 runner→服务器 54MB 需 >23 分钟，本机 34s——CI 那条路暂作构建契约
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
- `:17805` **代码默认**只绑回环；远端访问本来要走 SSH 隧道 `ssh -L 17805:127.0.0.1:17805`。
  ⚠ **但生产服务器实际配的是 `0.0.0.0:17805`（`aether-public-ports.ps1`），公网可达**——2026-10-28 用 socket 实测四个端口 17800/17801/17802/17805 都能从外网连上。桌面钱包/远程 CLI 直连靠的就是这个；**2026-10-28 决定：功能优先，暂不改回环，安全性作为待办**（改回环 = 桌面端必须走隧道）。
- **依赖仓（① ④）push 不会自动重新部署**——须手动 Run workflow。
- ⚠ **本仓是 2026-10-27 新拆出的仓**：`CTPSIM_SSH_HOST` / `CTPSIM_SSH_USER` / `CTPSIM_SSH_KEY`（+ 可选 `CTPSIM_SSH_PORT`）要在**本仓** Settings → Secrets 里配（GitHub 改名后 Secrets 随仓保留，无需重配）。服务器端路径/服务名/staging 未变。
- 首次部署会生成新链，treasury / producer 种子打印进 `node.log`，**必须立刻抄录并离线备份**（种子 = 全部未发行供给 + 出块权）。
- ⚠ **producer 种子必须写回服务 env**（上面第 ⑤ 步）：链文件只存公钥，服务重启时种子只能从
  `AETHER_LEDGER_PRODUCER_SEED` 取——缺它 + `restart/5000` 自恢复 = 无限崩溃循环（日志反复「链已加载」，永无「链节点已就绪」）。

（可选，接交易所现货）再给交易所服务加 `AETHER_LEDGER_CUSTODY_SEED` + `AETHER_LEDGER_NODE_ADDR`、
BOARD 加 `ax001`，重启交易所——桥起来后充值/提币才有归账方。见 `doc/funding-runbook.md`。

服务器初始化、服务 env、种子保管、充值/提币操作见 [`doc/funding-runbook.md`](doc/funding-runbook.md) 与 [`scripts/ledger-setup-server.ps1`](scripts/ledger-setup-server.ps1)。

## 构建与测试

```bash
# 宿主三件套 + 测试（CI 就是这条线）
dotnet build aether-coin-hosts.slnx -c Release
dotnet test  tests/Aether.Ledger.Wallet.Tests/Aether.Ledger.Wallet.Tests.csproj -c Release
```

10 例回归的口径：**进程内起真链节点（临时端口 + 临时链文件）→ 同进程跑 `WalletCli.RunAsync`**。这个 CLI 是资金操作面，最容易错的地方就是「引了哪份账本库 / 引了哪份交易所 RPC」——用例跑真节点，引错立刻红。
`withdraw` 不在单测覆盖内（要起交易所宿主含 custody 桥），见 `doc/funding-runbook.md` §4 的本地 E2E。

钱包 UI 的构建 / `--selftest` / 移动 head 见 ⑰ `aether-coin-desktop`。

## 与 CTP 线的关系

**只有一条出边**：本仓不引 ③⑤⑥⑦ 的任何类型；钱包 `withdraw` 引 ④ `Aether.Exchange.Rpc`（JSON-RPC 客户端）——除此之外与 CTP 线无任何编译期关系。
交易所要接链，是在 **⑧ 的交易所宿主**上加 `AETHER_SETTLEMENT_RAIL=ledger-aq` + `AETHER_LEDGER_CUSTODY_SEED` + `AETHER_LEDGER_NODE_ADDR`——本仓不需要知道交易所存在。

## 来历

2026-10-27 自 ⑧ `aether-ctp-hosts` 拆出（同日 ⑧ 先按「组合根」收编过一轮）。拆分理由：它有自己的服务端、客户端、部署和故障域，却和 CTP 宿主共享同一个构建与 CI 上下文——虚拟币的部署路径被 CTP 的构建链绑住了。
2026-10-28 钱包 UI 拆出到 ⑰ `aether-coin-desktop` 后，本仓只剩宿主，由 `aether-coin` 改名 `aether-coin-hosts`——与 ⑧ `aether-ctp-hosts` / ⑪ `aether-ctp-desktop` 的「宿主 / GUI」分法对齐。
