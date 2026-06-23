using GalaSoft.MvvmLight.Messaging;
using NetworkService.Helpers;
using System.ComponentModel;

namespace NetworkService.Model
{
    /// <summary>
    /// T4 - Distribuirani energetski resurs (DER).
    /// Validna izmerena vrednost: 1 do 5 MW.
    /// </summary>
    public class DerResource : ValidationBase, INotifyPropertyChanged
    {
        private static int _idCounter = 0;

        private int _id;
        private string _name;
        private DerResourceType _resourceType;
        private double _currentValue;

        public DerResource(string name, DerResourceType resourceType)
        {
            _id = ++_idCounter;
            _name = name;
            _resourceType = resourceType;
            _currentValue = 0;
        }

        internal DerResource(string name, DerResourceType resourceType, bool formOnly)
        {
            _id = 0;
            _name = name;
            _resourceType = resourceType;
            _currentValue = 0;
        }

        public int Id
        {
            get => _id;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public DerResourceType ResourceType
        {
            get => _resourceType;
            set
            {
                if (_resourceType != value)
                {
                    _resourceType = value;
                    OnPropertyChanged(nameof(ResourceType));
                }
            }
        }

        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue != value)
                {
                    _currentValue = value;
                    OnPropertyChanged(nameof(CurrentValue));
                    OnPropertyChanged(nameof(IsValueValid));
                    // Notify NetworkDisplayViewModel and MeasurementGraphViewModel
                    Messenger.Default.Send(this);
                }
            }
        }

        /// <summary>
        /// Shortcut property for ComboBox binding in forms.
        /// </summary>
        public DerTypeName TypeName
        {
            get => _resourceType.Name;
            set
            {
                if (_resourceType.Name != value)
                {
                    _resourceType = new DerResourceType(value);
                    OnPropertyChanged(nameof(TypeName));
                    OnPropertyChanged(nameof(ResourceType));
                }
            }
        }

        /// <summary>
        /// Returns true if CurrentValue is within 1–5 MW.
        /// </summary>
        public bool IsValueValid => _currentValue >= 1.0 && _currentValue <= 5.0;

        protected override void ValidateSelf()
        {
            if (string.IsNullOrWhiteSpace(_name))
                ValidationErrors[nameof(Name)] = "Resource name is required.";
            else if (_name.Length > 50)
                ValidationErrors[nameof(Name)] = "Name must not exceed 50 characters.";
        }
    }
}
