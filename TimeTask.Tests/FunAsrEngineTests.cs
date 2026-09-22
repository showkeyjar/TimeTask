using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// 高精度引擎（FunASR/SenseVoice）的纯逻辑契约：引擎选择解析、JSON 行协议解析、
    /// WAV 头构造、分段决策。识别质量靠模型，但这些边界错了整条链路就断了。
    /// </summary>
    [TestClass]
    public class FunAsrEngineTests
    {
        // ---------- 引擎选择 ----------

        [TestMethod]
        public void ResolveEngine_DefaultsToAutoFunAsrWithFallback()
        {
            foreach (var raw in new[] { null, "", "  ", "auto", "AUTO", "随便写" })
            {
                var c = AsrEngineChoice.Resolve(raw);
                Assert.AreEqual(AsrEngineKind.FunAsr, c.Kind, $"raw='{raw}' 应解析为 FunAsr");
                Assert.IsTrue(c.AllowVoskFallback, $"raw='{raw}' 默认必须允许回落 Vosk");
            }
        }

        [TestMethod]
        public void ResolveEngine_ExplicitKinds()
        {
            var vosk = AsrEngineChoice.Resolve(" vosk ");
            Assert.AreEqual(AsrEngineKind.Vosk, vosk.Kind);
            Assert.IsFalse(vosk.AllowVoskFallback);

            var funasr = AsrEngineChoice.Resolve("FunASR");
            Assert.AreEqual(AsrEngineKind.FunAsr, funasr.Kind);
            Assert.IsFalse(funasr.AllowVoskFallback, "显式 funasr 不回落（用户明确只要高精度）");
        }

        // ---------- JSON 行协议 ----------

        [TestMethod]
        public void FromJsonLine_OkResultCarriesTextAndConfidence()
        {
            var r = FunAsrResult.FromJsonLine("{\"ok\":true,\"text\":\"明天上午九点开评审会\",\"confidence\":0.87}");
            Assert.IsTrue(r.Ok);
            Assert.AreEqual("明天上午九点开评审会", r.Text);
            Assert.AreEqual(0.87, r.Confidence, 1e-6);
        }

        [TestMethod]
        public void FromJsonLine_ErrorAndBadLinesFailGracefully()
        {
            Assert.IsFalse(FunAsrResult.FromJsonLine("{\"ok\":false,\"error\":\"wav not found\"}").Ok);
            Assert.IsFalse(FunAsrResult.FromJsonLine("not json at all").Ok);
            Assert.IsFalse(FunAsrResult.FromJsonLine("").Ok);
            Assert.IsFalse(FunAsrResult.FromJsonLine(null).Ok);
            // 缺 confidence 字段：回落默认值而不是抛异常
            var r = FunAsrResult.FromJsonLine("{\"ok\":true,\"text\":\"hi\"}");
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(0.5, r.Confidence, 1e-6);
        }

        // ---------- WAV 构造 ----------

        [TestMethod]
        public void BuildWav16kMono_ProducesCorrectRiffHeader()
        {
            byte[] pcm = new byte[3200]; // 0.1s
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i % 251);
            byte[] wav = FunAsrEngine.BuildWav16kMono(pcm);

            Assert.AreEqual(44 + 3200, wav.Length);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual(36 + 3200, BitConverter.ToInt32(wav, 4));
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.AreEqual(16, BitConverter.ToInt32(wav, 16));       // fmt 大小
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 20));        // PCM
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22));        // 单声道
            Assert.AreEqual(16000, BitConverter.ToInt32(wav, 24));    // 采样率
            Assert.AreEqual(32000, BitConverter.ToInt32(wav, 28));    // 字节率
            Assert.AreEqual(2, BitConverter.ToInt16(wav, 32));        // 块对齐
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));       // 位深
            Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
            Assert.AreEqual(3200, BitConverter.ToInt32(wav, 40));
            for (int i = 0; i < pcm.Length; i++) Assert.AreEqual(pcm[i], wav[44 + i]);
        }

        [TestMethod]
        public void BuildWav16kMono_EmptyPayloadStillValid()
        {
            byte[] wav = FunAsrEngine.BuildWav16kMono(null);
            Assert.AreEqual(44, wav.Length);
            Assert.AreEqual(0, BitConverter.ToInt32(wav, 40));
        }

        // ---------- 分段决策 ----------

        [TestMethod]
        public void ShouldFlush_ForcesAtMaxSegmentLength()
        {
            // 12s = 384000 字节：达到即切，无论静音尾巴
            Assert.IsTrue(FunAsrSegmenter.ShouldFlush(384000, 0, 16000));
            Assert.IsFalse(FunAsrSegmenter.ShouldFlush(383999, 0, 16000));
        }

        [TestMethod]
        public void ShouldFlush_EarlyOnTrailingQuiet()
        {
            // ≥3s 且尾部连续静音 ≥1s（32000 字节）→ 提前切（说到停顿处断句，精度最好）
            Assert.IsTrue(FunAsrSegmenter.ShouldFlush(3 * 32000, 32000, 16000));
            // 达到最小说长但没有静音尾：不切（话还没说完）
            Assert.IsFalse(FunAsrSegmenter.ShouldFlush(3 * 32000, 31999, 16000));
            // 不足最小说长：即便静音也不切（避免碎片段）
            Assert.IsFalse(FunAsrSegmenter.ShouldFlush(3 * 32000 - 1, 3 * 32000, 16000));
        }

        [TestMethod]
        public void ShouldFlush_NeverOnEmpty()
        {
            Assert.IsFalse(FunAsrSegmenter.ShouldFlush(0, 999999, 16000));
        }

        // ---------- 能量计算 ----------

        [TestMethod]
        public void Rms_DistinguishesSilenceFromSpeech()
        {
            Assert.AreEqual(0.0, FunAsrSegmenter.Rms(new byte[3200]), 1e-9); // 纯静音
            // 满幅方波（32767/-32768 交替）：RMS 接近满幅
            byte[] loud = new byte[3200];
            for (int i = 0; i < loud.Length / 2; i++)
            {
                short v = (i % 2 == 0) ? (short)32767 : (short)-32768;
                loud[i * 2] = (byte)(v & 0xFF);
                loud[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            Assert.IsTrue(FunAsrSegmenter.Rms(loud) > 30000);
            Assert.IsTrue(FunAsrSegmenter.Rms(null) == 0);
        }
    }
}
