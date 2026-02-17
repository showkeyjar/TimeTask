using System;

namespace TimeTask
{
    internal enum VoiceListenerState
    {
        Unknown = 0,
        Installing = 1,
        Loading = 2,
        Unavailable = 3,
        Ready = 4,
        Recognizing = 5
    }

    internal sealed class VoiceListenerStatus
    {
        public VoiceListenerState State { get; set; }
        public string Message { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    internal static class VoiceListenerStatusCenter
    {
        private static readonly object Sync = new object();
        private static VoiceListenerStatus _current = new VoiceListenerStatus
        {
            State = VoiceListenerState.Unknown,
            Message = "语音状态未知",
            UpdatedAtUtc = DateTime.UtcNow
        };

        public static event EventHandler<VoiceListenerStatus> StatusChanged;

        public static VoiceListenerStatus Current
        {
            get
            {
                lock (Sync)
                {
                    return new VoiceListenerStatus
                    {
                        State = _current.State,
                        Message = _current.Message,
                        UpdatedAtUtc = _current.UpdatedAtUtc
                    };
                }
            }
        }

        public static void Publish(VoiceListenerState state, string message)
        {
            VoiceListenerStatus snapshot;
            lock (Sync)
            {
                _current = new VoiceListenerStatus
                {
                    State = state,
                    Message = message ?? string.Empty,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                snapshot = new VoiceListenerStatus
                {
                    State = _current.State,
                    Message = _current.Message,
                    UpdatedAtUtc = _current.UpdatedAtUtc
                };
            }

            try
            {
                StatusChanged?.Invoke(null, snapshot);
            }
            catch
            {
                // Keep status center fire-and-forget to avoid breaking runtime logic.
            }
        }
    }
}
