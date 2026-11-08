// Background music: a shuffled playlist (Resources/Music, CC0 tracks from OpenGameArt.org listed in Resources/Data/playlist.json)
// with gentle crossfades, like js/music.js. It pauses when the app is in the background.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class Music : MonoBehaviour
{
    const float FADE = 1.5f;
    public sealed class Track { public string file, title, artist, license, source; }
    static List<Track> tracks;
    public static List<Track> Credits => tracks ??= Data.Get("playlist")["tracks"].Select(t => new Track { file = t["file"].S.Replace(".m4a", ""), title = t["title"].S, artist = t["artist"].S, license = t["license"].S, source = t["source"].S }).ToList();
    static Music me;
    AudioSource a, b; readonly List<int> order = new List<int>(); int at = -1; float fadeT = -1; AudioSource from, to;
    public static bool On => me != null && me.on;
    bool on = true; float volume = 0.35f;

    public static void Init(GameObject host, bool on, float volume)
    {
        me = host.AddComponent<Music>(); me.on = on; me.volume = volume;
        me.a = host.AddComponent<AudioSource>(); me.b = host.AddComponent<AudioSource>();
        foreach (var s in new[] { me.a, me.b }) { s.playOnAwake = false; s.loop = false; s.volume = 0; }
        if (on && !Muted) me.Next();
    }
    /** Everything silent at once (the 🔊 button): the music pauses, and the sounds (Sfx), story voices (Voice) and the
     *  memory book's recordings don't start. AudioListener.volume alone isn't enough: a browser build ignores it.
     *  Settings still turns each kind on and off by itself. */
    public static bool Muted { get; private set; }
    public static void Mute(bool m)
    {
        Muted = m; AudioListener.volume = m ? 0 : 1; // the listener still covers the other builds
        if (m) { foreach (var s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (me == null || (s != me.a && s != me.b)) s.Stop(); }
        if (me == null) return;
        if (m) { me.a.Pause(); me.b.Pause(); }
        else if (me.on) { if (me.a.clip != null) me.a.UnPause(); if (me.b.clip != null) me.b.UnPause(); if (!me.a.isPlaying && !me.b.isPlaying) me.Next(); }
    }
    public static void ToggleMute() { var app = Game.I.App; app.muted = !app.muted; Mute(app.muted); app.Save(); }
    public static void Toggle()
    {
        if (me == null) return; me.on = !me.on; Game.I.App.music = me.on; Game.I.App.Save();
        if (me.on) { if (!Muted) me.Next(); } else { me.a.Stop(); me.b.Stop(); }
    }
    void Next()
    {
        if (Credits.Count == 0) return;
        if (order.Count == 0) { order.AddRange(Enumerable.Range(0, Credits.Count).OrderBy(_ => Random.value)); }
        at = order[0]; order.RemoveAt(0);
        var clip = Resources.Load<AudioClip>("Music/" + Credits[at].file); if (clip == null) return;
        clip.LoadAudioData();
        from = a.isPlaying ? a : b.isPlaying ? b : null; to = from == a ? b : a;
        to.clip = clip; to.volume = 0; to.Play(); fadeT = 0;
    }
    void Update()
    {
        if (!on || Muted) return;
        if (fadeT >= 0)
        {
            fadeT += Time.unscaledDeltaTime; float q = Mathf.Clamp01(fadeT / FADE);
            to.volume = q * volume; if (from != null) from.volume = (1 - q) * volume;
            if (q >= 1) { if (from != null) from.Stop(); fadeT = -1; }
        }
        var cur = to ?? a;
        // a browser decodes a whole track before it plays: wait for it instead of skipping to the next one
        if (cur == null || cur.clip == null || fadeT >= 0 || cur.clip.loadState == AudioDataLoadState.Loading) return;
        if (cur.isPlaying ? cur.clip.length - cur.time < FADE : Application.isFocused) Next();
    }
    void OnApplicationPause(bool paused) { if (!on || Muted) return; if (paused) { a.Pause(); b.Pause(); } else { if (a.clip != null) a.UnPause(); if (b.clip != null) b.UnPause(); } }
}
