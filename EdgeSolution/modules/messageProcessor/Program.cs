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

    class Program
    {

        static int counter;

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
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Logger.Log("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("leafDeviceInput", PipeMessage, ioTHubModuleClient);
            Logger.Log("leafDeviceInput message handler is set.");
        }

        /// <summary>
        /// This method is called whenever a leaf device sent a message to EdgeHub. 
        /// It log the message, and send reply to leaf device.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            try
            {
                byte[] messageBytes = message.GetBytes();
                string messageString = Encoding.UTF8.GetString(messageBytes);
                Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");
                var moduleClient = GetClientFromContext(userContext);
                await SendReplyToDevice(moduleClient, message.ConnectionDeviceId, messageString).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"PipeMessage got exception {ex.Message}", LogSeverity.Error);
            }
            
            return MessageResponse.Completed;
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
                string replyMessage = $"{counter}: hello from edge!";
                string jString = JsonConvert.SerializeObject(replyMessage);
                var methodRequest = new MethodRequest("LeafDeviceDirectMethod", Encoding.UTF8.GetBytes(jString));
                var response = await moduleClient.InvokeMethodAsync(deviceId, methodRequest);
                Logger.Log($"Invoked the direct method. status = {response.Status}");
            }
            catch (Exception ex)
            {
                Logger.Log($"SendReplyToDevice got exception {ex.Message}", LogSeverity.Error);
            }  
        }
    }
}
