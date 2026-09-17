using Mizan.App.Pages;

namespace Mizan.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    // Uygulama her soğuk açılışta profil sorar. Arka plandan dönüşte kök
    // sayfa değişmediği için kaldığın yerden devam edersin.
    //
    // Sayfa yapıcı parametresi olarak alınmıyor: XAML'i StaticResource
    // okuduğu için InitializeComponent uygulama kaynaklarını yükledikten
    // sonra oluşturulmalı.
    public App(IServiceProvider services)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        MainPage = services.GetRequiredService<ProfileSelectionPage>();
    }
}
