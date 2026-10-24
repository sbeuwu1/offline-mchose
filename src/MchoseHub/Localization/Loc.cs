using System;
using System.Collections.Generic;
using System.Globalization;

namespace MchoseHub.Localization
{
    /// <summary>
    /// In-code localization table. The UI picks Russian when the system UI culture is
    /// Russian (or when started with --lang ru), English otherwise.
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
        {
            ["Mw_Subtitle"] = "Device Settings & Diagnostics",
            ["Mw_TesterButton"] = "Hardware Tester [F8]",
            ["Mw_TesterTooltip"] = "Open Fullscreen Hardware Tester (F8)",
            ["Status_Disconnected"] = "Disconnected",
            ["Btn_Connect"] = "Connect Mouse",
            ["Btn_Disconnect"] = "Disconnect",
            ["Status_DeviceUnplugged"] = "Device unplugged",
            ["Status_Reconnecting"] = "Reconnecting...",
            ["Status_NotResponding"] = "Mouse not responding",
            ["Hero_Battery"] = "BATTERY",
            ["Hero_UpToHz"] = "•  Up to 8,000 Hz",
            ["Hero_UpToDpi"] = "•  Up to 42,000 DPI",
            ["Mode_Title"] = "Performance Mode",
            ["Mode_Subtitle"] = "Select sensor power curve and responsiveness",
            ["Mode_Performance"] = "Performance",
            ["Mode_PerformanceSub"] = "Eco / Balanced",
            ["Mode_Gaming"] = "Gaming Mode",
            ["Mode_GamingSub"] = "Recommended",
            ["Mode_Extreme"] = "Extreme Mode",
            ["Mode_ExtremeSub"] = "Max Response",
            ["Mode_Desc_Gaming"] = "Gaming Mode: Faster response, optimal for fast-paced gaming.",
            ["Mode_Desc_Performance"] = "Eco / Balanced: Extends battery life for productivity.",
            ["Mode_Desc_Extreme"] = "Extreme Mode: Maximum responsiveness, tightest frame timing.",
            ["Rate_Title"] = "Polling Rate",
            ["Rate_Subtitle"] = "Set mouse hardware reporting rate",
            ["Rate_BadgeDefault"] = "2000 Hz",
            ["Unit_Hz"] = "Hz",
            ["Rate_ValueFmt"] = "{0} Hz",
            ["Power_Title"] = "Debounce & Power Management",
            ["Power_Subtitle"] = "Switch debounce timing and wireless auto-sleep behavior",
            ["Power_Debounce"] = "Key Debounce (ms)",
            ["Power_DebounceSub"] = "Adjust click response debounce time",
            ["Power_DebounceDefault"] = "8ms",
            ["Power_DebounceValueFmt"] = "{0}ms",
            ["Power_Sleep"] = "Sleep Timer",
            ["Power_SleepSub"] = "Auto-sleep on 2.4G wireless",
            ["Power_SleepDefault"] = "3m",
            ["Power_SleepValueFmt"] = "{0}m",
            ["Power_Never"] = "Never",
            ["Dpi_Title"] = "DPI Stages & Sensitivity",
            ["Dpi_Subtitle"] = "Configure 1–6 stages (50 to 42,000 DPI)",
            ["Dpi_Levels"] = "Levels:",
            ["Dpi_StageDefault"] = "Stage 1: 800 DPI",
            ["Dpi_StageFmt"] = "Stage {0}: {1} DPI",
            ["Dpi_StageName"] = "Stage {0}",
            ["Dpi_Save"] = "Save DPI to Mouse",
            ["Sensor_Title"] = "Sensor & Motion Tuning",
            ["Sensor_Subtitle"] = "LOD, Motion Sync, Ripple Control, and Angle Snapping",
            ["Sensor_Lod"] = "Lift-Off Distance (LOD)",
            ["Sensor_LodSub"] = "Sensor tracking cutoff height",
            ["Sensor_Lod07"] = "0.7 mm",
            ["Sensor_Lod10"] = "1.0 mm",
            ["Sensor_Lod20"] = "2.0 mm",
            ["Sensor_MotionSync"] = "Motion Sync",
            ["Sensor_MotionSyncSub"] = "Synchronizes sensor frames with USB polling intervals",
            ["Sensor_Ripple"] = "Ripple Control",
            ["Sensor_RippleSub"] = "High-speed jitter smoothing algorithm",
            ["Sensor_Angle"] = "Angle Snapping",
            ["Sensor_AngleSub"] = "Linear horizontal and vertical path correction",
            ["Sensor_Rotate"] = "Rotate (Sensor Angle)",
            ["Sensor_RotateSub"] = "Compensates for tilted mouse grip angle (-30° to +30°)",
            ["Sensor_RotateDefault"] = "0°",
            ["Sensor_RotateFmt"] = "{0}°",
            ["Sensor_RotateReset"] = "0° Reset",
            ["Log_Title"] = "Hardware Packet Monitor",
            ["Log_Subtitle"] = "Direct USB HID Feature and Input Report capture",
            ["Log_Clear"] = "Clear Log",
            ["Tester_Title"] = "Offline Mchose • Hardware Tester",
            ["Tester_Header"] = "• Hardware Tester",
            ["Tester_Hint"] = "Move mouse to test polling rate • Click buttons to test switch interval • Press [ESC] to exit",
            ["Tester_Reset"] = "Reset [R]",
            ["Tester_Exit"] = "Exit [ESC]",
            ["Tester_MoveToTest"] = "MOVE MOUSE TO TEST",
            ["Tester_CurrentRate"] = "Current Polling Rate",
            ["Tester_Peak"] = "PEAK HZ",
            ["Tester_Average"] = "AVERAGE HZ",
            ["Tester_Status"] = "STATUS",
            ["Tester_Idle"] = "IDLE",
            ["Tester_SwitchTitle"] = "Switch Latency & Chatter",
            ["Tester_SwitchSubtitle"] = "Click interval and double-click detector",
            ["Tester_ResetClicks"] = "Reset Clicks",
            ["Tester_Left"] = "LEFT",
            ["Tester_Mid"] = "MID",
            ["Tester_Right"] = "RIGHT",
            ["Tester_Side1"] = "SIDE 1",
            ["Tester_Side2"] = "SIDE 2",
            ["Tester_Clicks"] = "CLICKS",
            ["Tester_Interval"] = "INTERVAL",
            ["Tester_DoubleClicks"] = "DOUBLE CLICKS",
            ["Tester_Ready"] = "Ready to test clicks",
            ["Unit_ZeroHz"] = "0 Hz",
            ["Tester_IntervalEmpty"] = "-- ms",
            ["Tester_IntervalValueFmt"] = "{0:F1} ms",
            ["Tester_RateValueFmt"] = "{0} Hz",
            ["Tester_Tier10K"] = "10000 Hz (10K)",
            ["Tester_Tier8K"] = "8000 Hz (8K)",
            ["Tester_Tier4K"] = "4000 Hz (4K)",
            ["Tester_Tier2K"] = "2000 Hz (2K)",
            ["Tester_Tier1K"] = "1000 Hz (1K)",
            ["Tester_Status10K"] = "10K ULTRA",
            ["Tester_Status8K"] = "8K ULTRA",
            ["Tester_Status4K"] = "4K",
            ["Tester_Status2K"] = "2K",
            ["Tester_Status1K"] = "1K",
            ["Tester_StatusActive"] = "ACTIVE",
            ["Tester_DoubleClickFmt"] = "⚠ Double-click detected ({0} • {1:F1}ms)",
            ["Tester_ClickFmt"] = "Click ({0} • {1:F1}ms)",
            ["Tester_ClickNoIntervalFmt"] = "Click ({0})",
            ["Tester_Btn_Left"] = "Left",
            ["Tester_Btn_Middle"] = "Middle",
            ["Tester_Btn_Right"] = "Right",
            ["Tester_Btn_Side1"] = "Side1",
            ["Tester_Btn_Side2"] = "Side2",
        };

