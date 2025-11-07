using System;
using System.Drawing;
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
        private DateTime _graphStartTime2;
        private const int MaxGraphPoints = 500;
        private const float MaxY = 255;
        private readonly Timer _graphTimer;
        private const double GapValue = double.NaN;
        private LineItem curve;
        private LineItem curve2;
        
        public bool HasData { get; private set; } = false;
       

        public MyZedGraph(ZedGraphControl zedGraphControl)
        {
            // Иницилизация полей класса
            _zedGraphControl = zedGraphControl;
            typeof(ZedGraphControl).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.NonPublic |
                                                                   System.Reflection.BindingFlags.Instance |
                                                                   System.Reflection.BindingFlags.SetProperty,
                                                                   Type.DefaultBinder, zedGraphControl, new object[] { true });

            _graphPoints = new RollingPointPairList(MaxGraphPoints);
            _graphPoints2 = new RollingPointPairList(MaxGraphPoints);

            InitializeZedGraph();

            // Настраиваем таймер для обновления графика
            _graphTimer = new Timer();
            _graphTimer.Interval = 50;
            // Обновление графика каждый тик таймера
            _graphTimer.Tick += (s, ev) =>
            {
                if (zedGraphControl != null && zedGraphControl.Visible)
                {
                    _zedGraphControl.AxisChange();
                    _zedGraphControl.Invalidate();
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
            myPane.YAxis.Scale.Max = MaxY;

            // Создание списка точек для графика 1
            curve = myPane.AddCurve("Данные byte1", _graphPoints, Color.Blue, SymbolType.Circle);
            // Создание списка точек для графика 2
            curve2 = myPane.AddCurve("Данные byte2", _graphPoints2, Color.Red, SymbolType.Triangle);

            // Настройка внешнего вида кривой 1,2
            curve.Line.Width = 1.5f;
            curve2.Line.Width = 1.5f;

            // Отображение точек на кривой 1
            curve.Symbol.Size = 4;
            curve.Symbol.Fill = new Fill(Color.Blue);
            curve.Symbol.IsVisible = true;
           
            // Отображение точек на кривой 2
            curve2.Symbol.Size = 4;
            curve2.Symbol.Fill = new Fill(Color.Red);
            curve2.Symbol.IsVisible = true;
            curve2.IsVisible = true;
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

        // Событие отображения данных при наведении на точку 
        private string ZedGraphControl_PointValueEvent(ZedGraphControl sender, GraphPane pane, CurveItem curve, int iPt)
        {
            PointPair point = curve[iPt];
            return $"Время: {point.X:F2} с\nЗначение: {point.Y:F0}";
        }

        public void AddData(DateTime timestamp,byte byte1, byte byte2)
        {

            if (HasData == false)
            {
                HasData = true;
                // Устанавливаем начальное время, если оно еще не установлено
                if (_graphStartTime == DateTime.MinValue)
                {
                    _graphStartTime = timestamp;
                }
            }

            // Вычисляем время в секундах от общей точки отсчета
            double timeElapsed = (timestamp - _graphStartTime).TotalSeconds;

            _graphPoints.Add(timeElapsed, byte1);
            _graphPoints2.Add(timeElapsed, byte2);


        }

        public void SetInitialTime(DateTime startTime)
        {
            _graphStartTime = startTime;
            HasData = true;
        }

        public void UpdateGraph()
        {
            if (_zedGraphControl.InvokeRequired)
            {
                _zedGraphControl.Invoke((MethodInvoker)(() => {
                    _zedGraphControl.AxisChange();
                    _zedGraphControl.Invalidate();
                }));
            }
            else
            {
                _zedGraphControl.AxisChange();
                _zedGraphControl.Invalidate();
            }
        }

        public void Clear()
        {
            // Очищаем графики
            _graphPoints.Clear();
            _graphPoints2.Clear();
            // Обновляем график
            _zedGraphControl.AxisChange();
            _zedGraphControl.Invalidate();
        }

        public void AddGap()
        {
            if (_graphPoints.Count > 0 && _graphPoints2.Count > 0)
            {
                double currentTime = (DateTime.Now - _graphStartTime).TotalSeconds;

                // Добавляем точку с NaN для создания разрыва
                _graphPoints.Add(currentTime, GapValue);
                _graphPoints2.Add(currentTime, GapValue);
            }
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
            _graphTimer?.Stop();
            _graphTimer?.Dispose();
        }
    }
}