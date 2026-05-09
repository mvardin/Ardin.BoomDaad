using Microsoft.Extensions.Logging;

namespace Ardin.BoomDaad
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            #if ANDROID
                builder.Services.AddSingleton<Ardin.BoomDaad.Services.IAudioLevelService, Ardin.BoomDaad.Services.AndroidAudioLevelService>();
            #endif
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
