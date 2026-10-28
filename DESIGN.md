# Aether.Coin — 设计说明

## 1. 这个仓是什么、不是什么

| 是 | 不是 |
|---|---|
| 虚拟币线的**宿主**：节点 / 钱包 / 验证器三个可执行单元 | 账本实现——引擎在 ① `aether-ledger`，本仓一行共识代码都没有 |
| **发布单元**：`deploy-ledger` 的构建源 | 被引用单元——不发 NuGet，没人 `ProjectReference` 本仓 |
| 组合根：按拓扑把 ①（+ 钱包的 ④ RPC）装进进程 | 业务仓库——没有撮合、没有策略、没有 agent |

和 ⑧ `aether-ctp-hosts` 同一条纪律（只引不产、目录名 = 程序集名 = 发布单元名），区别只是**一个仓只装一条线**。

## 2. 为什么要从 ⑧ 拆出来（2026-10-27）

拆之前三个 Web 宿主和三个 CTP 宿主住在同一个仓里，看代码没坏处，但**部署路径被绑住了**：

- `deploy-ledger` 只需要 ①④ 两个仓，却和需要 ③⑤⑥⑦ 的 CTP 宿主共享同一个 CI 上下文与发布习惯；
- 两者的故障域完全不同：节点重启 = 资产层短暂停服，交易所重启 = 撮合停摆——不该共享一个发布节奏；
- 虚拟币有明确的「服务端 / 客户端」形态（节点 vs 钱包），把它放进「CTP 组合根」会让这条分界在目录结构上消失。

拆分后：⑧ 只覆盖 CTP，⑯ 只覆盖虚拟币，两条线各自建、各自发、各自挂。

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
| 节点（服务端） | 钱包 / 交易所桥 / agent 工具 / 验证工具 | 查询型 RPC，无推送；`:17805` 默认只绑回环 |
| 钱包（客户端） | 人（运维发币 / 充值 / 提币） | **种子 = 全部财富**：明文 hex，不加密，保管等级 = 冷钱包 |
| 验证器（工具） | 审计 / 灾难恢复 | 不需要任何活进程；退出码即结论 |

### 4.3 为什么钱包 CLI 有可注入的 `WalletCli`

monorepo 版是 top-level 语句，测不了。迁仓是「换依赖接线」的动作，最容易错的是异常类型与登录载荷——
只有让 `RunAsync(args, TextWriter)` 能在同进程被调用，才能起一个真节点跑真 CLI。10 例回归就是这么来的。

### 4.4 钱包 UI：一个核三个头，种子能力是头决定的（2026-10-28）

```
        Wallet.App（View + VM + WalletService，零平台 API）
        ├─ Wallet.Desktop   Avalonia 桌面窗口（WinExe）+ --selftest
        ├─ Wallet.Android   net10.0-android，TFM/接线就位
        └─ Wallet.iOS       net10.0-ios，TFM/接线就位
```

三条约束，都是「宁可功能少，也不做错」：

- **VM 不引用任何平台 API**：`Aether.Ledger.Wallet.App` 只引 ①，view 层是同一份 XAML。手机上不用第二套界面——
  布局是「一条竖流」（`ScrollViewer` + `MaxWidth`），窄屏不挤、宽屏不散。
- **种子不是 VM 的事**：全部经 `ISeedStore`（`HasSeed` / `Describe` / `ReadAsync` / `WriteAsync`）。桌面 head 注入
  `FileSeedStore`（明文 + `0600`），移动 head 注入 `MissingSeedStore`——`WriteAsync` 抛 `NotSupportedException`，
  VM 统一捕获并把界面切成「种子存储未接入」。**手机端在 Keychain / Keystore 落地前不碰真钱**，
  这比「先写个明文文件占位、以后再换」安全得多：占位方案会真的被用来存钱。
- **读种子必须显式触发**：VM 构造不读种子（只查 `HasSeed` 显示提示），点「刷新/转账」才惰性读入内存；
  导入种子后立刻把输入框清空。

为什么敢在手机上只留接线：`net10.0` + 纯托管 Ed25519（① 的选型原因之一就是 libsodium 在 iOS 链接不过）
已经证明可移植；缺的只是**存种子的地方**，那是平台 API，恰好是头应该负责的部分。

验证方式：`--selftest` 不开窗口，进程内起真节点 → 从日志读 treasury 种子 → 走 VM 的
`ImportWalletCommand` / `TransferCommand` → 查收款余额。它同时证明三件事：链路通、VM 逻辑对、
**XAML 绑定的属性名没写错**（VM 改了名而 XAML 没改，编译不报错，只会静默不更新——这类 bug 只有跑起来才抓得到）。

## 5. 运行与验证

- 构建：`dotnet build aether-coin.slnx`（0 警告 0 错误）。CI 只构建 Node / Wallet / Verify / Wallet.Desktop
  四个项目——`slnx` 里的 android / ios 在 Linux runner 上没有 workload，进 CI 只会红。
- 测试：`dotnet test tests/Aether.Ledger.Wallet.Tests/…`——10 例钱包 CLI 回归（进程内真节点 + 临时链文件）。
- L2（手工）：起节点 → `wallet info` 读到高度 → `wallet balance <地址>`；验证器对同一链文件退出 0。
- L2（自动，无 GUI）：`dotnet run --project Aether.Ledger.Wallet.Desktop -c Release -- --selftest` → 6 项断言全绿。
- 部署：`deploy-ledger` 手动触发 → 服务器停节点、换目录、起节点、RPC 探活。

## 6. 已知约束与待办

| # | 级别 | 事项 | 现状 |
|---|---|---|---|
| 1 | ~~P1~~ 已修（2026-10-28） | 服务 env 缺 `AETHER_LEDGER_PRODUCER_SEED` | `ledger-setup-server.ps1` 原有 bug：只写 ADDR/CHAIN_FILE/LOG ⇒ 首次部署（新链）能跑、重启必崩。已加 `-ProducerSeed` 参数；**不带参重跑会沿用注册表里已有的种子**（不会把配好的种子抹掉）；链文件不存在时明确打印「本次写入会被忽略、首启后必须回写」 |
| 2 | P1 | 移动端种子安全存储 | 移动 head 现在注入 `MissingSeedStore`——能编译、能连节点、能查余额，但**不能建/导钱包**（界面禁用 + 一行说明）。第一批做 Keychain / Android Keystore |
| 3 | P2 | 无归档/裁剪 | 链文件只增长；本项目规模下可接受，需要时再加 `exportChain` 的分段 |
| 4 | P2 | RPC 明文无鉴权 | 钱包 UI 现在直连 `127.0.0.1:17805` 或 SSH 隧道；真要远程/手机连公网节点，必须先上 TLS + 鉴权（README §一 B 的三个前置条件之一） |
| 5 | P3 | 单地址串行出块 | 单 producer、单 nonce 流——够用即可，不是要解决的问题 |

## 7. 与文档的关系

- 服务器操作与种子保管：`doc/funding-runbook.md`
- 迁移决策：工作区根 `MIGRATION.md` §6 / §6.1
- 工作区级架构：工作区根 `README.md` §一 B
