using System;
using System.IO;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// JSON 存储读取工具：主文件损坏（截断 / 坏字节 / 解析失败）时自动回退同目录的 .bak。
    /// AtomicFile 每次写入都会保留上一代内容为 .bak，因此「损坏 = 回退一代」几乎总能找回数据；
    /// 全部失败才返回 null，由调用方按「新建」处理——不再把损坏静默当成空文件清零用户数据。
    /// </summary>
    public static class JsonStore
    {
        /// <summary>
        /// 读取并反序列化一个 JSON 存储。
        /// deserialize 传入具体的反序列化调用（JsonConvert / System.Text.Json 均可），
        /// 以便本类保持与序列化器无关。
        /// </summary>
        public static T Load<T>(string path, Func<string, T> deserialize) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || deserialize == null)
            {
                return null;
            }

            T result = TryLoadOne(path, deserialize, isBackup: false);
            if (result != null)
            {
                return result;
            }

            return TryLoadOne(path + ".bak", deserialize, isBackup: true);
        }

        /// <summary>
        /// 文本级读取：主文件不存在或读取失败时回退 .bak；都失败返回 null。
        /// 适合调用方自己解析（如 JsonDocument.Parse）的场景。
        /// </summary>
        public static string LoadText(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string main = TryReadOneText(path, isBackup: false);
            if (main != null)
            {
                return main;
            }

            return TryReadOneText(path + ".bak", isBackup: true);
        }

        private static string TryReadOneText(string path, bool isBackup)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                if (isBackup)
                {
                    VoiceRuntimeLog.Warn($"主文件读取失败，已从备份恢复文本：{path}");
                }
                return text;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn(
                    $"文本读取失败：{path}" + (isBackup ? "" : "，尝试 .bak"),
                    ex);
                return null;
            }
        }

        private static T TryLoadOne<T>(string path, Func<string, T> deserialize, bool isBackup) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                T result = deserialize(json);
                if (result != null)
                {
                    if (isBackup)
                    {
                        VoiceRuntimeLog.Warn($"主 JSON 损坏，已从备份恢复：{path}");
                    }
                    return result;
                }

                // 空文本等场景：反序列化器返回 null，按损坏处理。
                VoiceRuntimeLog.Warn(
                    $"JSON 反序列化为空：{path}" + (isBackup ? "" : "，尝试 .bak"),
                    null);
                return null;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn(
                    $"JSON 读取失败：{path}" + (isBackup ? "" : "，尝试 .bak"),
                    ex);
                return null;
            }
        }
    }
}
