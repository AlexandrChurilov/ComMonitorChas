using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ZedGraph;

namespace WindowsFormsApp1
{
    public class MyZedGraph
    {
        // Приватные поля класса 
        private readonly ZedGraphControl _zedGraphControl;
        private readonly RollingPointPairList _graphPoints;
        private readonly RollingPointPairList _graphPoints2;
        private DateTime _graphStartTime; 
        private const int MaxGraphPoints = 1000;
        private readonly Timer _graphTimer;
        private const double GapValue = double.NaN;
        private LineItem curve;
        private LineItem curve2;
        private readonly object _bufferLock = new object();
        private readonly List<Tuple<DateTime, byte, byte, bool>> _dataBuffer = new List<Tuple<DateTime, byte, byte, bool>>(100);
     
        public bool HasData { get; private set; } = false;

        public MyZedGraph(ZedGraphControl zedGraphControl)
        {
            // Иницилизация полей класса
            _zedGraphControl = zedGraphControl;

            // Двойная буферизация через рефлексию (приватное свойство DoubleBuffered)
            typeof(ZedGraphControl).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.NonPublic |
                                                                   System.Reflection.BindingFlags.Instance |
                                                                   System.Reflection.BindingFlags.SetProperty,
                                                                   Type.DefaultBinder, zedGraphControl, new object[] { true });

            _graphPoints = new RollingPointPairList(MaxGraphPoints);
            _graphPoints2 = new RollingPointPairList(MaxGraphPoints);

            InitializeZedGraph();

            // Настраиваем таймер для обновления графика
            _graphTimer = new Timer();
            _graphTimer.Interval = 100;
            // Обновление графика каждый тик таймера
            _graphTimer.Tick += (s, ev) =>
            {
                if (zedGraphControl != null && zedGraphControl.Visible)
                {
                    ProcessBufferedData();
                }
            };
        }

        private void InitializeZedGraph()
        {
            // Настройка контрола 
            GraphPane myPane = _zedGraphControl.GraphPane;
            myPane.Title.Text = "График данных";
            myPane.XAxis.Title.Text = "Время, с";
            myPane.YAxis.Title.Text = "Значение";
            myPane.YAxis.Scale.Min = 0;
            
            // Создание списка точек для графика 1
            curve = myPane.AddCurve("Данные byte1", _graphPoints, Color.Blue, SymbolType.Circle);
            // Создание списка точек для графика 2
            curve2 = myPane.AddCurve("Данные byte2", _graphPoints2, Color.Red, SymbolType.Triangle);

            // Настройка внешнего вида кривой 1,2
            curve.Line.Width = 1.5f;
            curve2.Line.Width = 1.5f;

            // Отображение точек на кривой 1
            curve.Symbol.Size = 3;
            curve.Symbol.Fill = new Fill(Color.Blue);
            curve.Symbol.IsVisible = false;
            
            // Отображение точек на кривой 2
            curve2.Symbol.Size = 3;
            curve2.Symbol.Fill = new Fill(Color.Red);
            curve2.Symbol.IsVisible = false;
            curve2.IsVisible = true;
            // Отключаем сглаживание
            curve.Line.IsAntiAlias = false;
            curve2.Line.IsAntiAlias = false;
            // Включаем отображение значений точек при наведении
            _zedGraphControl.IsShowPointValues = true;
            _zedGraphControl.PointValueEvent += ZedGraphControl_PointValueEvent;

            // Включаем прокрутку и масштабирование
            _zedGraphControl.IsShowHScrollBar = true;
            _zedGraphControl.IsShowVScrollBar = true;
            _zedGraphControl.IsAutoScrollRange = true;
            _zedGraphControl.ScrollGrace = 0.1;
            _zedGraphControl.ScrollMaxX = double.MaxValue;
            _zedGraphControl.ScrollMaxY = double.MaxValue;
            _zedGraphControl.ScrollMinX = double.MinValue;
            _zedGraphControl.ScrollMinY = double.MinValue;
            _zedGraphControl.ZoomStepFraction = 0.1;
            _zedGraphControl.IsEnableZoom = true;

            // Настраиваем сетку
            myPane.XAxis.MajorGrid.IsVisible = true;
            myPane.YAxis.MajorGrid.IsVisible = true;
            myPane.XAxis.MajorGrid.Color = Color.LightGray;
            myPane.YAxis.MajorGrid.Color = Color.LightGray;

            // Настраиваем легенду
            myPane.Legend.IsVisible = true;
            myPane.Legend.Position = LegendPos.Bottom;

            // Обновляем график
            _zedGraphControl.AxisChange(); // Сначала обновляем масштабы
            _zedGraphControl.Invalidate(); // Потом перерисовываем
        }

