namespace NetworkService.Model
{
    /// <summary>
    /// Predstavlja linijsku vezu između dva CanvasDerPair-a na Network Display prikazu.
    /// </summary>
    public class DerConnection
    {
        public CanvasDerPair From { get; set; }
        public CanvasDerPair To { get; set; }
    }
}
