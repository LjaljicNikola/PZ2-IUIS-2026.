using System;

namespace NetworkService.Model
{
    /// <summary>
    /// Jedan red u log datoteci: ID resursa, izmerena vrednost i vreme merenja.
    /// </summary>
    public class LogRow
    {
        public int Id { get; set; }
        public double CurrentValue { get; set; }
        public DateTime Date { get; set; }
    }
}
