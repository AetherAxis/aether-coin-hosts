using System.Collections.ObjectModel;
using Aether.Ledger.Wallet.App;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Ledger.Wallet.App.ViewModels;

/// <summary>
/// 钱包主界面的 VM：链概况 / 余额 / 转账 / 建钱包 / 导入种子。
/// <para>三端共享（桌面 / Android / iOS）：**不引用任何平台 API**，种子能力全部经 <see cref="ISeedStore"/>，
/// 由 head 决定「能不能用、安不安全」。移动 head 目前注入的是 <see cref="MissingSeedStore"/>，
/// 因此手机上打开会明确显示「种子存储未接入」，而不是偷偷写明文文件。</para>
/// <para>刻意保持「读种子要显式触发」：属性里不自动解锁钱包，避免一启动就把种子读进内存。</para>
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ISeedStore _seedStore;
    private readonly WalletService _service;
    private readonly WalletSettings _settings;
    private string? _seedHex;

    public MainViewModel(ISeedStore seedStore, WalletSettings settings, WalletService? service = null)
    {
        _seedStore = seedStore;
        _settings = settings;
        _service = service ?? new WalletService();

        NodeAddress = settings.NodeAddress;
        CustodyAddress = settings.CustodyAddress;
        ExchangeAccount = settings.ExchangeAccount;

        SaveSettingsCommand = new RelayCommand(SaveSettings);
        CreateWalletCommand = new AsyncRelayCommand(CreateWalletAsync);
        ImportWalletCommand = new AsyncRelayCommand(ImportWalletAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        TransferCommand = new AsyncRelayCommand(TransferAsync);

        Record($"钱包 UI · 种子存储：{seedStore.Describe}");
        if (seedStore.HasSeed)
            Record("检测到已有种子文件——点「刷新」即可解锁并查询余额。");
    }

    // —— 连接参数 ——

    private string _nodeAddress = "";
    public string NodeAddress { get => _nodeAddress; set => SetProperty(ref _nodeAddress, value); }

    private string _custodyAddress = "";
    public string CustodyAddress { get => _custodyAddress; set => SetProperty(ref _custodyAddress, value); }

    private string _exchangeAccount = "";
    public string ExchangeAccount { get => _exchangeAccount; set => SetProperty(ref _exchangeAccount, value); }

    // —— 钱包状态 ——

    private string _address = "（未解锁）";
    public string Address { get => _address; private set => SetProperty(ref _address, value); }

    private string _chainText = "尚未查询";
    public string ChainText { get => _chainText; private set => SetProperty(ref _chainText, value); }

    private string _balanceText = "—";
    public string BalanceText { get => _balanceText; private set => SetProperty(ref _balanceText, value); }

    private string _seedNote = "";
    public string SeedNote { get => _seedNote; private set => SetProperty(ref _seedNote, value); }

    private bool _canUseSeed = true;
    /// <summary>当前 head 是否具备可用的种子存储（移动端未接 Keychain/Keystore 时为 false）。</summary>
    public bool CanUseSeed { get => _canUseSeed; private set => SetProperty(ref _canUseSeed, value); }

    // —— 操作输入 ——

    private string _importSeed = "";
    public string ImportSeed { get => _importSeed; set => SetProperty(ref _importSeed, value); }

    private string _destination = "";
    public string Destination { get => _destination; set => SetProperty(ref _destination, value); }

    private string _amount = "";
    public string Amount { get => _amount; set => SetProperty(ref _amount, value); }

    private string _memo = "";
    public string Memo { get => _memo; set => SetProperty(ref _memo, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>操作日志（倒序追加，UI 直接绑 ItemsControl）。</summary>
    public ObservableCollection<string> Log { get; } = new();

    public IRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand CreateWalletCommand { get; }
    public IAsyncRelayCommand ImportWalletCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand TransferCommand { get; }

    // —— 命令实现 ——

    private void SaveSettings()
    {
        _settings.NodeAddress = NodeAddress.Trim();
        _settings.CustodyAddress = CustodyAddress.Trim();
        _settings.ExchangeAccount = ExchangeAccount.Trim();
        try
        {
            _settings.Save(WalletPaths.SettingsFile);
            Record($"设置已保存（节点 {_settings.NodeAddress}）");
        }
        catch (Exception ex)
        {
            Record($"⚠ 设置保存失败：{ex.Message}");
        }
    }

    private async Task CreateWalletAsync()
    {
        if (!CanUseSeed) { Record(MissingSeedStore.Message); return; }
        await Run(async () =>
        {
            var (seed, address) = WalletService.Create();
            await _seedStore.WriteAsync(seed);
            _seedHex = seed;
            Address = address;
            SeedNote = $"已生成并写入：{_seedStore.Describe}";
            Record($"✅ 新钱包：{address}");
            Record("⚠ 种子 = 全部财富。请立刻离线备份；丢失无人能找回。");
            await RefreshCoreAsync();
        });
    }

    private async Task ImportWalletAsync()
    {
        if (!CanUseSeed) { Record(MissingSeedStore.Message); return; }
        var seed = (ImportSeed ?? "").Trim();
        if (seed.Length == 0) { Record("请先粘贴种子（hex）再导入。"); return; }
        await Run(async () =>
        {
            var address = WalletService.AddressOf(seed);   // 先验证格式/长度，再落盘
            await _seedStore.WriteAsync(seed);
            _seedHex = seed;
            ImportSeed = "";                               // 导入后立刻从界面清掉
            Address = address;
            SeedNote = $"已导入到：{_seedStore.Describe}";
            Record($"✅ 已导入钱包：{address}");
            await RefreshCoreAsync();
        });
    }

    private async Task RefreshAsync() => await Run(async () =>
    {
        await EnsureWalletAsync();
        await RefreshCoreAsync();
    });

    private async Task TransferAsync() => await Run(async () =>
    {
        await EnsureWalletAsync();
        if (string.IsNullOrWhiteSpace(Destination)) { Record("请填写收款地址。"); return; }
        if (!decimal.TryParse((Amount ?? "").Trim(), out var coins) || coins <= 0) { Record("金额非法（应为正数）。"); return; }

        var hash = await _service.TransferAsync(NodeAddress, _seedHex!, Destination.Trim(), coins, Memo ?? "");
        Record($"✅ 已提交 {coins:0.########} → {Destination.Trim()}");
        Record($"   交易哈希 {hash}（出块异步完成，稍后刷新看余额）");
        await RefreshCoreAsync();
    });

    // —— 内部 ——

    private async Task EnsureWalletAsync()
    {
        if (_seedHex is not null) return;
        var seed = await _seedStore.ReadAsync();
        if (seed is null) { Record("还没有钱包：先「新建」或「导入种子」。"); return; }
        _seedHex = seed;
        Address = WalletService.AddressOf(seed);
        SeedNote = $"已解锁：{_seedStore.Describe}";
    }

    private async Task RefreshCoreAsync()
    {
        ChainText = await _service.DescribeChainAsync(NodeAddress);
        BalanceText = _seedHex is null
            ? "（未解锁——无法查询自己的余额）"
            : await _service.DescribeBalanceAsync(NodeAddress, WalletService.AddressOf(_seedHex));
        Record($"链：{ChainText.Replace('\n', ' ')}");
        Record($"余额：{BalanceText}");
    }

    /// <summary>统一异常边界：UI 上永远只看到一句人话，栈留控制台（别把节点内部错误糊到用户脸上）。</summary>
    private async Task Run(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (NotSupportedException ex)
        {
            CanUseSeed = false;
            SeedNote = ex.Message;
            Record($"⚠ {ex.Message}");
        }
        catch (Exception ex)
        {
            Record($"⚠ {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Record(string line)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss} {line}");
        while (Log.Count > 200) Log.RemoveAt(0);
    }
}
