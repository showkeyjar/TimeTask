using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TimeTask.Repair
{
    public static class EncodingRepair2
    {
        public static int Run(string target, string reference, bool apply)
        {
            var strict = new UTF8Encoding(false, true);
            var lenient = new UTF8Encoding(false, false);
            byte[] cur = File.ReadAllBytes(target);
            // 参照文本行尾归一化（当前文件在中文后的 \r 被吸收丢失，统一按 \n 匹配）
            string refText = lenient.GetString(File.ReadAllBytes(reference)).Replace("\r\n", "\n");

            // 1) walk & find damage cluster starts
            var clusters = new List<int>();
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

                // 损坏簇起点；跳过整个簇（lead+残缺字节+0x3F）
                if (clusters.Count == 0 || i > clusters[clusters.Count - 1] + 8) clusters.Add(i);
                i++;
            }

            Console.WriteLine("damage clusters: " + clusters.Count + " in " + Path.GetFileName(target));
            if (clusters.Count == 0) return 0;

            var starts = new List<int>();
            var ends = new List<int>();
            var texts = new List<string>();
            int unmatched = 0;
            foreach (int s in clusters)
            {
                // 簇终点：吞掉无效序列直到（不含）下一个合法 ASCII 可见字符或换行
                int e = s;
                while (e < cur.Length && e < s + 6)
                {
                    byte bb = cur[e];
                    if (bb == 0x0A || (bb >= 0x20 && bb < 0x7F && bb != 0x3F)) break;
                    e++;
                }

                int beforeStart = Math.Max(0, s - 60);
                string beforeCtx = lenient.GetString(cur, beforeStart, s - beforeStart).Replace("\r\n", "\n");
                int tailLen = Math.Min(20, beforeCtx.Length);
                string tail = tailLen > 0 ? beforeCtx.Substring(beforeCtx.Length - tailLen) : "";

                int afterLen = Math.Min(48, cur.Length - e);
                string afterCtx = lenient.GetString(cur, e, afterLen).Replace("\r\n", "\n");
                int headLen = Math.Min(20, afterCtx.Length);
                string head = headLen > 0 ? afterCtx.Substring(0, headLen) : "";

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

                if (repaired != null)
                {
                    Console.WriteLine(string.Format("repair @{0}: [{1}]", s, repaired.Replace("\r", "\\r").Replace("\n", "\\n")));
                    starts.Add(s); ends.Add(e); texts.Add(repaired);
                }
                else
                {
                    Console.WriteLine(string.Format("UNMATCHED @{0}: ...{1}[?]{2}...", s, tail, head));
                    unmatched++;
                    // 用 ? 占位，保证文件回到合法 UTF-8（可继续用编辑工具手工修）
                    starts.Add(s); ends.Add(e); texts.Add("?");
                }
            }

            if (!apply) { Console.WriteLine("preview only; unmatched=" + unmatched); return unmatched; }

            for (int r = starts.Count - 1; r >= 0; r--)
            {
                byte[] rep = Encoding.UTF8.GetBytes(texts[r]);
                byte[] merged = new byte[cur.Length - (ends[r] - starts[r]) + rep.Length];
                Array.Copy(cur, 0, merged, 0, starts[r]);
                Array.Copy(rep, 0, merged, starts[r], rep.Length);
                Array.Copy(cur, ends[r], merged, starts[r] + rep.Length, cur.Length - ends[r]);
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
