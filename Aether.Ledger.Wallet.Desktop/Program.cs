using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Aether.Ledger;
using Aether.Ledger.Wallet.App;
using Aether.Ledger.Wallet.App.ViewModels;
using WalletApp = Aether.Ledger.Wallet.App.App;
using Avalonia;

namespace Aether.Ledger.Wallet.Desktop;

/// <summary>
/// 桌面 head（首个可运行形态）。种子存储 = <see cref="FileSeedStore"/>（明文 + 0600，运维级托管）。
/// <para><c>--selftest</c>：不开窗口跑一遍真链路（进程内起真节点 → 建钱包 → 用 treasury 种子转账 →
/// 查余额），退出码 0 = 通过。GUI 在无显示环境跑不起来，但这条能跑——和 ⑮ 的 <c>--smoke</c> 同一个理由。</para>
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // head 决定种子存哪：桌面 = 文件（0600）。移动 head 不设，保持「未接入」的明确失败。
        WalletApp.CreateSeedStore = () => new FileSeedStore(WalletPaths.SeedFile);
        WalletApp.CreateSettings = () => WalletSettings.Load(WalletPaths.SettingsFile);

        if (args.Contains("--selftest"))
        {
            var ok = SelfTestAsync().GetAwaiter().GetResult();
            Environment.ExitCode = ok ? 0 : 1;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<WalletApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// L2 自检：一条链 + 一个钱包 + 一笔转账，全部走 VM 的命令（不是直接调服务），
    /// 因此顺带验证 XAML 绑定的属性名没写错。
    /// </summary>
    private static async Task<bool> SelfTestAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aether-wallet-ui-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var chainFile = Path.Combine(dir, "ledger.chain");
        var logFile = Path.Combine(dir, "node.log");
        var port = FreePort();
        var node = new LedgerNode(new LedgerNodeOptions("127.0.0.1", port, chainFile, 77, 1), logFile);
        var pass = true;

        void Check(bool condition, string what)
        {
            Console.WriteLine(condition ? $"[selftest] PASS ✅ {what}" : $"[selftest] FAIL ❌ {what}");
            pass &= condition;
        }

        try
        {
            node.Start();

            // 节点的第一份日志里带 treasury 种子——这是「新链只有一次机会抄录」的那一行，正好用它做转账资金。
            var treasury = await WaitForTreasurySeedAsync(logFile);
            Check(treasury is { Length: 64 }, "从节点日志读到 treasury 种子");

            var settings = new WalletSettings { NodeAddress = $"127.0.0.1:{port}" };
            var store = new FileSeedStore(Path.Combine(dir, "wallet.seed"));
            var vm = new MainViewModel(store, settings);

            await vm.RefreshCommand.ExecuteAsync(null);          // 未解锁也能查链
            Check(vm.ChainText.Contains("高度", StringComparison.Ordinal), "VM 查到链概况");

            vm.ImportSeed = treasury!;
            await vm.ImportWalletCommand.ExecuteAsync(null);
            Check(vm.Address.Length == 40, "导入 treasury 种子后得到 40 位地址");
            Check(File.Exists(store.Path), "种子落到文件");

            // 收款方：新建一个钱包（只取地址，不复用它的 App 状态）
            var (_, receiverAddress) = WalletService.Create();
            vm.Destination = receiverAddress;
            vm.Amount = "1.5";
            await vm.TransferCommand.ExecuteAsync(null);
            Check(vm.Log.Any(l => l.Contains("已提交 1.5", StringComparison.Ordinal)), "VM 提交转账");

            var credited = await WaitForBalanceAsync(vm, receiverAddress);
            Check(credited, "收款地址余额到账 1.5");

            Console.WriteLine($"[selftest] 临时目录 {dir}（可删）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[selftest] EXCEPTION ❌ {ex.Message}");
            pass = false;
        }
        finally
        {
            try { await node.StopAsync(); } catch { /* 已停 */ }
        }

        return pass;
    }

    private static async Task<bool> WaitForBalanceAsync(MainViewModel vm, string address)
    {
        var service = new WalletService();
        for (var i = 0; i < 20; i++)
        {
            var text = await service.DescribeBalanceAsync(vm.NodeAddress, address);
            if (text.Contains("币 1.5", StringComparison.Ordinal)) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task<string?> WaitForTreasurySeedAsync(string logFile)
    {
        var pattern = new Regex(@"treasury 种子（hex[^：]*）：([0-9a-f]{64})", RegexOptions.Compiled);
        for (var i = 0; i < 30; i++)
        {
            if (File.Exists(logFile))
            {
                var match = pattern.Match(await File.ReadAllTextAsync(logFile));
                if (match.Success) return match.Groups[1].Value;
            }
            await Task.Delay(200);
        }
        return null;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
