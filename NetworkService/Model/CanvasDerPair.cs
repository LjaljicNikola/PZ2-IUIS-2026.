namespace NetworkService.Model
{
    /// <summary>
    /// Povezuje ID canvas-a sa DER resursom koji je na njemu prikazan.
    /// </summary>
    public class CanvasDerPair
    {
        public int CanvasId { get; set; }
        public DerResource Resource { get; set; }
    }
}
