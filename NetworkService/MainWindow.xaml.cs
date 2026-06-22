using System.Windows;
using System.Windows.Input;
using NetworkService.ViewModel;

namespace NetworkService
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(DataContext is MainWindowViewModel vm)) return;

            // Ctrl+Z = Undo
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (vm.CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
                {
                    entitiesVM.ExecuteUndo();
                }
                else if (vm.CurrentViewModel is MeasurementGraphViewModel graphVM)
                {
                    graphVM.ExecuteUndo();
                }
                e.Handled = true;
            }
            // Ctrl+Shift+Z = Redo
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
            {
                if (vm.CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
                {
                    entitiesVM.ExecuteRedo();
                }
                e.Handled = true;
            }
            // Ctrl+Shift+U = Undo All
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.U)
            {
                if (vm.CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
                {
                    entitiesVM.ExecuteUndoAll();
                }
                e.Handled = true;
            }
        }
    }
}