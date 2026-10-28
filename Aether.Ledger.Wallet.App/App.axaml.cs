using Aether.Ledger.Wallet.App.ViewModels;
using Aether.Ledger.Wallet.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Aether.Ledger.Wallet.App;

/// <summary>
/// 钱包 UI 组合根（三端共用）：决定「用哪个种子存储」的钩子在这里，
/// 桌面 head 注入 <see cref="FileSeedStore"/>，移动 head 保持默认的 <see cref="MissingSeedStore"/>。
/// <para>为什么不做成 DI 容器：这里只有一个 View + 一个 VM，一个静态工厂钩子足够；
/// 真正的依赖注入等出现第二个页面再说（⑮ 是因为有 agent/工具/MCP 才上 DI）。</para>
/// </summary>
public partial class App : Application
{
    /// <summary>head 注入点：不设 = 移动端语义（明确报「种子存储未接入」，不落明文）。</summary>
    public static Func<ISeedStore>? CreateSeedStore { get; set; }

    public static Func<WalletSettings>? CreateSettings { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = CreateSettings?.Invoke() ?? new WalletSettings();
        var store = CreateSeedStore?.Invoke() ?? new MissingSeedStore("移动端");
        var vm = new MainViewModel(store, settings);
        var view = new MainView { DataContext = vm };

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window
                {
                    Title = "Aether 钱包",
                    Width = 820,
                    Height = 680,
                    Content = view,
                };
                break;
            case ISingleViewApplicationLifetime single:
                // Android / iOS：整屏就是这一个视图，没有窗口概念
                single.MainView = view;
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
