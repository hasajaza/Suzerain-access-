using System;
using System.Collections.Generic;
using System.Globalization;
using SuzerainAccess.Config;
using SuzerainAccess.Core;
using SuzerainAccess.Game;
using SuzerainAccess.Speech;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Keyboard access to the map. Sighted players click map "tokens" (cities, countries, locations,
    /// events). This browser lists the same tokens from the game's data and selects them through the
    /// game's own TokenManager, which opens the location panel exactly as a click would.
    ///
    /// Verified members:
    ///  Managers.Instance.TokenManager: GetOrderedTokenData() : List&lt;MapTokenData&gt;,
    ///     GetTokenByTokenDataNameInDatabase(string) : Token, SelectToken(Token), HoverOverToken(Token),
    ///     StopHoverOverToken(Token), isTokenSelectionEnabled
    ///  MapTokenData: Title, Subtitle, Type (Location/City/Country/Event), IsTokenEnabled, NameInDatabase,
    ///     IsCountry(), SupportsCountryDetails(), SupportsPopulationEconomy(), SupportsDescription(),
    ///     RelationsType, Capital, President, Language, Government, MilitaryStrength, GDP, Population,
    ///     GDPWorldRank, Description, StatusEffects, HasNewStatusEffect
    ///  Managers.Instance.MapManager.GetCurrentMapType() (Base / World / War)
    ///
    /// Directions are computed from where the game's camera draws each token on screen, so "up and left"
    /// means the same thing a sighted player sees. The war map of the Rizia content uses a different
    /// tile system (WarTileManager) and is not covered by this browser; see docs/TECHNICAL_NOTES.md.
    /// </summary>
    internal sealed class MapAccessibility
    {
        private enum Filter { All, WaitingForYou, Countries, Cities, Locations, EventLocations }

        private readonly SpeechManager _speech;
        private List<MapTokenData> _tokens = new List<MapTokenData>();
        private int _index = -1;
        private Filter _filter = Filter.All;
        private Token _hovered;
        private MapTokenData _capital;

        public bool IsActive { get; private set; }

        public MapAccessibility(SpeechManager speech)
        {
            _speech = speech;
        }

        private static TokenManager GetTokenManager()
        {
            try
            {
                var m = Managers.Instance;
                if (!UiUtil.Alive(m)) return null;
                var tm = m.TokenManager;
                return UiUtil.Alive(tm) ? tm : null;
            }
            catch { return null; }
        }

        public void Toggle()
        {
            if (IsActive) { Close("Map browser closed."); return; }

            var tm = GetTokenManager();
            if (tm == null) { _speech.Say("The map is not available here."); return; }
            try
            {
                var map = Managers.Instance.MapManager;
                if (UiUtil.Alive(map) && map.GetCurrentMapType() == MapConfiguration.MapType.War)
                {
                    _speech.Say("The war map is not supported by the map browser. Use Tab and Control Tab to reach the war panels.");
                    return;
                }
            }
            catch { }

            _filter = Filter.All; // always open on the full list, so no waiting event is hidden by a filter
            Refresh(tm);
            if (_tokens.Count == 0) { _speech.Say("No map locations are available right now."); return; }
            IsActive = true;
            string mapName = "";
            try { string n = MapName(Managers.Instance.MapManager.GetCurrentMapType()); mapName = char.ToUpperInvariant(n[0]) + n.Substring(1) + ". "; } catch { }

            // Start ON the location that has an event waiting, so there is no need to search for it.
            var events = _tokens.FindAll(t => GuidanceAccessibility.MarkerFor(t) == GuidanceAccessibility.Marker.Event);
            int firstEvent = _tokens.FindIndex(t => GuidanceAccessibility.MarkerFor(t) == GuidanceAccessibility.Marker.Event);
            int firstUpdate = _tokens.FindIndex(t => GuidanceAccessibility.MarkerFor(t) == GuidanceAccessibility.Marker.Update);
            _index = firstEvent >= 0 ? firstEvent : firstUpdate >= 0 ? firstUpdate : 0;

            string waitingText;
            if (events.Count == 0) waitingText = "No events waiting. ";
            else
            {
                var names = events.ConvertAll(t => TextUtil.Clean(t.Title));
                waitingText = (events.Count == 1 ? "1 event waiting, at " : events.Count + " events waiting, at ") +
                              TextUtil.JoinWith(", ", names) + ". ";
            }
            // Events waiting on the other map (for example the world map while you are on the main map).
            try
            {
                var elsewhere = GuidanceAccessibility.EventsOnOtherMap(Managers.Instance.MapManager.GetCurrentMapType());
                if (elsewhere.Count > 0)
                    waitingText += (elsewhere.Count == 1 ? "1 event waiting on the other map, at " : elsewhere.Count + " events waiting on the other map, at ") +
                                   TextUtil.JoinWith(", ", elsewhere) + ". Control F6 switches maps. ";
            }
            catch { }
            string help = ModConfig.AtLeast(Verbosity.Normal)
                ? "Enter selects. Control Tab jumps to the next location with an event. Arrows or Tab move through all locations, Page Down changes the filter, F7 reads details, Backspace closes."
                : "";
            _speech.Say("Map browser. " + mapName + _tokens.Count + " locations. " + waitingText + help);
            _speech.Say(Describe(_tokens[_index], _index, _tokens.Count), interrupt: false);
            Hover(tm, _tokens[_index]);
        }

        // ------------------------------------------------------------------ switching main map / world map

        private MapConfiguration.MapType? _switchFrom;
        private float _switchDeadline;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        private static string MapName(MapConfiguration.MapType t)
        {
            switch (t)
            {
                case MapConfiguration.MapType.Base: return "main map";
                case MapConfiguration.MapType.World: return "world map";
                case MapConfiguration.MapType.War: return "war map";
                default: return t.ToString();
            }
        }

        /// <summary>
        /// Ctrl+F6: switch between the main (country) map and the world map through the game's own map
        /// button (verified: HUDPanel.IsMapButtonActive / IsMapButtonInteractable / OnMapButtonClick,
        /// MapManager.GetCurrentMapType). The new map is announced once the game has switched.
        /// </summary>
        public void SwitchMap()
        {
            try
            {
                var panels = ScreenTracker.GetPanels();
                var hud = panels != null ? panels.HUDPanel : null;
                var map = Managers.Instance != null ? Managers.Instance.MapManager : null;
                if (!UiUtil.Alive(hud) || !UiUtil.Alive(map) || !hud.IsMapButtonActive())
                {
                    _speech.Say("Switching maps is not available right now.");
                    return;
                }
                if (!hud.IsMapButtonInteractable())
                {
                    _speech.Say("The map cannot be switched at the moment.");
                    return;
                }
                if (IsActive) Close(null);
                _switchFrom = map.GetCurrentMapType();
                _switchDeadline = (float)_clock.Elapsed.TotalSeconds + 5f;
                hud.OnMapButtonClick();
                ModLog.Info("Map switch requested from " + _switchFrom);
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-switch", ex);
                _speech.Say("Could not switch the map.");
            }
        }

        /// <summary>Announces the new map once the switch has happened.</summary>
        public void Tick()
        {
            if (_switchFrom == null) return;
            try
            {
                var map = Managers.Instance != null ? Managers.Instance.MapManager : null;
                if (!UiUtil.Alive(map)) return;
                var now = map.GetCurrentMapType();
                if (now != _switchFrom.Value)
                {
                    _switchFrom = null;
                    _speech.Say(char.ToUpperInvariant(MapName(now)[0]) + MapName(now).Substring(1) + ". Press F6 to browse its locations.");
                }
                else if (_clock.Elapsed.TotalSeconds > _switchDeadline)
                {
                    _switchFrom = null;
                    _speech.Say("The map did not change.");
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-switch-tick", ex);
                _switchFrom = null;
            }
        }

        public void Close(string message)
        {
            if (!IsActive) return;
            IsActive = false;
            var tm = GetTokenManager();
            if (tm != null && UiUtil.Alive(_hovered))
            {
                try { tm.StopHoverOverToken(_hovered); } catch { }
            }
            _hovered = null;
            if (message != null) _speech.Say(message);
        }

        private void Refresh(TokenManager tm)
        {
            var result = new List<MapTokenData>();
            MapConfiguration.MapType? currentMap = null;
            try { currentMap = Managers.Instance.MapManager.GetCurrentMapType(); } catch { }
            try
            {
                var ordered = tm.GetOrderedTokenData();
                for (int i = 0; ordered != null && i < ordered.Count; i++)
                {
                    var d = ordered[i];
                    if (d == null || !d.IsTokenEnabled) continue;
                    if (!Matches(d)) continue;
                    var token = tm.GetTokenByTokenDataNameInDatabase(d.NameInDatabase);
                    if (!UiUtil.Alive(token) || !token.gameObject.activeInHierarchy) continue;
                    // Only locations that belong to the map you are on (verified: Token.GetMapConfiguration().mapType):
                    // the main map lists your country's locations, the world map lists the world's.
                    if (currentMap.HasValue)
                    {
                        MapConfiguration.MapType? tokenMap = null;
                        try { var cfg = token.GetMapConfiguration(); if (cfg != null) tokenMap = cfg.mapType; } catch { }
                        if (tokenMap.HasValue && tokenMap.Value != currentMap.Value) continue;
                    }
                    result.Add(d);
                }
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-refresh", ex);
            }
            _tokens = result;
            // Reference point for directions: the capital shown on this map (see MapGeography.FindCapital).
            var allOnMap = new List<MapTokenData>();
            try
            {
                var ordered = tm.GetOrderedTokenData();
                MapConfiguration.MapType? map = null;
                try { map = Managers.Instance.MapManager.GetCurrentMapType(); } catch { }
                for (int i = 0; ordered != null && i < ordered.Count; i++)
                {
                    var d = ordered[i];
                    if (d == null) continue;
                    var tok = tm.GetTokenByTokenDataNameInDatabase(d.NameInDatabase);
                    if (!UiUtil.Alive(tok)) continue;
                    try { var cfg = tok.GetMapConfiguration(); if (map.HasValue && cfg != null && cfg.mapType != map.Value) continue; } catch { }
                    allOnMap.Add(d);
                }
            }
            catch { }
            _capital = MapGeography.FindCapital(tm, allOnMap);
        }

        private bool Matches(MapTokenData d)
        {
            switch (_filter)
            {
                case Filter.Countries: return d.Type == MapTokenData.TokenType.Country;
                case Filter.Cities: return d.Type == MapTokenData.TokenType.City;
                case Filter.Locations: return d.Type == MapTokenData.TokenType.Location;
                case Filter.EventLocations: return d.Type == MapTokenData.TokenType.Event;
                case Filter.WaitingForYou: return GuidanceAccessibility.MarkerFor(d) != GuidanceAccessibility.Marker.None;
                default: return true;
            }
        }

        public void Move(int delta)
        {
            var tm = GetTokenManager();
            if (tm == null) { Close("The map is no longer available."); return; }
            Refresh(tm);
            if (_tokens.Count == 0) { _speech.Say("No locations for this filter."); return; }
            _index = _index < 0 ? 0 : (_index + delta + _tokens.Count) % _tokens.Count;
            _speech.Say(Describe(_tokens[_index], _index, _tokens.Count));
            Hover(tm, _tokens[_index]);
        }

        public void MoveToEdge(bool last)
        {
            var tm = GetTokenManager();
            if (tm == null) { Close("The map is no longer available."); return; }
            Refresh(tm);
            if (_tokens.Count == 0) { _speech.Say("No locations for this filter."); return; }
            _index = last ? _tokens.Count - 1 : 0;
            _speech.Say(Describe(_tokens[_index], _index, _tokens.Count));
            Hover(tm, _tokens[_index]);
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: next / previous location with an event (or new information).</summary>
        public void JumpToWaiting(int delta)
        {
            var tm = GetTokenManager();
            if (tm == null) { Close("The map is no longer available."); return; }
            Refresh(tm);
            if (_tokens.Count == 0) { _speech.Say("No locations for this filter."); return; }
            for (int pass = 0; pass < 2; pass++)
            {
                var wanted = pass == 0 ? GuidanceAccessibility.Marker.Event : GuidanceAccessibility.Marker.Update;
                for (int step = 1; step <= _tokens.Count; step++)
                {
                    int i = ((_index + delta * step) % _tokens.Count + _tokens.Count) % _tokens.Count;
                    if (GuidanceAccessibility.MarkerFor(_tokens[i]) != wanted) continue;
                    _index = i;
                    _speech.Say(Describe(_tokens[i], i, _tokens.Count));
                    Hover(tm, _tokens[i]);
                    return;
                }
            }
            _speech.Say("No location has an event or new information right now.");
        }

        public void CycleFilter(int delta)
        {
            int count = Enum.GetValues(typeof(Filter)).Length;
            _filter = (Filter)(((int)_filter + delta + count) % count);
            var tm = GetTokenManager();
            if (tm != null) Refresh(tm);
            _index = 0;
            _speech.Say("Filter: " + FilterName(_filter) + ", " + _tokens.Count + " locations.");
            if (_tokens.Count > 0) _speech.Say(Describe(_tokens[0], 0, _tokens.Count), interrupt: false);
        }

        public void SelectCurrent()
        {
            var tm = GetTokenManager();
            if (tm == null || _index < 0 || _index >= _tokens.Count) { _speech.Say("Nothing selected."); return; }
            if (!tm.isTokenSelectionEnabled)
            {
                _speech.Say("Map locations cannot be selected right now.");
                return;
            }
            var data = _tokens[_index];
            var token = tm.GetTokenByTokenDataNameInDatabase(data.NameInDatabase);
            if (!UiUtil.Alive(token)) { _speech.Say("That location is no longer on the map."); return; }
            try
            {
                tm.SelectToken(token);
                Close(null);
                _speech.Say("Selected " + TextUtil.Clean(data.Title) + ".");
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-select", ex);
                _speech.Say("Could not select the location.");
            }
        }

        public void ReadDetails()
        {
            if (_index < 0 || _index >= _tokens.Count) { _speech.Say("No location."); return; }
            string where = Geography(_tokens[_index]);
            _speech.Say(Details(_tokens[_index]) + (string.IsNullOrEmpty(where) ? "" : " Position: " + where + "."));
        }

        private void Hover(TokenManager tm, MapTokenData data)
        {
            if (!ModConfig.MapHoverTokens.Value) return;
            try
            {
                var token = tm.GetTokenByTokenDataNameInDatabase(data.NameInDatabase);
                if (UiUtil.Alive(_hovered) && (!UiUtil.Alive(token) || _hovered.Pointer != token.Pointer)) tm.StopHoverOverToken(_hovered);
                if (UiUtil.Alive(token)) { tm.HoverOverToken(token); _hovered = token; }
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-hover", ex);
            }
        }

        // ------------------------------------------------------------------ text

        private string Describe(MapTokenData d, int index, int total)
        {
            string title = TextUtil.Clean(d.Title);
            string type = TypeName(d.Type);
            string relation = "";
            try { if (d.IsCountry() && d.SupportsCountryDetails()) relation = RelationName(d.RelationsType); } catch { }
            string isNew = TextUtil.Join(GuidanceAccessibility.MarkerText(GuidanceAccessibility.MarkerFor(d)), d.HasNewStatusEffect ? "new status effect" : "");
            string pos = ModConfig.AnnouncePositions.Value ? (index + 1) + " of " + total : "";
            string direction = ModConfig.AtLeast(Verbosity.Normal) ? Geography(d) : "";
            return TextUtil.Join(title, type, TextUtil.Clean(d.Subtitle), relation, isNew, direction, pos);
        }

        /// <summary>Region of the map, and for non-country locations the direction from the capital.</summary>
        private string Geography(MapTokenData d)
        {
            try
            {
                var tm = GetTokenManager();
                if (tm == null) return "";
                var token = tm.GetTokenByTokenDataNameInDatabase(d.NameInDatabase);
                if (!UiUtil.Alive(token)) return "";
                string region = MapGeography.Region(token);
                string fromCapital = "";
                if (_capital != null && d.Type != MapTokenData.TokenType.Country && d.NameInDatabase != _capital.NameInDatabase)
                {
                    var capitalToken = tm.GetTokenByTokenDataNameInDatabase(_capital.NameInDatabase);
                    fromCapital = MapGeography.FromReference(token, capitalToken, TextUtil.Clean(_capital.Title));
                }
                else if (_capital != null && d.NameInDatabase == _capital.NameInDatabase)
                {
                    fromCapital = "the capital";
                }
                return TextUtil.Join(region, fromCapital);
            }
            catch { return ""; }
        }

        private static string Details(MapTokenData d)
        {
            var parts = new List<string>();
            parts.Add(TextUtil.Sentence(TextUtil.Join(TextUtil.Clean(d.Title), TypeName(d.Type), TextUtil.Clean(d.Subtitle))));
            try
            {
                if (d.SupportsCountryDetails())
                {
                    if (d.IsCountry()) parts.Add("Relations: " + RelationName(d.RelationsType) + ".");
                    AddIf(parts, "Capital", d.Capital);
                    AddIf(parts, "President", d.President);
                    AddIf(parts, "Government", d.Government);
                    AddIf(parts, "Language", d.Language);
                    parts.Add("Military strength: " + StrengthName(d.MilitaryStrength) + ".");
                }
                if (d.SupportsPopulationEconomy())
                {
                    parts.Add("Population value: " + TextUtil.Number(d.Population) + ".");
                    parts.Add("GDP value: " + TextUtil.Number(d.GDP) + ".");
                    if (d.GDPWorldRank > 0) parts.Add("GDP world rank: " + d.GDPWorldRank.ToString(CultureInfo.InvariantCulture) + ".");
                }
                string ethnicity = Demographics.FromTokenData(d, ethnicity: true);
                string religion = Demographics.FromTokenData(d, ethnicity: false);
                if (!string.IsNullOrEmpty(ethnicity)) parts.Add("Ethnicity: " + ethnicity + ".");
                if (!string.IsNullOrEmpty(religion)) parts.Add("Religion: " + religion + ".");
                if (d.SupportsDescription()) AddIf(parts, "", d.Description);
                int effects = d.StatusEffects != null ? d.StatusEffects.Count : 0;
                if (effects > 0) parts.Add(effects + (effects == 1 ? " status effect" : " status effects") + "; select the location and open its information panel to read them.");
            }
            catch (Exception ex)
            {
                ModLog.Exception("map-details", ex);
            }
            return TextUtil.JoinWith(" ", parts);
        }

        private static string FilterName(Filter f)
        {
            switch (f)
            {
                case Filter.WaitingForYou: return "locations with events or new information";
                case Filter.EventLocations: return "event locations";
                default: return f.ToString().ToLowerInvariant();
            }
        }

        private static void AddIf(List<string> parts, string label, string value)
        {
            string v = TextUtil.Clean(value);
            if (string.IsNullOrEmpty(v)) return;
            parts.Add(TextUtil.Sentence(string.IsNullOrEmpty(label) ? v : label + ": " + v));
        }

        private static string TypeName(MapTokenData.TokenType type)
        {
            switch (type)
            {
                case MapTokenData.TokenType.City: return "city";
                case MapTokenData.TokenType.Country: return "country";
                case MapTokenData.TokenType.Location: return "location";
                case MapTokenData.TokenType.Event: return "event";
                default: return type.ToString();
            }
        }

        private static string RelationName(MapTokenData.CountryRelationsType r)
        {
            switch (r)
            {
                case MapTokenData.CountryRelationsType.Hostile: return "hostile";
                case MapTokenData.CountryRelationsType.Unfriendly: return "unfriendly";
                case MapTokenData.CountryRelationsType.Neutral: return "neutral";
                case MapTokenData.CountryRelationsType.Friendly: return "friendly";
                case MapTokenData.CountryRelationsType.Allied: return "allied";
                case MapTokenData.CountryRelationsType.AtWar: return "at war";
                default: return r.ToString();
            }
        }

        private static string StrengthName(MapTokenData.MilitaryStrengthType s)
        {
            switch (s)
            {
                case MapTokenData.MilitaryStrengthType.Weak: return "weak";
                case MapTokenData.MilitaryStrengthType.Equivalent: return "equivalent";
                case MapTokenData.MilitaryStrengthType.Strong: return "strong";
                case MapTokenData.MilitaryStrengthType.Overwhelming: return "overwhelming";
                default: return s.ToString();
            }
        }
    }
}
