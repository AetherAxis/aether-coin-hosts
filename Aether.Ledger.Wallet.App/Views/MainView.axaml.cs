using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Aether.Ledger.Wallet.App.Views;

/// <summary>钱包主视图（三端共用一份 XAML）。DataContext = <see cref="ViewModels.MainViewModel"/>，由 App 注入。</summary>
public partial class MainView : UserControl
{
    public MainView() => AvaloniaXamlLoader.Load(this);
}
