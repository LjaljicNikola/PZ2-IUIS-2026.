namespace NetworkService.Model
{
    /// <summary>
    /// T4 - Mogući tipovi DER entiteta.
    /// </summary>
    public enum DerTypeName
    {
        SolarPanel,
        Windgenerator
    }

    /// <summary>
    /// Tip DER resursa sa imenom i putanjom do slike.
    /// </summary>
    public class DerResourceType
    {
        public DerTypeName Name { get; }
        public string ImagePath { get; }

        public DerResourceType(DerTypeName name)
        {
            Name = name;
            ImagePath = GetImagePath(name);
        }

        private static string GetImagePath(DerTypeName name)
        {
            switch (name)
            {
                case DerTypeName.SolarPanel:
                    return "pack://application:,,,/NetworkService;component/DerTypeImages/SolarPanel.png";
                case DerTypeName.Windgenerator:
                    return "pack://application:,,,/NetworkService;component/DerTypeImages/Windgenerator.png";
                default:
                    return null;
            }
        }
    }
}
