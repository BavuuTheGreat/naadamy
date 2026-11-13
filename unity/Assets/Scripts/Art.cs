// Pictures first drawn by the web version (removed in #82): province trip maps, animals, Nomin…
// They are PNG files in Resources/Art kept as raw bytes, decoded here into PxCanvas pictures.
using System.Collections.Generic;
using UnityEngine;

public static class Art
{
    static readonly Dictionary<string, PxCanvas> cache = new Dictionary<string, PxCanvas>();
    /** A picture by name ("trip_tuv", "critter_horse_0"); an empty 1×1 picture if it is missing. */
    public static PxCanvas Get(string name)
    {
        if (cache.TryGetValue(name, out var c)) return c;
        var ta = Resources.Load<TextAsset>("Art/" + name + ".png");
        if (ta == null) { Debug.LogWarning("Naadamy: missing Resources/Art/" + name + ".png.bytes"); return cache[name] = new PxCanvas(1, 1); }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(ta.bytes);
        c = PxCanvas.From(t.GetPixels32(), t.width, t.height); Object.Destroy(t);
        return cache[name] = c;
    }
}