        private static readonly Dictionary<string, string> Russian = new(StringComparer.Ordinal)
        {
            ["Mw_Subtitle"] = "Настройки устройства и диагностика",
            ["Mw_TesterButton"] = "Тест оборудования [F8]",
            ["Mw_TesterTooltip"] = "Открыть полноэкранный тест оборудования (F8)",
            ["Status_Disconnected"] = "Отключено",
            ["Btn_Connect"] = "Подключить мышь",
            ["Btn_Disconnect"] = "Отключить",
            ["Status_DeviceUnplugged"] = "Устройство отключено",
            ["Status_Reconnecting"] = "Переподключение…",
            ["Status_NotResponding"] = "Мышь не отвечает",
            ["Hero_Battery"] = "БАТАРЕЯ",
            ["Hero_UpToHz"] = "•  До 8 000 Гц",
            ["Hero_UpToDpi"] = "•  До 42 000 DPI",
            ["Mode_Title"] = "Режим производительности",
            ["Mode_Subtitle"] = "Кривая энергопотребления сенсора и отклик",
            ["Mode_Performance"] = "Экономный",
            ["Mode_PerformanceSub"] = "Эко / Баланс",
            ["Mode_Gaming"] = "Игровой",
            ["Mode_GamingSub"] = "Рекомендуется",
            ["Mode_Extreme"] = "Экстремальный",
            ["Mode_ExtremeSub"] = "Макс. отклик",
            ["Mode_Desc_Gaming"] = "Игровой режим: быстрый отклик, оптимален для динамичных игр.",
            ["Mode_Desc_Performance"] = "Эко / Баланс: продлевает работу от батареи для повседневных задач.",
            ["Mode_Desc_Extreme"] = "Экстремальный режим: максимальный отклик и минимальная задержка кадра.",
            ["Rate_Title"] = "Частота опроса",
            ["Rate_Subtitle"] = "Частота передачи данных мыши по USB",
            ["Rate_BadgeDefault"] = "2000 Гц",
            ["Unit_Hz"] = "Гц",
            ["Rate_ValueFmt"] = "{0} Гц",
            ["Power_Title"] = "Дребезг и энергосбережение",
            ["Power_Subtitle"] = "Время антидребезга и автосон в режиме 2.4G",
            ["Power_Debounce"] = "Антидребезг (мс)",
            ["Power_DebounceSub"] = "Задержка срабатывания клика",
            ["Power_DebounceDefault"] = "8 мс",
            ["Power_DebounceValueFmt"] = "{0} мс",
            ["Power_Sleep"] = "Таймер сна",
            ["Power_SleepSub"] = "Автосон при работе по 2.4G",
            ["Power_SleepDefault"] = "3 мин",
            ["Power_SleepValueFmt"] = "{0} мин",
            ["Power_Never"] = "Никогда",
            ["Dpi_Title"] = "Уровни DPI и чувствительность",
            ["Dpi_Subtitle"] = "Настройте 1–6 уровней (от 50 до 42 000 DPI)",
            ["Dpi_Levels"] = "Уровни:",
            ["Dpi_StageDefault"] = "Уровень 1: 800 DPI",
            ["Dpi_StageFmt"] = "Уровень {0}: {1} DPI",
            ["Dpi_StageName"] = "Уровень {0}",
            ["Dpi_Save"] = "Сохранить DPI в мышь",
            ["Sensor_Title"] = "Настройка сенсора",
            ["Sensor_Subtitle"] = "LOD, Motion Sync, Ripple Control и Angle Snapping",
            ["Sensor_Lod"] = "Высота отрыва (LOD)",
            ["Sensor_LodSub"] = "Высота, на которой сенсор перестаёт отслеживать",
            ["Sensor_Lod07"] = "0.7 мм",
            ["Sensor_Lod10"] = "1.0 мм",
            ["Sensor_Lod20"] = "2.0 мм",
            ["Sensor_MotionSync"] = "Motion Sync",
            ["Sensor_MotionSyncSub"] = "Синхронизирует кадры сенсора с опросом USB",
            ["Sensor_Ripple"] = "Ripple Control",
            ["Sensor_RippleSub"] = "Сглаживание дрожания на высокой скорости",
            ["Sensor_Angle"] = "Angle Snapping",
            ["Sensor_AngleSub"] = "Линейное выравнивание по горизонтали и вертикали",
            ["Sensor_Rotate"] = "Поворот (угол сенсора)",
            ["Sensor_RotateSub"] = "Компенсация наклона мыши в руке (от -30° до +30°)",
            ["Sensor_RotateDefault"] = "0°",
            ["Sensor_RotateFmt"] = "{0}°",
            ["Sensor_RotateReset"] = "Сброс 0°",
            ["Log_Title"] = "Монитор пакетов",
            ["Log_Subtitle"] = "Прямой перехват HID Feature и Input отчётов USB",
            ["Log_Clear"] = "Очистить лог",
            ["Tester_Title"] = "Offline Mchose • Тест оборудования",
            ["Tester_Header"] = "• Тест оборудования",
            ["Tester_Hint"] = "Двигайте мышью для замера частоты • Нажимайте кнопки для замера интервала • [ESC] — выход",
            ["Tester_Reset"] = "Сброс [R]",
            ["Tester_Exit"] = "Выход [ESC]",
            ["Tester_MoveToTest"] = "ДВИГАЙТЕ МЫШЬЮ",
            ["Tester_CurrentRate"] = "Текущая частота опроса",
            ["Tester_Peak"] = "ПИК, ГЦ",
            ["Tester_Average"] = "СРЕДНЯЯ, ГЦ",
            ["Tester_Status"] = "СТАТУС",
            ["Tester_Idle"] = "ПРОСТОЙ",
            ["Tester_SwitchTitle"] = "Задержка и дребезг кнопок",
            ["Tester_SwitchSubtitle"] = "Интервал кликов и детектор двойных нажатий",
            ["Tester_ResetClicks"] = "Сбросить клики",
            ["Tester_Left"] = "ЛЕВАЯ",
            ["Tester_Mid"] = "СРЕДНЯЯ",
            ["Tester_Right"] = "ПРАВАЯ",
            ["Tester_Side1"] = "БОК 1",
            ["Tester_Side2"] = "БОК 2",
            ["Tester_Clicks"] = "КЛИКИ",
            ["Tester_Interval"] = "ИНТЕРВАЛ",
            ["Tester_DoubleClicks"] = "ДВОЙНЫЕ",
            ["Tester_Ready"] = "Готово к проверке кнопок",
            ["Unit_ZeroHz"] = "0 Гц",
            ["Tester_IntervalEmpty"] = "-- мс",
            ["Tester_IntervalValueFmt"] = "{0:F1} мс",
            ["Tester_RateValueFmt"] = "{0} Гц",
            ["Tester_Tier10K"] = "10000 Гц (10K)",
            ["Tester_Tier8K"] = "8000 Гц (8K)",
            ["Tester_Tier4K"] = "4000 Гц (4K)",
            ["Tester_Tier2K"] = "2000 Гц (2K)",
            ["Tester_Tier1K"] = "1000 Гц (1K)",
            ["Tester_Status10K"] = "10K ULTRA",
            ["Tester_Status8K"] = "8K ULTRA",
            ["Tester_Status4K"] = "4K",
            ["Tester_Status2K"] = "2K",
            ["Tester_Status1K"] = "1K",
            ["Tester_StatusActive"] = "АКТИВНА",
            ["Tester_DoubleClickFmt"] = "⚠ Двойной клик ({0} • {1:F1} мс)",
            ["Tester_ClickFmt"] = "Клик ({0} • {1:F1} мс)",
            ["Tester_ClickNoIntervalFmt"] = "Клик ({0})",
            ["Tester_Btn_Left"] = "левая",
            ["Tester_Btn_Middle"] = "средняя",
            ["Tester_Btn_Right"] = "правая",
            ["Tester_Btn_Side1"] = "бок 1",
            ["Tester_Btn_Side2"] = "бок 2",
        };

        public static string Language { get; private set; } = "en";

        private static CultureInfo _formatCulture = CultureInfo.InvariantCulture;

        /// <summary>Picks the UI language from --lang or the system UI culture.</summary>
        public static void Initialize(string[]? args = null)
        {
            string? requested = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                    {
                        requested = args[i].Substring("--lang=".Length);
                    }
                    else if (string.Equals(args[i], "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        requested = args[++i];
                    }
                }
            }

            UseLanguage(requested ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }

        /// <summary>Forces a language ("ru" or anything else for English).</summary>
        public static void UseLanguage(string? code)
        {
            bool russian = (code ?? string.Empty).Trim().StartsWith("ru", StringComparison.OrdinalIgnoreCase);
            Language = russian ? "ru" : "en";
            _formatCulture = russian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture;
        }

        public static string T(string key)
        {
            if (Language == "ru" && Russian.TryGetValue(key, out string? ru))
            {
                return ru;
            }

            return English.TryGetValue(key, out string? en) ? en : key;
        }

        public static string F(string key, params object[] args)
        {
            string format = T(key);
            try
            {
                return string.Format(_formatCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
