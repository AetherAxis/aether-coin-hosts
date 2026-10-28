namespace Aether.Ledger.Wallet.App;

/// <summary>
/// 钱包 UI 的本地数据目录（与 ⑮ 的 <c>~/.aether/aether-ai-app</c> 同一习惯）。
/// <para><c>AETHER_WALLET_DATA_DIR</c> 可覆盖——CI / 冒烟必须指到临时目录，别脏了用户真实的种子文件。</para>
/// </summary>
public static class WalletPaths
{
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("AETHER_WALLET_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
                    ? home
                    : Path.GetTempPath(),
                ".aether", "wallet-app");

    public static string SeedFile => Path.Combine(DataDir, "wallet.seed");

    public static string SettingsFile => Path.Combine(DataDir, "wallet-ui.json");
}
