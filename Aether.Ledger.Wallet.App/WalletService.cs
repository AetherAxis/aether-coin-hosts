using Aether.Ledger;

namespace Aether.Ledger.Wallet.App;

/// <summary>
/// 钱包 UI 对 ① <c>Aether.Ledger</c> 的唯一调用面：查链、查余额、签名转账。
/// <para><b>为什么单独一层</b>：VM 只认人话（「余额」「提交」），链的细节（endpoint 解析、atom 换算、
/// nonce、签名）留在这里——这样 CLI（<c>WalletCli</c>）和 UI 走的是同一套语义，不会长出第二套规矩。</para>
/// <para>刻意不做的事：不碰交易所 RPC（提现是可选能力，之后单独接），不落盘、不缓存余额。</para>
/// </summary>
public sealed class WalletService
{
    /// <summary>链概况（高度 / chainId / 固定供给）。</summary>
    public async Task<string> DescribeChainAsync(string nodeAddress, CancellationToken ct = default)
    {
        var (host, port) = ParseEndpoint(nodeAddress);
        using var client = new LedgerClient(host, port);
        var info = await client.GetInfoAsync(ct);
        return $"高度 {info.Height} · chainId {info.ChainId} · 创世 {info.GenesisHash[..12]}…\n"
             + $"币总供给 {LedgerSupply.Format(info.TotalSupplyAtoms)}（固定，不可增发）";
    }

    /// <summary>某地址的余额（币 / 计价币 / nonce）。</summary>
    public async Task<string> DescribeBalanceAsync(string nodeAddress, string address, CancellationToken ct = default)
    {
        var (host, port) = ParseEndpoint(nodeAddress);
        using var client = new LedgerClient(host, port);
        var account = await client.GetBalanceAsync(LedgerAddress.Parse(address).Hex, ct);
        return $"币 {LedgerSupply.Format(account.BalanceAtoms)} · 计价币 {LedgerSupply.Format(account.QuoteBalanceAtoms)}"
             + $"（nonce {account.Nonce}）";
    }

    /// <summary>
    /// 签名并提交一笔转账。链上转账是**客户端签名**：节点只验签、出块，不托管私钥——
    /// 所以这个方法要在调用方持有种子的进程里跑（CLI 与 UI 都是这个模型）。
    /// </summary>
    /// <returns>交易哈希（出块异步完成）。</returns>
    public async Task<string> TransferAsync(
        string nodeAddress, string seedHex, string to, decimal coins, string memo, CancellationToken ct = default)
    {
        if (coins <= 0) throw new ArgumentException("金额必须大于 0", nameof(coins));

        var wallet = new LedgerWallet(Convert.FromHexString(seedHex.Trim()));
        var destination = LedgerAddress.Parse(to);
        var atoms = CoinsToAtoms(coins);

        var (host, port) = ParseEndpoint(nodeAddress);
        using var client = new LedgerClient(host, port);
        var info = await client.GetInfoAsync(ct);
        var account = await client.GetBalanceAsync(wallet.Address.Hex, ct);
        var tx = LedgerTransaction.Sign(
            info.ChainId, wallet, destination, atoms, account.Nonce + 1, memo ?? "", LedgerSupply.Assets.Coin);
        await client.SubmitTxAsync(tx, ct);
        return tx.Hash;
    }

    /// <summary>生成新钱包（种子由调用方负责存——这里只产生、不落盘）。</summary>
    public static (string SeedHex, string Address) Create()
    {
        var wallet = LedgerWallet.Create();
        return (Convert.ToHexString(wallet.Seed).ToLowerInvariant(), wallet.Address.Hex);
    }

    /// <summary>由种子推出地址（导入已有钱包后要显示它）。</summary>
    public static string AddressOf(string seedHex)
        => new LedgerWallet(Convert.FromHexString(seedHex.Trim())).Address.Hex;

    /// <summary>币数 → 原子数（与 CLI 同一口径：8 位小数、四舍五入）。</summary>
    public static ulong CoinsToAtoms(decimal coins)
    {
        var atoms = Math.Round(coins * (decimal)LedgerSupply.AtomsPerCoin);
        if (atoms <= 0 || atoms > ulong.MaxValue) throw new ArgumentException($"金额超出范围：{coins}");
        return (ulong)atoms;
    }

    public static (string Host, int Port) ParseEndpoint(string address)
    {
        var text = (address ?? "").Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var port))
            return (text[..colon], port);
        if (int.TryParse(text, out var portOnly))
            return ("127.0.0.1", portOnly);
        throw new FormatException($"节点地址非法：\"{address}\"（应为 host:port，如 127.0.0.1:17805）");
    }
}
