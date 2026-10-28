# Aether.Coin.Hosts — 设计说明

## 1. 这个仓是什么、不是什么

| 是 | 不是 |
|---|---|
| 虚拟币线的**宿主**：节点 / 钱包 CLI / 验证器三个可执行单元 | 账本实现——引擎在 ① `aether-ledger`，本仓一行共识代码都没有 |
| **发布单元**：`deploy-ledger` 的构建源 | 被引用单元——不发 NuGet，没人 `ProjectReference` 本仓 |
| 组合根：按拓扑把 ①（+ 钱包的 ④ RPC）装进进程 | 业务仓库——没有撮合、没有策略、没有 agent |
| | GUI 仓——钱包 UI 在 ⑰ `aether-coin-desktop`（2026-10-28 拆出） |

和 ⑧ `aether-ctp-hosts` 同一条纪律（只引不产、目录名 = 程序集名 = 发布单元名），区别只是**一个仓只装一条线**。

## 2. 来历：为什么从 ⑧ 拆出来、又为什么改名（2026-10-27 / 10-28）

拆之前三个链宿主和三个 CTP 宿主住在同一个仓里，看代码没坏处，但**部署路径被绑住了**：

- `deploy-ledger` 只需要 ①④ 两个仓，却和需要 ③⑤⑥⑦ 的 CTP 宿主共享同一个 CI 上下文与发布习惯；
- 两者的故障域完全不同：节点重启 = 资产层短暂停服，交易所重启 = 撮合停摆——不该共享一个发布节奏；
- 虚拟币有明确的「服务端 / 客户端」形态（节点 vs 钱包），把它放进「CTP 组合根」会让这条分界在目录结构上消失。

拆分后：⑧ 只覆盖 CTP，⑯ 只覆盖虚拟币，两条线各自建、各自发、各自挂。

2026-10-28 钱包 UI 拆出到 ⑰ `aether-coin-desktop` 后，本仓只剩宿主，由 `aether-coin` 改名 `aether-coin-hosts`——与 ⑧ `aether-ctp-hosts` / ⑪ `aether-ctp-desktop` 的「宿主 / GUI」分法对齐（GUI 与宿主的部署域、演进驱动不同：App 分发 vs `deploy-ledger`、UX/种子存储 vs 引擎接线）。

## 3. 边界与依赖

```
① aether-ledger (库)  ←── ⑯ Node / Verify
                     ←── ⑯ Wallet ──→ ④ Aether.Exchange.Rpc（withdraw 的 JSON-RPC 客户端）
```

- **只有一条出边**（钱包的提现走交易所 RPC）。除它之外本仓不认识任何 CTP 类型。
- 交易所要接链，是**交易所侧**加配置（`AETHER_SETTLEMENT_RAIL=ledger-aq` + custody seed + 节点地址），本仓不感知。
- 反过来也不成立：节点不知道有交易所、有 agent、有钱包——它只认签名与链文件。

## 4. 关键设计

### 4.1 链文件是唯一真相

节点内存态一律可从链文件重建（重启 = 重放）。因此**不需要额外的状态库**，也没有「账本与链对不上」的窗口。
代价是重启要重扫——对本项目的链高（万级）可接受；若将来到百万级，要做的是分片/快照，不是加库。

### 4.2 三个宿主的职责边界

| 形态 | 谁来用 | 设计约束 |
|---|---|---|
| 节点（服务端） | 钱包 / 钱包 UI / 交易所桥 / agent 工具 / 验证工具 | 查询型 RPC，无推送；`:17805` 代码默认只绑回环（生产服务器现配 `0.0.0.0` 供桌面钱包直连） |
| 钱包（客户端） | 人（运维发币 / 充值 / 提币） | **种子 = 全部财富**：明文 hex，不加密，保管等级 = 冷钱包 |
| 验证器（工具） | 审计 / 灾难恢复 | 不需要任何活进程；退出码即结论 |

### 4.3 为什么钱包 CLI 有可注入的 `WalletCli`

monorepo 版是 top-level 语句，测不了。迁仓是「换依赖接线」的动作，最容易错的是异常类型与登录载荷——
只有让 `RunAsync(args, TextWriter)` 能在同进程被调用，才能起一个真节点跑真 CLI。10 例回归就是这么来的。

（钱包 UI 的设计——一个核三个头、`ISeedStore` 种子注入、`--selftest`——随拆分迁至 ⑰ `aether-coin-desktop/DESIGN.md`。）

## 5. 运行与验证

- 构建：`dotnet build aether-coin-hosts.slnx`（4 项目：三宿主 + tests）。
- 测试：`dotnet test tests/Aether.Ledger.Wallet.Tests/…`——10 例钱包 CLI 回归（进程内真节点 + 临时链文件）。
- L2（手工）：起节点 → `wallet info` 读到高度 → `wallet balance <地址>`；验证器对同一链文件退出 0。
- 钱包 UI 的 L2（`--selftest`）在 ⑰。
- 部署：`deploy-ledger` 手动触发 → 服务器停节点、换目录、起节点、RPC 探活。

## 6. 已知约束与待办

| # | 级别 | 事项 | 现状 |
|---|---|---|---|
| 1 | ~~P1~~ 已修（2026-10-28） | 服务 env 缺 `AETHER_LEDGER_PRODUCER_SEED` | `ledger-setup-server.ps1` 原有 bug：只写 ADDR/CHAIN_FILE/LOG ⇒ 首次部署（新链）能跑、重启必崩。已加 `-ProducerSeed` 参数；**不带参重跑会沿用注册表里已有的种子**（不会把配好的种子抹掉）；链文件不存在时明确打印「本次写入会被忽略、首启后必须回写」 |
| 2 | P2 | 无归档/裁剪 | 链文件只增长；本项目规模下可接受，需要时再加 `exportChain` 的分段 |
| 3 | P2 | RPC 明文无鉴权 | 钱包（CLI/UI）现在直连 `127.0.0.1:17805` 或 SSH 隧道；真要远程/手机连公网节点，必须先上 TLS + 鉴权（节点侧改造落本仓，UI 侧提醒见 ⑰ DESIGN） |
| 4 | P3 | 单地址串行出块 | 单 producer、单 nonce 流——够用即可，不是要解决的问题 |

（原「移动端种子安全存储」待办随钱包 UI 迁至 ⑰ DESIGN §5。）

## 7. 与文档的关系

- 钱包 UI：⑰ `aether-coin-desktop`（README / DESIGN）
- 服务器操作与种子保管：`doc/funding-runbook.md`
- 迁移决策：工作区根 `MIGRATION.md` §6 / §6.1
- 工作区级架构：工作区根 `README.md` §一 B
