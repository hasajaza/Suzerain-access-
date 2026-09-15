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
        // "Government Budget (min: -20, max: 30)" -> name and range. The game puts the range in the
        // tooltip title; it is spoken as a proper range instead of being read as part of the name.
        private static readonly System.Text.RegularExpressions.Regex RangeInName =
            new System.Text.RegularExpressions.Regex(@"\s*\((?:\s*min\s*:\s*(?<min>-?\d+))?\s*,?\s*(?:max\s*:\s*(?<max>-?\d+))?\s*\)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string StatName(TemplateHUDStat stat)
        {
            string name = TextUtil.FirstLine(SafeText(() => stat.statTooltipText.text));
            if (!string.IsNullOrEmpty(name)) return RangeInName.Replace(name, "").Trim();
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

        /// <summary>
        /// Minimum and maximum of a statistic. HUDStatProperties.MinValue / MaxValue hold the NAME of a
        /// Dialogue System variable (for example "BaseGame.Sordland_HUDStat_GovernmentBudget_Max"), not a
        /// number, so the variable is resolved; if that fails, the range in the tooltip title is used.
        /// </summary>
        private static bool TryRange(TemplateHUDStat stat, out int min, out int max, out bool hasMin, out bool hasMax)
        {
            min = max = 0; hasMin = hasMax = false;
            try
            {
                var props = stat.currentHUDStatData?.HUDStatProperties;
                if (props != null)
                {
                    if (props.HasMinValue && TryNumber(props.MinValue, out min)) hasMin = true;
                    if (props.HasMaxValue && TryNumber(props.MaxValue, out max)) hasMax = true;
                }
            }
            catch { }

            if (!hasMin || !hasMax)
            {
                try
                {
                    var m = RangeInName.Match(TextUtil.FirstLine(SafeText(() => stat.statTooltipText.text)));
                    if (m.Success)
                    {
                        if (!hasMin && m.Groups["min"].Success && int.TryParse(m.Groups["min"].Value, out int a)) { min = a; hasMin = true; }
                        if (!hasMax && m.Groups["max"].Success && int.TryParse(m.Groups["max"].Value, out int b)) { max = b; hasMax = true; }
                    }
                }
                catch { }
            }
            return hasMin || hasMax;
        }

        /// <summary>A plain number, or the value of the Dialogue System variable with that name.</summary>
        private static bool TryNumber(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (int.TryParse(text.Trim(), out value)) return true;
            try
            {
                var result = PixelCrushers.DialogueSystem.DialogueLua.GetVariable(text.Trim());
                if (result.isNumber) { value = result.asInt; return true; }
            }
            catch { }
            return false;
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

            if (ModConfig.DetailedNumbers.Value && TryRange(stat, out int min, out int max, out bool hasMin, out bool hasMax))
            {
                if (hasMin && hasMax) spoken += ", range " + TextUtil.Signed(min) + " to " + TextUtil.Signed(max);
                else if (hasMax) spoken += ", maximum " + TextUtil.Signed(max);
                else spoken += ", minimum " + TextUtil.Signed(min);
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

        public static string TextStatName(TemplateHUDTextStat stat)
        {
            string name = TextUtil.Clean(SafeText(() => stat.statNameText.text));
            return RangeInName.Replace(name, "").Trim();
        }

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

        /// <summary>The effect's data, from either of the two places the game keeps it.</summary>
        public static TokenStatusEffectData EffectData(TemplateTokenEffect effect)
        {
            try
            {
                var data = effect.GetTokenStatusEffectData();
                if (data != null) return data;
            }
            catch { }
            try { return effect.currentTokenStatusEffectData; } catch { return null; }
        }

        public static string EffectTitle(TemplateTokenEffect effect)
        {
            string shown = TextUtil.Clean(SafeText(() => effect.tooltipTitle.text));
            if (!string.IsNullOrEmpty(shown)) return shown;
            try { return TextUtil.Clean(EffectData(effect)?.TokenStatusEffectProperties?.Title); } catch { return ""; }
        }

        public static string EffectSubtitle(TemplateTokenEffect effect)
        {
            string shown = TextUtil.Clean(SafeText(() => effect.tooltipSubtitle.text));
            if (!string.IsNullOrEmpty(shown)) return shown;
            try { return TextUtil.Clean(EffectData(effect)?.TokenStatusEffectProperties?.Subtitle); } catch { return ""; }
        }

        /// <summary>
        /// Progress of an effect as a percentage, or -1 if it has none. Taken from the game data, and if that
        /// is unavailable from how far the progress bar is filled (Image.fillAmount), which is what a sighted
        /// player sees.
        /// </summary>
        public static int EffectPercent(TemplateTokenEffect effect)
        {
            try
            {
                var data = EffectData(effect);
                if (data != null)
                {
                    int percent = data.ProgressPercentage;
                    if (percent > 0) return percent;
                    var props = data.TokenStatusEffectProperties;
                    if (props != null && props.ProgressPercentage > 0) return props.ProgressPercentage;
                }
            }
            catch { }
            try
            {
                var fill = effect.backgroundProgressFill;
                if (fill != null && fill.enabled && fill.gameObject.activeInHierarchy)
                    return (int)Math.Round(Math.Max(0f, Math.Min(1f, fill.fillAmount)) * 100f);
            }
            catch { }
            return -1;
        }

        public static string SafeText(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
