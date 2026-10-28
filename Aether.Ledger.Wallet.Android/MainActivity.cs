using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Aether.Ledger.Wallet.Android;

[Activity(
    Label = "Aether 钱包",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // 骨架阶段不注入种子存储：App 默认用 MissingSeedStore，界面会明确显示「未接入 Keychain/Keystore」。
    // 接 AndroidX.Security.Crypto 或 Keystore 时在这里设 App.CreateSeedStore。
}
