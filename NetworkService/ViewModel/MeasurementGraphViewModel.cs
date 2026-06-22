using GalaSoft.MvvmLight.Messaging;
using NetworkService.Model;
using Notification.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetworkService.ViewModel
{
    public class MeasurementGraphViewModel : BindableBase
    {
        // ── Lista svih resursa (za ComboBox) ─────────────────────────────────
        private ObservableCollection<DerResource> _allResources;
        public ObservableCollection<DerResource> AllResources
        {
            get => _allResources;
            set
            {
                _allResources = value;
                OnPropertyChanged(nameof(AllResources));
                // ✅ Kada se promeni lista, osveži i CG4 grafikon
                DrawTypeDistributionGraph();
            }
        }

        // ── Selektovani resurs ───────────────────────────────────────────────
        private DerResource _selectedResource;
        public DerResource SelectedResource
        {
            get => _selectedResource;
            set
            {
                if (_selectedResource != value)
                {
                    // CG4 Undo: čuvamo prethodnu selekciju
                    _undoStack.Push(_selectedResource);
                    UndoCommand.RaiseCanExecuteChanged();

                    _selectedResource = value;
                    OnPropertyChanged(nameof(SelectedResource));
                    DrawBarGraph();
                }
            }
        }

        // ── Canvas za G2 grafikon ─────────────────────────────────────────────
        private Canvas _graphCanvas;
        public Canvas GraphCanvas
        {
            get => _graphCanvas;
            set
            {
                _graphCanvas = value;
                OnPropertyChanged(nameof(GraphCanvas));
                if (_graphCanvas != null)
                {
                    _graphCanvas.SizeChanged += (s, e) => DrawBarGraph();
                    // ✅ Kada se Canvas postavi, odmah nacrtaj bar grafikon
                    DrawBarGraph();
                }
            }
        }

        // ── Canvas za CG4 stacked bar grafikon po tipovima ────────────────────
        private Canvas _typeGraphCanvas;
        public Canvas TypeGraphCanvas
        {
            get => _typeGraphCanvas;
            set
            {
                _typeGraphCanvas = value;
                OnPropertyChanged(nameof(TypeGraphCanvas));
                if (_typeGraphCanvas != null)
                {
                    _typeGraphCanvas.SizeChanged += (s, e) => DrawTypeDistributionGraph();

                    // ═══════════════════════════════════════════════════════════════
                    // ✅ KLJUČNA PROMENA: Kada se Canvas postavi, odmah nacrtaj grafikon!
                    // ═══════════════════════════════════════════════════════════════
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DrawTypeDistributionGraph();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        // ── CG4 Undo stack (prethodni selektovani resurs) ────────────────────
        private readonly Stack<DerResource> _undoStack = new Stack<DerResource>();

        // ── Komande ──────────────────────────────────────────────────────────
        public MyICommand<Canvas> InitializeGraphCommand { get; private set; }
        public MyICommand<Canvas> InitializeTypeGraphCommand { get; private set; }
        public MyICommand UndoCommand { get; private set; }

        private readonly NotificationManager _notificationManager = new NotificationManager();

        // Validni opseg za T4: 1–5 MW
        private const double MinValue = 1.0;
        private const double MaxValue = 5.0;

        public MeasurementGraphViewModel()
        {
            AllResources = new ObservableCollection<DerResource>();

            InitializeGraphCommand = new MyICommand<Canvas>(canvas => GraphCanvas = canvas);

            // ═══════════════════════════════════════════════════════════════════
            // ✅ KLJUČNA PROMENA: InitializeTypeGraphCommand sada odmah crta grafikon
            // ═══════════════════════════════════════════════════════════════════
            InitializeTypeGraphCommand = new MyICommand<Canvas>(canvas =>
            {
                TypeGraphCanvas = canvas;
                // Nakon što se Canvas postavi, odmah nacrtaj grafikon
                DrawTypeDistributionGraph();
            });

            UndoCommand = new MyICommand(OnUndo, CanUndo);

            Messenger.Default.Register<ObservableCollection<DerResource>>(this, OnResourcesReceived);
            Messenger.Default.Register<CanvasDerPair>(this, OnPairReceived);

            // ✅ NOVI HANDLER: Kada se entiteti promene (dodavanje/brisanje)
            Messenger.Default.Register<RefreshTypeGraphMessage>(this, OnRefreshTypeGraph);
        }

        // ── Messenger handleri ───────────────────────────────────────────────

        private void OnResourcesReceived(ObservableCollection<DerResource> resources)
        {
            AllResources = resources;
            // DrawTypeDistributionGraph() se poziva iz settera AllResources
        }

        private void OnPairReceived(CanvasDerPair pair)
        {
            // Nova vrednost pristigla – osveži grafikon ako je isti resurs selektovan
            if (pair.Resource == SelectedResource)
                DrawBarGraph();

            // CG4 type distribution se uvek osvežava
            DrawTypeDistributionGraph();
        }

        // ✅ NOVI HANDLER: Osvežavanje grafikona na zahtev
        private void OnRefreshTypeGraph(RefreshTypeGraphMessage message)
        {
            DrawTypeDistributionGraph();
        }

        // ── G2: Bar grafikon (poslednjih 5 vrednosti za selektovani resurs) ──

        private void DrawBarGraph()
        {
            if (SelectedResource == null || GraphCanvas == null || GraphCanvas.ActualHeight <= 0)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                GraphCanvas.Children.Clear();

                var logEntries = ReadLog();
                var last5 = logEntries
                    .Where(l => l.Id == SelectedResource.Id)
                    .OrderByDescending(l => l.Date)
                    .Take(5)
                    .Reverse()
                    .ToList();

                if (last5.Count == 0) return;

                double canvasHeight = GraphCanvas.ActualHeight;
                double canvasWidth = GraphCanvas.ActualWidth;

                // Fiksna visina prostora za labele
                double labelAreaHeight = 30;
                double yAxisWidth = 45;
                double chartHeight = canvasHeight - labelAreaHeight - 10;
                double chartWidth = canvasWidth - yAxisWidth - 10;

                // Iscrtaj Y-osu sa mrežom
                DrawYAxis(yAxisWidth, chartHeight, labelAreaHeight);

                // Barovi
                double barWidth = 40;
                double spacing = (chartWidth - last5.Count * barWidth) / (last5.Count + 1);

                for (int i = 0; i < last5.Count; i++)
                {
                    double value = last5[i].CurrentValue;
                    // Normalizacija prema maksimumu ose (6 MW da ima prostora)
                    double normalized = Math.Max(0, value) / 6.0;
                    double rectHeight = normalized * chartHeight;

                    bool isValid = value >= MinValue && value <= MaxValue;
                    Brush fill = isValid ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // zelena
                                         : new SolidColorBrush(Color.FromRgb(239, 68, 68)); // crvena

                    double x = yAxisWidth + spacing + i * (barWidth + spacing);
                    double y = chartHeight - rectHeight + 5; // +5 gornji padding

                    // Rectangle (bar)
                    Rectangle rect = new Rectangle
                    {
                        Width = barWidth,
                        Height = Math.Max(1, rectHeight),
                        Fill = fill,
                        Stroke = Brushes.White,
                        StrokeThickness = 0.5
                    };
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    GraphCanvas.Children.Add(rect);

                    // Vrednost iznad bara
                    TextBlock valueLabel = new TextBlock
                    {
                        Text = $"{value:F2}",
                        Foreground = Brushes.White,
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center,
                        Width = barWidth
                    };
                    Canvas.SetLeft(valueLabel, x);
                    Canvas.SetTop(valueLabel, y - 15);
                    GraphCanvas.Children.Add(valueLabel);

                    // Datum na X-osi
                    TextBlock dateLabel = new TextBlock
                    {
                        Text = last5[i].Date.ToString("HH:mm:ss"),
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                        FontSize = 9,
                        TextAlignment = TextAlignment.Center,
                        Width = barWidth
                    };
                    Canvas.SetLeft(dateLabel, x);
                    Canvas.SetTop(dateLabel, chartHeight + 10);
                    GraphCanvas.Children.Add(dateLabel);
                }
            });
        }

        private void DrawYAxis(double yAxisWidth, double chartHeight, double labelAreaHeight)
        {
            // Y-osa linija
            Line yLine = new Line
            {
                X1 = yAxisWidth,
                Y1 = 5,
                X2 = yAxisWidth,
                Y2 = chartHeight + 5,
                Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                StrokeThickness = 1
            };
            GraphCanvas.Children.Add(yLine);

            // X-osa linija
            Line xLine = new Line
            {
                X1 = yAxisWidth,
                Y1 = chartHeight + 5,
                X2 = GraphCanvas.ActualWidth - 5,
                Y2 = chartHeight + 5,
                Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                StrokeThickness = 1
            };
            GraphCanvas.Children.Add(xLine);

            // Podeoci Y-ose: 0, 1, 2, 3, 4, 5, 6 MW
            for (int mw = 0; mw <= 6; mw++)
            {
                double y = chartHeight + 5 - (mw / 6.0) * chartHeight;

                // Horizontalna linija mreže
                Line gridLine = new Line
                {
                    X1 = yAxisWidth,
                    Y1 = y,
                    X2 = GraphCanvas.ActualWidth - 5,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 180, 180, 180)),
                    StrokeThickness = 0.5
                };
                GraphCanvas.Children.Add(gridLine);

                // Label na Y-osi
                TextBlock label = new TextBlock
                {
                    Text = $"{mw}MW",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    FontSize = 9
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, y - 7);
                GraphCanvas.Children.Add(label);
            }

            // Oznake validnog opsega (1–5 MW) isprekidanom linijom
            foreach (double limit in new[] { MinValue, MaxValue })
            {
                double y = chartHeight + 5 - (limit / 6.0) * chartHeight;
                Line limitLine = new Line
                {
                    X1 = yAxisWidth,
                    Y1 = y,
                    X2 = GraphCanvas.ActualWidth - 5,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(250, 204, 21)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };
                GraphCanvas.Children.Add(limitLine);
            }
        }

        // ── CG4: Stacked bar grafikon zastupljenosti tipova ──────────────────

        private void DrawTypeDistributionGraph()
        {
            // ═══ PROVERA: Ako canvas nije spreman, sačekaj ═══
            if (TypeGraphCanvas == null || TypeGraphCanvas.ActualWidth <= 0 || TypeGraphCanvas.ActualHeight <= 0)
            {
                // Pokušaj ponovo za 100ms ako canvas još nije renderovan
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (TypeGraphCanvas != null && TypeGraphCanvas.ActualWidth > 0)
                    {
                        DrawTypeDistributionGraph();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                TypeGraphCanvas.Children.Clear();

                // ═══ PROVERA: Da li postoje resursi ═══
                if (AllResources == null || AllResources.Count == 0)
                {
                    // Prikaži poruku "No resources"
                    TextBlock noDataText = new TextBlock
                    {
                        Text = "No resources in system",
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Canvas.SetLeft(noDataText, TypeGraphCanvas.ActualWidth / 2 - 70);
                    Canvas.SetTop(noDataText, TypeGraphCanvas.ActualHeight / 2 - 10);
                    TypeGraphCanvas.Children.Add(noDataText);
                    return;
                }

                int totalCount = AllResources.Count;
                if (totalCount == 0) return;

                double canvasWidth = TypeGraphCanvas.ActualWidth;
                double canvasHeight = TypeGraphCanvas.ActualHeight;
                double barHeight = 30;
                double barTop = (canvasHeight - barHeight) / 2.0;

                // Broj resursa po tipu
                int solarCount = AllResources.Count(r => r.ResourceType.Name == DerTypeName.SolarPanel);
                int windCount = AllResources.Count(r => r.ResourceType.Name == DerTypeName.Windgenerator);

                double solarRatio = (double)solarCount / totalCount;
                double windRatio = (double)windCount / totalCount;

                double availableWidth = canvasWidth - 10;
                double solarWidth = solarRatio * availableWidth;
                double windWidth = windRatio * availableWidth;

                // Solar deo (žuta)
                if (solarWidth > 0)
                {
                    Rectangle solarRect = new Rectangle
                    {
                        Width = solarWidth,
                        Height = barHeight,
                        Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8)),
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(solarRect, 5);
                    Canvas.SetTop(solarRect, barTop);
                    TypeGraphCanvas.Children.Add(solarRect);

                    if (solarWidth > 40)
                    {
                        TextBlock solarLabel = new TextBlock
                        {
                            Text = $"Solar {solarRatio:P0}",
                            Foreground = Brushes.Black,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold
                        };
                        Canvas.SetLeft(solarLabel, 5 + solarWidth / 2 - 25);
                        Canvas.SetTop(solarLabel, barTop + 7);
                        TypeGraphCanvas.Children.Add(solarLabel);
                    }
                }

                // Wind deo (plava)
                if (windWidth > 0)
                {
                    Rectangle windRect = new Rectangle
                    {
                        Width = windWidth,
                        Height = barHeight,
                        Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(windRect, 5 + solarWidth);
                    Canvas.SetTop(windRect, barTop);
                    TypeGraphCanvas.Children.Add(windRect);

                    if (windWidth > 40)
                    {
                        TextBlock windLabel = new TextBlock
                        {
                            Text = $"Wind {windRatio:P0}",
                            Foreground = Brushes.White,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold
                        };
                        Canvas.SetLeft(windLabel, 5 + solarWidth + windWidth / 2 - 25);
                        Canvas.SetTop(windLabel, barTop + 7);
                        TypeGraphCanvas.Children.Add(windLabel);
                    }
                }

                // Legenda ispod
                double legendY = barTop + barHeight + 5;

                Rectangle solarLegend = new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8))
                };
                Canvas.SetLeft(solarLegend, 5); Canvas.SetTop(solarLegend, legendY);
                TypeGraphCanvas.Children.Add(solarLegend);

                TextBlock solarLegendText = new TextBlock
                {
                    Text = $"Solar Panel ({solarCount})",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 10
                };
                Canvas.SetLeft(solarLegendText, 20); Canvas.SetTop(solarLegendText, legendY);
                TypeGraphCanvas.Children.Add(solarLegendText);

                Rectangle windLegend = new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246))
                };
                Canvas.SetLeft(windLegend, 120); Canvas.SetTop(windLegend, legendY);
                TypeGraphCanvas.Children.Add(windLegend);

                TextBlock windLegendText = new TextBlock
                {
                    Text = $"Windgenerator ({windCount})",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 10
                };
                Canvas.SetLeft(windLegendText, 135); Canvas.SetTop(windLegendText, legendY);
                TypeGraphCanvas.Children.Add(windLegendText);

                // Procenat skala ispod
                for (int pct = 0; pct <= 100; pct += 20)
                {
                    double x = 5 + (pct / 100.0) * availableWidth;
                    TextBlock pctLabel = new TextBlock
                    {
                        Text = $"{pct}%",
                        Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                        FontSize = 9
                    };
                    Canvas.SetLeft(pctLabel, x - 10);
                    Canvas.SetTop(pctLabel, legendY + 18);
                    TypeGraphCanvas.Children.Add(pctLabel);
                }
            });
        }

        // ── CG4 Undo ─────────────────────────────────────────────────────────

        private bool CanUndo() => _undoStack.Count > 0 && _undoStack.Peek() != null;

        private void OnUndo()
        {
            if (_undoStack.Count == 0) return;
            var prev = _undoStack.Pop();
            UndoCommand.RaiseCanExecuteChanged();

            if (prev != null)
            {
                _selectedResource = prev;
                OnPropertyChanged(nameof(SelectedResource));
                DrawBarGraph();
                _notificationManager.Show("Undo",
                    $"Reverted to resource \"{prev.Name}\".",
                    NotificationType.Information, "WindowNotificationArea");
            }
        }

        // ── Čitanje log fajla ─────────────────────────────────────────────────

        private List<LogRow> ReadLog()
        {
            var entries = new List<LogRow>();
            try
            {
                string path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"../../Logs/Log.txt"));

                if (!System.IO.File.Exists(path)) return entries;

                foreach (var line in System.IO.File.ReadAllLines(path).Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    if (int.TryParse(parts[0], out int id) &&
                        double.TryParse(parts[1], out double val) &&
                        DateTime.TryParse(parts[2], out DateTime dt))
                    {
                        entries.Add(new LogRow { Id = id, CurrentValue = val, Date = dt });
                    }
                }
            }
            catch { /* Ako log ne postoji, vrati praznu listu */ }
            return entries;
        }
    }
}