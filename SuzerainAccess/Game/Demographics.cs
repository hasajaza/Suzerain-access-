using System;
using System.Collections.Generic;
using System.Globalization;
using SuzerainAccess.UI;

namespace SuzerainAccess.Game
{
    /// <summary>
    /// Ethnicity and religion are shown to sighted players only as pie charts (ChartUtil library),
    /// so their headings have no text next to them. This reads the numbers behind the charts.
    ///
    /// Verified members:
    ///  TokenInformationPanel.ethnicityChart / religionChart : ChartUtil.Chart
    ///  ChartUtil.Chart.chartData : ChartUtil.ChartData (series : List&lt;Series&gt;, categories : List&lt;string&gt;)
    ///  ChartUtil.Series (name, show, data : List&lt;Data&gt;), ChartUtil.Data (value, show)
    ///  MapTokenData.SupportsDemographics(), MapTokenData.DemographicsProperties
    ///     .EthnicityDatas / .ReligionDatas : lists of (Name, Value)
    ///
    /// Percentages are each group's share of the chart total, which is exactly what a pie chart shows.
    /// </summary>
    internal static class Demographics
    {
        /// <summary>Reads what a chart on screen displays. Returns empty if the chart has no data.</summary>
        public static string FromChart(ChartUtil.Chart chart)
        {
            var entries = new List<(string name, float value)>();
            try
            {
                if (!UiUtil.Alive(chart)) return string.Empty;
                var data = chart.chartData;
                if (!UiUtil.Alive(data) || data.series == null) return string.Empty;
                var categories = data.categories;
                int categoryCount = categories != null ? categories.Count : 0;

                for (int s = 0; s < data.series.Count; s++)
                {
                    var series = data.series[s];
                    if (series == null || !series.show || series.data == null) continue;
                    if (series.data.Count > 1 && categoryCount >= series.data.Count)
                    {
                        // One series, one slice per category.
                        for (int i = 0; i < series.data.Count; i++)
                        {
                            var d = series.data[i];
                            if (d == null || !d.show) continue;
                            entries.Add((TextUtil.Clean(categories[i]), d.value));
                        }
                    }
                    else
                    {
                        // One series per slice.
                        float sum = 0f;
                        for (int i = 0; i < series.data.Count; i++)
                            if (series.data[i] != null && series.data[i].show) sum += series.data[i].value;
                        entries.Add((TextUtil.Clean(series.name), sum));
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
            return Format(entries);
        }

        /// <summary>Reads the demographics straight from a location's data (used by the map browser).</summary>
        public static string FromTokenData(MapTokenData token, bool ethnicity)
        {
            var entries = new List<(string name, float value)>();
            try
            {
                if (token == null || !token.SupportsDemographics()) return string.Empty;
                var props = token.DemographicsProperties;
                if (props == null) return string.Empty;
                if (ethnicity)
                {
                    var list = props.EthnicityDatas;
                    for (int i = 0; list != null && i < list.Count; i++)
                        if (list[i] != null) entries.Add((TextUtil.CamelToWords(TextUtil.Clean(list[i].Name)), list[i].Value));
                }
                else
                {
                    var list = props.ReligionDatas;
                    for (int i = 0; list != null && i < list.Count; i++)
                        if (list[i] != null) entries.Add((TextUtil.CamelToWords(TextUtil.Clean(list[i].Name)), list[i].Value));
                }
            }
            catch
            {
                return string.Empty;
            }
            return Format(entries);
        }

        private static string Format(List<(string name, float value)> entries)
        {
            float total = 0f;
            foreach (var e in entries) if (e.value > 0f) total += e.value;
            if (total <= 0f) return string.Empty;
            entries.Sort((a, b) => b.value.CompareTo(a.value));
            var parts = new List<string>();
            foreach (var e in entries)
            {
                if (e.value <= 0f || string.IsNullOrEmpty(e.name)) continue;
                double percent = e.value / total * 100.0;
                string p = percent < 1.0 ? "less than 1 percent" : Math.Round(percent).ToString(CultureInfo.InvariantCulture) + " percent";
                parts.Add(e.name + " " + p);
            }
            return TextUtil.JoinWith(", ", parts);
        }
    }
}
