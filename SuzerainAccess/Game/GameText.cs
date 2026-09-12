using System;
using SuzerainAccess.Config;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Game
{
    /// <summary>
    /// Turns Suzerain's statistic widgets into words. Verified members:
    ///  TemplateHUDStat: statText, statTooltipText, statTooltipModifierText, currentValue (int), currentHUDStatData
    ///  HUDStatData.HUDStatProperties: Title, HasMinValue/MinValue, HasMaxValue/MaxValue
    ///  TemplateHUDTextStat: statNameText, statValueText, red/yellow/green/black (Color)
    /// </summary>
    internal static class GameText
    {
        public static string StatName(TemplateHUDStat stat)
        {
            string name = TextUtil.FirstLine(SafeText(() => stat.statTooltipText.text));
            if (!string.IsNullOrEmpty(name)) return name;
            try
            {
                var data = stat.currentHUDStatData;
                if (data != null)
                {
                    var props = data.HUDStatProperties;
                    if (props != null && !string.IsNullOrWhiteSpace(props.Title)) return TextUtil.Clean(props.Title);
                    if (!string.IsNullOrWhiteSpace(data.NameInDatabase)) return TextUtil.CamelToWords(data.NameInDatabase);
                }
            }
            catch { }
            return "Statistic";
        }

        public static string StatValue(TemplateHUDStat stat)
        {
            int value;
            try { value = stat.currentValue; }
            catch { return TextUtil.Clean(SafeText(() => stat.statText.text)); }

            string shown = TextUtil.Clean(SafeText(() => stat.statText.text));
            string spoken = TextUtil.Signed(value);
            // If the widget shows something other than the plain number (for example "5/10"), say that too.
            string plain = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(shown) && shown != plain && shown != "+" + plain && shown != "-" + plain.TrimStart('-'))
                spoken = TextUtil.SpeakFraction(shown);

            if (ModConfig.DetailedNumbers.Value)
            {
                try
                {
                    var props = stat.currentHUDStatData?.HUDStatProperties;
                    if (props != null && props.HasMaxValue && !string.IsNullOrWhiteSpace(props.MaxValue))
                        spoken += ", maximum " + TextUtil.Clean(props.MaxValue);
                }
                catch { }
            }
            return spoken;
        }

        public static string StatModifiers(TemplateHUDStat stat)
        {
            return TextUtil.Clean(SafeText(() => stat.statTooltipModifierText.text));
        }

        public static string StatTooltip(TemplateHUDStat stat)
        {
            return TextUtil.Clean(SafeText(() => stat.statTooltipText.text));
        }

        public static string TextStatName(TemplateHUDTextStat stat) => TextUtil.Clean(SafeText(() => stat.statNameText.text));

        public static string TextStatValue(TemplateHUDTextStat stat)
        {
            string value = TextUtil.Clean(SafeText(() => stat.statValueText.text));
            if (!ModConfig.DescribeColors.Value) return value;
            string color = null;
            try
            {
                Color c = stat.statValueText.color;
                if (Near(c, stat.green)) color = "green";
                else if (Near(c, stat.yellow)) color = "yellow";
                else if (Near(c, stat.red)) color = "red";
            }
            catch { }
            return color == null ? value : value + ", shown in " + color;
        }

        private static bool Near(Color a, Color b) =>
            Math.Abs(a.r - b.r) < 0.03f && Math.Abs(a.g - b.g) < 0.03f && Math.Abs(a.b - b.b) < 0.03f;

        public static string SafeText(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
