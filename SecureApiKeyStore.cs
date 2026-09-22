using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// LLM API Key 的加密存储（Windows DPAPI，CurrentUser 作用域）。
    /// 明文 Key 放在 exe 旁的 App.config 有两个问题：
    /// 1) 配置文件会随目录拷贝/备份/截图一起泄露 Key；
    /// 2) 装在 Program Files 时修改配置还需要管理员权限。
    /// 这里把 Key 存到用户数据目录的加密文件（绑定当前用户+本机，拷走也解不开），
    /// 首次读到明文配置 Key 时自动迁入加密存储，并尽力清除配置里的明文。
    /// </summary>
    public static class SecureApiKeyStore
    {
        // 附加熵：降低同机其他程序"顺手解密"的可能（DPAPI 本身绑定用户，熵是额外一道）
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TimeTask.LlmKey.v1");

        private static string StorePath => AppPaths.GetDataFile("llm_key.bin");

        /// <summary>读取加密 Key；不存在或解密失败返回 null（调用方回落到配置明文）。</summary>
        public static string Load()
        {
            try
            {
                string path = StorePath;
                if (!File.Exists(path))
                {
                    return null;
                }
                byte[] cipher = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn($"读取加密 API Key 失败：{StorePath}", ex);
                return null;
            }
        }

        /// <summary>写入加密 Key（原子写）。</summary>
        public static void Save(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
            string path = StorePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            AtomicFile.WriteAllBytes(path, cipher);
        }

        /// <summary>删除加密存储（用户在设置里清空 Key 时调用）。</summary>
        public static void Delete()
        {
            try
            {
                string path = StorePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn("删除加密 API Key 失败。", ex);
            }
        }

        /// <summary>
        /// 把配置里的明文 Key 迁入加密存储，并尽力把配置项清成占位符。
        /// 清明文可能因权限失败（Program Files 只读）——没关系，加密副本已落盘，
        /// 下次启动仍然优先读加密存储。
        /// </summary>
        public static void MigrateFromPlaintextConfig(string configKey, string plaintextValue)
        {
            try
            {
                Save(plaintextValue);
                VoiceRuntimeLog.Info("API Key 已从明文配置迁入加密存储（DPAPI）。");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("API Key 加密迁移失败，将继续使用配置明文。", ex);
                return; // 加密失败时不动配置，避免两头皆空
            }

            try
            {
                var config = System.Configuration.ConfigurationManager.OpenExeConfiguration(
                    System.Configuration.ConfigurationUserLevel.None);
                var setting = config.AppSettings.Settings[configKey];
                if (setting != null)
                {
                    setting.Value = "(migrated-to-secure-store)";
                    config.Save(System.Configuration.ConfigurationSaveMode.Modified);
                    VoiceRuntimeLog.Info("配置中的明文 API Key 已清除。");
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn("清除配置中的明文 Key 失败（可能无写权限）；加密副本已生效。", ex);
            }
        }
    }
}
