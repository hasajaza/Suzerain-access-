using System;
using System.Collections.Generic;
using SuzerainAccess.Core;
using SuzerainAccess.UI;
using UnityEngine;

namespace SuzerainAccess.Features
{
    /// <summary>
    /// Describes where a map location is, the way a sighted player reads the map:
    ///  * its region of the map ("in the north-west of the map", "in the centre of the map"), from the
    ///    token's position inside the map's bounds (verified: Token.GetMapConfiguration().bounds);
    ///  * for locations of a country's map, the compass direction from the capital
    ///    (the capital is found through the game data: a country's MapTokenData.Capital that names a
    ///    location on the same map).
    ///
    /// The game stores no compass directions. North is taken as the top of the map as the game's camera
    /// shows it: the mod projects the map's axes through the camera once to learn which world axis runs
    /// up the screen and which runs to the right. If no camera is available, the usual Unity top-down
    /// convention (x = east, z = north) is assumed.
    /// </summary>
    internal static class MapGeography
    {
        private sealed class Frame
        {
            public int EastAxis, NorthAxis;
            public float EastSign, NorthSign;
            public Vector3 Min, Size;
        }

        private static readonly Dictionary<MapConfiguration.MapType, Frame> Frames = new Dictionary<MapConfiguration.MapType, Frame>();

        private static float Axis(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;

        private static Frame GetFrame(Token token)
        {
            var cfg = token.GetMapConfiguration();
            if (cfg == null) return null;
            if (Frames.TryGetValue(cfg.mapType, out var cached)) return cached;

            var bounds = cfg.bounds;
            Vector3 size = bounds.size, min = bounds.min, center = bounds.center;
            // The map lies in the plane of its two largest dimensions; the thinnest one points "up".
            int thin = 0;
            for (int i = 1; i < 3; i++) if (Axis(size, i) < Axis(size, thin)) thin = i;
            int a = thin == 0 ? 1 : 0, b = thin == 2 ? 1 : 2;

            var f = new Frame { Min = min, Size = size, EastAxis = 0, NorthAxis = 2, EastSign = 1f, NorthSign = 1f };
            bool fromCamera = false;
            try
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 pa = center, pb = center;
                    SetAxis(ref pa, a, Axis(center, a) + Math.Max(1f, Axis(size, a) * 0.25f));
                    SetAxis(ref pb, b, Axis(center, b) + Math.Max(1f, Axis(size, b) * 0.25f));
                    Vector3 s0 = cam.WorldToScreenPoint(center), sa = cam.WorldToScreenPoint(pa), sb = cam.WorldToScreenPoint(pb);
                    float ax = sa.x - s0.x, ay = sa.y - s0.y, bx = sb.x - s0.x, by = sb.y - s0.y;
                    if (Math.Abs(ax) + Math.Abs(ay) > 0.001f && Math.Abs(bx) + Math.Abs(by) > 0.001f)
                    {
                        // The axis that moves most horizontally on screen is east-west.
                        if (Math.Abs(ax) >= Math.Abs(bx)) { f.EastAxis = a; f.EastSign = Math.Sign(ax); f.NorthAxis = b; f.NorthSign = Math.Sign(by); }
                        else { f.EastAxis = b; f.EastSign = Math.Sign(bx); f.NorthAxis = a; f.NorthSign = Math.Sign(ay); }
                        if (f.EastSign == 0) f.EastSign = 1;
                        if (f.NorthSign == 0) f.NorthSign = 1;
                        fromCamera = true;
                    }
                }
            }
            catch { }
            if (!fromCamera) { f.EastAxis = a; f.NorthAxis = b; }
            ModLog.Info($"Map geography for {cfg.mapType}: east = axis {f.EastAxis} ({(f.EastSign > 0 ? "+" : "-")}), north = axis {f.NorthAxis} ({(f.NorthSign > 0 ? "+" : "-")}), from {(fromCamera ? "the camera" : "the default convention")}.");
            Frames[cfg.mapType] = f;
            return f;
        }

        private static void SetAxis(ref Vector3 v, int i, float value)
        {
            if (i == 0) v.x = value; else if (i == 1) v.y = value; else v.z = value;
        }

        /// <summary>Position as (east, north), each 0..1 across the map. False if unknown.</summary>
        private static bool Normalized(Token token, out float east, out float north)
        {
            east = north = 0f;
            var f = GetFrame(token);
            if (f == null) return false;
            Vector3 p = token.transform.position;
            float sx = Axis(f.Size, f.EastAxis), sy = Axis(f.Size, f.NorthAxis);
            if (sx <= 0.0001f || sy <= 0.0001f) return false;
            east = (Axis(p, f.EastAxis) - Axis(f.Min, f.EastAxis)) / sx;
            north = (Axis(p, f.NorthAxis) - Axis(f.Min, f.NorthAxis)) / sy;
            if (f.EastSign < 0) east = 1f - east;
            if (f.NorthSign < 0) north = 1f - north;
            return true;
        }

        /// <summary>"in the north-west of the map", "in the centre of the map"...</summary>
        public static string Region(Token token)
        {
            try
            {
                if (!Normalized(token, out float e, out float n)) return "";
                string ns = n > 0.66f ? "north" : n < 0.34f ? "south" : "";
                string ew = e > 0.66f ? "east" : e < 0.34f ? "west" : "";
                string dir = ns.Length > 0 && ew.Length > 0 ? ns + "-" + ew : ns + ew;
                return dir.Length == 0 ? "in the centre of the map" : "in the " + dir + " of the map";
            }
            catch { return ""; }
        }

        /// <summary>"south-east of <reference name>", or "" if not applicable.</summary>
        public static string FromReference(Token token, Token reference, string referenceName)
        {
            try
            {
                if (!UiUtil.Alive(reference) || reference.Pointer == token.Pointer) return "";
                if (!Normalized(token, out float e1, out float n1) || !Normalized(reference, out float e0, out float n0)) return "";
                float dx = e1 - e0, dy = n1 - n0;
                if (Math.Sqrt(dx * dx + dy * dy) < 0.04) return "next to " + referenceName;
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI; // 0 = east, 90 = north
                string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
                int sector = (int)Math.Round(((angle % 360) + 360) % 360 / 45.0) % 8;
                return names[sector] + " of " + referenceName;
            }
            catch { return ""; }
        }

        /// <summary>
        /// The capital shown on the current map: a country's MapTokenData.Capital that matches the title of a
        /// city in <paramref name="onMap"/>. Returns the capital's data, or null.
        /// </summary>
        public static MapTokenData FindCapital(TokenManager tm, List<MapTokenData> onMap)
        {
            try
            {
                var titles = new Dictionary<string, MapTokenData>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in onMap)
                    if (d != null && d.Type == MapTokenData.TokenType.City) titles[TextUtil.Clean(d.Title)] = d;
                if (titles.Count == 0) return null;
                var all = tm.GetOrderedTokenData();
                for (int i = 0; all != null && i < all.Count; i++)
                {
                    var c = all[i];
                    if (c == null || !c.IsCountry()) continue;
                    string capital = TextUtil.Clean(c.Capital);
                    if (!string.IsNullOrEmpty(capital) && titles.TryGetValue(capital, out var city)) return city;
                }
            }
            catch { }
            return null;
        }
    }
}
