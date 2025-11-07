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
                _graph.AddData(frame.Timestamp,frame.Byte1, frame.Byte2);
            }

            // Принудительно обновляем график
            _graph.UpdateGraph();

            _graph.StartAndStopGraph(_mainForm1._isRunningLogic);

        }

        private void OnNewDataReceived(object sender, DataReceivedEventArgs e)
        {
            // Обновляем график в UI потоке
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)(() => _graph.AddData(e.Timestamp,e.Byte1, e.Byte2)));
            }
            else
            {
                _graph.AddData(e.Timestamp, e.Byte1, e.Byte2);
            }
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
                        //zedGraphControl.MasterPane.GetImage().Save(filePath, format);
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
     

        public void ReloadHistoricalData()
        {
            // Очищаем график
            _graph.Clear();

            // Загружаем исторические данные
            LoadHistoricalData();
        }

        

        private void button1_Click(object sender, EventArgs e)
        {
            SaveGraphAsImage();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (_graph.HasData == false)
            {
                return;
            }
            _graph.Clear();
        }
    }
}
