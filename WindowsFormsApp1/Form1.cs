using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZedGraph;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }
        // Константы 
        private const byte PACKET_HEADER_LENGTH = 3;
        private const byte PACKET_DATA_LENGTH = 2;
        private const byte MIN_PACKET_LENGTH = (byte)(PACKET_HEADER_LENGTH + PACKET_DATA_LENGTH);
        private string HEADER;

        // Флаги 
        private bool _isReceivingDataNow = false;
        private bool _isNewRxData = false;
        public bool _isRunningLogic = false;


        // Приватные поля класса 
        private static int _timePrdLife = 0;
        private static int _countTimeNoRxData = 0;
        private static int _cntLife = 0;
        private static int _cntErrRx = 0;
        private SerialPort _serialPort;
        private object _portLock = new object();
        private static int countPackages = 0;
        private Timer _timerRxData;
        private byte[] _receiveBuffer; // Общий буфер для накопления данных
        private int _bufferIndex = 0; // Текущая позиция в буфере
        private const int MAX_HISTORY_POINTS = 5000;
        private Form2 _graphForm;

        public event EventHandler<DataReceivedEventArgs> NewDataReceived;
        public List<DataPoint> _frame = new List<DataPoint>(MAX_HISTORY_POINTS);

        public Form1()
        {
            
            InitializeComponent();
            typeof(ListView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(listView_DataReceive, true, null);
           
            _serialPort = new SerialPort();
            _serialPort.DataBits = 8;
            _serialPort.StopBits = StopBits.One;
            _serialPort.Parity = Parity.None;
            _serialPort.ReadTimeout = 25;
            _timerRxData = new Timer();
            
            _serialPort.Close();
            _timerRxData.Interval = 50;

            SetupListViewReceiveDataProtocol();
            StateControls("StateButtonStart_Dis");
            StateControls("StateButtonStop_Dis");

            if (_serialPort.ReadTimeout >= _timerRxData.Interval)
            {
                MessageBox.Show("ReadTimeout >= timer_TxRx.Interval", "Ошибка таймаута", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
                Close();
            }

            AddLogMessage(LogLevel.Info, "Приложение запущено");
            FindComPorts();
            // Подписываемся на событие корректного закрытия через кнопку закрытия
            this.FormClosing += new FormClosingEventHandler(FormClosingEventHandler);
        }

        private void SetupListViewReceiveDataProtocol()
        {
            listView_DataReceive.View = View.Details;
            listView_DataReceive.GridLines = true;
            listView_DataReceive.FullRowSelect = true;
            listView_DataReceive.Columns.Clear();
          
            
                listView_DataReceive.Columns.Add("#", 40, HorizontalAlignment.Right);
                listView_DataReceive.Columns.Add("Byte 1", 80, HorizontalAlignment.Right);
                listView_DataReceive.Columns.Add("Byte 2", 80, HorizontalAlignment.Right);
            

            // Включаем виртуальный режим
            listView_DataReceive.VirtualMode = true;
            listView_DataReceive.VirtualListSize = 0; // Будет обновляться динамически

            listView_DataReceive.RetrieveVirtualItem += new RetrieveVirtualItemEventHandler(ListViewDataReceive_RetrieveVirtualItem);
            listView_DataReceive.Refresh();

        }
        private void ListViewDataReceive_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e )
        {
            if (e.ItemIndex < _frame.Count)
            {
                var frame = _frame[e.ItemIndex];
                var item = new ListViewItem((e.ItemIndex + 1).ToString());
                if(this.checkBox_HbyteLByte.Checked==true)
                {
                    var number = ConvertToInt16(frame.Byte2, frame.Byte1);
                    item.SubItems.Add(number.ToString());
                    item.SubItems.Add("");
                }
                else
                {
                    item.SubItems.Add(frame.Byte1.ToString());
                    item.SubItems.Add(frame.Byte2.ToString());
                }
                
                e.Item = item;
            }
            else
            {
                e.Item = new ListViewItem(" ");
                e.Item.SubItems.Add(" ");
                e.Item.SubItems.Add(" ");
            }
        }
        private void ReSizeListViewDataReceive()
        {
            if (listView_DataReceive.InvokeRequired)
            {
                listView_DataReceive.Invoke(new Action(() => {
                    listView_DataReceive.VirtualListSize = _frame.Count;

                    // Автопрокрутка к последней строке
                    if (listView_DataReceive.Items.Count > 0 && listView_DataReceive.TopItem != null)
                    {
                        listView_DataReceive.EnsureVisible(_frame.Count - 1);
                    }
                }));
            }
            else
            {
                listView_DataReceive.VirtualListSize = _frame.Count;
                if (listView_DataReceive.Items.Count > 0)
                {
                    listView_DataReceive.EnsureVisible(_frame.Count - 1);
                }
            }
        }

        private void FindComPorts()
        {
            comboBox_ComPorts.Items.Clear();
            const int retryCount = 5;
            string[] arrayComPorts = new string[0];
            for (int i = 0; i < retryCount; i++)
            {
                arrayComPorts = SerialPort.GetPortNames();

                if (arrayComPorts.Length == 0)
                {
                    if (i < retryCount - 1)
                    {
                        AddLogMessage(LogLevel.Warning, $"Порт не найден. Повторная попытка {i + 2} из {retryCount} через 1 секунду...");
                        Task.Delay(1000).Wait();
                        continue;
                    }
                }
                else { break; }
            }
            if (arrayComPorts.Length == 0)
            {
                comboBox_ComPorts.Items.Add("Not ports");
                //StateControls("StateComboBox_Dis");
                AddLogMessage(LogLevel.Warning, "Порты не найдены");
            }
            else
            {
                //Проверка занят ли порт 
                for (int i = 0; i < arrayComPorts.Length; i++)
                {
                    try
                    {
                        var sp = new SerialPort(arrayComPorts[i]);
                        sp.Open();
                        sp.Close();
                    }
                    catch (Exception ex)
                    {
                        AddLogMessage(LogLevel.Warning, $"Порт {arrayComPorts[i]} возможно занят или другие проблемы");
                        break;
                    }
                    comboBox_ComPorts.Items.Add(arrayComPorts[i]);
                }
                //comboBox_ComPorts.Items.AddRange(arrayComPorts);
                if (comboBox_ComPorts.Items.Count > 0)
                {
                    StateControls("StateComboBox_Enb");
                }
                else
                {
                    comboBox_ComPorts.Items.Add("Not ports");
                }
            }
            comboBox_ComPorts.SelectedIndex = 0;
        }


        private void OpenComPort()
        {
            if (_serialPort.IsOpen)
            {
                return;
            }
            if (comboBox_ComPorts.Text == "Not ports" || comboBox_ComPorts.Text == null || string.IsNullOrEmpty(comboBox_Baud.Text) || string.IsNullOrEmpty(comboBox_ComPorts.Text))
            {
                MessageBox.Show("Not ports");
                return;
            }
            _serialPort.PortName = comboBox_ComPorts.Text;
            _serialPort.BaudRate = Int32.Parse(comboBox_Baud.Text);
          

            try
            {
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Oшибка подключения", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (_serialPort.IsOpen)
            {
                _receiveBuffer = new byte[1024];
                StateControls("StateLabelStateComPort_Open");
                StateControls("StateComboBox_Dis");
                StateControls("StateComboBoxBaud_Dis");
                StateControls("StateButtonSearch_Dis");
                StateControls("StateButtonStart_Enb");
                StateControls("StateGroupBoxReceive_Enb");
                label_ErrorsTitle.BackColor = SystemColors.Control;
                label_Heartbeat.BackColor = SystemColors.Control;
                AddLogMessage(LogLevel.Info, $"Порт открыт штатно");
                _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
            }
        }
        private void StartReadDate()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                
                _isRunningLogic = true;
                _timerRxData.Start();
                if (_receiveBuffer.Length > 0)
                {
                    Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length);
                }
                AddLogMessage(LogLevel.Info, "Вывод данных...");
                StateControls("StateButtonStop_Enb");
                StateControls("StateButtonStart_Dis");
                StateControls("StateButtonSaveData_Dis");
                StateControls("StateButtonReset_Dis");
                StateControls("StateTextBoxHeaderProtocolReceive_Dis");
                HEADER = TextBoxHeaderProtocolReceive.Text;
                _timerRxData.Tick += new EventHandler(CommunicationTimer_Tick);
                SyncGraphState(true);
                if (_graphForm != null)
                {
                    _graphForm.button2.Enabled = false;
                }

            }
        }

        private void CloseComPort()
        {
            lock (_portLock)
            {
                while (_isReceivingDataNow == true)
                {
                    System.Threading.Thread.Sleep(10);
                }
                _timerRxData.Stop();
                _serialPort.DataReceived -= new SerialDataReceivedEventHandler(DataReceivedHandler);
                _timerRxData.Tick -= new EventHandler(CommunicationTimer_Tick);
                _serialPort.Close();
                if (!_serialPort.IsOpen)
                {
                    Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length);
                    _timerRxData.Stop();
                    StateControls("StateComboBox_Enb");
                    StateControls("StateComboBoxBaud_Enb");
                    StateControls("StateLabelStateComPort_Close");
                    StateControls("StateButtonSearch_Enb");
                    StateControls("StateButtonStop_Dis");
                    StateControls("StateButtonStart_Dis");
                    StateControls("StateTextBoxHeaderProtocolReceive_Enb");
                    StateControls("StateGroupBoxReceive_Dis");
                    _isRunningLogic = false;
                    AddLogMessage(LogLevel.Info, "Порт закрыт");
                    label_ErrorsTitle.BackColor = SystemColors.Control;
                    label_ErrorsTitle.Text = "Disconnect";
                    label_Heartbeat.BackColor = SystemColors.Control;
                    label_Heartbeat.Text = "0";
                    if (_frame.Count > 0)
                    {
                        StateControls("StateButtonSaveData_Enb");
                    }
                    StateControls("StateButtonReset_Enb");
                    if(_graphForm!=null)
                    {
                        _graphForm.button2.Enabled = true;
                    }
                    
                }
            }
        }

        private void StopReadDate()
        {
            _isRunningLogic = false;
            _graphForm?.AddGap();
            _timerRxData.Stop();
            //_graph.StartAndStopGraph(_isRunningLogic);
            // Добавляем разрыв в график
            AddLogMessage(LogLevel.Info, "Вывод данных остановлен");
            StateControls("StateButtonStop_Dis");
            StateControls("StateButtonStart_Enb");
            StateControls("StateTextBoxHeaderProtocolReceive_Enb");
            if (_frame.Count > 0)
            {
                StateControls("StateButtonSaveData_Enb");  
            }
            StateControls("StateButtonReset_Enb");
            SyncGraphState(true);
            if (_graphForm != null)
            {
                _graphForm.button2.Enabled = true;
            }
        }
        private void SyncGraphState(bool isRunning)
        {
            if (_graphForm != null && !_graphForm.IsDisposed)
            {
                // (если вызов из другого потока)
                if (_graphForm.InvokeRequired)
                {
                    _graphForm.Invoke(new Action<bool>(state => {
                        if (!_graphForm.IsDisposed)
                        {
                            _graphForm.SyncGraphState(state);
                        }
                    }), isRunning);
                }
                else
                {
                    _graphForm.SyncGraphState(isRunning);
                }
            }
        }

        private void ProcessReceivedData()
        {
            if(!_serialPort.IsOpen)
            {
                return;
            }
            byte[] marker = Encoding.ASCII.GetBytes(HEADER);
            int processedBytes = 0;

            // Обрабатываем все доступные пакеты в буфере
            while (processedBytes < _bufferIndex)
            {
                // Ищем маркер в оставшейся части буфера
                int indexData = IndexOf(_receiveBuffer, marker, _bufferIndex, processedBytes);

                if (indexData == -1)
                {
                    // Маркер не найден, выходим
                    AddLogMessage(LogLevel.Warning, $"Маркер протокола {HEADER} не найден");
                    break;
                }
                // Проверяем, достаточно ли данных для полного пакета
                if (indexData + MIN_PACKET_LENGTH <= _bufferIndex)
                {
                    // Извлекаем данные
                    byte value1 = _receiveBuffer[indexData + PACKET_HEADER_LENGTH];
                    byte value2 = _receiveBuffer[indexData + PACKET_HEADER_LENGTH + 1];

                    
                    _frame.Add(new DataPoint(DateTime.Now, value1, value2));
                    // Добавляем данные в таблицу
                    ReSizeListViewDataReceive();
                    AddDataToGrid(value1, value2);

                    if (_frame.Count >= MAX_HISTORY_POINTS)
                    {
                        _frame.RemoveAt(0); // Удаляем самые старые данные
                    }
                   
                    processedBytes = indexData + MIN_PACKET_LENGTH;
                }
                else
                {
                    // Неполный пакет, оставляем его в буфере для следующей обработки
                    processedBytes = indexData;
                    break;
                }
            }

            // Сдвигаем оставшиеся данные в начало буфера
            if (processedBytes > 0)
            {
                int remainingBytes = _bufferIndex - processedBytes;
                if (remainingBytes > 0)
                {
                    Array.Copy(_receiveBuffer, processedBytes, _receiveBuffer, 0, remainingBytes);
                }
                _bufferIndex = remainingBytes;
            }
        }

        // Обновленный метод IndexOf с поддержкой смещения
        private int IndexOf(byte[] array, byte[] value, int length, int offset = 0)
        {
            for (int i = offset; i < length - value.Length + 1; i++)
            {
                bool match = true;
                for (int j = 0; j < value.Length; j++)
                {
                    if (array[i + j] != value[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
        private void UpdateConnectionStatus(LogLevel connect)
        {
            if (connect == LogLevel.Info)
            {
                label_ErrorsTitle.Text = "Connect";
                label_ErrorsTitle.BackColor = Color.Lime;
                label_Heartbeat.BackColor = Color.Lime;
            }
            else if (connect == LogLevel.Warning)
            {
                label_ErrorsTitle.Text = "Waiting...";
                label_ErrorsTitle.BackColor = Color.Yellow;
                label_Heartbeat.BackColor = Color.Red;
            }
            else if (connect == LogLevel.Error)
            {
                label_ErrorsTitle.Text = "Disconnect";
                label_ErrorsTitle.BackColor = Color.Red;
                label_Heartbeat.BackColor = Color.Red;
            }
        }

        private void UpdateHeartbeatIndicator()
        {
            _timePrdLife += _timerRxData.Interval;
            if (_timePrdLife >= 150)
            {
                _cntLife++;
                if (_cntLife > 99)
                {
                    _cntLife = 0;
                }
                label_Heartbeat.Text = _cntLife.ToString();
                _timePrdLife = 0;
            }
        }

        private UInt16 ConvertToInt16(byte highByte, byte lowByte)
        {
            return (UInt16)(highByte << 8 | lowByte);
        }

        private void AddDataToGrid(byte byte1, byte byte2)
        {           
             NewDataReceived?.Invoke(this,new DataReceivedEventArgs(DateTime.Now,byte1,byte2));
        }

        public void AddLogMessage(LogLevel level, string log)
        {

            if (listBox_Log.InvokeRequired)
            {
                // Если да, то вызываем этот метод асинхронно в правильном потоке
                listBox_Log.Invoke(new Action<LogLevel, string>(AddLogMessage), level, log);
                return;
            }
            
            // Если мы уже в правильном потоке, то просто добавляем сообщение
            listBox_Log.Items.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {log}");

            // Прокручиваем вниз к последнему элементу
            listBox_Log.SelectedIndex = listBox_Log.Items.Count - 1;
            listBox_Log.ClearSelected();
        }

        public void ExportToCsv()
        {
            if (_frame == null || _isRunningLogic)
            {
                return;
            }
            if(_frame.Count == 0)
            {
                MessageBox.Show("No data", "Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveFileDialog fileDialog = new SaveFileDialog()
            {
                Filter = "CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"dataExport_{DateTime.Now:yyyyMMdd_HHmm}",
                Title = "Сохранить в CSV",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                
            };

            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(fileDialog.FileName, false, Encoding.UTF8))
                    {
                        if (!checkBox_HbyteLByte.Checked)
                            sw.WriteLine("#,Byte1,Byte2,Time");
                        else
                            sw.WriteLine("#,Number,Time");

                        for (int i = 0; i < _frame.Count; i++)
                        {
                            int numberpackage = i + 1;
                            if (checkBox_HbyteLByte.Checked)
                            {
                                sw.WriteLine($"{numberpackage},{ConvertToInt16(_frame[i].Byte2, _frame[i].Byte1)},{_frame[i].Timestamp:HH:mm:ss.fff}");
                            }
                            else
                              sw.WriteLine($"{numberpackage},{_frame[i].Byte1},{_frame[i].Byte2},{_frame[i].Timestamp:HH:mm:ss.fff}");
                        }
                        AddLogMessage(LogLevel.Info, $"Данные сохранены в CSV - {fileDialog.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage(LogLevel.Error, $"Ошибка сохранения данных. {ex.Message}");
                }
            }
           
        }
        
        private void StateControls(string state)
        {
            if (state.Equals("StateComboBox_Dis"))
            {
                comboBox_ComPorts.Enabled = false;
            }
            if (state.Equals("StateComboBox_Enb"))
            {
                comboBox_ComPorts.Enabled = true;
            }
            if (state.Equals("StateLabelStateComPort_Open"))
            {
                label_StateComPort.Text = "Open";
                label_StateComPort.BackColor = Color.Lime;
            }
            if (state.Equals("StateLabelStateComPort_Close"))
            {
                label_StateComPort.Text = "Not open";
                label_StateComPort.BackColor = SystemColors.Control;
            }
            if (state.Equals("StateComboBoxBaud_Enb"))
            {
                comboBox_Baud.Enabled = true;

            }
            if (state.Equals("StateComboBoxBaud_Dis"))
            {
                comboBox_Baud.Enabled = false;
            }
            if (state.Equals("StateButtonSearch_Dis"))
            {
                button_search.Enabled = false;
            }
            if (state.Equals("StateButtonSearch_Enb"))
            {
                button_search.Enabled = true;
            }
            if (state.Equals("StateButtonStart_Enb"))
            {
                button_start.Enabled = true;
            }
            if (state.Equals("StateButtonStart_Dis"))
            {
                button_start.Enabled = false;
            }
            if (state.Equals("StateButtonStop_Enb"))
            {
                button_stop.Enabled = true;
            }
            if (state.Equals("StateButtonStop_Dis"))
            {
                button_stop.Enabled = false;
            }
            if (state.Equals("StateButtonSaveData_Enb"))
            {
                button_save_data.Enabled = true;
            }
            if (state.Equals("StateButtonSaveData_Dis"))
            {
                button_save_data.Enabled = false;
            }
            if (state.Equals("StateButtonReset_Enb"))
            {
                button_Clear.Enabled = true;
            }
            if (state.Equals("StateButtonReset_Dis"))
            {
                button_Clear.Enabled = false;
            }
            if (state.Equals("StateButtonClearGraph_Enb"))
            {
                button_Clear.Enabled = true;
            }
            if (state.Equals("StateButtonClearGraph_Dis"))
            {
                button_Clear.Enabled = false;
            }
            if (state.Equals("StateTextBoxHeaderProtocolReceive_Enb"))
            {
                TextBoxHeaderProtocolReceive.Enabled = true;
            }
            if (state.Equals("StateTextBoxHeaderProtocolReceive_Dis"))
            {
                TextBoxHeaderProtocolReceive.Enabled = false;
            }
            if (state.Equals("StateGroupBoxReceive_Dis"))
            {
                groupBoxReceive.Enabled = false;
            }
            if (state.Equals("StateGroupBoxReceive_Enb"))
            {
                groupBoxReceive.Enabled = true;
            }



        }
        // Обрботчики терминала

        private void UpdateUIWithReceivedData(byte[] receivedBytes)
        {
            if (!listBox_HEX.IsDisposed)
            {
                if (listBox_HEX.Items.Count > 1000)
                {
                    listBox_HEX.Items.RemoveAt(0);
                    listBox_ASСII.Items.RemoveAt(0);
                }
                foreach (byte b in receivedBytes)
                {
                    listBox_HEX.Items.Add(b.ToString("X2"));
                    if (listBox_ASСII.Items.Count != listBox_HEX.Items.Count - 2)
                    {
                        listBox_ASСII.Items.Add(" ");
                    }


                }
                listBox_HEX.Items.Add(" ");
                listBox_HEX.TopIndex = listBox_HEX.Items.Count - 1;

            }
            if (!listBox_DEC.IsDisposed)
            {
                if (listBox_DEC.Items.Count > 1000)
                {
                    listBox_DEC.Items.RemoveAt(0);
                }
                foreach (byte b in receivedBytes)
                {
                    listBox_DEC.Items.Add(b);
                }
                listBox_DEC.Items.Add(" ");
                listBox_DEC.TopIndex = listBox_DEC.Items.Count - 1;
            }

            if (!listBox_ASСII.IsDisposed)
            {
                string asciiString = Encoding.ASCII.GetString(receivedBytes);

                if (!string.IsNullOrEmpty(asciiString))
                {
                    if (listBox_ASСII.Items.Count == listBox_HEX.Items.Count)
                    {
                        listBox_ASСII.Items.RemoveAt(listBox_ASСII.Items.Count - 1);
                        listBox_ASСII.Items.RemoveAt(listBox_ASСII.Items.Count - 1);
                    }

                    listBox_ASСII.Items.Add(asciiString);
                }
                //listBox_ASСII.Items.Add(" ");
                listBox_ASСII.TopIndex = listBox_ASСII.Items.Count - 1;
            }
            if (!listBox_BIN.IsDisposed)
            {
                if (listBox_BIN.Items.Count > 1000)
                {
                    listBox_BIN.Items.Remove(0);
                }
                foreach (byte b in receivedBytes)
                {
                    string bits = Convert.ToString(b, 2).PadLeft(8, '0');
                    listBox_BIN.Items.Add(bits);
                }
                listBox_BIN.Items.Add(" ");

                listBox_BIN.TopIndex = listBox_BIN.Items.Count - 1;
            }
        }

        // Обработка для отправки через терминал HEX
        public byte[] ParseHex(string input)
        {
            var bytes = new List<byte>();
            try
            {

                foreach (var token in input.Replace("\r", "").Replace("\n", "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    bytes.Add(Convert.ToByte(token, 16));

            }
            catch (Exception ex)
            {

                AddLogMessage(LogLevel.Warning, $"Ошибка данных: {ex.Message}");

            }
            return bytes.ToArray();
        }
        //  Обработка для отправки через терминал DEC
        public byte[] ParseDec(string input)
        {
            var bytes = new List<byte>();
            try
            {
                foreach (var token in input.Replace("\r", "").Replace("\n", "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        throw new FormatException($"Некорректное число: {token}");
                    //bytes.Add((byte)(v & 0xFF)); - отстаток от деления
                    bytes.Add(Convert.ToByte(token, 10));
                }
            }
            catch (Exception ex)
            {
                AddLogMessage(LogLevel.Warning, $"Ошибка данных: {ex.Message}");
            }

            return bytes.ToArray();

        }
        // Отправить через терминал
        public byte[] SendByFormat(SerialPort port, string input)
        {
            if (!port.IsOpen) return null;

            if (radioButton_DEC.Checked)
            {
                var bytesDec = ParseDec(input);
                if (bytesDec.Length > 0 && bytesDec != null)
                {
                    port.Write(bytesDec, 0, bytesDec.Length);
                }

                return bytesDec;
            }
            else if (radioButton_HEX.Checked)
            {
                var bytesHex = ParseHex(input);
                if (bytesHex.Length > 0 && bytesHex != null)
                {
                    port.Write(bytesHex, 0, bytesHex.Length);
                }
                return bytesHex;
            }
            else if (radioButton_ASCII.Checked)
            {
                var bytesAscii = Encoding.ASCII.GetBytes(input.Replace("\r", "").Replace("\n", ""));
                port.Encoding = Encoding.ASCII;
                if (bytesAscii.Length > 0 && bytesAscii != null)
                {
                    port.Write(bytesAscii, 0, bytesAscii.Length);
                }

                return bytesAscii;
            }
            return null;

        }

        public void UpateListBoxTransmit(byte[] receivedBytes)
        {
            if (radioButton_HEX.Checked)
            {
                foreach (byte b in receivedBytes)
                {
                    listBox_Transmit.Items.Add(b.ToString("X2"));
                }
            }
            if (radioButton_DEC.Checked)
            {
                foreach (byte b in receivedBytes)
                {
                    listBox_Transmit.Items.Add(b);
                }
            }

            if (radioButton_ASCII.Checked)
            {
                string asciiString = Encoding.ASCII.GetString(receivedBytes);
                if (!string.IsNullOrEmpty(asciiString))
                {
                    listBox_Transmit.Items.Add(asciiString);
                }
            }

        }

        // Обработчики событий 
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort.IsOpen != true) return;

            byte[] chunk;
            lock (_portLock)
            {
                int bytesToRead = _serialPort.BytesToRead;

                if (bytesToRead <= 0) return;

                _isReceivingDataNow = true;

                chunk = new byte[bytesToRead];
                int read = _serialPort.Read(chunk, 0, bytesToRead);
                if (read != bytesToRead)
                {
                    if (read <= 0) return;
                    Array.Resize(ref chunk, read);
                }
            }

            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (Pages.SelectedTab == tabPage1)
                    {
                        // переносим данные в общий буфер в UI-потоке
                        if (_bufferIndex + chunk.Length > _receiveBuffer.Length)
                        {
                            Array.Clear(_receiveBuffer, 0, _receiveBuffer.Length);
                            _bufferIndex = 0;
                            AddLogMessage(LogLevel.Warning, "Буфер переполнен, очистка....");
                        }
                        Buffer.BlockCopy(chunk, 0, _receiveBuffer, _bufferIndex, chunk.Length);
                        _bufferIndex += chunk.Length;
                        _isNewRxData = true;
                    }
                    else if (Pages.SelectedTab == tabPage3)
                    {
                        UpdateUIWithReceivedData(chunk);
                    }
                }));
            }

            _isReceivingDataNow = false;
        }
       

        private void CommunicationTimer_Tick(object sender, EventArgs e)
        {
            if (!_serialPort.IsOpen)
            {
                return;
            }
            _timerRxData.Stop();

            try
            {
                // Проверка наличия данных
                if (!_isNewRxData)
                {
                    _countTimeNoRxData += _timerRxData.Interval;

                    // Если данных нет дольше 1500 мс, считаем соединение разорванным
                    if (_countTimeNoRxData > 1500)
                    {
                        _countTimeNoRxData = 0;

                        if (!SerialPort.GetPortNames().Contains(_serialPort.PortName))
                        {
                            _isNewRxData = false;
                            AddLogMessage(LogLevel.Error, "Обрыв порта, порт не найден");
                            CloseComPort();
                            UpdateConnectionStatus(LogLevel.Error);
                            FindComPorts();

                            if (comboBox_ComPorts.Items.Contains(_serialPort.PortName))
                            {
                                for (int i = 0; i < comboBox_ComPorts.Items.Count; i++)
                                {
                                    if (comboBox_ComPorts.Items[i].Equals(_serialPort.PortName))
                                    {
                                        comboBox_ComPorts.SelectedIndex = i;
                                        OpenComPort();
                                        break;

                                    }
                                }
                            }
                        }
                        else if (_isRunningLogic)
                        {
                            UpdateConnectionStatus(LogLevel.Warning);
                            AddLogMessage(LogLevel.Warning, "Нет данных более 1500 мс");
                            _countTimeNoRxData = 0;
                        }
                    }
                }
                else
                {
                    // Обновляем статус соединения
                    UpdateConnectionStatus(LogLevel.Info);

                    // Обработка данных
                    ProcessReceivedData();

                    // Сброс флагов
                    _isNewRxData = false;
                    _countTimeNoRxData = 0;

                    // Обновляем индикатор "сердцебиения"
                    UpdateHeartbeatIndicator();
                }
            }
            finally
            {
                // Всегда перезапускаем таймер
                _timerRxData.Start();
            }
        }

        // События контроллов

        private void Form1_Load(object sender, EventArgs e)
        {
            comboBox_Baud.Items.AddRange(new string[] { "9600", "19200", "38400", "57600", "115200" });
            comboBox_Baud.SelectedIndex = 0;
            label_ErrorsTitle.Text = "Disconnect";
            label_Heartbeat.Text = "0";
            label_ErrorsTitle.BackColor = SystemColors.Control;
            label_Heartbeat.BackColor = SystemColors.Control;
            TextBoxHeaderProtocolReceive.Text = "*D#";
          
        }
    
        // Обработчик события закрытия формы
        private void FormClosingEventHandler(object sender, FormClosingEventArgs e)
        {
            if(e.CloseReason==CloseReason.UserClosing)
            {
                if(_isRunningLogic==true)
                {
                    StopReadDate();
                }

                if (_graphForm != null && !_graphForm.IsDisposed)
                {
                    _graphForm.Close(); // Вызываем закрытие формы
                    System.Threading.Thread.Sleep(50);
                }
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    CloseComPort();
                }
                if (_serialPort != null)
                {
                    _serialPort.Dispose();
                    _serialPort = null;
                }

                ClearTerminalReceive();
            }
        }

 
        private void tabControl1_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (e.TabPage == tabPage3 && _isRunningLogic)
            {
                e.Cancel = true;
            }
        }

        private void button_ClearGraph_Click(object sender, EventArgs e)
        {
            if (_isRunningLogic)
            {
                return;
            }
            DialogResult result = MessageBox.Show("Все данные будут утеряны. Сбросить?", "Сброс", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (result == DialogResult.No)
            {
                return;
            }
            _isReceivingDataNow = false;
            _isNewRxData = false;
            _timePrdLife = 0;
            _countTimeNoRxData = 0;
            _cntLife = 0;
            _cntErrRx = 0;
            countPackages = 0;
            _receiveBuffer = new byte[1024];
            _bufferIndex = 0;
            listView_DataReceive.Invalidate();
            _timePrdLife = 0;
     
            _frame.Clear();
            //_graph.Clear();
            //_graph.Dispose();
            StateControls("StateButtonSaveData_Dis");
            StateControls("StateButtonReset_Dis");
        }

       
        private void ClearTerminalReceive()
        {
            if (Pages.SelectedTab == tabPage3)
            {
                listBox_ASСII.Items.Clear();
                listBox_BIN.Items.Clear();
                listBox_DEC.Items.Clear();
                listBox_HEX.Items.Clear();
            }
        }


        private void button_OpenComPort_Click(object sender, EventArgs e)
        {
            OpenComPort();
        }

        private void button_CloseComPort_Click(object sender, EventArgs e)
        {
            CloseComPort();
        }

        private void button_search_Click_1(object sender, EventArgs e)
        {
            FindComPorts();
        }

        private void radioButton1_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.DataBits = 5;
        }

        private void radioButton2_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.DataBits = 6;
        }

        private void radioButton3_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.DataBits = 7;
        }

        private void radioButton4_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.DataBits = 8;
        }

        private void radioButton13_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.StopBits = StopBits.One;
        }

        private void radioButton12_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.StopBits = StopBits.OnePointFive;
        }

        private void radioButton11_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.StopBits = StopBits.Two;
        }

        private void radioButton8_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.Parity = Parity.None;
        }

        private void radioButton7_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.Parity = Parity.Odd;
        }

        private void radioButton6_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.Parity = Parity.Even;
        }

        private void radioButton5_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.Parity = Parity.Mark;
        }

        private void radioButton9_CheckedChanged_1(object sender, EventArgs e)
        {
            _serialPort.Parity = Parity.Space;
        }

  
        private void button1_Click(object sender, EventArgs e)
        {
            ClearTerminalReceive();
        }

        private void button_ClearTransmit_Click(object sender, EventArgs e)
        {
            listBox_Transmit.Items.Clear();
        }

        private void button_start_Click(object sender, EventArgs e)
        {
            StartReadDate();
        }

        private void button_stop_Click(object sender, EventArgs e)
        {
            StopReadDate();
        }

        private void button_Clear_Click_1(object sender, EventArgs e)
        {
            if (_isRunningLogic)
            {
                return;
            }
            DialogResult result = MessageBox.Show("Все данные будут утеряны. Сбросить?", "Сброс", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (result == DialogResult.No)
            {
                return;
            }
            _isReceivingDataNow = false;
            _isNewRxData = false;
            _timePrdLife = 0;
            _countTimeNoRxData = 0;
            _cntLife = 0;
            _cntErrRx = 0;
            countPackages = 0;
            _receiveBuffer = new byte[1024];
            _bufferIndex = 0;

            _timePrdLife = 0;

            _frame.Clear();
            if (_graphForm != null)
            {
                _graphForm.ClearGraph();
            }
            //_graph.Clear();
            //_graph.Dispose();
            SetupListViewReceiveDataProtocol();
            StateControls("StateButtonSaveData_Dis");
            StateControls("StateButtonReset_Dis");
        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            // Проверяем, не закрыта ли форма
            if (_graphForm == null || _graphForm.IsDisposed)
            {
                _graphForm = new Form2(this);
                _graphForm.FormClosed += (s, args) => _graphForm = null;
            }
            else
            {
                _graphForm.ReloadHistoricalData();
            }
            _graphForm.Show();
            _graphForm.WindowState = FormWindowState.Normal; // Если форма была свернута
            _graphForm.BringToFront(); // Помещаем окно на передний план
        }

        private void button_save_data_Click(object sender, EventArgs e)
        {
            ExportToCsv();
        }

        private void checkBox_HbyteLByte_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_HbyteLByte.Checked)
            {
                listView_DataReceive.Columns[1].Text = "Number";
                listView_DataReceive.Columns[1].Width = 160;
                listView_DataReceive.Columns[2].Text = "";
                listView_DataReceive.Columns[2].Width = 0;
                _graphForm?.ReloadHistoricalData();
            }
            else
            {
                listView_DataReceive.Columns[1].Text = "Byte 1";
                listView_DataReceive.Columns[1].Width = 80;
                listView_DataReceive.Columns[2].Text = "Byte 2";
                listView_DataReceive.Columns[2].Width = 80;
                _graphForm?.ReloadHistoricalData();
            }
            listView_DataReceive.Refresh();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (!_serialPort.IsOpen || _isNewRxData) return;

            byte[] listBoxData = SendByFormat(_serialPort, textBox_TransmitData.Text);

            if (listBoxData.Length > 0 && listBoxData != null)
            {
                UpateListBoxTransmit(listBoxData);
            }
        }
    }
}