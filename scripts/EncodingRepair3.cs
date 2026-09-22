using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TimeTask.Repair
{
    public static class EncodingRepair3
    {
        public static int Run(string target, string reference, bool apply)
        {
            var strict = new UTF8Encoding(false, true);
            var lenient = new UTF8Encoding(false, false);
            byte[] cur = File.ReadAllBytes(target);
            string refText = lenient.GetString(File.ReadAllBytes(reference)).Replace("\r\n", "\n");

            // ---- pass 1: collect damage clusters (start,end) ----
            var cStart = new List<int>();
            var cEnd = new List<int>();
            int i = 0;
            while (i < cur.Length)
            {
                byte b = cur[i];
                if (b < 0x80) { i++; continue; }
                int need;
                if (b >= 0xF0) need = 3;
                else if (b >= 0xE0) need = 2;
                else if (b >= 0xC0) need = 1;
                else need = 0;

                bool ok = need > 0;
                if (ok)
                {
                    for (int k = 1; k <= need; k++)
                    {
                        if (i + k >= cur.Length) { ok = false; break; }
                        byte nb = cur[i + k];
                        if (nb < 0x80 || nb >= 0xC0) { ok = false; break; }
                    }
                }
                if (ok) { i += 1 + need; continue; }

                int s0 = i;
                int e0 = i;
                while (e0 < cur.Length && e0 < s0 + 6)
                {
                    byte bb = cur[e0];
                    if (bb == 0x0A || (bb >= 0x20 && bb < 0x7F && bb != 0x3F)) break;
                    e0++;
                }
                cStart.Add(s0); cEnd.Add(e0);
                i = e0 > s0 ? e0 : s0 + 1;
            }

            Console.WriteLine("damage clusters: " + cStart.Count + " in " + Path.GetFileName(target));
            if (cStart.Count == 0) return 0;

            // ---- pass 2: sequential repair with clean contexts ----
            var repTexts = new List<string>();
            int unmatched = 0;

            // text of the valid prefix before the first cluster
            string repairedSoFar = lenient.GetString(cur, 0, cStart[0]).Replace("\r\n", "\n");

            for (int idx = 0; idx < cStart.Count; idx++)
            {
                int s = cStart[idx], e = cEnd[idx];
                int segStart = e;
                int segEnd = (idx + 1 < cStart.Count)
                    ? Math.Min(cur.Length, cStart[idx + 1])
                    : Math.Min(cur.Length, e + 64);
                string segment = lenient.GetString(cur, segStart, segEnd - segStart).Replace("\r\n", "\n");

                string tail = repairedSoFar.Length > 20 ? repairedSoFar.Substring(repairedSoFar.Length - 20) : repairedSoFar;
                string head = segment.Length > 20 ? segment.Substring(0, 20) : segment;

                string repaired = null;
                if (tail.Length >= 4 && head.Length >= 2)
                {
                    int posA = refText.LastIndexOf(tail, StringComparison.Ordinal);
                    if (posA >= 0)
                    {
                        int posAEnd = posA + tail.Length;
                        int posB = refText.IndexOf(head, posAEnd, StringComparison.Ordinal);
                        if (posB >= 0 && posB <= posAEnd + 60) repaired = refText.Substring(posAEnd, posB - posAEnd);
                    }
                }

                if (repaired == null)
                {
                    unmatched++;
                    Console.WriteLine(string.Format("UNMATCHED @{0}: ...{1}[?]{2}...", s, tail, head));
                    repaired = "?";
                }
                else
                {
                    Console.WriteLine(string.Format("repair @{0}: [{1}]", s, repaired.Replace("\r", "\\r").Replace("\n", "\\n")));
                }
                repTexts.Add(repaired);
                repairedSoFar += repaired + segment;
            }

            if (!apply) { Console.WriteLine("preview only; unmatched=" + unmatched); return unmatched; }

            // ---- pass 3: merge (from end) & write ----
            for (int r = cStart.Count - 1; r >= 0; r--)
            {
                byte[] rep = Encoding.UTF8.GetBytes(repTexts[r]);
                byte[] merged = new byte[cur.Length - (cEnd[r] - cStart[r]) + rep.Length];
                Array.Copy(cur, 0, merged, 0, cStart[r]);
                Array.Copy(rep, 0, merged, cStart[r], rep.Length);
                Array.Copy(cur, cEnd[r], merged, cStart[r] + rep.Length, cur.Length - cEnd[r]);
                cur = merged;
            }

            try { strict.GetString(cur); Console.WriteLine("strict decode OK"); }
            catch (Exception ex) { Console.WriteLine("STILL INVALID: " + ex.Message); }
            File.WriteAllBytes(target, cur);
            Console.WriteLine("written; unmatched=" + unmatched);
            return unmatched;
        }
    }
}
