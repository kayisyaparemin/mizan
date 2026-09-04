using Microsoft.Maui.Handlers;

namespace CoinFlow.App.Controls;

/// <summary>
/// Numerik klavyeyi (Keyboard="Numeric") korurken işaretli girişe izin verir;
/// böylece kullanıcı eksi işaretiyle negatif tutar yazabilir. Yalnızca
/// <c>controls:SignedNumericEntry.AllowNegative="True"</c> olan Entry'lere uygulanır.
/// </summary>
public static class SignedNumericEntry
{
    public static readonly BindableProperty AllowNegativeProperty =
        BindableProperty.CreateAttached(
            "AllowNegative",
            typeof(bool),
            typeof(SignedNumericEntry),
            false);

    public static bool GetAllowNegative(BindableObject view) =>
        (bool)view.GetValue(AllowNegativeProperty);

    public static void SetAllowNegative(BindableObject view, bool value) =>
        view.SetValue(AllowNegativeProperty, value);

    /// <summary>Uygulama başlangıcında bir kez çağrılır (MauiProgram).</summary>
    public static void Configure()
    {
        EntryHandler.Mapper.AppendToMapping(
            nameof(SignedNumericEntry),
            (handler, view) =>
            {
#if ANDROID
                if (view is Entry entry &&
                    GetAllowNegative(entry) &&
                    handler.PlatformView is Android.Widget.EditText editText)
                {
                    editText.InputType |=
                        Android.Text.InputTypes.NumberFlagSigned;
                }
#endif
            });
    }
}
