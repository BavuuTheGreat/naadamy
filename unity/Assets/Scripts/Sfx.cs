// Sound effects made in code, like the web version's TG.sfx: no audio files needed.
using System;
using UnityEngine;

public static class Sfx
{
    const int RATE = 22050;
    static AudioSource src;
    static AudioClip tap, pop, good, bad, pour, chime, win;

    public static void Init(GameObject host)
    {
        src = host.AddComponent<AudioSource>();
        src.playOnAwake = false;
        tap = Tone("tap", 0.05f, t => Sq(880, t) * Env(t, 0.05f) * 0.25f);
        pop = Tone("pop", 0.12f, t => Mathf.Sin(2 * Mathf.PI * (400 + 900 * t / 0.12f) * t) * Env(t, 0.12f) * 0.5f);
        bad = Tone("bad", 0.25f, t => Sq(t < 0.12f ? 220 : 165, t) * Env(t, 0.25f) * 0.2f);
        chime = Tone("chime", 0.4f, t => (Mathf.Sin(2 * Mathf.PI * 1320 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 1980 * t)) * Env(t, 0.4f) * 0.3f);
        good = Notes("good", new[] { 523f, 659f, 784f }, 0.09f);
        win = Notes("win", new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.1f);
        var rnd = new System.Random(7);
        pour = Tone("pour", 0.35f, t => (float)(rnd.NextDouble() * 2 - 1) * Mathf.Sin(Mathf.PI * t / 0.35f) * 0.18f);
    }
    static float Env(float t, float len) => Mathf.Clamp01(t / 0.005f) * (1 - t / len);
    static float Sq(float hz, float t) => Mathf.Sin(2 * Mathf.PI * hz * t) > 0 ? 1 : -1;
    static AudioClip Tone(string name, float len, Func<float, float> f)
    {
        int n = (int)(len * RATE); var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = f(i / (float)RATE);
        var c = AudioClip.Create(name, n, 1, RATE, false); c.SetData(d, 0); return c;
    }
    static AudioClip Notes(string name, float[] hz, float each)
    {
        return Tone(name, hz.Length * each + 0.1f, t =>
        {
            int k = Mathf.Min(hz.Length - 1, (int)(t / each)); float lt = t - k * each;
            return Mathf.Sin(2 * Mathf.PI * hz[k] * t) * Mathf.Clamp01(lt / 0.005f) * Mathf.Exp(-lt * 9) * 0.35f;
        });
    }
    public static bool On = true;
    static void Play(AudioClip c, float vol = 1, float pitch = 1) { if (src == null || c == null || !On || Music.Muted) return; src.pitch = pitch; src.PlayOneShot(c, vol); }

