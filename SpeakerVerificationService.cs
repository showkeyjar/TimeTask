using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace TimeTask
{
    public sealed class SpeakerVerificationService
    {
        private readonly string _profilePath;
        private SpeakerProfile _profile;

        public SpeakerVerificationService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "TimeTask");
            Directory.CreateDirectory(dir);
            _profilePath = Path.Combine(dir, "speaker_profile.json");
            _profile = LoadProfile();
        }

        public bool HasProfile => _profile != null && _profile.Vector?.Length > 0;

        public void Enroll(byte[] pcm16, int sampleRate)
        {
            var vector = ComputeEmbedding(pcm16, sampleRate);
            if (vector == null || vector.Length == 0)
                return;

            if (_profile == null)
            {
                _profile = new SpeakerProfile
                {
                    Vector = vector,
                    Samples = 1
                };
            }
            else
            {
                int n = Math.Max(1, _profile.Samples);
                for (int i = 0; i < vector.Length; i++)
                {
                    _profile.Vector[i] = (_profile.Vector[i] * n + vector[i]) / (n + 1);
                }
                _profile.Samples = n + 1;
            }

            SaveProfile(_profile);
        }

        public double Verify(byte[] pcm16, int sampleRate)
        {
            if (!HasProfile) return 0;
            var vector = ComputeEmbedding(pcm16, sampleRate);
            if (vector == null || vector.Length == 0)
                return 0;

            return CosineSimilarity(_profile.Vector, vector);
        }

        private static double CosineSimilarity(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        private static SpeakerProfile LoadProfile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SpeakerProfile>(json);
            }
            catch
            {
                return null;
            }
        }

        private SpeakerProfile LoadProfile()
        {
            return LoadProfile(_profilePath);
        }

        private void SaveProfile(SpeakerProfile profile)
        {
            try
            {
                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_profilePath, json);
            }
            catch { }
        }

        private static double[] ComputeEmbedding(byte[] pcm16, int sampleRate)
        {
            if (pcm16 == null || pcm16.Length < 3200) return null;
            if (sampleRate <= 0) return null;

            var samples = ToFloatSamples(pcm16);
            int frameSize = (int)(0.025 * sampleRate); // 25ms
            int hop = (int)(0.010 * sampleRate); // 10ms
            if (frameSize <= 0 || hop <= 0) return null;

            var feats = new List<double[]>();
            for (int start = 0; start + frameSize <= samples.Length; start += hop)
            {
                var frame = samples.Skip(start).Take(frameSize).ToArray();
                feats.Add(ExtractFeatures(frame, sampleRate));
            }

            if (feats.Count == 0) return null;
            int dim = feats[0].Length;
            var avg = new double[dim];
            foreach (var f in feats)
            {
                for (int i = 0; i < dim; i++) avg[i] += f[i];
            }
            for (int i = 0; i < dim; i++) avg[i] /= feats.Count;
            return avg;
        }

        private static float[] ToFloatSamples(byte[] pcm16)
        {
            int count = pcm16.Length / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = BitConverter.ToInt16(pcm16, i * 2);
                samples[i] = s / 32768f;
            }
            return samples;
        }

        private static double[] ExtractFeatures(float[] frame, int sampleRate)
        {
            double rms = Math.Sqrt(frame.Select(x => x * x).Average() + 1e-12);
            double zcr = 0;
            for (int i = 1; i < frame.Length; i++)
            {
                if ((frame[i - 1] >= 0) != (frame[i] >= 0)) zcr++;
            }
            zcr /= frame.Length;

            int nfft = 512;
            var spectrum = PowerSpectrum(frame, nfft);
            double centroid = SpectralCentroid(spectrum, sampleRate);
            double rolloff = SpectralRolloff(spectrum, sampleRate, 0.85);
            var bands = BandEnergies(spectrum, sampleRate, 8);

            var features = new List<double> { rms, zcr, centroid, rolloff };
            features.AddRange(bands);
            return features.ToArray();
        }

        private static double[] PowerSpectrum(float[] frame, int nfft)
        {
            var complex = new Complex[nfft];
            int len = Math.Min(frame.Length, nfft);
            for (int i = 0; i < len; i++)
            {
                complex[i] = new Complex(frame[i], 0);
            }
            for (int i = len; i < nfft; i++) complex[i] = Complex.Zero;

            FFT(complex);
            int bins = nfft / 2;
            var power = new double[bins];
            for (int i = 0; i < bins; i++)
            {
                power[i] = complex[i].Magnitude * complex[i].Magnitude;
            }
            return power;
        }

        private static void FFT(Complex[] buffer)
        {
            int n = buffer.Length;
            int bits = (int)(Math.Log(n) / Math.Log(2));
            for (int j = 1, i = 0; j < n; j++)
            {
                int bit = n >> 1;
                for (; i >= bit; bit >>= 1) i -= bit;
                i += bit;
                if (j < i)
                {
                    var temp = buffer[j];
                    buffer[j] = buffer[i];
                    buffer[i] = temp;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    for (int j = 0; j < len / 2; j++)
                    {
                        Complex u = buffer[i + j];
                        Complex v = buffer[i + j + len / 2] * w;
                        buffer[i + j] = u + v;
                        buffer[i + j + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }

        private static double SpectralCentroid(double[] spectrum, int sampleRate)
        {
            double num = 0, den = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                double freq = (i * sampleRate) / (2.0 * spectrum.Length);
                num += freq * spectrum[i];
                den += spectrum[i];
            }
            if (den == 0) return 0;
            return num / den;
        }

        private static double SpectralRolloff(double[] spectrum, int sampleRate, double threshold)
        {
            double total = spectrum.Sum();
            double target = total * threshold;
            double sum = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                sum += spectrum[i];
                if (sum >= target)
                {
                    return (i * sampleRate) / (2.0 * spectrum.Length);
                }
            }
            return sampleRate / 2.0;
        }

        private static double[] BandEnergies(double[] spectrum, int sampleRate, int bands)
        {
            var energies = new double[bands];
            int len = spectrum.Length;
            for (int b = 0; b < bands; b++)
            {
                int start = (int)(b * len / (double)bands);
                int end = (int)((b + 1) * len / (double)bands);
                double sum = 0;
                for (int i = start; i < end && i < len; i++) sum += spectrum[i];
                energies[b] = sum / Math.Max(1, end - start);
            }
            return energies;
        }

        private sealed class SpeakerProfile
        {
            public double[] Vector { get; set; }
            public int Samples { get; set; }
        }
    }
}
