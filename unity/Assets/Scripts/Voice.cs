// Spoken story lines: Tools/tts/voice.mjs reads every Say/Ask line of the story chapters and has Chimege speak the Mongolian
// words in the speaker's voice, into Resources/Voice/<id>.wav. The id comes from the speaker and the words, so a changed line
// simply has no clip until the script runs again (the line then types out with the old blips). Mongolian only.
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class Voice
{
    static AudioSource src;
    /** Speak story lines: the setting is on and the game is in Mongolian. */
    public static bool On => Game.I != null && Game.I.App != null && Game.I.App.voice && !Game.I.App.en && !Music.Muted;
    /** The clip's name: the first 12 hex digits of SHA-1("who|words"), as Tools/tts/voice.mjs names it. */
    public static string Id(string who, string mn)
    {
        using (var sha = SHA1.Create())
        {
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(who + "|" + mn)); var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }
    /** Starts the line's clip when there is one; null when the line has no voice (or voices are off). */
    public static AudioClip Speak(string who, string mn)
    {
        if (!On || string.IsNullOrEmpty(mn)) return null;
        var c = Resources.Load<AudioClip>("Voice/" + Id(who, mn)); if (c == null) return null;
        if (src == null) { src = Game.I.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; }
        src.Stop(); src.clip = c; src.Play(); return c;
    }
    public static void Stop() { if (src != null) src.Stop(); }
}
