using NetworkService.Model;
using System.Collections.ObjectModel;

namespace NetworkService
{
    /// <summary>
    /// Grupacija DER resursa po tipu – koristi se kao izvor podataka za TreeView.
    /// </summary>
    public class DerResourcesByType
    {
        public DerTypeName TypeName { get; set; }
        public ObservableCollection<DerResource> Resources { get; set; }

        public DerResourcesByType(DerTypeName typeName)
        {
            TypeName = typeName;
            Resources = new ObservableCollection<DerResource>();
        }
    }
}
