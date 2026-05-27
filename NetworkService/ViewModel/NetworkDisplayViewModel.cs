using GalaSoft.MvvmLight.Messaging;
using NetworkService.Model;
using Notification.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NetworkService.ViewModel
{
    public class NetworkDisplayViewModel : BindableBase
    {
        // ── Kolekcija za TreeView (grupisano po tipu) ────────────────────────
        private ObservableCollection<DerResourcesByType> _allResources;
        public ObservableCollection<DerResourcesByType> AllResources
        {
            get => _allResources;
            set { _allResources = value; OnPropertyChanged(nameof(AllResources)); }
        }

        // ── Selektovani resurs za drag ───────────────────────────────────────
        private DerResource _selectedResource;
        public DerResource SelectedResource
        {
            get => _selectedResource;
            set { _selectedResource = value; OnPropertyChanged(nameof(SelectedResource)); }
        }

        // ── Canvas ↔ Resource parovi ─────────────────────────────────────────
        public static ObservableCollection<CanvasDerPair> CanvasDerPairs { get; private set; }

        // ── Linije veza između canvas-ova ────────────────────────────────────
        public ObservableCollection<DerConnection> Connections { get; private set; }
        public ObservableCollection<Line> Lines { get; private set; }
        private Canvas _lineCanvas;

        // ── Rečnik canvas objekata (ID → Canvas) ────────────────────────────
        private readonly Dictionary<int, Canvas> _canvasDictionary = new Dictionary<int, Canvas>();

        // ── Drag state ───────────────────────────────────────────────────────
        private bool _isDragging;
        private Point _dragStartPoint;

        // ── CG4 Undo stack ────────────────────────────────────────────────────
        private readonly Stack<UndoEntry> _undoStack = new Stack<UndoEntry>();
        private enum UndoActionType { DropFromTree, DropFromCanvas, Connect, ClearCanvas }

        private class UndoEntry
        {
            public UndoActionType ActionType { get; set; }
            // DropFromTree: Resource dropped, CanvasId it was placed on
            public DerResource DroppedResource { get; set; }
            public int ToCanvasId { get; set; }
            // DropFromCanvas: moved from → to
            public int FromCanvasId { get; set; }
            // Connect:
            public DerConnection Connection { get; set; }
            // ClearCanvas:
            public List<DerConnection> SavedConnections { get; set; }
        }

        // ── CG4 History paleta ────────────────────────────────────────────────
        public ObservableCollection<ActionRecord> ActionHistory { get; private set; }

        // ── Komande ──────────────────────────────────────────────────────────
        public MyICommand<object> MouseDownCommand { get; private set; }
        public MyICommand<object> MouseMoveCommand { get; private set; }
        public MyICommand<object> ResetDragCommand { get; private set; }
        public MyICommand<Canvas> DropCommand { get; private set; }
        public MyICommand<Canvas> ClearCanvasCommand { get; private set; }
        public MyICommand<Canvas> CanvasLoadedCommand { get; private set; }
        public MyICommand<Canvas> MouseDownCanvasCommand { get; private set; }
        public MyICommand<Canvas> MouseMoveCanvasCommand { get; private set; }
        public MyICommand<Tuple<DerResource, DerResource>> ConnectResourcesCommand { get; private set; }
        public MyICommand<Canvas> SetLineCanvasCommand { get; private set; }
        public MyICommand UndoCommand { get; private set; }
        public MyICommand UndoAllCommand { get; private set; }

        private readonly NotificationManager _notificationManager = new NotificationManager();

        public NetworkDisplayViewModel()
        {
            ActionHistory = new ObservableCollection<ActionRecord>();
            AllResources = new ObservableCollection<DerResourcesByType>();
            CanvasDerPairs = new ObservableCollection<CanvasDerPair>();
            Connections = new ObservableCollection<DerConnection>();
            Lines = new ObservableCollection<Line>();

            MouseDownCommand = new MyICommand<object>(OnMouseDown);
            MouseMoveCommand = new MyICommand<object>(OnMouseMove);
            ResetDragCommand = new MyICommand<object>(_ => ResetDragState());
            DropCommand = new MyICommand<Canvas>(OnDrop);
            ClearCanvasCommand = new MyICommand<Canvas>(canvas => OnClearCanvas(canvas));
            CanvasLoadedCommand = new MyICommand<Canvas>(OnCanvasLoaded);
            MouseDownCanvasCommand = new MyICommand<Canvas>(OnCanvasMouseDown);
            MouseMoveCanvasCommand = new MyICommand<Canvas>(OnCanvasMouseMove);
            ConnectResourcesCommand = new MyICommand<Tuple<DerResource, DerResource>>(OnConnectResources);
            SetLineCanvasCommand = new MyICommand<Canvas>(canvas => _lineCanvas = canvas);
            UndoCommand = new MyICommand(OnUndo, CanUndo);
            UndoAllCommand = new MyICommand(OnUndoAll, CanUndo);

            Messenger.Default.Register<ObservableCollection<DerResource>>(this, OnResourcesReceived);
            Messenger.Default.Register<DerResource>(this, OnResourceValueChanged);
        }

        #region Drag & Drop

        private void ResetDragState()
        {
            SelectedResource = null;
            _isDragging = false;
        }

        private void OnMouseDown(object parameter)
        {
            if (parameter is DerResource resource)
            {
                SelectedResource = resource;
                _dragStartPoint = Mouse.GetPosition(Application.Current.MainWindow);
                _isDragging = false;
            }
        }

        private void OnMouseMove(object parameter)
        {
            if (SelectedResource == null) return;
            if (Mouse.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point current = Mouse.GetPosition(Application.Current.MainWindow);
                Vector diff = _dragStartPoint - current;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    DragDrop.DoDragDrop(Application.Current.MainWindow, SelectedResource, DragDropEffects.Move);
                    ResetDragState();
                }
            }
        }

        private void OnDrop(Canvas canvas)
        {
            if (canvas == null || SelectedResource == null) return;

            int canvasId = int.Parse(canvas.Tag.ToString());
            var existingPair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == canvasId);

            if (existingPair != null)
            {
                // Palo je na popunjeni canvas → pokuša konekciju
                bool alreadyConnected = Connections.Any(c =>
                    (c.From.Resource == SelectedResource && c.To.Resource == existingPair.Resource) ||
                    (c.From.Resource == existingPair.Resource && c.To.Resource == SelectedResource));

                if (existingPair.Resource != SelectedResource && !alreadyConnected)
                {
                    ConnectResourcesCommand.Execute(Tuple.Create(SelectedResource, existingPair.Resource));
                }
                ResetDragState();
                return;
            }

            var sourceCanvasPair = CanvasDerPairs.FirstOrDefault(p => p.Resource == SelectedResource);
            if (sourceCanvasPair != null)
            {
                // Premestanje sa jednog canvas-a na drugi
                int oldCanvasId = sourceCanvasPair.CanvasId;
                if (_canvasDictionary.TryGetValue(oldCanvasId, out Canvas oldCanvas))
                {
                    ClearCanvasVisuals(oldCanvas);
                    var newPair = new CanvasDerPair { CanvasId = canvasId, Resource = SelectedResource };
                    CanvasDerPairs.Add(newPair);
                    UpdateCanvasUI(canvas, SelectedResource);
                    MoveAllConnections(oldCanvasId, canvasId);
                    CanvasDerPairs.Remove(sourceCanvasPair);

                    _undoStack.Push(new UndoEntry
                    {
                        ActionType = UndoActionType.DropFromCanvas,
                        DroppedResource = SelectedResource,
                        FromCanvasId = oldCanvasId,
                        ToCanvasId = canvasId
                    });
                    RaiseUndoCanExecute();
                    RecordAction($"Moved \"{SelectedResource.Name}\" from canvas {oldCanvasId} to {canvasId}");
                }
                ResetDragState();
                RedrawLines();
                return;
            }

            // Novo prevlačenje iz TreeView-a
            var pair = new CanvasDerPair { CanvasId = canvasId, Resource = SelectedResource };
            CanvasDerPairs.Add(pair);
            UpdateCanvasUI(canvas, SelectedResource);
            RemoveResourceFromTree(SelectedResource);

            _undoStack.Push(new UndoEntry
            {
                ActionType = UndoActionType.DropFromTree,
                DroppedResource = SelectedResource,
                ToCanvasId = canvasId
            });
            RaiseUndoCanExecute();
            RecordAction($"Placed \"{SelectedResource.Name}\" on canvas {canvasId}");

            ResetDragState();
            RedrawLines();
        }

        #endregion

        #region Canvas UI

        private void OnCanvasLoaded(Canvas canvas)
        {
            if (canvas?.Tag == null) return;
            if (int.TryParse(canvas.Tag.ToString(), out int id))
            {
                _canvasDictionary[id] = canvas;
                var pair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == id);
                if (pair != null) UpdateCanvasUI(canvas, pair.Resource);
            }
        }

        private void UpdateCanvasUI(Canvas canvas, DerResource resource)
        {
            if (canvas == null || resource == null) return;

            // Ažuriranje TextBlock-a (ID + vrednost + MW)
            if (canvas.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock tb)
            {
                tb.Text = $"ID:{resource.Id}  {resource.CurrentValue:F2} MW";
                // CG4/T4: crvena ako je van opsega 1–5 MW, zelena ako je unutar
                tb.Foreground = resource.IsValueValid ? Brushes.LimeGreen : Brushes.Red;
                tb.FontWeight = FontWeights.Bold;
            }

            // Pozadinska slika prema tipu
            BitmapImage bmp = new BitmapImage(new Uri(resource.ResourceType.ImagePath, UriKind.Absolute));
            canvas.Background = new ImageBrush(bmp) { Stretch = Stretch.Uniform };
        }

        private void ClearCanvasVisuals(Canvas canvas)
        {
            canvas.Background = Brushes.LightGray;
            if (canvas.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock tb)
                tb.Text = string.Empty;
        }

        private void OnCanvasMouseDown(Canvas canvas)
        {
            if (canvas == null) return;
            var pair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == int.Parse(canvas.Tag.ToString()));
            if (pair?.Resource != null)
            {
                SelectedResource = pair.Resource;
                _dragStartPoint = Mouse.GetPosition(Application.Current.MainWindow);
                _isDragging = false;
            }
        }

        private void OnCanvasMouseMove(Canvas canvas)
        {
            if (SelectedResource == null || canvas == null) return;
            if (Mouse.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point current = Mouse.GetPosition(Application.Current.MainWindow);
                Vector diff = _dragStartPoint - current;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    DragDrop.DoDragDrop(Application.Current.MainWindow, SelectedResource,
                        DragDropEffects.Copy | DragDropEffects.Move);
                    ResetDragState();
                }
            }
        }

        private void OnClearCanvas(Canvas canvas, bool suppressNotification = false)
        {
            if (canvas == null) return;
            int canvasId = int.Parse(canvas.Tag.ToString());
            var pair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == canvasId);
            if (pair == null) return;

            var savedConnections = Connections.Where(c =>
                c.From.Resource == pair.Resource || c.To.Resource == pair.Resource).ToList();

            ReturnResourceToTree(pair.Resource, canvasId);

            _undoStack.Push(new UndoEntry
            {
                ActionType = UndoActionType.ClearCanvas,
                DroppedResource = pair.Resource,
                ToCanvasId = canvasId,
                SavedConnections = savedConnections
            });
            RaiseUndoCanExecute();
            RecordAction($"Removed \"{pair.Resource.Name}\" from canvas {canvasId}");

            if (!suppressNotification)
                _notificationManager.Show("Canvas Cleared",
                    $"Resource returned to tree view.", NotificationType.Success, "WindowNotificationArea");
        }

        #endregion

        #region Connections

        private void OnConnectResources(Tuple<DerResource, DerResource> tuple)
        {
            var fromPair = CanvasDerPairs.FirstOrDefault(p => p.Resource == tuple.Item1);
            var toPair = CanvasDerPairs.FirstOrDefault(p => p.Resource == tuple.Item2);
            if (fromPair == null || toPair == null) return;

            var connection = new DerConnection { From = fromPair, To = toPair };
            Connections.Add(connection);
            DrawConnection(connection);

            _undoStack.Push(new UndoEntry { ActionType = UndoActionType.Connect, Connection = connection });
            RaiseUndoCanExecute();
            RecordAction($"Connected \"{tuple.Item1.Name}\" ↔ \"{tuple.Item2.Name}\"");
        }

        private void DrawConnection(DerConnection connection)
        {
            if (!_canvasDictionary.TryGetValue(connection.From.CanvasId, out Canvas fromCanvas)) return;
            if (!_canvasDictionary.TryGetValue(connection.To.CanvasId, out Canvas toCanvas)) return;

            Point fromCenter = fromCanvas.TranslatePoint(
                new Point(fromCanvas.ActualWidth / 2, fromCanvas.ActualHeight / 2), _lineCanvas);
            Point toCenter = toCanvas.TranslatePoint(
                new Point(toCanvas.ActualWidth / 2, toCanvas.ActualHeight / 2), _lineCanvas);

            Line line = new Line
            {
                Stroke = Brushes.Orange,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                X1 = fromCenter.X, Y1 = fromCenter.Y,
                X2 = toCenter.X, Y2 = toCenter.Y,
                Tag = connection
            };

            Lines.Add(line);
        }

        private void RemoveConnectionsForResource(DerResource resource)
        {
            var toRemove = Lines
                .Where(l => l.Tag is DerConnection dc &&
                            (dc.From.Resource == resource || dc.To.Resource == resource))
                .ToList();

            foreach (var line in toRemove)
            {
                if (line.Tag is DerConnection conn) Connections.Remove(conn);
                Lines.Remove(line);
            }
        }

        private void MoveAllConnections(int fromCanvasId, int toCanvasId)
        {
            var newPair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == toCanvasId);
            if (newPair == null) return;

            foreach (var conn in Connections)
            {
                if (conn.From.CanvasId == fromCanvasId) conn.From = newPair;
                if (conn.To.CanvasId == fromCanvasId) conn.To = newPair;
            }

            RedrawLines();
        }

        private void RedrawLines()
        {
            Lines.Clear();
            foreach (var conn in Connections.ToList())
                DrawConnection(conn);
        }

        #endregion

        #region Tree helpers

        private void RemoveResourceFromTree(DerResource resource)
        {
            foreach (var group in AllResources)
            {
                if (group.Resources.Remove(resource)) break;
            }
        }

        private void ReturnResourceToTree(DerResource resource, int canvasId)
        {
            if (resource == null) return;

            var group = AllResources.FirstOrDefault(g => g.TypeName == resource.ResourceType.Name);
            group?.Resources.Add(resource);

            var pair = CanvasDerPairs.FirstOrDefault(p => p.CanvasId == canvasId);
            if (pair != null)
            {
                CanvasDerPairs.Remove(pair);
                if (_canvasDictionary.TryGetValue(canvasId, out var canvas))
                    ClearCanvasVisuals(canvas);
            }

            RemoveConnectionsForResource(resource);
            RedrawLines();
        }

        #endregion

        #region Messenger handlers

        private void OnResourcesReceived(ObservableCollection<DerResource> resources)
        {
            // Ukloni sa canvas-a sve resurse kojih više nema u listi
            var validIds = new HashSet<int>(resources.Select(r => r.Id));
            var orphaned = CanvasDerPairs.Where(p => !validIds.Contains(p.Resource.Id)).ToList();

            foreach (var p in orphaned)
            {
                if (_canvasDictionary.TryGetValue(p.CanvasId, out var canvas))
                    ClearCanvasVisuals(canvas);
                RemoveConnectionsForResource(p.Resource);
                CanvasDerPairs.Remove(p);
            }

            // Grupisanje za TreeView
            var grouped = new ObservableCollection<DerResourcesByType>();
            var placedIds = new HashSet<int>(CanvasDerPairs.Select(p => p.Resource.Id));

            foreach (var typeGroup in resources.GroupBy(r => r.ResourceType.Name))
            {
                var grp = new DerResourcesByType(typeGroup.Key);
                foreach (var r in typeGroup)
                {
                    if (!placedIds.Contains(r.Id))
                        grp.Resources.Add(r);
                }
                grouped.Add(grp);
            }

            AllResources = grouped;
            RedrawLines();
        }

        private void OnResourceValueChanged(DerResource changedResource)
        {
            var pair = CanvasDerPairs.FirstOrDefault(p => p.Resource == changedResource);
            if (pair == null) return;
            if (_canvasDictionary.TryGetValue(pair.CanvasId, out var canvas))
                UpdateCanvasUI(canvas, changedResource);
        }

        #endregion

        #region CG4 Undo

        private bool CanUndo() => _undoStack.Count > 0;

        private void OnUndo()
        {
            if (_undoStack.Count == 0) return;
            PerformUndo(_undoStack.Pop());
            RaiseUndoCanExecute();
            _notificationManager.Show("Undo", "Last action undone.",
                NotificationType.Information, "WindowNotificationArea");
        }

        private void OnUndoAll()
        {
            if (_undoStack.Count == 0) return;
            while (_undoStack.Count > 0)
                PerformUndo(_undoStack.Pop());
            RaiseUndoCanExecute();
            _notificationManager.Show("Undo All", "All actions undone.",
                NotificationType.Information, "WindowNotificationArea");
        }

        private void PerformUndo(UndoEntry entry)
        {
            switch (entry.ActionType)
            {
                case UndoActionType.Connect:
                    Connections.Remove(entry.Connection);
                    RedrawLines();
                    RecordAction($"[Undo] Disconnected {entry.Connection.From.Resource.Name} ↔ {entry.Connection.To.Resource.Name}");
                    break;

                case UndoActionType.DropFromTree:
                    var treePair = CanvasDerPairs.FirstOrDefault(p => p.Resource == entry.DroppedResource);
                    if (treePair != null)
                        OnClearCanvas(_canvasDictionary.TryGetValue(treePair.CanvasId, out var c) ? c : null, true);
                    RecordAction($"[Undo] Returned \"{entry.DroppedResource.Name}\" to tree");
                    break;

                case UndoActionType.DropFromCanvas:
                    // Vrati resurs na stari canvas
                    var currentPair = CanvasDerPairs.FirstOrDefault(p => p.Resource == entry.DroppedResource);
                    if (currentPair != null)
                    {
                        if (_canvasDictionary.TryGetValue(currentPair.CanvasId, out var newCvs))
                            ClearCanvasVisuals(newCvs);
                        CanvasDerPairs.Remove(currentPair);
                    }
                    var restoredPair = new CanvasDerPair
                        { CanvasId = entry.FromCanvasId, Resource = entry.DroppedResource };
                    CanvasDerPairs.Add(restoredPair);
                    if (_canvasDictionary.TryGetValue(entry.FromCanvasId, out var oldCvs))
                        UpdateCanvasUI(oldCvs, entry.DroppedResource);
                    MoveAllConnections(entry.ToCanvasId, entry.FromCanvasId);
                    RecordAction($"[Undo] Moved \"{entry.DroppedResource.Name}\" back to canvas {entry.FromCanvasId}");
                    break;

                case UndoActionType.ClearCanvas:
                    var restored = new CanvasDerPair
                        { CanvasId = entry.ToCanvasId, Resource = entry.DroppedResource };
                    CanvasDerPairs.Add(restored);
                    var group = AllResources.FirstOrDefault(g => g.TypeName == entry.DroppedResource.ResourceType.Name);
                    group?.Resources.Remove(entry.DroppedResource);
                    if (_canvasDictionary.TryGetValue(entry.ToCanvasId, out var restCvs))
                        UpdateCanvasUI(restCvs, entry.DroppedResource);
                    foreach (var conn in entry.SavedConnections)
                    {
                        if (conn.From.Resource == entry.DroppedResource) conn.From = restored;
                        if (conn.To.Resource == entry.DroppedResource) conn.To = restored;
                        if (!Connections.Contains(conn)) Connections.Add(conn);
                    }
                    RedrawLines();
                    RecordAction($"[Undo] Restored \"{entry.DroppedResource.Name}\" to canvas {entry.ToCanvasId}");
                    break;
            }
        }

        private void RaiseUndoCanExecute()
        {
            UndoCommand.RaiseCanExecuteChanged();
            UndoAllCommand.RaiseCanExecuteChanged();
        }

        #endregion

        private void RecordAction(string description)
        {
            ActionHistory.Insert(0, new ActionRecord(description));
        }
    }
}
