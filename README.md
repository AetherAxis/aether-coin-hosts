# Aether.Coin

**虚拟币线的宿主仓**——引擎是 ① `aether-ledger`（纯库），本仓只做三件事：把引擎变成**服务端节点**、**客户端钱包**、**离线验证器**，并负责它们的发布。

和 ⑧ `aether-apps` 同一套纪律：**只引不产**，目录名 = 程序集名 = 发布单元名。区别是本仓只装虚拟币这一条线，CTP 宿主留在 ⑧。

## 组成

| 目录 | 形态 | 是什么 | 引 |
|---|---|---|---|
| `Aether.Ledger.Node` | **服务端** | 链节点：TCP JSON-RPC `:17805`（默认仅回环）+ 出块循环 + 链文件持久化 | ① |
| `Aether.Ledger.Wallet` | **客户端** | 钱包 CLI：`new` / `info` / `balance` / `transfer` / `deposit` / `withdraw` | ① ④ |
| `Aether.Ledger.Verify` | 工具 | 离线验证链文件（全量重放 + 五条规则），退出码 `0`=有效 / `2`=无效 | ① |

三个都是**同样地位的外部客户端**视角：钱包和交易所桥（⑧ `LedgerCashRail`）走同一条 RPC、持各自的种子、自己签名。节点不认识它们中的任何一个。

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

## 部署

`deploy-ledger`（`workflow_dispatch` 手动触发）→ 三件套 win-x64 自包含发布 → SSH 到服务器：
停节点 → 换目录 → 起节点 → RPC 探活。

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

服务器初始化、服务 env、种子保管、充值/提币操作见 [`doc/funding-runbook.md`](doc/funding-runbook.md) 与 [`scripts/ledger-setup-server.ps1`](scripts/ledger-setup-server.ps1)。

## 构建与测试

```bash
dotnet build aether-coin.slnx
dotnet test  aether-coin.slnx     # tests/Aether.Ledger.Wallet.Tests：10 例钱包 CLI 回归
```

10 例回归的口径：**进程内起真链节点（临时端口 + 临时链文件）→ 同进程跑 `WalletCli.RunAsync`**。这个 CLI 是资金操作面，最容易错的地方就是「引了哪份账本库 / 引了哪份交易所 RPC」——用例跑真节点，引错立刻红。
`withdraw` 不在单测覆盖内（要起交易所宿主含 custody 桥），见 `doc/funding-runbook.md` §4 的本地 E2E。

## 与 CTP 线的关系

**只有一条出边**：本仓不引 ③⑤⑥⑦ 的任何类型；钱包 `withdraw` 引 ④ `Aether.Exchange.Rpc`（JSON-RPC 客户端）——除此之外与 CTP 线无任何编译期关系。
交易所要接链，是在 **⑧ 的交易所宿主**上加 `AETHER_SETTLEMENT_RAIL=ledger-aq` + `AETHER_LEDGER_CUSTODY_SEED` + `AETHER_LEDGER_NODE_ADDR`——本仓不需要知道交易所存在。

## 来历

2026-10-27 自 ⑧ `aether-apps` 拆出（同日 ⑧ 先按「组合根」收编过一轮）。拆分理由：它有自己的服务端、客户端、部署和故障域，却和 CTP 宿主共享同一个构建与 CI 上下文——虚拟币的部署路径被 CTP 的构建链绑住了。
