using System.Text.Json;

namespace Aether.Ledger.Wallet.App;

/// <summary>
/// 钱包 UI 的少量设置（节点地址 / 托管地址 / 交易所账户）。这是**连接参数**，不是密钥——
/// 所以放普通 JSON 文件；种子永远走 <see cref="ISeedStore"/>，两者不混。
/// </summary>
public sealed class WalletSettings
{
    /// <summary>链节点 <c>host:port</c>（默认本机——服务器形态见 ⑯ 的 runbook：远端走 SSH 隧道）。</summary>
    public string NodeAddress { get; set; } = "127.0.0.1:17805";

    /// <summary>交易所托管地址（充值时要填；留空表示不充值）。</summary>
    public string CustodyAddress { get; set; } = "";

    /// <summary>交易所账户标识（充值 memo = 它）。</summary>
    public string ExchangeAccount { get; set; } = "";

    public static WalletSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<WalletSettings>(File.ReadAllText(path)) ?? new WalletSettings();
        }
        catch
        {
            // 设置文件坏了不该挡住开钱包——退回默认值（种子不在这个文件里，丢不了钱）
        }
        return new WalletSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
