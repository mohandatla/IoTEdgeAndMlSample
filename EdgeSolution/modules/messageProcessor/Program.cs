//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

namespace messageProcessor
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Newtonsoft.Json;
    using System.Net;
    using System.Net.Http;

    class Program
    {

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            // MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // ITransportSettings[] settings = { mqttSetting };
            AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            ITransportSettings[] settings = { amqpSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("leafDeviceInput", PipeMessage, ioTHubModuleClient);
        }

        /// <summary>
        /// This method is called whenever a leaf device sent a message to EdgeHub. 
        /// It log the message, and send reply to leaf device.
        /// </summary>
        static Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            string messageId = message.MessageId == null ? "": message.MessageId;
            var moduleClient = GetClientFromContext(userContext);

            try
            {
                if(message.ContentType == "image/jpeg")
                {
                    byte[] messageBytes = message.GetBytes();
                    if(messageBytes != null)
                    {
                        Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Received image from app: {messageId}");
                        byte[] rawMessageBytes = System.Convert.FromBase64String(Encoding.UTF8.GetString(messageBytes));
                        Task t = CallImageClassifier(moduleClient, message.ConnectionDeviceId, messageId, rawMessageBytes);
                    }
                    else
                    {
                        Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Received empty data from app.");
                    }
                }
                else
                {
                    string messageString = Encoding.UTF8.GetString(message.GetBytes());
                    Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Received message {messageString} from app: {messageId}");
                    var cloudTask = SendMessageToCloud(moduleClient, messageString, messageId);
                    var deviceTask = SendReplyToDevice(moduleClient, message.ConnectionDeviceId, "Hello from edge!");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"PipeMessage got exception {ex.Message}", LogSeverity.Error);
            }
            
            return Task.FromResult(MessageResponse.Completed);
        }

        static ModuleClient GetClientFromContext(object userContext)
        {
            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new ArgumentException($"Could not cast userContext. Expected {typeof(ModuleClient)} but got: {userContext.GetType()}");
            }
            return moduleClient;
        }

        /// <summary>
        /// This method will invoke direct method "LeafDeviceDirectMethod" on the device to show case bi-directional communication.
        /// </summary>
        static async Task SendReplyToDevice(ModuleClient moduleClient, string deviceId, string receivedMessage)
        {
            try
            {
                string jString = JsonConvert.SerializeObject(receivedMessage);
                var methodRequest = new MethodRequest("LeafDeviceDirectMethod", Encoding.UTF8.GetBytes(jString));
                var response = await moduleClient.InvokeMethodAsync(deviceId, methodRequest);
                if(response.Status == 200)
                {
                    Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Sent to app: {receivedMessage}.");
                }
                else
                {
                    Logger.Log($"Error occurred invoking LeafDeviceDirectMethod. error status code {response.Status}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SendReplyToDevice got exception {ex.Message}", LogSeverity.Error);
            }
        }

        private static async Task SendMessageToCloud(ModuleClient moduleClient, string message, string messageId)
        {
            using (var eventMessage = new Message(Encoding.UTF8.GetBytes(message)))
            {
                eventMessage.ContentEncoding = "utf-8";
                eventMessage.ContentType = "application/json";
                eventMessage.MessageId = messageId;
                await moduleClient.SendEventAsync("cloudMessage", eventMessage).ConfigureAwait(false);
                Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Sent to cloud: {messageId}{message}");
            }
        }

        private static async Task CallImageClassifier(ModuleClient moduleClient, string deviceId, string messageId, byte[] fileContent)
        {
            try{
                Logger.Log("Invoked CallImageClassifier");
                string message = "";

                using(var client = new HttpClient())
                {          
                    using(var request = new HttpRequestMessage())
                    {
                        request.Method = HttpMethod.Post;
                        request.RequestUri = new Uri("http://fruitclassifier/image");
                        request.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
                        client.Timeout = TimeSpan.FromSeconds(60);
                        request.Content = new ByteArrayContent(fileContent);
                        Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Request to classifier: {messageId}");
                        var response = await client.SendAsync(request);
                        message = await response.Content.ReadAsStringAsync();
                        Logger.Log($"{DateTime.Now.ToUniversalTime().ToString("HH:mm:ss")} Response from classifier: {messageId}{message}");
                    }
                }

                var cloudTask = SendMessageToCloud(moduleClient, message, messageId);
                Task deviceTask = SendReplyToDevice(moduleClient, deviceId, message);
            }
             catch (Exception ex)
            {
                Logger.Log($"CallImageClassifier got exception {ex.Message}", LogSeverity.Error);
            }
        }
    }
}
