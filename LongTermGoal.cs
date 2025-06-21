using System;

namespace TimeTask
{
    public class LongTermGoal
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string TotalDuration { get; set; }
        public DateTime CreationDate { get; set; }
        public bool IsActive { get; set; }

        public LongTermGoal()
        {
            Id = Guid.NewGuid().ToString();
            CreationDate = DateTime.Now;
            IsActive = false; // Default to not active, will be set explicitly
        }
    }
}
