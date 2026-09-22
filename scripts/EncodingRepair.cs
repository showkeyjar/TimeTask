using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TimeTask.Repair
{
    public static class EncodingRepair
    {
        public static int Run(string target, string reference, bool apply)
        {
            var strict = new UTF8Encoding(false, true);
            var lenient = new UTF8Encoding(false, false);
            byte[] cur = File.ReadAllBytes(target);
            string refText = lenient.GetString(File.ReadAllBytes(reference));

            // 1) walk & find damage spots (start offset of invalid sequence)
            var spots = new List<int>();
            int i = 0;
            while (i < cur.Length)
            {
                byte b = cur[i];
                if (b < 0x80) { i++; continue; }
                int need;
                if (b >= 0xF0) need = 3;
                else if (b >= 0xE0) need = 2;
                else if (b >= 0xC0) need = 1;
                else { spots.Add(i); i++; continue; } // stray continuation byte

                bool ok = true;
                for (int k = 1; k <= need; k++)
                {
                    if (i + k >= cur.Length) { ok = false; break; }
                    byte nb = cur[i + k];
                    if (nb < 0x80 || nb >= 0xC0) { ok = false; break; }
                }
                if (ok) { i += 1 + need; continue; }
                spots.Add(i);
                // skip the lead byte; re-examine following bytes one at a time
                i++;
            }

            Console.WriteLine("damage spots: " + spots.Count + " in " + Path.GetFileName(target));
            if (spots.Count == 0) return 0;

            // 2) repair each spot via reference matching
            var replacements = new List<KeyValuePair<int, int>>(); // start -> replaceEnd (exclusive)
            var newTexts = new List<string>();
            int unmatched = 0;
            foreach (int s in spots)
            {
                int beforeStart = Math.Max(0, s - 60);
                string beforeCtx = lenient.GetString(cur, beforeStart, s - beforeStart);
                int tailLen = Math.Min(24, beforeCtx.Length);
                if (tailLen == 0) { unmatched++; continue; }
                string tail = beforeCtx.Substring(beforeCtx.Length - tailLen);

                int e = s;
                while (e < cur.Length && e < s + 6 && cur[e] != 0x0A) e++;
                int afterLen = Math.Min(48, cur.Length - e);
                string afterCtx = lenient.GetString(cur, e, afterLen);
                int headLen = Math.Min(20, afterCtx.Length);
                string head = afterCtx.Substring(0, headLen);

                int posA = refText.LastIndexOf(tail, StringComparison.Ordinal);
                if (posA < 0) { unmatched++; Console.WriteLine("UNMATCHED spot @" + s); continue; }
                int posAEnd = posA + tail.Length;
                int posB = refText.IndexOf(head, posAEnd, StringComparison.Ordinal);
                if (posB < 0 || posB > posAEnd + 40) { unmatched++; Console.WriteLine("UNMATCHED spot @" + s + " (head)"); continue; }

                string replacement = refText.Substring(posAEnd, posB - posAEnd);
                Console.WriteLine(string.Format("repair @{0}: [{1}]", s, replacement.Replace("\r", "\\r").Replace("\n", "\\n")));
                replacements.Add(new KeyValuePair<int, int>(s, e));
                newTexts.Add(replacement);
            }

            if (!apply)
            {
                Console.WriteLine("preview only; unmatched=" + unmatched);
                return unmatched;
            }

            // 3) apply from the end
            for (int r = replacements.Count - 1; r >= 0; r--)
            {
                int s = replacements[r].Key, e = replacements[r].Value;
                byte[] rep = Encoding.UTF8.GetBytes(newTexts[r]);
                byte[] merged = new byte[cur.Length - (e - s) + rep.Length];
                Array.Copy(cur, 0, merged, 0, s);
                Array.Copy(rep, 0, merged, s, rep.Length);
                Array.Copy(cur, e, merged, s + rep.Length, cur.Length - e);
                cur = merged;
            }

            // 4) validate & write
            try { strict.GetString(cur); Console.WriteLine("strict decode OK"); }
            catch (Exception ex) { Console.WriteLine("STILL INVALID: " + ex.Message); }
            File.WriteAllBytes(target, cur);
            Console.WriteLine("written; unmatched=" + unmatched);
            return unmatched;
        }
    }
}
