using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// 原子文件写入工具。
    /// 所有用户数据（CSV/JSON）落盘都应经过这里：先写同目录临时文件，再用
    /// File.Replace 替换目标并保留一份 .bak。断电/崩溃时目标文件要么是完整的
    /// 旧内容、要么是完整的新内容，绝不会出现半截文件；最坏情况还能从 .bak 找回上一代数据。
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// 目标文件旁保留的上一代备份后缀（例如 1.csv.bak）。
        /// </summary>
        private const string BackupSuffix = ".bak";

        /// <summary>
        /// 原子写入全部文本（UTF-8 无 BOM 由调用方决定，默认与 File.ReadAllText 约定一致使用 UTF-8）。
        /// </summary>
        public static void WriteAllText(string path, string contents, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目标路径不能为空。", nameof(path));
            }

            encoding = encoding ?? Encoding.UTF8;
            WriteCore(path, tempPath =>
            {
                File.WriteAllText(tempPath, contents, encoding);
            });
        }

        /// <summary>
        /// 原子写入逐行文本。
        /// </summary>
        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目标路径不能为空。", nameof(path));
            }

            encoding = encoding ?? Encoding.UTF8;
            WriteCore(path, tempPath =>
            {
                File.WriteAllLines(tempPath, lines, encoding);
            });
        }

        /// <summary>
        /// 原子写入字节。
        /// </summary>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目标路径不能为空。", nameof(path));
            }
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            WriteCore(path, tempPath =>
            {
                File.WriteAllBytes(tempPath, bytes);
            });
        }

        private static void WriteCore(string path, Action<string> writeToTemp)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            string backupPath = path + BackupSuffix;

            // 1) 写入同目录临时文件（同卷保证 Replace/Move 的原子性）。
            writeToTemp(tempPath);

            try
            {
                if (File.Exists(path))
                {
                    // 2) 替换目标并把旧内容留作 .bak。
                    File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    // 首次写入：目标不存在，直接原子改名。
                    File.Delete(tempPath + ".bak");
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(tempPath, path);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // File.Replace 在某些文件系统/被占用场景会失败：退化为「删旧→改名」，
                /// 仍优于直接覆盖写（直接写中途崩溃必损坏）。
                VoiceRuntimeLog.Error($"AtomicFile 替换失败，退化为 Move 覆盖：{path}", ex);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Copy(path, backupPath, overwrite: true);
                        File.Delete(path);
                    }
                    File.Move(tempPath, path);
                }
                catch (Exception ex2)
                {
                    VoiceRuntimeLog.Error($"AtomicFile 写入最终失败：{path}", ex2);
                    throw;
                }
            }
        }
    }
}
