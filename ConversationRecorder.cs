using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TimeTask
{
    public enum ConversationType
    {
        Unknown,
        Meeting,
        Dialog,
        Monologue,
        PhoneCall
    }

    public class ConversationSegment
    {
        public string SegmentId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string RecognizedText { get; set; }
        public string AudioFilePath { get; set; }
        public float Confidence { get; set; }
        public string SpeakerId { get; set; }
    }

    public class ConversationSession
    {
        public string SessionId { get; set; }
        public ConversationType Type { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<ConversationSegment> Segments { get; set; } = new List<ConversationSegment>();
        public string Summary { get; set; }
        public List<string> ExtractedTasks { get; set; } = new List<string>();
        public List<string> Participants { get; set; } = new List<string>();
        public bool IsProcessed { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    public class ConversationRecorder
    {
        private readonly string _audioStoragePath;
        private readonly string _sessionsDataPath;
        private readonly object _lock = new object();
        private List<ConversationSession> _sessions;
        private ConversationSession _currentSession;
        private List<byte> _currentAudioBuffer;
        private DateTime _sessionStartTime;
        private bool _isRecording;
        private bool _conversationDetectedRaised;
        private const int MinConversationTurns = 3;
        private const int MinConversationDurationSeconds = 10;
        private const int MaxSessionDurationMinutes = 120;

        public event Action<ConversationSession> ConversationDetected;
        public event Action<ConversationSession> ConversationEnded;

        public ConversationRecorder(string appDataPath)
        {
            _audioStoragePath = Path.Combine(appDataPath, "conversations", "audio");
            _sessionsDataPath = Path.Combine(appDataPath, "conversations", "sessions.json");
            
            Directory.CreateDirectory(_audioStoragePath);
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionsDataPath));
            
            _sessions = new List<ConversationSession>();
            _currentAudioBuffer = new List<byte>();
            LoadSessions();
        }

        public void StartSession()
        {
            lock (_lock)
            {
                if (_isRecording) return;

                _isRecording = true;
                _sessionStartTime = DateTime.Now;
                _conversationDetectedRaised = false;
                _currentAudioBuffer.Clear();

                _currentSession = new ConversationSession
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    StartTime = _sessionStartTime,
                    Type = ConversationType.Unknown,
                    Segments = new List<ConversationSegment>(),
                    IsProcessed = false
                };

                Console.WriteLine($"[ConversationRecorder] Started new session: {_currentSession.SessionId}");
            }
        }

        public void AddAudioSegment(byte[] audioData, string recognizedText, float confidence, string speakerId = null)
        {
            lock (_lock)
            {
                if (!_isRecording || _currentSession == null) return;

                if (audioData != null && audioData.Length > 0)
                {
                    _currentAudioBuffer.AddRange(audioData);
                }

                var segment = new ConversationSegment
                {
                    SegmentId = Guid.NewGuid().ToString("N"),
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now,
                    RecognizedText = recognizedText,
                    Confidence = confidence,
                    SpeakerId = speakerId ?? "unknown"
                };

                _currentSession.Segments.Add(segment);

                DetectConversationType();
                CheckSessionCompletion();
            }
        }

        private void DetectConversationType()
        {
            if (_currentSession == null || _currentSession.Segments.Count == 0) return;

            var allText = string.Join(" ", _currentSession.Segments.Select(s => s.RecognizedText));
            
            if (Regex.IsMatch(allText, @"(会议|开会|讨论|评审|同步|汇报|演示)", RegexOptions.IgnoreCase))
            {
                _currentSession.Type = ConversationType.Meeting;
            }
            else if (Regex.IsMatch(allText, @"(电话|通话|接电话|打电话)", RegexOptions.IgnoreCase))
            {
                _currentSession.Type = ConversationType.PhoneCall;
            }
            else if (_currentSession.Segments.Count >= MinConversationTurns)
            {
                _currentSession.Type = ConversationType.Dialog;
            }
            else
            {
                _currentSession.Type = ConversationType.Monologue;
            }

            ExtractParticipants();

            if (!_conversationDetectedRaised &&
                _currentSession.Segments.Count >= MinConversationTurns &&
                _currentSession.Type != ConversationType.Monologue &&
                _currentSession.Type != ConversationType.Unknown)
            {
                _conversationDetectedRaised = true;
                ConversationDetected?.Invoke(_currentSession);
            }
        }

        private void ExtractParticipants()
        {
            var speakers = _currentSession.Segments
                .Select(s => s.SpeakerId)
                .Distinct()
                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "unknown")
                .ToList();

            _currentSession.Participants = speakers;
        }

        private void CheckSessionCompletion()
        {
            if (_currentSession == null) return;

            var duration = DateTime.Now - _currentSession.StartTime;

            if (duration.TotalMinutes >= MaxSessionDurationMinutes)
            {
                EndSession();
                return;
            }

            if (_currentSession.Type == ConversationType.Meeting && duration.TotalMinutes >= 30)
            {
                if (_currentSession.Segments.Count >= MinConversationTurns)
                {
                    EndSession();
                }
            }
            else if (_currentSession.Type == ConversationType.Dialog && duration.TotalMinutes >= 15)
            {
                if (_currentSession.Segments.Count >= MinConversationTurns)
                {
                    EndSession();
                }
            }
        }

        public void EndSession()
        {
            lock (_lock)
            {
                if (!_isRecording || _currentSession == null) return;

                _isRecording = false;
                _currentSession.EndTime = DateTime.Now;

                var duration = _currentSession.EndTime.Value - _currentSession.StartTime;
                
                if (duration.TotalSeconds >= MinConversationDurationSeconds && _currentSession.Segments.Count >= 2)
                {
                    SaveAudioData();
                    ExtractTasksFromConversation();
                    
                    _sessions.Add(_currentSession);
                    SaveSessions();
                    
                    ConversationEnded?.Invoke(_currentSession);
                    Console.WriteLine($"[ConversationRecorder] Ended session: {_currentSession.SessionId}, Type: {_currentSession.Type}, Duration: {duration.TotalMinutes:F1}min");
                }
                else
                {
                    Console.WriteLine($"[ConversationRecorder] Discarded session (too short): {_currentSession.SessionId}");
                }

                _currentSession = null;
                _currentAudioBuffer.Clear();
            }
        }

        private void SaveAudioData()
        {
            if (_currentSession == null || _currentAudioBuffer.Count == 0) return;

            try
            {
                string dateFolder = _currentSession.StartTime.ToString("yyyy-MM-dd");
                string datePath = Path.Combine(_audioStoragePath, dateFolder);
                Directory.CreateDirectory(datePath);

                string fileName = $"{_currentSession.StartTime:HHmmss}_{_currentSession.SessionId}.wav";
                string filePath = Path.Combine(datePath, fileName);
                WritePcm16MonoWav(filePath, _currentAudioBuffer.ToArray(), 16000);

                _currentSession.Segments.ForEach(s => s.AudioFilePath = filePath);
                
                Console.WriteLine($"[ConversationRecorder] Saved audio: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationRecorder] Failed to save audio: {ex.Message}");
            }
        }

        private static void WritePcm16MonoWav(string filePath, byte[] pcm, int sampleRate)
        {
            pcm = pcm ?? new byte[0];
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs, Encoding.ASCII))
            {
                int byteRate = sampleRate * 2;
                short blockAlign = 2;
                short bitsPerSample = 16;
                int subchunk2Size = pcm.Length;
                int chunkSize = 36 + subchunk2Size;

                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(chunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(subchunk2Size);
                if (subchunk2Size > 0)
                {
                    bw.Write(pcm);
                }
            }
        }

        private void ExtractTasksFromConversation()
        {
            if (_currentSession == null) return;

            var intentRecognizer = new IntentRecognizer();
            var extractedTasks = new List<string>();

            foreach (var segment in _currentSession.Segments)
            {
                if (intentRecognizer.IsPotentialTask(segment.RecognizedText))
                {
                    var taskDesc = intentRecognizer.ExtractTaskDescription(segment.RecognizedText);
                    if (!string.IsNullOrWhiteSpace(taskDesc))
                    {
                        extractedTasks.Add(taskDesc);
                    }
                }
            }

            _currentSession.ExtractedTasks = extractedTasks.Distinct().ToList();
        }

        public List<ConversationSession> GetSessions(ConversationType? type = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            lock (_lock)
            {
                var query = _sessions.AsEnumerable();

                if (type.HasValue)
                {
                    query = query.Where(s => s.Type == type.Value);
                }

                if (startDate.HasValue)
                {
                    query = query.Where(s => s.StartTime >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(s => s.StartTime <= endDate.Value);
                }

                return query.OrderByDescending(s => s.StartTime).ToList();
            }
        }

        public ConversationSession GetSession(string sessionId)
        {
            lock (_lock)
            {
                return _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            }
        }

        public void MarkSessionAsProcessed(string sessionId)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.IsProcessed = true;
                    session.ProcessedAt = DateTime.Now;
                    SaveSessions();
                }
            }
        }

        public void UpdateSessionSummary(string sessionId, string summary)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.Summary = summary;
                    SaveSessions();
                }
            }
        }

        public void DeleteSession(string sessionId)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    DeleteAudioFiles(session);
                    _sessions.Remove(session);
                    SaveSessions();
                }
            }
        }

        private void DeleteAudioFiles(ConversationSession session)
        {
            try
            {
                var audioFiles = session.Segments
                    .Select(s => s.AudioFilePath)
                    .Distinct()
                    .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f))
                    .ToList();

                foreach (var audioFile in audioFiles)
                {
                    File.Delete(audioFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationRecorder] Failed to delete audio files: {ex.Message}");
            }
        }

        public void CleanupOldSessions(int retentionDays = 30)
        {
            lock (_lock)
            {
                var cutoffDate = DateTime.Now.AddDays(-retentionDays);
                var oldSessions = _sessions.Where(s => s.StartTime < cutoffDate).ToList();

                foreach (var session in oldSessions)
                {
                    DeleteAudioFiles(session);
                }

                _sessions = _sessions.Where(s => s.StartTime >= cutoffDate).ToList();
                SaveSessions();
            }
        }

        private void LoadSessions()
        {
            try
            {
                if (File.Exists(_sessionsDataPath))
                {
                    string json = File.ReadAllText(_sessionsDataPath);
                    var serializer = new JavaScriptSerializer();
                    _sessions = serializer.Deserialize<List<ConversationSession>>(json) ?? new List<ConversationSession>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationRecorder] Failed to load sessions: {ex.Message}");
                _sessions = new List<ConversationSession>();
            }
        }

        private void SaveSessions()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_sessions);
                File.WriteAllText(_sessionsDataPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationRecorder] Failed to save sessions: {ex.Message}");
            }
        }

        public bool IsRecording => _isRecording;
        public ConversationSession CurrentSession => _currentSession;
    }
}
