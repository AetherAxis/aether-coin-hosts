namespace Aether.Ledger.Wallet.App;

/// <summary>
/// 种子存储端口。<b>种子 = 全部财富</b>——用什么介质存，决定这个 UI 能不能上线。
/// <para>桌面 head 现在用 <see cref="FileSeedStore"/>（明文 hex + 0600）；移动 head 必须换 Keychain / Keystore
/// （<see cref="MissingSeedStore"/> 是占位：明确报「未接入」，绝不静默退化成明文文件）。</para>
/// </summary>
public interface ISeedStore
{
    /// <summary>是否已有一份可读的种子。</summary>
    bool HasSeed { get; }

    /// <summary>人读的存储形态（UI 上要显式告诉用户种子存在哪、有多安全）。</summary>
    string Describe { get; }

    /// <summary>读出种子（hex）。没有种子时返回 null。</summary>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>写入种子（hex）。实现必须自己保证介质安全等级。</summary>
    Task WriteAsync(string seedHex, CancellationToken ct = default);
}

/// <summary>
/// 文件种子存储（桌面形态）：<c>&lt;dataDir&gt;/wallet.seed</c>，Unix 下强制 0600。
/// <para>⚠ 这是**运维级**保管（等同 CLI 的 <c>--out</c>）：明文 hex，机器被人拿到就等于钱包被人拿到。
/// 桌面要再上一个台阶就换 DPAPI / 系统钥匙串——那是独立一批，不在本骨架里假装做了。</para>
/// </summary>
public sealed class FileSeedStore(string path) : ISeedStore
{
    public string Path { get; } = path;

    public bool HasSeed => File.Exists(Path);

    public string Describe => $"明文文件：{Path}（权限 0600；仅限可信托管环境）";

    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Path)) return null;
        var text = (await File.ReadAllTextAsync(Path, ct)).Trim();
        return text.Length == 0 ? null : text;
    }

    public async Task WriteAsync(string seedHex, CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        await File.WriteAllTextAsync(Path, seedHex.Trim() + Environment.NewLine, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

/// <summary>
/// 移动端占位实现：**明确失败**，不退化。
/// <para>iPhone / Android 的种子必须进 Keychain / Keystore（iOS <c>SecRecord</c>、Android <c>AndroidX.Security.Crypto</c>），
/// 这件事与「UI 能跑」是两件事——先让 UI 起来，再把这块补上，别让 App 带着明文种子出门。</para>
/// </summary>
public sealed class MissingSeedStore(string platform) : ISeedStore
{
    public const string Message =
        "本平台还没有接种子安全存储（Keychain / Keystore）——种子 = 全部财富，暂不允许在此创建或导入。"
        + " 桌面 head 可正常使用；移动端待第一批安全存储落地。";

    public bool HasSeed => false;

    public string Describe => $"{platform}：种子安全存储未接入（占位）";

    public Task<string?> ReadAsync(CancellationToken ct = default) => throw new NotSupportedException(Message);

    public Task WriteAsync(string seedHex, CancellationToken ct = default) => throw new NotSupportedException(Message);
}
