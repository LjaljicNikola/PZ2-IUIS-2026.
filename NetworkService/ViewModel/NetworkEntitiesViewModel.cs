using GalaSoft.MvvmLight.Messaging;
using NetworkService.Helpers;
using NetworkService.Model;
using Notification.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace NetworkService.ViewModel
{
    public class NetworkEntitiesViewModel : BindableBase
    {
        // x Kolekcija resursa x
        public ObservableCollection<DerResource> Resources { get; set; }

        // x Filtrirani pogled za DataGrid 
        public ICollectionView FilteredView { get; private set; }

        //  Selektovani red u tabeli 
        private DerResource _selectedResource;
        public DerResource SelectedResource
        {
            get => _selectedResource;
            set
            {
                if (_selectedResource != value)
                {
                    _selectedResource = value;
                    OnPropertyChanged(nameof(SelectedResource));
                    DeleteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        //  P1 pretraga: naziv ili tip 
        private bool _isNameFilterSelected = true;
        public bool IsNameFilterSelected
        {
            get => _isNameFilterSelected;
            set
            {
                if (_isNameFilterSelected != value)
                {
                    _isNameFilterSelected = value;
                    OnPropertyChanged(nameof(IsNameFilterSelected));
                    FilteredView.Refresh();
                }
            }
        }

        // IsTypeFilterSelected je suprotno od IsNameFilterSelected – binding pomocnik
        public bool IsTypeFilterSelected
        {
            get => !_isNameFilterSelected;
            set
            {
                bool newNameVal = !value;
                if (_isNameFilterSelected != newNameVal)
                {
                    _isNameFilterSelected = newNameVal;
                    OnPropertyChanged(nameof(IsNameFilterSelected));
                    OnPropertyChanged(nameof(IsTypeFilterSelected));
                    FilteredView.Refresh();
                }
            }
        }

        private string _filterText;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText != value)
                {
                    _filterText = value;
                    OnPropertyChanged(nameof(FilterText));
                    FilteredView.Refresh();
                }
            }
        }

        //  CG4 dodatni filter: van/unutar opsega 
        private bool _showOnlyOutOfRange = false;
        public bool ShowOnlyOutOfRange
        {
            get => _showOnlyOutOfRange;
            set
            {
                if (_showOnlyOutOfRange != value)
                {
                    _showOnlyOutOfRange = value;
                    OnPropertyChanged(nameof(ShowOnlyOutOfRange));

                    // Međusobno isključivi
                    if (value && _showOnlyInRange)
                    {
                        _showOnlyInRange = false;
                        OnPropertyChanged(nameof(ShowOnlyInRange));
                    }
                    FilteredView.Refresh();
                }
            }
        }

        private bool _showOnlyInRange = false;
        public bool ShowOnlyInRange
        {
            get => _showOnlyInRange;
            set
            {
                if (_showOnlyInRange != value)
                {
                    _showOnlyInRange = value;
                    OnPropertyChanged(nameof(ShowOnlyInRange));

                    if (value && _showOnlyOutOfRange)
                    {
                        _showOnlyOutOfRange = false;
                        OnPropertyChanged(nameof(ShowOnlyOutOfRange));
                    }
                    FilteredView.Refresh();
                }
            }
        }

        //  Form za dodavanje 
        private DerResource _currentResource;
        public DerResource CurrentResource
        {
            get => _currentResource;
            set { _currentResource = value; OnPropertyChanged(nameof(CurrentResource)); }
        }

        public IEnumerable<DerTypeName> ResourceTypes =>
            Enum.GetValues(typeof(DerTypeName)).Cast<DerTypeName>();

        //  CG4 History paleta 
        public ObservableCollection<ActionRecord> ActionHistory { get; private set; }

        //  CG4 Undo stack (jedan po jedan) 
        private readonly Stack<UndoEntry> _undoStack = new Stack<UndoEntry>();

        private readonly Stack<UndoEntry> _redoStack = new Stack<UndoEntry>();


        private enum UndoActionType { Add, Delete }
        private class UndoEntry
        {
            public UndoActionType ActionType { get; set; }
            public DerResource Resource { get; set; }
        }

        //  Komande 
        public MyICommand AddCommand { get; private set; }
        public MyICommand DeleteCommand { get; private set; }
        public MyICommand UndoCommand { get; private set; }
        public MyICommand UndoAllCommand { get; private set; }
        public MyICommand ClearFilterCommand { get; private set; }

        private readonly NotificationManager _notificationManager = new NotificationManager();

        public NetworkEntitiesViewModel()
        {
            ActionHistory = new ObservableCollection<ActionRecord>();
            LoadResources();

            FilteredView = CollectionViewSource.GetDefaultView(Resources);
            FilteredView.Filter = ApplyFilter;

            AddCommand = new MyICommand(OnAdd);
            DeleteCommand = new MyICommand(OnDelete, CanDelete);
            UndoCommand = new MyICommand(OnUndo, CanUndo);
            RedoCommand = new MyICommand(OnRedo, CanRedo);
            UndoAllCommand = new MyICommand(OnUndoAll, CanUndo);
            ClearFilterCommand = new MyICommand(OnClearFilter);

            CurrentResource = new DerResource(string.Empty, new DerResourceType(DerTypeName.SolarPanel), true);
        }

        //  Punjenje početnih podataka 
        public void LoadResources()
        {
            Resources = new ObservableCollection<DerResource>
            {
                new DerResource("SunnyFarm-Alpha", new DerResourceType(DerTypeName.SolarPanel)),
                new DerResource("SunnyFarm-Beta",  new DerResourceType(DerTypeName.SolarPanel)),
                new DerResource("NordWind-01",     new DerResourceType(DerTypeName.Windgenerator))
            };
        }

        //  Dodavanje 
        private void OnAdd()
        {
            CurrentResource.Validate();
            if (!CurrentResource.IsValid)
            {
                _notificationManager.Show("Validation Error",
                    "Please fix the validation errors before adding.",
                    NotificationType.Error, "WindowNotificationArea");
                return;
            }

            var newResource = new DerResource(CurrentResource.Name, CurrentResource.ResourceType);
            Resources.Add(newResource);

            _undoStack.Push(new UndoEntry { ActionType = UndoActionType.Add, Resource = newResource });
            _redoStack.Clear();
            UndoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();

            RecordAction($"Added DER resource \"{newResource.Name}\" (ID {newResource.Id})");

            Messenger.Default.Send(Resources);

            //  Slanje poruke za osvežavanje CG4 grafikona
            Messenger.Default.Send(new RefreshTypeGraphMessage());

            //  Slanje poruke o novom resursu
            Messenger.Default.Send(newResource);

            ResetForm();
            RestartSimulator();

            _notificationManager.Show("Success",
                $"Resource \"{newResource.Name}\" added successfully.",
                NotificationType.Success, "WindowNotificationArea");
        }

        //  Brisanje 
        private void OnDelete()
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete \"{SelectedResource.Name}\"?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No) return;

            var deleted = SelectedResource;
            int deletedId = deleted.Id;
            _undoStack.Push(new UndoEntry { ActionType = UndoActionType.Delete, Resource = deleted });
            _redoStack.Clear();
            UndoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();

            Resources.Remove(deleted);
            RecordAction($"Deleted DER resource \"{deleted.Name}\" (ID {deleted.Id})");

            Messenger.Default.Send(Resources);

            //  Slanje poruke za osvežavanje CG4 grafikona
            Messenger.Default.Send(new RefreshTypeGraphMessage());

            //  Slanje poruke o obrisanom resursu
            Messenger.Default.Send(deletedId);

            RestartSimulator();

            _notificationManager.Show("Success",
                $"Resource \"{deleted.Name}\" deleted successfully.",
                NotificationType.Success, "WindowNotificationArea");
        }

        private bool CanDelete() => SelectedResource != null;

        //  CG4 Undo (poslednja akcija) 
        private void OnUndo()
        {
            if (_undoStack.Count == 0) return;

            var entry = _undoStack.Pop();
            _redoStack.Push(entry);
            PerformUndo(entry);

            UndoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();
            RedoCommand?.RaiseCanExecuteChanged();

            _notificationManager.Show("Undo",
                "Last action undone successfully.",
                NotificationType.Information, "WindowNotificationArea");
        }

        //  CG4 Undo All (sve akcije odjednom) 
        private void OnUndoAll()
        {
            if (_undoStack.Count == 0) return;

            while (_undoStack.Count > 0)
                PerformUndo(_undoStack.Pop());

            UndoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();

            Messenger.Default.Send(Resources);

            //  Slanje poruke za osvežavanje CG4 grafikona
            Messenger.Default.Send(new RefreshTypeGraphMessage());

            RestartSimulator();

            _notificationManager.Show("Undo All",
                "All actions have been undone.",
                NotificationType.Information, "WindowNotificationArea");
        }

        private bool CanUndo() => _undoStack.Count > 0;

        private void PerformUndo(UndoEntry entry)
        {
            switch (entry.ActionType)
            {
                case UndoActionType.Add:
                    Resources.Remove(entry.Resource);
                    RecordAction($"[Undo] Removed \"{entry.Resource.Name}\" (ID {entry.Resource.Id})");
                    Messenger.Default.Send(Resources);

                    //  Slanje poruke za osvežavanje CG4 grafikona
                    Messenger.Default.Send(new RefreshTypeGraphMessage());

                    RestartSimulator();
                    break;

                case UndoActionType.Delete:
                    Resources.Add(entry.Resource);
                    RecordAction($"[Undo] Restored \"{entry.Resource.Name}\" (ID {entry.Resource.Id})");
                    Messenger.Default.Send(Resources);

                    //  Slanje poruke za osvežavanje CG4 grafikona
                    Messenger.Default.Send(new RefreshTypeGraphMessage());

                    RestartSimulator();
                    break;
            }
        }

        //  Poništavanje filtera 
        private void OnClearFilter()
        {
            FilterText = string.Empty;
            ShowOnlyOutOfRange = false;
            ShowOnlyInRange = false;
            IsNameFilterSelected = true;
        }

        //  P1 + CG4 filter logika 
        private bool ApplyFilter(object obj)
        {
            if (!(obj is DerResource resource)) return false;

            // P1 pretraga po nazivu ili tipu
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                if (_isNameFilterSelected)
                {
                    if (resource.Name.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
                else
                {
                    if (resource.ResourceType.Name.ToString()
                            .IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
            }

            // CG4 dodatni filter po opsegu
            if (_showOnlyOutOfRange && resource.IsValueValid)
                return false;

            if (_showOnlyInRange && !resource.IsValueValid)
                return false;

            return true;
        }

        private void ResetForm()
        {
            CurrentResource = new DerResource(string.Empty, new DerResourceType(DerTypeName.SolarPanel), true);
        }

        private void RecordAction(string description)
        {
            ActionHistory.Insert(0, new ActionRecord(description));
        }

        private void RestartSimulator()
        {
            try
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("MeteringSimulator"))
                    process.Kill();

                string relativePath = @"../../../../../MeteringSimulator/MeteringSimulator/bin/Debug/MeteringSimulator.exe";
                string exePath = Path.GetFullPath(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

                System.Diagnostics.Process.Start(exePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to restart simulator: " + ex.Message);
            }
        }
        private void PerformRedo(UndoEntry entry)
        {
            switch (entry.ActionType)
            {
                case UndoActionType.Add:
                    Resources.Add(entry.Resource);
                    RecordAction($"[Redo] Restored \"{entry.Resource.Name}\" (ID {entry.Resource.Id})");
                    Messenger.Default.Send(Resources);
                    Messenger.Default.Send(new RefreshTypeGraphMessage());
                    RestartSimulator();
                    break;

                case UndoActionType.Delete:
                    Resources.Remove(entry.Resource);
                    RecordAction($"[Redo] Removed \"{entry.Resource.Name}\" (ID {entry.Resource.Id})");
                    Messenger.Default.Send(Resources);
                    Messenger.Default.Send(new RefreshTypeGraphMessage());
                    RestartSimulator();
                    break;
            }
        }

        private void OnRedo()
        {
            if (_redoStack.Count == 0) return;

            var entry = _redoStack.Pop();
            _undoStack.Push(entry); //  Vraćamo u Undo stack

            PerformRedo(entry);

            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();

            _notificationManager.Show("Redo",
                "Action redone successfully.",
                NotificationType.Information, "WindowNotificationArea");
        }

        private bool CanRedo() => _redoStack.Count > 0;

        public void ExecuteUndo()
        {
            OnUndo();
        }
        public MyICommand RedoCommand { get; private set; }

        public bool CanExecuteUndo()
        {
            return _undoStack.Count > 0;
        }

        public void ExecuteUndoAll()
        {
            OnUndoAll();
        }

        public bool CanExecuteUndoAll()
        {
            return _undoStack.Count > 0;
        }

        public void ExecuteRedo()
        {
            OnRedo();
        }
    }
}