    public static void Tap() => Play(tap);
    static readonly System.Collections.Generic.Dictionary<string, AudioClip> blips = new System.Collections.Generic.Dictionary<string, AudioClip>();
    /** One talking blip in a character's voice (js/dialog.js VOICES): guide sing-song, Basar's bark, the General's growl. */
    public static void Blip(string who, float mul = 1)
    {
        if (!blips.TryGetValue(who, out var c))
        {
            float f = who == "basar" ? 310 : who == "general" ? 135 : who == "emee" ? 440 : who == "historian" ? 370 : who == "chinggis" ? 118 : who == "temujin" ? 150 : who == "man" ? 140 : who == "elder" ? 128 : who == "woman" ? 400 : who == "child" ? 300 : 560, dur = who == "basar" ? 0.06f : who == "emee" ? 0.075f : 0.045f;
            bool bark = who == "basar", sq = who != "emee" && who != "historian";
            c = blips[who] = Tone("blip_" + who, dur + 0.02f, t => { float hz = bark ? f * Mathf.Lerp(1, 0.55f, t / dur) : f; float w = sq ? Sq(hz, t) * 0.5f : Mathf.Sin(2 * Mathf.PI * hz * t); return t < dur ? w * Env(t, dur) * 0.35f : 0; });
        }
        Play(c, 0.6f, mul * (1 + (UnityEngine.Random.value - 0.5f) * 0.18f));
    }
    static AudioClip hit, swish, thud, horn, twang, crowd;
    /** A crowd roaring: noise that swells and falls, with a murmur of many voices in it. */
    public static void Crowd(float vol = 0.7f)
    {
        if (crowd == null)
        {
            var rnd = new System.Random(11); float lp = 0, lp2 = 0;
            crowd = Tone("crowd", 1.8f, t =>
            {
                float n = (float)(rnd.NextDouble() * 2 - 1); lp += (n - lp) * 0.12f; lp2 += (lp - lp2) * 0.3f;
                float voices = Mathf.Sin(2 * Mathf.PI * (210 + 40 * Mathf.Sin(t * 7)) * t) * 0.08f + Mathf.Sin(2 * Mathf.PI * (320 + 50 * Mathf.Sin(t * 5.3f)) * t) * 0.06f;
                float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((1.8f - t) / 0.7f);
                return (lp2 * 1.6f + voices * (0.5f + 0.5f * Mathf.Sin(t * 9))) * env * 0.6f;
            });
        }
        Play(crowd, vol);
    }
    /** A blow landing: a short noisy knock. */
    public static void Hit(float pitch = 1) { hit ??= Noise("hit", 0.09f, 0.45f, 180); Play(hit, 0.8f, pitch); }
    /** A swing through the air. */
    public static void Swish() { swish ??= Noise("swish", 0.16f, 0.25f, 2400); Play(swish, 0.7f, 0.9f + UnityEngine.Random.value * 0.2f); }
    /** Something heavy falling or a horse's stamp. */
    public static void Thud() { thud ??= Tone("thud", 0.22f, t => Mathf.Sin(2 * Mathf.PI * (90 - 120 * t) * t) * Env(t, 0.22f) * 0.6f); Play(thud); }
    /** A bowstring and a whistling arrow (the Mongol dugaraa sum whistles in flight). */
    public static void Twang() { twang ??= Tone("twang", 0.3f, t => (Sq(196 - 40 * t, t) * 0.2f * Mathf.Exp(-t * 30) + Mathf.Sin(2 * Mathf.PI * (1600 + 600 * t) * t) * 0.12f * Mathf.Clamp01(t * 20) * (1 - t / 0.3f))); Play(twang); }
    /** A horn call: the allies are coming. */
    public static void Horn() { horn ??= Tone("horn", 1.1f, t => { float f = t < 0.35f ? 196 : 262; float saw = 2 * ((t * f) % 1) - 1; return (saw * 0.35f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.4f) * Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((1.1f - t) / 0.3f) * 0.35f; }); Play(horn); }
    static AudioClip Noise(string name, float len, float vol, float cut)
    {
        var rnd = new System.Random(name.Length); float lp = 0, a = Mathf.Clamp01(cut / RATE * 6.28f);
        return Tone(name, len, t => { lp += ((float)(rnd.NextDouble() * 2 - 1) - lp) * a; return lp * vol * 3 * Env(t, len); });
    }
    public static void Pop() => Play(pop);
    public static void Good() => Play(good);
    public static void Bad() => Play(bad);
    public static void Chime() => Play(chime);
    public static void Win() => Play(win);
    public static void Pour(float level = 0.5f) => Play(pour, 1, 0.8f + level * 0.6f);
    static AudioClip melody;
    /** A slow pentatonic tune with a soft, bowed sound (the morin khuur at home). */
    public static void Melody()
    {
        if (melody == null)
        {
            float[] notes = { 392, 440, 523, 587, 523, 440, 392, 330, 392 };
            melody = Tone("melody", notes.Length * 0.38f + 0.5f, t =>
            {
                int k = Mathf.Min(notes.Length - 1, (int)(t / 0.38f)); float lt = t - k * 0.38f, f = notes[k] * (1 + 0.01f * Mathf.Clamp01(lt / 0.3f));
                float env = lt < 0.08f ? lt / 0.08f : Mathf.Max(0, 1 - (lt - 0.08f) / 0.34f);
                float saw = 2 * ((t * f) % 1) - 1; return (saw * 0.5f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f) * env * 0.12f;
            });
        }
        Play(melody);
    }
}
