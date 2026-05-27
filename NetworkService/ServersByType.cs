using NetworkService.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NetworkService
{
    public class ServersByType
    {
        public TypeName TypeName {  get; set; }

        public ObservableCollection<Server> Servers { get; set; }

        public ServersByType(TypeName typeName)
        {
            TypeName = typeName;
            Servers = new ObservableCollection<Server>();
        }
    }
}
