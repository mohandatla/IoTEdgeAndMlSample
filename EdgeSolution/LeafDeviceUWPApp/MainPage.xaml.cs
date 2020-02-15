using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Azure.Devices.Client;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Newtonsoft.Json;
using System.IO;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LeafDeviceUWPApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private DeviceClient _deviceClient;
        private int sentMessageCount = 0;

        private Task<MethodResponse> HelloWorldDirectMethodCallback(MethodRequest methodRequest, object userContext)
        {
            var data = Encoding.UTF8.GetString(methodRequest.Data);
            if (!String.IsNullOrEmpty(data))
            {
                DisplayLogMessage($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Received:{data}");
                DisplayResponseMessage(data);
                string jString = JsonConvert.SerializeObject("Success");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(jString), 200));
            }
            else
            {
                DisplayLogMessage($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Received:No reply");
                DisplayResponseMessage("No reply");
                string jString = JsonConvert.SerializeObject("EmptyData");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(jString), 400));
            }
        }

        private void Initialize()
        {
            try
            {
                const string deviceConnectionString = "HostName=IotEdgeAndMlHub-gnfytbogtqjte.azure-devices.net;DeviceId=demoleafdevice;SharedAccessKey=0Dcf1CD9cgojck0BWlKUN4bj2N0KdWxnU0GRpCtFYto=;GatewayHostName=172.23.223.130";
                const string azureIotTestRootCertificateFilePath = "azure-iot-test-only.root.ca.cert.pem";
                CertificateManager.InstallCACert(azureIotTestRootCertificateFilePath);
                
                _deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString);
                _deviceClient.SetMethodHandlerAsync("LeafDeviceDirectMethod", HelloWorldDirectMethodCallback, null);
            }
            catch (Exception ex)
            {
                DisplayResponseMessage("Error occurred during Initialize");
                DisplayLogMessage($"Initialize got exception.\nException message: {ex.Message}");
            }
        }
        private async void DisplayLogMessage(string message)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TextBlockLog.Text = message + "\n" + TextBlockLog.Text;
            });
        }

        private async void DisplayResponseMessage(string message)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TextBlockMessage.Text = message;
            });
        }

        /// <summary>
        /// Uses the DeviceClient to send a message to the IoT Hub
        /// </summary>
        /// <param name="deviceClient">Azure Devices client for connecting and send data to IoT Hub</param>
        /// <param name="message">JSON string representing serialized device data</param>
        /// <returns>Task for async execution</returns>
        private async void SendEvent(DeviceClient deviceClient, string message)
        {
            using (var eventMessage = new Message(Encoding.UTF8.GetBytes(message)))
            {
                // Set the content type and encoding so the IoT Hub knows to treat the message body as JSON
                eventMessage.ContentEncoding = "utf-8";
                eventMessage.ContentType = "application/json";
                await deviceClient.SendEventAsync(eventMessage).ConfigureAwait(false);
            }
        }
       public MainPage()
        {
            this.InitializeComponent();
            Initialize();
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string message = MessageTextBox.Text;
            DisplayLogMessage($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Sent: {message}");
            SendEvent(_deviceClient, message);
        }

        private void SendFrameButton_Click(object sender, RoutedEventArgs e)
        {
            const string filePath = "pear.jpg";
            DisplayLogMessage($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Sent image: {filePath}");
            var fileContent = File.ReadAllBytes(filePath);
            string base64Message = Convert.ToBase64String(fileContent);
            SendEvent(_deviceClient, base64Message);
        }
    }
}
