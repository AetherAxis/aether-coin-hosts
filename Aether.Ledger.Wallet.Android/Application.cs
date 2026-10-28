using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Aether.Ledger.Wallet.Android;

[Application]
public class Application : AvaloniaAndroidApplication<Aether.Ledger.Wallet.App.App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder).WithInterFont();
}
