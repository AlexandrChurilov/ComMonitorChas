using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZedGraph;
using static WindowsFormsApp1.Form1;

namespace WindowsFormsApp1
{
    public partial class Form2 : Form
    {
        private Form1 _mainForm1 { get; }
        private MyZedGraph _graph;
        
        

        public Form2(Form1 mainForm)
        {
            InitializeComponent();
            _mainForm1 = mainForm;
            
            _graph = new MyZedGraph(this.zedGraphControl);
            LoadHistoricalData();
            mainForm.NewDataReceived += OnNewDataReceived;
            SyncGraphState(_mainForm1._isRunningLogic);
            
        }
        public void SyncGraphState(bool isRunning)
        {
            _graph?.StartAndStopGraph(isRunning);
        }

        private void LoadHistoricalData()
        {
            if (_mainForm1._frame == null || _mainForm1._frame.Count == 0)
                return;

            _graph.StartAndStopGraph(false);

            // Устанавливаем начальное время по первой точке
            if (_mainForm1._frame.Count > 0)
            {
                _graph.SetInitialTime(_mainForm1._frame[0].Timestamp);
            }

            // Загружаем все исторические данные с их временными метками
            foreach (var frame in _mainForm1._frame)
            {
                _graph.AddData(frame.Timestamp,frame.Byte1, frame.Byte2, _mainForm1.checkBox_HbyteLByte.Checked);
            }

            // Принудительно обновляем график
            //_graph.UpdateGraph();

            _graph.StartAndStopGraph(_mainForm1._isRunningLogic);

        }

        private void OnNewDataReceived(object sender, DataReceivedEventArgs e)
        {
            _graph.AddData(e.Timestamp, e.Byte1, e.Byte2, _mainForm1.checkBox_HbyteLByte.Checked);
        }

        private void SaveGraphAsImage()
        {
            if (_mainForm1._isRunningLogic)
            {
                return;
            }
            // Открываем диалог сохранения файла
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg|Bitmap Files (*.bmp)|*.bmp|All files (*.*)|*.*";
                saveFileDialog.FilterIndex = 1;
                saveFileDialog.RestoreDirectory = true;

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;
                    string extension = Path.GetExtension(filePath).ToLower();

                    ImageFormat format;
                    switch (extension)
                    {
                        case ".jpg":
                            format = ImageFormat.Jpeg;
                            break;
                        case ".bmp":
                            format = ImageFormat.Bmp;
                            break;
                        default:
                            format = ImageFormat.Png;
                            break;
                    }
                    try
                    {
                        // Сохраняем график как изображение
                        zedGraphControl.MasterPane.GetImage().Save(filePath, format);
                        _mainForm1.AddLogMessage(LogLevel.Info, $"График сохранен как изображение: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        _mainForm1.AddLogMessage(LogLevel.Error, $"Ошибка при сохранении графика: {ex.Message}");
                    }
                }
            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Отписываемся от события
            if (_mainForm1 != null)
            {
                _mainForm1.NewDataReceived -= OnNewDataReceived;
            }

            // Останавливаем и освобождаем график
            if (_graph != null)
            {
                _graph.StartAndStopGraph(false);
                _graph.Dispose();
                _graph = null;
            }

            base.OnFormClosed(e);
        }
        public void AddGap()
        {
            _graph.AddGap();
        }

        public void ReloadHistoricalData()
        {
            // Очищаем график
            _graph.Clear();

            // Загружаем исторические данные
            LoadHistoricalData();
        }

        
        public void ClearGraph()
        {
            if (_graph.HasData == false)
            {
                return;
            }
            _graph.Clear();
        }
        

        private void checkBox_showPoints_CheckedChanged_1(object sender, EventArgs e)
        {
            _graph.ShowPoints(checkBox_showPoints.Checked);
            if (checkBox_showPoints.Checked)
            {
                numericUpDown1.Enabled = true;
            }
            else
            {
                numericUpDown1.Value = 3;
                numericUpDown1.Enabled = false;
            }
        }

        private void numericUpDown1_ValueChanged_1(object sender, EventArgs e)
        {
            _graph.SizePoints((float)numericUpDown1.Value);
        }

        private void checkBox_ShowCurve1_CheckedChanged_1(object sender, EventArgs e)
        {
            _graph.IsVisibleCurve1 = checkBox_ShowCurve1.Checked;
        }

        private void checkBox_ShowCurve2_CheckedChanged_1(object sender, EventArgs e)
        {
            _graph.IsVisibleCurve2 = checkBox_ShowCurve2.Checked;
        }

        private void numericUpDown2_ValueChanged_1(object sender, EventArgs e)
        {
            _graph.SizeCurve((float)numericUpDown2.Value);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            SaveGraphAsImage();
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            ClearGraph();
        }
    }
}
