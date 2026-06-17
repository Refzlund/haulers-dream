using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HaulersDream.Core
{
    /// <summary>
    /// Compact, copy-paste-safe encoding of a settings profile for sharing. A token carries three things: the mod
    /// version it was made on, the profile name, and the map of CHANGED setting values (field name -> already-encoded
    /// value string). Only deviations from that version's defaults are stored, so a near-default profile produces a
    /// tiny token; the importer reconstructs the rest from the named version's defaults.
    ///
    /// <para>Wire format: <c>"HDP" + flag + base64(body)</c>, where <c>flag</c> is <c>'1'</c> when the body is
    /// DEFLATE-compressed and <c>'0'</c> when it is the raw payload (compression is used only when it is actually
    /// smaller — DEFLATE+base64 inflates very short inputs, so the encoder picks the shorter of the two). The
    /// pre-compression payload is line-oriented UTF-8: line 0 = version, line 1 = escaped name, then one
    /// <c>key\tvalue</c> line per changed setting. Field names are identifiers and encoded values never contain
    /// <c>\n</c>/<c>\t</c> (collections use the unit/record separators U+001F / U+001E), so framing needs no escaping
    /// beyond the (user-entered) name.</para>
    /// </summary>
    public static class ProfileCodec
    {
        public const string Prefix = "HDP";

        public sealed class ProfileData
        {
            public string Version = "";
            public string Name = "";
            public Dictionary<string, string> Changed = new Dictionary<string, string>();
        }

        public static string Encode(string version, string name, IEnumerable<KeyValuePair<string, string>> changed)
        {
            var sb = new StringBuilder();
            sb.Append(version ?? "").Append('\n');
            sb.Append(Escape(name ?? "")).Append('\n');
            if (changed != null)
                foreach (var kv in changed)
                    sb.Append(kv.Key).Append('\t').Append(kv.Value ?? "").Append('\n');

            byte[] raw = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] deflated = Deflate(raw);
            bool compressed = deflated.Length < raw.Length;
            return Prefix + (compressed ? '1' : '0') + Convert.ToBase64String(compressed ? deflated : raw);
        }

        /// <summary>Parse a token. Returns false (never throws) on any malformed input — a paste is untrusted user
        /// input, so a bad string is an expected "couldn't read that" case, not a fault to surface.</summary>
        public static bool TryDecode(string token, out ProfileData data)
        {
            data = null;
            if (string.IsNullOrEmpty(token)) return false;
            token = token.Trim();
            if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length < Prefix.Length + 2) return false;
            char flag = token[Prefix.Length];
            if (flag != '0' && flag != '1') return false;

            try
            {
                byte[] body = Convert.FromBase64String(token.Substring(Prefix.Length + 1));
                byte[] raw = flag == '1' ? Inflate(body) : body;
                string payload = Encoding.UTF8.GetString(raw);
                string[] lines = payload.Split('\n');
                if (lines.Length < 2) return false;

                var d = new ProfileData { Version = lines[0], Name = Unescape(lines[1]) };
                for (int i = 2; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length == 0) continue;
                    int tab = line.IndexOf('\t');
                    if (tab < 0) continue;
                    d.Changed[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
                data = d;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    ds.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Inflate(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var ds = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                ds.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
