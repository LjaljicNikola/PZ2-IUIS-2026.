using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using NetworkService.Model;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;

namespace NetworkService.ViewModel
{
    public class MainWindowViewModel : BindableBase
    {
        private NetworkDisplayViewModel _networkDisplayViewModel;
        private NetworkEntitiesViewModel _networkEntitiesViewModel;
        private MeasurementGraphViewModel _measurementGraphViewModel;
        private BindableBase _currentViewModel;

        public BindableBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                SetProperty(ref _currentViewModel, value);
                RaiseCanExecuteChanged();
            }
        }

        public MyICommand<string> NavCommand { get; private set; }
        public MyICommand ExitCommand { get; private set; }

        // ════════════════════════════════════════════════════════════════
        // ✅ UNDO/REDO/UNDO ALL KOMANDE
        // ════════════════════════════════════════════════════════════════
        public MyICommand UndoCommand { get; private set; }
        public MyICommand RedoCommand { get; private set; }
        public MyICommand UndoAllCommand { get; private set; }

        public MainWindowViewModel()
        {
            _networkEntitiesViewModel = new NetworkEntitiesViewModel();
            _networkDisplayViewModel = new NetworkDisplayViewModel();
            _measurementGraphViewModel = new MeasurementGraphViewModel();

            NavCommand = new MyICommand<string>(OnNav);
            ExitCommand = new MyICommand(ExecuteExit);

            // ════════════════════════════════════════════════════════════
            // ✅ INICIJALIZACIJA UNDO/REDO/UNDO ALL KOMANDI
            // ════════════════════════════════════════════════════════════
            UndoCommand = new MyICommand(OnUndo, CanUndo);
            RedoCommand = new MyICommand(OnRedo, CanRedo);
            UndoAllCommand = new MyICommand(OnUndoAll, CanUndoAll);

            CurrentViewModel = _networkEntitiesViewModel;

            // Inicijalna sinhronizacija liste resursa prema ostalim view modelima
            Messenger.Default.Send(_networkEntitiesViewModel.Resources);

            CreateListener();
        }

        private void ExecuteExit()
        {
            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to exit?",
                "Confirm Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }

        private void OnNav(string destination)
        {
            switch (destination)
            {
                case "NetworkEntities":
                    CurrentViewModel = _networkEntitiesViewModel;
                    break;
                case "NetworkDisplay":
                    CurrentViewModel = _networkDisplayViewModel;
                    break;
                case "MeasurementGraph":
                    CurrentViewModel = _measurementGraphViewModel;
                    break;
                case "Exit":
                    ExecuteExit();
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // ✅ IMPLEMENTACIJA UNDO/REDO/UNDO ALL - STVARNO POZIVA METODE
        // ════════════════════════════════════════════════════════════════

        private void OnUndo()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                // Pozivamo Undo metodu iz NetworkEntitiesViewModel
                var method = entitiesVM.GetType().GetMethod("Undo");
                if (method != null)
                {
                    method.Invoke(entitiesVM, null);
                }
            }
            else if (CurrentViewModel is MeasurementGraphViewModel graphVM)
            {
                // Pozivamo Undo metodu iz MeasurementGraphViewModel
                var method = graphVM.GetType().GetMethod("Undo");
                if (method != null)
                {
                    method.Invoke(graphVM, null);
                }
            }

            RaiseCanExecuteChanged();
        }

        private bool CanUndo()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                var method = entitiesVM.GetType().GetMethod("CanUndo");
                if (method != null)
                {
                    return (bool)method.Invoke(entitiesVM, null);
                }
            }
            else if (CurrentViewModel is MeasurementGraphViewModel graphVM)
            {
                var method = graphVM.GetType().GetMethod("CanUndo");
                if (method != null)
                {
                    return (bool)method.Invoke(graphVM, null);
                }
            }
            return false;
        }

        private void OnRedo()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                var method = entitiesVM.GetType().GetMethod("Redo");
                if (method != null)
                {
                    method.Invoke(entitiesVM, null);
                }
            }
            RaiseCanExecuteChanged();
        }

        private bool CanRedo()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                var method = entitiesVM.GetType().GetMethod("CanRedo");
                if (method != null)
                {
                    return (bool)method.Invoke(entitiesVM, null);
                }
            }
            return false;
        }

        private void OnUndoAll()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                var method = entitiesVM.GetType().GetMethod("UndoAll");
                if (method != null)
                {
                    method.Invoke(entitiesVM, null);
                }
            }
            RaiseCanExecuteChanged();
        }

        private bool CanUndoAll()
        {
            if (CurrentViewModel is NetworkEntitiesViewModel entitiesVM)
            {
                var method = entitiesVM.GetType().GetMethod("CanUndoAll");
                if (method != null)
                {
                    return (bool)method.Invoke(entitiesVM, null);
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        // ✅ OSVEŽAVANJE KOMANDI
        // ════════════════════════════════════════════════════════════════

        public void RaiseCanExecuteChanged()
        {
            UndoCommand?.RaiseCanExecuteChanged();
            RedoCommand?.RaiseCanExecuteChanged();
            UndoAllCommand?.RaiseCanExecuteChanged();
        }

        private void CreateListener()
        {
            var tcp = new TcpListener(IPAddress.Any, 25675);
            tcp.Start();

            var listeningThread = new Thread(() =>
            {
                while (true)
                {
                    var tcpClient = tcp.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        NetworkStream stream = tcpClient.GetStream();
                        byte[] bytes = new byte[1024];
                        int i = stream.Read(bytes, 0, bytes.Length);
                        string incoming = Encoding.ASCII.GetString(bytes, 0, i);

                        if (incoming.Equals("Need object count"))
                        {
                            int count = _networkEntitiesViewModel.Resources.Count;
                            byte[] data = Encoding.ASCII.GetBytes(count.ToString());
                            stream.Write(data, 0, data.Length);
                        }
                        else
                        {
                            // Format: "Entitet_N:value"
                            string[] parts = incoming.Split(':');
                            if (parts.Length == 2 &&
                                parts[0].StartsWith("Entitet_") &&
                                int.TryParse(parts[0].Substring(8), out int entityIndex) &&
                                double.TryParse(parts[1], out double newValue))
                            {
                                UpdateResourceValue(entityIndex, newValue);
                            }
                        }
                    }, null);
                }
            });

            listeningThread.IsBackground = true;
            listeningThread.Start();
        }

        private void UpdateResourceValue(int entityIndex, double newValue)
        {
            if (entityIndex >= 0 && entityIndex < _networkEntitiesViewModel.Resources.Count)
            {
                var resource = _networkEntitiesViewModel.Resources[entityIndex];
                if (resource != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        resource.CurrentValue = newValue;
                        WriteLog(resource);

                        // Obavestiti NetworkDisplayViewModel o promeni
                        var pair = new CanvasDerPair { CanvasId = 0, Resource = resource };
                        Messenger.Default.Send(pair);
                    });
                }
            }
        }

        private void WriteLog(DerResource updatedResource)
        {
            string relativePath = @"../../Logs/Log.txt";
            string filePath = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

            string logDirectory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);

            bool fileExists = File.Exists(filePath);

            using (StreamWriter sw = new StreamWriter(filePath, append: true))
            {
                if (!fileExists)
                    sw.WriteLine("Id,CurrentValue,Date");

                sw.WriteLine($"{updatedResource.Id},{updatedResource.CurrentValue},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
        }
        
    }
}