        public void SizePoints(float sizePoint)
        {
            curve.Symbol.Size = sizePoint;
            curve2.Symbol.Size = sizePoint;
        }

        public void SizeCurve(float sizeCurve)
        {
            curve.Line.Width = sizeCurve;
            curve2.Line.Width = sizeCurve;
        }

        public void ShowPoints(bool havePoints)
        {
            curve.Symbol.IsVisible= havePoints;
            curve2.Symbol.IsVisible = havePoints;
        }

        public bool IsVisibleCurve1
        {
            get { return curve.IsVisible; }
            set { curve.IsVisible = value; }
        }
        public bool IsVisibleCurve2
        {
            get { return curve2.IsVisible; }
            set { curve2.IsVisible = value; }
        }


        // Событие отображения данных при наведении на точку 
        private string ZedGraphControl_PointValueEvent(ZedGraphControl sender, GraphPane pane, CurveItem curve, int iPt)
        {
            PointPair point = curve[iPt];
            return $"Время: {point.X:F2} с\nЗначение: {point.Y:F0}";
        }

        public void AddData(DateTime timestamp, byte byte1, byte byte2, bool stateCheckBox)
        {
            lock (_bufferLock)
            {
                _dataBuffer.Add(Tuple.Create(timestamp, byte1, byte2, stateCheckBox));
            }
            if (HasData == false)
            {
                HasData = true;
                if (_graphStartTime == DateTime.MinValue)
                {
                    _graphStartTime = timestamp;
                }
            }
        }

        private void ProcessBufferedData()
        {
            List<Tuple<DateTime, byte, byte, bool>> dataToProcess;

            lock (_bufferLock)
            {
                if (_dataBuffer.Count == 0) return;
                dataToProcess = new List<Tuple<DateTime, byte, byte, bool>>(_dataBuffer);
                _dataBuffer.Clear();
            }

            // Обрабатываем все точки за один раз
            foreach (var data in dataToProcess)
            {
                double timeElapsed = (data.Item1 - _graphStartTime).TotalSeconds;

                if (data.Item4) // stateCheckBox
                {
                    _graphPoints.Add(timeElapsed, (UInt16)(data.Item3 << 8 | data.Item2));
                }
                else
                {
                    _graphPoints.Add(timeElapsed, data.Item2);
                    _graphPoints2.Add(timeElapsed, data.Item3);
                }
            }
            if (_graphPoints.Count > 0)
            {
                double maxX = _graphPoints[_graphPoints.Count - 1].X;
                double currentMaxX = _zedGraphControl.GraphPane.XAxis.Scale.Max;
                // Если пользователь смотрит на конец графика (в пределах 2 секунд от конца)
                if (Math.Abs(currentMaxX - maxX) <= 2.0)
                {
                    // Продолжаем автопрокрутку
                    double visibleRange = 20.0;
                    _zedGraphControl.GraphPane.XAxis.Scale.Min = Math.Max(0, maxX - visibleRange);
                    _zedGraphControl.GraphPane.XAxis.Scale.Max = maxX + 1;
                }           
            }
            // вызов обновления для всех точек
            _zedGraphControl.AxisChange();
            _zedGraphControl.Invalidate();
        }

        public void SetInitialTime(DateTime startTime)
        {
            _graphStartTime = startTime;
            HasData = true;
        }

        public void UpdateGraph()
        {
            ProcessBufferedData();
        }

        public void Clear()
        {
            // Очищаем буфер
            lock (_bufferLock)
            {
                _dataBuffer.Clear();
            }
            // Очищаем графики
            _graphPoints.Clear();
            _graphPoints2.Clear();
            // Сбрасываем состояние
            HasData = false;
            _graphStartTime = DateTime.MinValue;
            // Обновляем график
            _zedGraphControl.AxisChange();
            _zedGraphControl.Invalidate();
        }

        public void AddGap()
        {
            if (!HasData || _graphPoints.Count == 0) return;

            double currentTime = (DateTime.Now - _graphStartTime).TotalSeconds;
            _graphPoints.Add(currentTime, GapValue);
            _graphPoints2.Add(currentTime, GapValue);
        }
        public void StartAndStopGraph(bool start)
        {
            if (start == true)
            {
                _graphTimer.Start();
            }
            else 
            {
                _graphTimer.Stop();
            }
        }
       
        public void Dispose()
        {
            Clear();
            _graphTimer?.Stop();
            _graphTimer?.Dispose();
        }
    }
}