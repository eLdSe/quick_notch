using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace QuickNotchCs;

public sealed record NotificationSoundOption(string Key, string NameRu, string NameEn, string Description, string FilePath);

/// <summary>
/// Сервис воспроизведения звуков уведомлений QuickNotch.
/// Выполняется 100% асинхронно в фоновом пуле потоков (Task.Run),
/// не блокируя UI-поток (0% idle CPU, Addy Osmani standards).
/// </summary>
public static class NotificationSoundService
{
    private static readonly string WinMedia = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Media\";

    public static readonly NotificationSoundOption[] Sounds =
    [
        new("messaging", "Деликатный колокольчик", "Gentle Chime", "Windows Notify Messaging (по умолчанию)", WinMedia + "Windows Notify Messaging.wav"),
        new("balloon", "Мягкий хлоп (Balloon)", "Soft Pop (Balloon)", "Windows Balloon", WinMedia + "Windows Balloon.wav"),
        new("infobar", "Стеклянный аккорд", "Glass Chord", "Windows Information Bar", WinMedia + "Windows Information Bar.wav"),
        new("standard", "Классический звонок", "Classic Notify", "Windows Notify", WinMedia + "Windows Notify.wav"),
        new("none", "Без звука", "Silent", "Беззвучный режим уведомлений", ""),
    ];

    public static void Play(string? soundKey = null)
    {
        string key = string.IsNullOrWhiteSpace(soundKey) ? "messaging" : soundKey.Trim();
        if (string.Equals(key, "none", StringComparison.OrdinalIgnoreCase)) return;

        NotificationSoundOption? opt = Array.Find(Sounds, s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
                                      ?? Sounds[0];

        if (string.IsNullOrEmpty(opt.FilePath) || !File.Exists(opt.FilePath)) return;

        Task.Run(() =>
        {
            try
            {
                using var player = new SoundPlayer(opt.FilePath);
                player.PlaySync();
            }
            catch
            {
                // Игнорируем ошибки отсутствия аудиодрайвера или занятого устройства вывода
            }
        });
    }

    public static void Preview(string soundKey) => Play(soundKey);
}
