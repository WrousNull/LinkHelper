using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace linkHelper
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort serialPort;
        private Socket clientSocket, listenerSocket;
        private byte[] buffer = new byte[1024];
        public MainWindow()
        {
            InitializeComponent();
            InitializeCombox();
            InitializeNet();
        }
        private void InitializeCombox()
        {
            port.ItemsSource = SerialPort.GetPortNames();
            baudRate.ItemsSource = new int[] { 9600, 19200, 38400, 57600, 115200 };
            dataBit.ItemsSource = new int[] { 5, 6, 7, 8 };
            stopBit.ItemsSource = Enum.GetValues(typeof(StopBits));
            parityBit.ItemsSource = Enum.GetValues(typeof(Parity));

            port.SelectedIndex = 0;
            baudRate.SelectedIndex = 4;
            dataBit.SelectedIndex = 3;
            stopBit.SelectedIndex = 1;
            parityBit.SelectedIndex = 0;

            Close.IsEnabled = false;
        }
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (port.SelectedItem == null)
                {
                    LogMessage("Cannot find a valid port.");
                    return;
                }
                string portName = port.SelectedItem.ToString();                     //获取当前设置的端口名（如COM3）
                int baudRateValue = int.Parse(baudRate.SelectedItem.ToString());    //获得当前设置的波特率
                Parity parity = (Parity)parityBit.SelectedItem;                     //获得当前设置的校验位
                int dataBits = int.Parse(dataBit.SelectedItem.ToString());          //获得当前设置的数据位
                StopBits stopBits = (StopBits)stopBit.SelectedItem;                 //获得当前设置的终止位

                serialPort = new SerialPort(portName, baudRateValue, parity, dataBits, stopBits);    //根据上面获得的信息，初始化串口对象
                serialPort.ReadBufferSize = 1000;
                serialPort.DataReceived += SerialPort_DataReceived;                 //将 SerialPort_DataReceived 方法与串口数据接收事件绑定，每当串口接收到数据时，都会触发这个方法
                serialPort.Open();                                                  //如果这个地方打开失败，则会抛出一系列异常，这些异常会被下面的catch捕获

                LogMessage("Port opened successfully!");

                Open.IsEnabled = false;                                             //一旦打开了一个串口，就不能再打开其他串口了，除非先把这个关上
                Close.IsEnabled = true;
            }
            catch (Exception ex)
            {
                LogMessage(ex.Message);
            }
        }
        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)  //串口有信息到来时，触发该方法
        {
            var message = await Task.Run(() =>          //异步读取缓冲区的数据
            {
                return serialPort.ReadExisting();
            });
            /*
             * 串口事件（如SerialPort.DataReceived事件）通常是在串口的内部线程上触发的。
             * SerialPort类使用一个内部线程来监听串口数据，并在数据到达时触发事件。
             * 因此，DataReceived事件处理程序不是在UI线程上运行的，而是在串口的内部线程上运行的。
             * 所以此处，虽然默认ConfigureAwait(true)，但是不会回到UI线程，而是回到了串口线程。
             */
            ShowRecive($"Received: {message}");
        }
        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                logBox.Text += $"{DateTime.Now}: {message}\n";
            });
        }
        private void ShowRecive(string message)
        {
            Dispatcher.Invoke(() =>
            {
                reciveMessageBox.Text += message + "\n";
            });
        }
        private void Close_Click(object sender, RoutedEventArgs e)      //关闭一个连接
        {
            serialPort.Close();
            Open.IsEnabled = true;
            Close.IsEnabled = false;
            LogMessage("Port is close successfully.");
        }
        private byte[] StringToByteArray(string str)
        {
            try
            {
                int numberChars = str.Length;
                byte[] bytes = new byte[numberChars / 2];
                for (int i = 0; i < numberChars; i += 2)
                {
                    bytes[i / 2] = Convert.ToByte(str.Substring(i, 2), 16);
                }
                return bytes;
            }
            catch (Exception ex)
            {
                LogMessage($"Error converting hex to byte array: {ex.Message}");
                return null;
            }
        }
        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string message = sendMessageBox.Text;
            if (IsHex.IsChecked == true)
            {
                string hexString = message.Replace(" ", "").Replace("\r", "").Replace("\n", "");
                if (hexString.Length % 2 != 0)
                {
                    LogMessage("Invalid hex string length.");
                    return;
                }
                try
                {
                    byte[] byteData = StringToByteArray(hexString);
                    await serialPort.BaseStream.WriteAsync(byteData, 0, byteData.Length);
                }
                catch (Exception ex)
                {
                    LogMessage($"Error converting message: {ex.Message}");
                    return;
                }
            }
            else
            {
                serialPort.Write(message);
            }
            LogMessage("send: " + message);
        }
        //Net Port—————————————————————————————————————————————————————————————————————————————————————————————————————————————————
        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            IPAddress ipAddress;
            try
            {
                ipAddress = IPAddress.Parse(IPAddressBox.Text);
            }
            catch (Exception ex)
            {
                LogNetMessage(ex.Message);
                return;
            }
            int netPort;
            if (int.TryParse(PortBox.Text, out netPort))
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await clientSocket.ConnectAsync(new IPEndPoint(ipAddress, netPort));        //异步连接指定的端口
               
                    _ = ReceiveMessagesAsync(clientSocket); // Fire and forget
                }
                catch (Exception ex)
                {
                    LogNetMessage(ex.Message);
                    return;
                }
                LogNetMessage($"Connected to {clientSocket.RemoteEndPoint}");
                ConnectBtn.IsEnabled = false;
                DisConnect.IsEnabled = true;
                ListenBtn.IsEnabled = false;
            }
            else
            {
                LogNetMessage("Invalid net port!");
            }
        }
        private void DisConnect_Click(object sender, RoutedEventArgs e)
        {
            clientSocket.Close();
            ConnectBtn.IsEnabled = true;
            DisConnect.IsEnabled = false;
            ListenBtn.IsEnabled = true;
            LogNetMessage("Connect closed.");
        }
        private async Task HandleClientAsync(Socket client)
        {
            clientSocket = client;
            LogNetMessage("Client connected.");
            await ReceiveMessagesAsync(client);
        }
        private async Task ReceiveMessagesAsync(Socket client)
        {
            while (true)
            {
                int bytesRead = await client.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                if (bytesRead > 0)
                {
                    string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    Dispatcher.Invoke(() => ShowNetRecive($"Received: {receivedData}"));
                }
                else
                {
                    break;
                }
            }
        }
        private async void ListenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PortBox.Text, out int port))
            {
                IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);

                listenerSocket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    listenerSocket.Bind(localEndPoint);
                    listenerSocket.Listen(10);
                    
                    LogNetMessage($"Listening on {ipAddress}:{port}");
                    StopListenBtn.IsEnabled = true;
                    ListenBtn.IsEnabled = false;
                    ConnectBtn.IsEnabled = false;
                    while (true)
                    {
                        var client = await listenerSocket.AcceptAsync();
                        _ = HandleClientAsync(client); // Fire and forget
                    }
                }
                catch (Exception ex)
                {
                    LogNetMessage($"Error starting server: {ex.Message}");
                }
            }
            else
            {
                LogNetMessage("Invalid port number.");
            }
        }
        private async void NetSend_Click(object sender, RoutedEventArgs e)
        {
            if (clientSocket != null && clientSocket.Connected)
            {
                TextRange textRange = new TextRange(NetSendBox.Document.ContentStart, NetSendBox.Document.ContentEnd);
                string message = textRange.Text;
                byte[] byteData = null;

                if (IsNetHex.IsChecked == true)
                {
                    string hexString = message.Replace(" ", "");
                    hexString = hexString.Replace("\r", "").Replace("\n", "");
                    // Check if the hex string length is valid
                    if (hexString.Length % 2 != 0)
                    {
                        LogNetMessage("Invalid hex string length.");
                        return;
                    }

                    try
                    {
                        byteData = StringToByteArray(hexString);
                    }
                    catch (Exception ex)
                    {
                        LogNetMessage($"Error converting message: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    byteData = Encoding.ASCII.GetBytes(message);
                }

                if (byteData != null)
                {
                    try
                    {
                        await clientSocket.SendAsync(new ArraySegment<byte>(byteData), SocketFlags.None);
                    }
                    catch (Exception ex)
                    {

                        LogNetMessage(ex.Message);
                        return;
                    }
                 
                    LogNetMessage($"Sent: {message}");
                }
                else
                {
                    LogNetMessage("Error converting message to byte array.");
                }
            }
            else
            {
                LogNetMessage("Client socket is not connected.");
            }
        }
        private void InitializeNet()
        {
            IPAddressBox.Text = "127.0.0.1";
            PortBox.Text = "13000";
            StopListenBtn.IsEnabled = false;
            DisConnect.IsEnabled = false;
        }
        private void LogNetMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                NetLog.Text += $"{DateTime.Now}: {message}\n";
            });
        }
        private void StopListenBtn_Click(object sender, RoutedEventArgs e)
        {
            //CancellationTokenSource?.Cancel();

            if (listenerSocket != null)
            {
                listenerSocket.Close();
                listenerSocket = null;

                LogNetMessage("Stopped listening.");
                ConnectBtn.IsEnabled = true;
                StopListenBtn.IsEnabled = false;
                ConnectBtn.IsEnabled = true;
                ListenBtn.IsEnabled = true;
            }
        }
        private void ShowNetRecive(string message)
        {
            Dispatcher.Invoke(() =>
            {
                netRecive.Text += message + "\n";
            });
        }
    }
}
