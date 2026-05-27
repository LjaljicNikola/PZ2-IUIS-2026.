using System;

namespace NetworkService.Model
{
    /// <summary>
    /// CG4 - Evidencija jedne izvršene akcije za History paletu.
    /// </summary>
    public class ActionRecord
    {
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }

        public ActionRecord(string description)
        {
            Description = description;
            Timestamp = DateTime.Now;
        }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] {Description}";
        }
    }
}
