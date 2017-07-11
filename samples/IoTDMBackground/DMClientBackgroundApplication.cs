/*
Copyright 2017 Microsoft
Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
and associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Devices.Management;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Diagnostics;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace IoTDMBackground
{
    class DeviceManagementRequestHandler : IDeviceManagementRequestHandler
    {
        public DeviceManagementRequestHandler()
        {
        }

        // It is always ok to reboot
        Task<bool> IDeviceManagementRequestHandler.IsSystemRebootAllowed()
        {
            return Task.FromResult(true);
        }
    }

    public sealed class DMClientBackgroundApplication : IBackgroundTask
    {
        private DeviceClient _deviceClient;
        private DeviceManagementClient _dmClient;
        private BackgroundTaskDeferral _deferral;

        private void LogError()
        {
            Log("Unable to start background app", LoggingLevel.Error);
        }
        private void Log(string message, LoggingLevel level)
        {
            /*
            You can collect the events generated by this method with xperf or another
            ETL controller tool. To collect these events in an ETL file:

            xperf -start MySession -f MyFile.etl -on 8aac9209-1e2b-5166-31b6-7c4af4bf7d27
            (call LogError())
            xperf -stop MySession

            After collecting the ETL file, you can decode the trace using xperf, wpa,
            or tracerpt. For example, to decode MyFile.etl with tracerpt:

            tracerpt MyFile.etl
            (generates dumpfile.xml)
            */
            using (var channel = new LoggingChannel("IoTDMBackground", null)) // null means use default options.
            {
                // Use this Id in xperf parameter
                Debug.WriteLine(channel.Id);
                channel.LogMessage(message, level);
            }
        }

        private async Task<string> GetConnectionStringAsync()
        {
            var tpmDevice = new TpmDevice(0);

            string connectionString = "";

            do
            {
                try
                {
                    connectionString = await tpmDevice.GetConnectionStringAsync();
                    break;
                }
                catch (Exception)
                {
                    // We'll just keep trying.
                }
                Debug.WriteLine("Waiting...");
                await Task.Delay(1000);

            } while (true);

            return connectionString;
        }

        private async Task InitializeDeviceClientAsync()
        {
            try
            {
                // Attempt to close any existing connections before
                // creating a new one
                if (_deviceClient != null)
                {
                    await this._deviceClient.CloseAsync().ContinueWith((t) =>
                    {
                        var e = t.Exception;
                        if (e != null)
                        {
                            var msg = "this.deviceClient.CloseAsync exception: " + e.Message + "\n" + e.StackTrace;
                            Log(msg, LoggingLevel.Error);
                        }
                    });
                }

                string deviceConnectionString = await GetConnectionStringAsync();

                // Create DeviceClient. Application uses DeviceClient for telemetry messages, device twin
                // as well as device management
                this._deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);

                // Handle connection status changes by recreating the connection as needed
                this._deviceClient.SetConnectionStatusChangesHandler(async (ConnectionStatus status, ConnectionStatusChangeReason reason) => {
                    Log($"Connection changed: {status.ToString()} {reason.ToString()}", LoggingLevel.Verbose);
                    switch (reason)
                    {
                        case ConnectionStatusChangeReason.Connection_Ok:
                            // No need to do anything, this is the expectation
                            break;

                        case ConnectionStatusChangeReason.Retry_Expired:
                            await InitializeDeviceClientAsync();
                            break;

                        case ConnectionStatusChangeReason.Bad_Credential:
                        case ConnectionStatusChangeReason.Client_Close:
                        case ConnectionStatusChangeReason.Communication_Error:
                        case ConnectionStatusChangeReason.Device_Disabled:
                        case ConnectionStatusChangeReason.Expired_SAS_Token:
                            // TODO: do these need to reset the connection???

                        case ConnectionStatusChangeReason.No_Network:
                            // This seems to lead to Retry_Expired, so we can 
                            // ignore this ... maybe log the error.

                        default:
                            LogError();
                            break;
                    }
                });

                // IDeviceTwin abstracts away communication with the back-end.
                // AzureIoTHubDeviceTwinProxy is an implementation of Azure IoT Hub
                IDeviceTwin deviceTwinProxy = new AzureIoTHubDeviceTwinProxy(this._deviceClient);

                // IDeviceManagementRequestHandler handles device management-specific requests to the app,
                // such as whether it is OK to perform a reboot at any givem moment, according the app business logic
                // ToasterDeviceManagementRequestHandler is the Toaster app implementation of the interface
                IDeviceManagementRequestHandler appRequestHandler = new DeviceManagementRequestHandler();

                // Create the DeviceManagementClient, the main entry point into device management
                _dmClient = await DeviceManagementClient.CreateAsync(deviceTwinProxy, appRequestHandler);

                // Set the callback for desired properties update. The callback will be invoked
                // for all desired properties -- including those specific to device management
                await this._deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyUpdate, null);

                // Tell the deviceManagementClient to sync the device with the current desired state.
                // Disabled due to: https://github.com/ms-iot/iot-core-azure-dm-client/issues/105
                // await this.deviceManagementClient.ApplyDesiredStateAsync();
            }
            catch
            {
                LogError();
            }
        }

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();

            await InitializeDeviceClientAsync();

        }

        private async Task OnDesiredPropertyUpdate(TwinCollection desiredProperties, object userContext)
        {
            // Let the device management client process properties specific to device management
            _dmClient.ApplyDesiredStateAsync(desiredProperties);
        }
    }
}
