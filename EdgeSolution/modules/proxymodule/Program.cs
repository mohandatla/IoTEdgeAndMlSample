// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace proxymodule
{
    using System;
    using System.Runtime.Loader;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Newtonsoft.Json;

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
        /// messages.
        /// </summary>
        static async Task Init()
        {
            try
            {
                AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
                ITransportSettings[] settings = { amqpSetting };

                // Open a connection to the Edge runtime
                ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
                await ioTHubModuleClient.OpenAsync();

                // Register callback to be called when a message is received by the module
                await ioTHubModuleClient.SetInputMessageHandlerAsync("leafdeviceinput", PipeMessage, ioTHubModuleClient);
            }
            catch (Exception ex)
            {
                Logger.Log($"Init got exception {ex.Message}", LogSeverity.Error);
            }
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
                if(message.ContentType == "application/json")
                {
                    string messageString = Encoding.UTF8.GetString(message.GetBytes());
                    Logger.Log($"{UtcDateTime} Received message {messageString} from app: {messageId}");
                    var cloudTask = SendMessageToCloud(moduleClient, messageString, messageId);
                    var deviceTask = SendMessageToLeafDevice(moduleClient, message.ConnectionDeviceId, "Hello from edge!");
                }
                else
                {
                    Logger.Log($"Undefined message content type received.", LogSeverity.Warning);
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
        /// This method will invoke direct method "LeafDeviceDirectMethod" on 
        /// the device to send message from iotedge module to native windows application.
        /// </summary>
        static async Task SendMessageToLeafDevice(ModuleClient moduleClient, string deviceId, string receivedMessage)
        {
            try
            {
                string jString = JsonConvert.SerializeObject(receivedMessage);
                var methodRequest = new MethodRequest("LeafDeviceDirectMethod", Encoding.UTF8.GetBytes(jString));
                var response = await moduleClient.InvokeMethodAsync(deviceId, methodRequest);
                if(response.Status == 200)
                {
                    Logger.Log($"{UtcDateTime} Sent to app: {receivedMessage}.");
                }
                else
                {
                    Logger.Log($"Error occurred invoking LeafDeviceDirectMethod. error status code {response.Status}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SendMessageToLeafDevice got exception {ex.Message}", LogSeverity.Error);
            }
        }

        private static async Task SendMessageToCloud(ModuleClient moduleClient, string message, string messageId)
        {
            using (var eventMessage = new Message(Encoding.UTF8.GetBytes(message)))
            {
                eventMessage.ContentEncoding = "utf-8";
                eventMessage.ContentType = "application/json";
                eventMessage.MessageId = messageId;
                await moduleClient.SendEventAsync("cloudoutput", eventMessage).ConfigureAwait(false);
                Logger.Log($"{UtcDateTime} Sent to cloud: {messageId}{message}");
            }
        }

        private static string UtcDateTime
        {
            get
            {
                return DateTime.Now.ToUniversalTime().ToString("G");
            }
        }
    }
}
