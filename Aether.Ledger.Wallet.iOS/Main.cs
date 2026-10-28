using UIKit;

namespace Aether.Ledger.Wallet.iOS;

public class Application
{
    // 骨架阶段不注入种子存储：App 默认 MissingSeedStore（界面明确提示，不写明文）。
    // 接 Keychain（SecRecord）时在这里设 App.CreateSeedStore。
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
