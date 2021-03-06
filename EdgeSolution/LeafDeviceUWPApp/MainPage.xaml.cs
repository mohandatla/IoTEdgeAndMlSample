﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Azure.Devices.Client;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Newtonsoft.Json;

namespace LeafDeviceUWPApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private DeviceClient deviceClient;

        private string UtcDateTime
        {
            get
            {
                return DateTime.Now.ToUniversalTime().ToString("G");
            }
        }

        /// <summary>
        /// This method will be invoked upon direct method (LeafDeviceDirectMethod) call from edge.
        /// </summary>
        /// <param name="methodRequest">direct method request object contains the details of request.</param>
        /// <param name="userContext">Context object passed in when the callback was registered.</param>
        /// <returns>MethodResponse task.</returns>
        /// This method will be invoked upon direct method (LeafDeviceDirectMethod) call.
        private Task<MethodResponse> DeviceCallback(MethodRequest methodRequest, object userContext)
        {
            var data = Encoding.UTF8.GetString(methodRequest.Data);
            if (!String.IsNullOrEmpty(data))
            {
                DisplayLog($"{UtcDateTime} Received:{data}");
                DisplayErrorOrResponse(data);
                string jString = JsonConvert.SerializeObject("Success");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(jString), 200));
            }
            else
            {
                DisplayLog($"{UtcDateTime} Received:empty message");
                DisplayErrorOrResponse("Received:empty message");
                string jString = JsonConvert.SerializeObject("EmptyData");
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(jString), 400));
            }
        }

        private void Initialize()
        {
            try
            {
                // Leaf device connection string needs to be updated in the below format 
                //"HostName=<IoThub url>;DeviceId=<device id>;SharedAccessKey=<shared access key>;GatewayHostName=<edge device ip address>" 
                // Please look at end user guide for creating leaf device.
                const string deviceConnectionString = "<leaf device connection string with edge IP address.>";
                
                // Update device certificate path.
                const string azureIotTestRootCertificateFilePath = "azure-iot-test-only.root.ca.cert.pem";
                CertificateManager.InstallCACert(azureIotTestRootCertificateFilePath);
                
                deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString);
                deviceClient.SetMethodHandlerAsync("LeafDeviceDirectMethod", DeviceCallback, null);
            }
            catch (Exception ex)
            {
                DisplayErrorOrResponse("Fatal error occurred during initialization. Please go through end user guide for troubleshooting.");
                DisplayLog($"Initialize got exception.\nException message: {ex.Message}");
            }
        }

        /// <summary>
        /// Display log in UI.
        /// </summary>
        private async void DisplayLog(string message)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TextBlockLog.Text = message + "\n" + TextBlockLog.Text;
            });
        }

        /// <summary>
        /// Display error or edge response in UI.
        /// </summary>
        private async void DisplayErrorOrResponse(string message)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ErrorOrResponseTextBlock.Text = message;
            });
        }

        /// <summary>
        /// Sends an event to a hub.
        /// </summary>
        /// <param name="deviceClient">Azure devices client to connect and send data to IoT Hub</param>
        /// <param name="message">The message to send.</param>
        /// <returns>Task for async execution</returns>
        private async void SendEvent(DeviceClient deviceClient, string message)
        {
            using (var eventMessage = new Message(Encoding.UTF8.GetBytes(message)))
            {
                eventMessage.ContentEncoding = "utf-8";
                eventMessage.ContentType = "application/json";
                eventMessage.MessageId = Guid.NewGuid().ToString("D");
                await deviceClient.SendEventAsync(eventMessage).ConfigureAwait(false);
            }
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string message = MessageTextBox.Text;
            DisplayLog($"{UtcDateTime} Sent: {message}");
            SendEvent(deviceClient, message);
        }

       public MainPage()
        {
            this.InitializeComponent();
            Initialize();
        }
    }
}
