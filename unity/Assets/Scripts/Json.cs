// A small JSON reader for the content copied from the web version (Assets/Resources/Data/*.json).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/** A JSON value with easy, forgiving access: missing keys give an empty J, never an exception. */
public sealed class J : IEnumerable<J>
{
    public readonly object v;
    public J(object v) { this.v = v; }
    static readonly J Nothing = new J(null);

    public J this[string k] => v is Dictionary<string, object> d && d.TryGetValue(k, out var x) ? new J(x) : Nothing;
    public J this[int i] => v is List<object> l && i >= 0 && i < l.Count ? new J(l[i]) : Nothing;
    public bool Has(string k) => v is Dictionary<string, object> d && d.ContainsKey(k);
    public bool IsNull => v == null;
    public bool IsList => v is List<object>;
    public bool IsObject => v is Dictionary<string, object>;
    public int Count => v is List<object> l ? l.Count : v is Dictionary<string, object> d ? d.Count : 0;
    public IEnumerable<string> Keys => v is Dictionary<string, object> d ? d.Keys : (IEnumerable<string>)Array.Empty<string>();
    public string S => v as string ?? (v is double n ? n.ToString(CultureInfo.InvariantCulture) : v is bool b ? (b ? "true" : "false") : null);
    public int I => v is double n ? (int)n : v is string s && int.TryParse(s, out var r) ? r : 0;
    public float F => v is double n ? (float)n : 0;
    public bool B => v is bool b ? b : v != null && !(v is double n && n == 0) && !(v is string s && s == "");
    /** A {mn, en} object as Tx; a plain string is the same in both languages. */
    public Tx T => v is string s ? new Tx(Emo.Strip(s), Emo.Strip(s)) : new Tx(Emo.Strip(this["mn"].S ?? ""), Emo.Strip(this["en"].S ?? this["mn"].S ?? ""));
    public List<string> Strings() { var r = new List<string>(); foreach (var x in this) r.Add(x.S); return r; }
    public IEnumerator<J> GetEnumerator()
    {
        if (v is List<object> l) foreach (var x in l) yield return new J(x);
        else if (v is Dictionary<string, object> d) foreach (var x in d.Values) yield return new J(x);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => S ?? "";

    // ---------------------------------------------------------------- parsing
    public static J Parse(string s) { int i = 0; var r = Value(s, ref i); return new J(r); }
    static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
    static object Value(string s, ref int i)
    {
        Ws(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true) { Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; /* : */ d[k] = Value(s, ref i); Ws(s, ref i); if (s[i++] == '}') return d; }
        }
        if (c == '[')
        {
            var l = new List<object>(); i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i++] == ']') return l; }
        }
        if (c == '"') return Str(s, ref i);
        if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }
    static string Str(string s, ref int i)
    {
        var sb = new StringBuilder(); i++; // opening quote
        while (true)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break; case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                default: sb.Append(e); break;
            }
        }
    }
    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "") { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c == '\n') sb.Append("\\n"); else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); }
        return sb.Append('"').ToString();
    }
}

/** The web version's content, loaded once per file from Resources/Data. */
public static class Data
{
    static readonly Dictionary<string, J> cache = new Dictionary<string, J>();
    public static J Get(string name)
    {
        if (cache.TryGetValue(name, out var j)) return j;
        var ta = Resources.Load<TextAsset>("Data/" + name);
        j = ta != null ? J.Parse(ta.text) : new J(null);
        if (ta == null) Debug.LogWarning("Naadamy: missing Resources/Data/" + name + ".json");
        return cache[name] = j;
    }
}
