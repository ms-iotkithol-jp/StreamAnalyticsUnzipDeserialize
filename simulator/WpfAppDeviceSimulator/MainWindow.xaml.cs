using Microsoft.Azure.Devices.Client;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

namespace WpfAppDeviceSimulator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DeviceClient deviceClient;
        private CancellationTokenSource cancellationTokenSource;
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            tbIoTCS.Text = iothubconnectionstring;
        }

        private string iothubconnectionstring = "<- connection string for device of IoT Hub ->";
        Task sendingAction;
        private async void buttonConnect_Click(object sender, RoutedEventArgs e)
        {
            if (deviceClient == null)
            {
                deviceClient = DeviceClient.CreateFromConnectionString(tbIoTCS.Text);
                await deviceClient.OpenAsync();
                cancellationTokenSource = new CancellationTokenSource();
                sendingAction = SendTelemetry(cancellationTokenSource.Token);
                buttonConnect.Content = "Stop & Disconnect";
                ShowLog("Connected.");
            }
            else
            {
                cancellationTokenSource.Cancel();
                try
                {
                    await sendingAction;
                }
                catch(OperationCanceledException ex)
                {
                    ShowLog($"{nameof(OperationCanceledException)} thrown with message: {ex.Message}");
                }
                finally
                {
                    cancellationTokenSource.Dispose();
                }
                await deviceClient.CloseAsync();
                deviceClient.Dispose();
                deviceClient = null;
                buttonConnect.Content = "Connect & Start";
                ShowLog("Disconnected");
            }
        }

        private double updateAd(double ad, double v)
        {
            if (ad > 0 && v >= 1.0)
            {
                return -1;
            }
            if (ad<0 && v <= -1.0)
            {
                return 1;
            }
            return ad;
        }
        static int maxNoLimit = 100;
        List<TelemetryData> dataPackets = new List<TelemetryData>();
        private async Task ProcessAndSend()
        {
            int unitSize = sizeof(long) + 3 * sizeof(float);
            if (dataPackets.Count >= maxNoLimit)
            {
                var buf = new byte[unitSize * maxNoLimit];
                int index = 0;
                for (int i = 0; i < maxNoLimit; i++)
                {
                    BitConverter.GetBytes(dataPackets[i].timestamp.Ticks).CopyTo(buf, index);
                    index += sizeof(long);
                    BitConverter.GetBytes((float)dataPackets[i].accelx).CopyTo(buf, index);
                    index += sizeof(float);
                    BitConverter.GetBytes((float)dataPackets[i].accely).CopyTo(buf, index);
                    index += sizeof(float);
                    BitConverter.GetBytes((float)dataPackets[i].accelz).CopyTo(buf, index);
                    index += sizeof(float);
                }
                dataPackets.Clear();
                // compress and send
                using (var outStream = new MemoryStream())
                {
                    using (var zgs = new GZipStream(outStream, CompressionMode.Compress))
                    {
                        zgs.Write(buf, 0, buf.Length);
                        zgs.Flush();
                        var zipedBuf = outStream.ToArray();
                        var msg = new Message(zipedBuf);
                        msg.Properties.Add("data-type", "gzip");
                        await deviceClient.SendEventAsync(msg);
                        ShowLog($"Send - {zipedBuf.Length} bytes");
                        if (cbStore.IsChecked.Value)
                        {
                            var filename = System.IO.Path.Combine(tbStoreFolder.Text, $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.gzip");
                            using (var fs = File.Open(filename, FileMode.Create))
                            {
                                fs.Write(zipedBuf, 0, zipedBuf.Length);
                            }
                            ShowLog($"Stored - {filename}");
                        }
                    }
                }
            }
        }

        private async Task SendTelemetry(CancellationToken token)
        {
            double ax = -1.0;
            double ay = 1.0;
            double az = 0.0;
            double da = 0.01;
            double adx = 1.0;
            double ady = -1.0;
            double adz = 1.0;
            token.ThrowIfCancellationRequested();
            while (true)
            {
                ax += da * adx;
                ay += da * ady;
                az += da * adz;
                adx = updateAd(adx, ax);
                ady = updateAd(ady, ay);
                adz = updateAd(adz, az);
                var telemetry = new TelemetryData()
                {
                    timestamp = DateTime.Now,
                    accelx = ax,
                    accely = ay,
                    accelz = az
                };
                dataPackets.Add(telemetry);
                ShowLog($"Added {Newtonsoft.Json.JsonConvert.SerializeObject(telemetry)}");
                await ProcessAndSend();
                await Task.Delay(100);
                if (token.IsCancellationRequested)
                {
                    dataPackets.Clear();
                    token.ThrowIfCancellationRequested();
                }
            }
        }

        private int logIndex = 0;
        private void ShowLog(string msg)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now.ToString("HH:mm:ss.fff")}:{msg}");
            sb.Append(tbLog.Text);
            tbLog.Text = sb.ToString();

        }

        private void cbStore_Checked(object sender, RoutedEventArgs e)
        {
            if (cbStore.IsChecked.Value)
            {
                buttonSelectFolder.IsEnabled = true;
            }
            else
            {
                buttonSelectFolder.IsEnabled = false;
            }
        }

        private void buttonSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog()== CommonFileDialogResult.Ok)
            {
                tbStoreFolder.Text = dialog.FileName;
            }
        }
    }

    class TelemetryData
    {
        public DateTime timestamp { get; set; }
        public double accelx { get; set; }
        public double accely { get; set; }
        public double accelz { get; set; }
    }
}
