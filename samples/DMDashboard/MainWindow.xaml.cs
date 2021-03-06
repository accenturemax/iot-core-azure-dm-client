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

using DMDashboard.StorageManagement;
using Microsoft.Azure.Devices;
using Microsoft.Devices.Management;
using Microsoft.Devices.Management.DMDataContract;
using Microsoft.WindowsAzure.Storage;       // Namespace for CloudStorageAccount
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DMDashboard
{
    public partial class MainWindow : Window
    {
        const string DTRefreshing = "\"refreshing\"";
        const string DTRootNodeString = "{ \"properties\" : { \"desired\" : { \"" + DMJSonConstants.DTWindowsIoTNameSpace + "\" : ";
        const string DTRootNodeSuffixString = "}}}";

        const string IotHubConnectionString = "IotHubConnectionString";
        const string StorageConnectionString = "StorageConnectionString";

        private const int NumberOfDevicesToPopulate = 100;
        private const string DeviceIdHintText = "<if looking for a single device, enter it's ID>";

        public string DeviceIdToPopulate { get; set; } = DeviceIdHintText;

        Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public MainWindow()
        {
            InitializeComponent();

            var connectionString = this.config.AppSettings.Settings[IotHubConnectionString];
            if (connectionString != null && !string.IsNullOrEmpty(connectionString.Value)) {
                ConnectionStringBox.Text = connectionString.Value;
            }

            connectionString = this.config.AppSettings.Settings[StorageConnectionString];
            if (connectionString != null && !string.IsNullOrEmpty(connectionString.Value))
            {
                AzureStorageExplorer.ConnectionString = connectionString.Value;
            }

            Desired_RootCATrustedCertificates_Root.ShowCertificateDetails += ShowCertificateDetails;
            Desired_RootCATrustedCertificates_CA.ShowCertificateDetails += ShowCertificateDetails;
            Desired_RootCATrustedCertificates_TrustedPublisher.ShowCertificateDetails += ShowCertificateDetails;
            Desired_RootCATrustedCertificates_TrustedPeople.ShowCertificateDetails += ShowCertificateDetails;
            Desired_CertificateStore_CA_System.ShowCertificateDetails += ShowCertificateDetails;
            Desired_CertificateStore_Root_System.ShowCertificateDetails += ShowCertificateDetails;
            Desired_CertificateStore_My_User.ShowCertificateDetails += ShowCertificateDetails;
            Desired_CertificateStore_My_System.ShowCertificateDetails += ShowCertificateDetails;

            Reported_RootCATrustedCertificates_Root.ShowCertificateDetails += ShowCertificateDetails;
            Reported_RootCATrustedCertificates_Root.ExportCertificateDetails += ExportCertificateDetails;
            Reported_RootCATrustedCertificates_CA.ShowCertificateDetails += ShowCertificateDetails;
            Reported_RootCATrustedCertificates_TrustedPublisher.ShowCertificateDetails += ShowCertificateDetails;
            Reported_RootCATrustedCertificates_TrustedPeople.ShowCertificateDetails += ShowCertificateDetails;
            Reported_CertificateStore_CA_System.ShowCertificateDetails += ShowCertificateDetails;
            Reported_CertificateStore_Root_System.ShowCertificateDetails += ShowCertificateDetails;
            Reported_CertificateStore_My_User.ShowCertificateDetails += ShowCertificateDetails;
            Reported_CertificateStore_My_System.ShowCertificateDetails += ShowCertificateDetails;
        }

        private void ToggleUIElementVisibility(UIElement element)
        {
            if (element.Visibility == Visibility.Collapsed)
            {
                element.Visibility = Visibility.Visible;
            }
            else
            {
                element.Visibility = Visibility.Collapsed;
            }
        }

        private async void ListDevices(string connectionString)
        {
            RegistryManager registryManager = RegistryManager.CreateFromConnectionString(connectionString);

            // Avoid duplicates in the list
            DeviceListBox.ItemsSource = null;

            // Populate devices.
            IEnumerable<Device> devices;
            if (DeviceIdToPopulate != DeviceIdHintText && !string.IsNullOrWhiteSpace(DeviceIdToPopulate))
            {
                devices = new List<Device> { await registryManager.GetDeviceAsync(DeviceIdToPopulate) };
            }
            else
            {
                devices = await registryManager.GetDevicesAsync(NumberOfDevicesToPopulate);
            }

            
            List<string> deviceIds = new List<string>();
            foreach (var device in devices)
            {
                Debug.WriteLine("->" + device.Id);
                deviceIds.Add(device.Id);
            }

            deviceIds.Sort();
            DeviceListBox.ItemsSource = deviceIds;

            this.config.AppSettings.Settings[IotHubConnectionString].Value = connectionString;
            this.config.Save(ConfigurationSaveMode.Modified);
        }

        private void OnListDevices(object sender, RoutedEventArgs e)
        {
            ListDevices(ConnectionStringBox.Text);
        }

        private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
        {
            string deviceIdString = (string)DeviceListBox.SelectedItem;
            ConnectedProperties.IsEnabled = false;
            if (!String.IsNullOrEmpty(deviceIdString))
            {
                _deviceTwin = new DeviceTwinAndMethod(ConnectionStringBox.Text, deviceIdString);
                ConnectedProperties.IsEnabled = true;
            }
            SelectedDeviceName.Text = deviceIdString;
        }

        private async void OnManageAppLifeCycle(string appLifeCycleAction, string packageFamilyName)
        {
            AppxLifeCycleDataContract.ManageAppLifeCycleParams parameters = new AppxLifeCycleDataContract.ManageAppLifeCycleParams();
            parameters.action = appLifeCycleAction;
            parameters.pkgFamilyName = packageFamilyName;

            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(AppxLifeCycleDataContract.ManageAppLifeCycleAsync, parameters.ToJsonString(), new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("ManageAppLifeCycle(start) Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnStartApplication(object sender, RoutedEventArgs e)
        {
            OnManageAppLifeCycle(AppxLifeCycleDataContract.JsonStart, LifeCyclePkgFamilyName.Text);
        }

        private void OnStopApplication(object sender, RoutedEventArgs e)
        {
            OnManageAppLifeCycle(AppxLifeCycleDataContract.JsonStop, LifeCyclePkgFamilyName.Text);
        }

        private void CertificateInfoToUI(List<string> hashes, CertificateSelector certificateSelector)
        {
            if (hashes == null)
            {
                return;
            }
            hashes.Sort();
            if (certificateSelector != null)
            {
                List<CertificateSelector.CertificateDetails> certificateList = new List<CertificateSelector.CertificateDetails>();
                foreach (string hash in hashes)
                {
                    CertificateSelector.CertificateDetails certificateData = new CertificateSelector.CertificateDetails();
                    certificateData.Hash = hash;
                    certificateData.FileName = "<unknown>";
                    certificateList.Add(certificateData);
                }
                certificateSelector.SetCertificateList(certificateList);
            }
        }

        private void CertificatesInfoToUI(CertificatesDataContract.ReportedProperties certificatesInfo)
        {
            CertificateInfoToUI(certificatesInfo.rootCATrustedCertificates_CA, Reported_RootCATrustedCertificates_CA);
            CertificateInfoToUI(certificatesInfo.rootCATrustedCertificates_Root, Reported_RootCATrustedCertificates_Root);
            CertificateInfoToUI(certificatesInfo.rootCATrustedCertificates_TrustedPublisher, Reported_RootCATrustedCertificates_TrustedPublisher);
            CertificateInfoToUI(certificatesInfo.rootCATrustedCertificates_TrustedPeople, Reported_RootCATrustedCertificates_TrustedPeople);

            CertificateInfoToUI(certificatesInfo.certificateStore_CA_System, Reported_CertificateStore_CA_System);
            CertificateInfoToUI(certificatesInfo.certificateStore_Root_System, Reported_CertificateStore_Root_System);
            CertificateInfoToUI(certificatesInfo.certificateStore_My_User, Reported_CertificateStore_My_User);
            CertificateInfoToUI(certificatesInfo.certificateStore_My_System, Reported_CertificateStore_My_System);
        }

        private async void ReadDTReported()
        {
            DeviceTwinData deviceTwinData = await _deviceTwin.GetDeviceTwinData();
            Debug.WriteLine("json = " + deviceTwinData.reportedPropertiesJson);

            JObject desiredObject = (JObject)JsonConvert.DeserializeObject(deviceTwinData.reportedPropertiesJson);

            JToken windowsToken;
            if (!desiredObject.TryGetValue(DMJSonConstants.DTWindowsIoTNameSpace, out windowsToken) || windowsToken.Type != JTokenType.Object)
            {
                return;
            }
            JObject windowsObject = (JObject)windowsToken;

            foreach (JProperty jsonProp in windowsObject.Children())
            {
                if (jsonProp.Name == "timeInfo" && jsonProp.Value.Type == JTokenType.Object)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    TimeReportedState.FromJson((JObject)jsonProp.Value);
                }
                else if (jsonProp.Name == TimeSvcReportedState.SectionName && jsonProp.Value.Type == JTokenType.Object)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    TimeSvcReportedState.FromJson((JObject)jsonProp.Value);
                }
                else if (jsonProp.Name == CertificatesDataContract.SectionName && jsonProp.Value.Type == JTokenType.Object)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    var reportedProperties = CertificatesDataContract.ReportedProperties.FromJsonObject((JObject)jsonProp.Value);
                    CertificatesInfoToUI(reportedProperties);
                }
                else if (jsonProp.Name == DeviceInfoDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    if (jsonProp.Value is JObject)
                    {
                        DeviceInfoReportedState.FromJsonObject((JObject)jsonProp.Value);
                    }
                    else
                    {
                        MessageBox.Show("Expected json object as a value for " + DeviceInfoReportedState.SectionName);
                    }
                }
                else if (jsonProp.Name == ExternalStorageDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    if (jsonProp.Value is JObject)
                    {
                        var reportedProperties = ExternalStorageDataContract.ReportedProperties.FromJsonObject((JObject)jsonProp.Value);
                        AzureStorageReportedConnectionString.Text = reportedProperties.connectionString;
                    }
                    else
                    {
                        MessageBox.Show("Expected json object as a value for " + DeviceInfoReportedState.SectionName);
                    }
                }
                else if (jsonProp.Name == DmAppStoreUpdateDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    if (jsonProp.Value is JObject)
                    {
                        DmAppStoreUpdateReportedState.FromJsonObject((JObject)jsonProp.Value);
                    }
                    else
                    {
                        MessageBox.Show("Expected json object as a value for " + DmAppStoreUpdateDataContract.SectionName);
                    }
                }
                else if (jsonProp.Name == RebootInfoDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    RebootInfoReportedState.FromJsonObject(jsonProp.Value);
                }
                else if (jsonProp.Name == RebootCmdDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    RebootCmdReportedState.FromJson(jsonProp.Value);
                }
                else if (jsonProp.Name == WindowsUpdatePolicyDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    WindowsUpdatePolicyReportedState.FromJsonObject(jsonProp.Value);
                }
                else if (jsonProp.Name == WindowsUpdatesDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    if (jsonProp.Value is JObject)
                    {
                        WindowsUpdatesConfigurationToUI((JObject)jsonProp.Value);
                    }
                    else
                    {
                        MessageBox.Show("Expected json object as a value for " + WindowsUpdatesDataContract.SectionName);
                    }
                }
                else if (jsonProp.Name == WindowsTelemetryDataContract.SectionName)
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    if (jsonProp.Value is JObject)
                    {
                        WindowsTelemetryReportedState.FromJsonObject((JObject)jsonProp.Value);
                    }
                    else
                    {
                        MessageBox.Show("Expected json object as a value for " + WindowsTelemetryDataContract.SectionName);
                    }
                }
                else if (jsonProp.Name == "apps")
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    TheAppsStatus.AppsStatusJsonToUI(jsonProp.Value);
                }
                else if (jsonProp.Name == "deviceHealthAttestation")
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    var jobj = JObject.Parse(jsonProp.Value.ToString());
                    DeviceHealthAttestationReportedState.FromJson(jobj);
                }
                else if (jsonProp.Name == "wifi")
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    this.WifiReportedState.FromJson(jsonProp.Value);
                }
                else if (jsonProp.Name == "eventTracingCollectors")
                {
                    Debug.WriteLine(jsonProp.Value.ToString());
                    this.ReportedDiagnosticLogs.FromJson((JObject)jsonProp.Value);
                }
            }
        }

        private void OnReadDTReported(object sender, RoutedEventArgs e)
        {
            ReadDTReported();
        }


        private async void RebootSystemAsync()
        {
            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(RebootCmdDataContract.RebootCmdAsync, "{}", new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("Reboot Command Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnRebootSystem(object sender, RoutedEventArgs e)
        {
            RebootSystemAsync();
        }

        private async void FactoryResetAsync()
        {
            var resetParams = new FactoryResetDataContract.ResetParams();
            resetParams.clearTPM = DesiredClearTPM.IsChecked == true;
            resetParams.recoveryPartitionGUID = DesiredRecoveryPartitionGUID.Text;
            string resetParamsString = resetParams.ToJsonString();

            Debug.WriteLine("Reset params : " + resetParamsString);

            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(FactoryResetDataContract.StartFactoryResetAsync, resetParamsString, new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("FactoryReset Command Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnFactoryReset(object sender, RoutedEventArgs e)
        {
            FactoryResetAsync();
        }

        private async void StartDmAppStoreUpdateAsync()
        {
            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(DmAppStoreUpdateDataContract.StartDmAppStoreUpdateAsync, "{}", new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("FactoryReset Command Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnStartDmAppStoreUpdate(object sender, RoutedEventArgs e)
        {
            StartDmAppStoreUpdateAsync();
        }

        private async void UpdateDTReportedAsync()
        {
            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(CommonDataContract.ReportAllAsync, "{}", new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("UpdateDTReportedAsync Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnUpdateDTReported(object sender, RoutedEventArgs e)
        {
            UpdateDTReportedAsync();
        }

        private async Task UpdateTwinData(string jsonString)
        {
            Debug.WriteLine("---- Desired Properties ----");
            Debug.WriteLine(jsonString);

            // Task t is to avoid the 'not awaited' warning.
            await _deviceTwin.UpdateTwinData(jsonString);
        }

        private async Task UpdateTwinData(string refreshingValue, string finalValue)
        {
            await UpdateTwinData(DTRootNodeString + refreshingValue + DTRootNodeSuffixString);
            await UpdateTwinData(DTRootNodeString + finalValue + DTRootNodeSuffixString);

            MessageBox.Show("Desired state sent to Device Twin!");
        }

        private async Task SetDesired(string sectionName, string sectionValueString)
        {
            string refreshingValue = "{ \"" + sectionName + "\" : " + DTRefreshing + " }";
            string finalValue = "{ " + sectionValueString  + " }";

            await UpdateTwinData(refreshingValue, finalValue);
        }

        private void OnSetTimeInfo(object sender, RoutedEventArgs e)
        {
            SetDesired(TimeDesiredState.SectionName, TimeDesiredState.ToJson()).FireAndForget();
        }

        private void OnSetTimeService(object sender, RoutedEventArgs e)
        {
            SetDesired(TimeSvcDesiredState.SectionName, TimeSvcDesiredState.ToJson()).FireAndForget();
        }

        private void OnSetExternalStorageInfo(object sender, RoutedEventArgs e)
        {
            ExternalStorageDataContract.DesiredProperties desiredProperties = new ExternalStorageDataContract.DesiredProperties();
            desiredProperties.connectionString = AzureStorageDesiredConnectionString.Text;
            SetDesired(ExternalStorageDataContract.SectionName, desiredProperties.ToJsonString()).FireAndForget();
        }

        private void OnSetWindowsUpdatePolicyInfo(object sender, RoutedEventArgs e)
        {
            SetDesired(WindowsUpdatePolicyDesiredState.SectionName, WindowsUpdatePolicyDesiredState.ToJsonString()).FireAndForget();
        }

        private void OnSetDiagnosticLogsInfo(object sender, RoutedEventArgs e)
        {
            SetDesired(DesiredDiagnosticLogs.SectionName, DesiredDiagnosticLogs.ToJson()).FireAndForget();
        }

        private void OnDeviceDeleteFile(object sender, RoutedEventArgs e)
        {
            DeviceDeleteFile deviceDeleteFile = new DeviceDeleteFile(_deviceTwin);
            deviceDeleteFile.Owner = this;
            deviceDeleteFile.DataContext = null;
            deviceDeleteFile.ShowDialog();
        }

        private void OnDeviceUploadFile(object sender, RoutedEventArgs e)
        {
            DeviceUploadFile deviceUploadFile = new DeviceUploadFile(_deviceTwin);
            deviceUploadFile.Owner = this;
            deviceUploadFile.DataContext = null;
            deviceUploadFile.ShowDialog();
        }

        private WindowsUpdatesDataContract.DesiredProperties UIToWindowsUpdatesConfiguration()
        {
            WindowsUpdatesDataContract.DesiredProperties desiredProperties = new WindowsUpdatesDataContract.DesiredProperties();
            desiredProperties.approved = DesiredApproved.Text;
            return desiredProperties;
        }

        private void WindowsUpdatesConfigurationToUI(JObject root)
        {
            WindowsUpdatesDataContract.ReportedProperties reportedProperties = WindowsUpdatesDataContract.ReportedProperties.FromJsonObject(root);

            ReportedInstalled.Text = reportedProperties.installed;
            ReportedApproved.Text = reportedProperties.approved;
            ReportedFailed.Text = reportedProperties.failed;
            ReportedInstallable.Text = reportedProperties.installable;
            ReportedPendingReboot.Text = reportedProperties.pendingReboot;
            ReportedLastScanTime.Text = reportedProperties.lastScanTime;
            ReportedDeferUpgrade.IsChecked = reportedProperties.deferUpgrade;
        }

        private void OnSetWindowsUpdatesInfo(object sender, RoutedEventArgs e)
        {
            SetDesired(WindowsUpdatesDataContract.SectionName, UIToWindowsUpdatesConfiguration().ToJsonString()).FireAndForget();
        }

        private void OnSetWindowsTelemetry(object sender, RoutedEventArgs e)
        {
            SetDesired(WindowsTelemetryDataContract.SectionName, WindowsTelemetryDesiredState.ToJsonString()).FireAndForget();
        }

        private void PopulateCertificateList(
            IEnumerable<CertificateSelector.CertificateSummary> certsToInstall,
            IEnumerable<string> certsToUninstall,
            List<CertificatesDataContract.CertificateInfo> desiredList)
        {
            if (desiredList == null)
            {
                return;
            }

            if (certsToInstall != null)
            {
                foreach (CertificateSelector.CertificateSummary certificateSummary in certsToInstall)
                {
                    CertificatesDataContract.CertificateInfo certificateInfo = new CertificatesDataContract.CertificateInfo();
                    certificateInfo.Hash = certificateSummary.Hash;
                    certificateInfo.StorageFileName = certificateSummary.StorageFileName;
                    certificateInfo.State = CertificatesDataContract.JsonStateInstalled;
                    desiredList.Add(certificateInfo);
                }
            }

            if (certsToUninstall != null)
            {
                foreach (string hash in certsToUninstall)
                {
                    CertificatesDataContract.CertificateInfo certificateInfo = new CertificatesDataContract.CertificateInfo();
                    certificateInfo.Hash = hash;
                    certificateInfo.StorageFileName = "";
                    certificateInfo.State = CertificatesDataContract.JsonStateUninstalled;
                    desiredList.Add(certificateInfo);
                }
            }
        }

        private CertificatesDataContract.DesiredProperties UIToCertificateConfiguration()
        {
            CertificatesDataContract.DesiredProperties certificatesDesiredProperties = new CertificatesDataContract.DesiredProperties();

            PopulateCertificateList(
                Desired_RootCATrustedCertificates_Root.CertsToInstall,
                Desired_RootCATrustedCertificates_Root.CertsToUninstall,
                certificatesDesiredProperties.rootCATrustedCertificates_Root);

            PopulateCertificateList(
                Desired_RootCATrustedCertificates_CA.CertsToInstall,
                Desired_RootCATrustedCertificates_CA.CertsToUninstall,
                certificatesDesiredProperties.rootCATrustedCertificates_CA);

            PopulateCertificateList(
                Desired_RootCATrustedCertificates_TrustedPublisher.CertsToInstall,
                Desired_RootCATrustedCertificates_TrustedPublisher.CertsToUninstall,
                certificatesDesiredProperties.rootCATrustedCertificates_TrustedPublisher);

            PopulateCertificateList(
                Desired_RootCATrustedCertificates_TrustedPeople.CertsToInstall,
                Desired_RootCATrustedCertificates_TrustedPeople.CertsToUninstall,
                certificatesDesiredProperties.rootCATrustedCertificates_TrustedPeople);

            PopulateCertificateList(
                Desired_CertificateStore_CA_System.CertsToInstall,
                Desired_CertificateStore_CA_System.CertsToUninstall,
                certificatesDesiredProperties.certificateStore_CA_System);

            PopulateCertificateList(
                Desired_CertificateStore_Root_System.CertsToInstall,
                Desired_CertificateStore_Root_System.CertsToUninstall,
                certificatesDesiredProperties.certificateStore_Root_System);

            PopulateCertificateList(
                Desired_CertificateStore_My_User.CertsToInstall,
                Desired_CertificateStore_My_User.CertsToUninstall,
                certificatesDesiredProperties.certificateStore_My_User);

            PopulateCertificateList(
                Desired_CertificateStore_My_System.CertsToInstall,
                Desired_CertificateStore_My_System.CertsToUninstall,
                certificatesDesiredProperties.certificateStore_My_System);

            return certificatesDesiredProperties;
        }

        private void OnSetCertificateConfiguration(object sender, RoutedEventArgs e)
        {
            CertificatesDataContract.DesiredProperties desiredProperties = UIToCertificateConfiguration();
            string json = desiredProperties.ToJsonString();
            Debug.WriteLine("certificates:");
            Debug.WriteLine(json);

            SetDesired(CertificatesDataContract.SectionName, json).FireAndForget();
        }

        private void OnSetRebootInfo(object sender, RoutedEventArgs e)
        {
            SetDesired(RebootInfoDesiredState.SectionName, RebootInfoDesiredState.ToJsonString()).FireAndForget();
        }

        private void OnSetAppsConfiguration(object sender, RoutedEventArgs e)
        {
            SetDesired(TheAppsConfigurator.SectionName, TheAppsConfigurator.ToJson()).FireAndForget();
        }

        private void OnSetDeviceInfo(object sender, RoutedEventArgs e)
        {
            DeviceInfoDataContract.DesiredProperties desiredProperties = new DeviceInfoDataContract.DesiredProperties();
            SetDesired(DeviceInfoDataContract.SectionName, desiredProperties.ToJsonString()).FireAndForget();
        }

        private void OnSetAllDesiredProperties(object sender, RoutedEventArgs e)
        {
            StringBuilder json = new StringBuilder();

            json.Append("{");
            json.Append(TimeDesiredState.ToJson());
            json.Append(",");
            json.Append(UIToCertificateConfiguration().ToJsonString());
            json.Append(",");
            json.Append(RebootInfoDesiredState.ToJsonString());
            json.Append(",");
            json.Append(WindowsUpdatePolicyDesiredState.ToJsonString());
            json.Append(",");
            json.Append(UIToWindowsUpdatesConfiguration().ToJsonString());
            json.Append(",");
            json.Append(WindowsTelemetryDesiredState.ToJsonString());
            json.Append(",");
            json.Append(DeviceHealthAttestationDesiredState.ToJson());
            json.Append(",");
            json.Append(WifiDesiredState.ToJson());
            json.Append("}");

            UpdateTwinData(DTRefreshing, json.ToString()).FireAndForget();
        }

        private async void UploadAppx(string connectionString, string container, string appxLocalPath, string dep0LocalPath, string dep1LocalPath, string certLocalPath)
        {
            // Retrieve storage account from connection string.
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            // Create the blob client.
            var blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve a reference to a container.
            var containerRef = blobClient.GetContainerReference(container);

            // Create the container if it doesn't already exist.
            await containerRef.CreateIfNotExistsAsync();

            // Appx
            {
                var blob = containerRef.GetBlockBlobReference(new FileInfo(appxLocalPath).Name);
                await blob.UploadFromFileAsync(appxLocalPath);
            }

            // Dep1
            if (!string.IsNullOrEmpty(dep0LocalPath))
            {
                var blob = containerRef.GetBlockBlobReference(new FileInfo(dep0LocalPath).Name);
                await blob.UploadFromFileAsync(dep0LocalPath);
            }

            // Dep2
            if (!string.IsNullOrEmpty(dep1LocalPath))
            {
                var blob = containerRef.GetBlockBlobReference(new FileInfo(dep1LocalPath).Name);
                await blob.UploadFromFileAsync(dep1LocalPath);
            }

            // Certificate
            if (!string.IsNullOrEmpty(certLocalPath))
            {
                var blob = containerRef.GetBlockBlobReference(new FileInfo(certLocalPath).Name);
                await blob.UploadFromFileAsync(certLocalPath);
            }
        }

        private async void DeviceHealthAttestationReportButtonAsync(object sender, RoutedEventArgs e)
        {
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(DeviceHealthAttestationDataContract.ReportNowMethodName, "{}", new TimeSpan(0, 0, 30), new CancellationToken());
        }

        private void DeviceHealthAttestationSetInfoButtonAsync(object sender, RoutedEventArgs e)
        {
            SetDesired(DeviceHealthAttestationDesiredState.SectionName, DeviceHealthAttestationDesiredState.ToJson()).FireAndForget();
        }

        private void OnExpandAzureStorageExplorer(object sender, RoutedEventArgs e)
        {
            ToggleUIElementVisibility(AzureStorageExplorer);
        }

        private async Task<DeviceMethodReturnValue> RequestCertificateDetailsAsync(string connectionString, string containerName, string cspPath, string hash, string targetFileName)
        {
            GetCertificateDetailsParams getCertificateDetailsParams = new GetCertificateDetailsParams();
            getCertificateDetailsParams.path = cspPath;
            getCertificateDetailsParams.hash = hash;
            getCertificateDetailsParams.connectionString = connectionString;
            getCertificateDetailsParams.containerName = containerName;
            getCertificateDetailsParams.blobName = hash + ".json";
            string parametersJson = JsonConvert.SerializeObject(getCertificateDetailsParams);
            Debug.WriteLine(parametersJson);

            CancellationToken cancellationToken = new CancellationToken();
            return await _deviceTwin.CallDeviceMethod(DMJSonConstants.DTWindowsIoTNameSpace + ".getCertificateDetails", parametersJson, new TimeSpan(0, 0, 30), cancellationToken);
        }

        private void ShowCertificateDetails(CertificateSelector sender, CertificateSelector.CertificateDetails certificateData)
        {
            CertificateDetails certificateDetails = new CertificateDetails();
            certificateDetails.Owner = this;
            certificateDetails.DataContext = certificateData;
            certificateDetails.ShowDialog();
        }

        private async void ExportCertificateDetailsAsync(CertificateSelector sender, CertificateSelector.CertificateDetails certificateData)
        {
            MessageBox.Show("Exporting certificate details from the device to Azure storage...");
            string targetFileName = certificateData.Hash + ".json";
            DeviceMethodReturnValue result = await RequestCertificateDetailsAsync(AzureStorageDesiredConnectionString.Text, AzureStorageContainerName.Text, sender.CertificatesPath, certificateData.Hash, targetFileName);
            GetCertificateDetailsResponse response = JsonConvert.DeserializeObject<GetCertificateDetailsResponse>(result.Payload);
            if (response == null || response.Status != 0)
            {
                MessageBox.Show("Error: could not schedule certificate export");
                return;
            }

            CertificateExportDetails.CertificateExportDetailsData certificateExportDetailsData = new CertificateExportDetails.CertificateExportDetailsData();
            certificateExportDetailsData.ConnectionString = AzureStorageDesiredConnectionString.Text;
            certificateExportDetailsData.ContainerName = AzureStorageContainerName.Text;
            certificateExportDetailsData.BlobName = targetFileName;

            CertificateExportDetails certificateExportDetails = new CertificateExportDetails();
            certificateExportDetails.Owner = this;
            certificateExportDetails.DataContext = certificateExportDetailsData;
            certificateExportDetails.Show();
        }

        private void ExportCertificateDetails(CertificateSelector sender, CertificateSelector.CertificateDetails certificateData)
        {
            ExportCertificateDetailsAsync(sender, certificateData);
        }

        private void OnSetWifiConfiguration(object sender, RoutedEventArgs e)
        {
            SetDesired(WifiDesiredState.SectionName, WifiDesiredState.ToJson()).FireAndForget();
        }

        public async void ExportWifiProfileDetails(string profileName, string storageConnectionString, string storageContainer, string blobName)
        {
            var details = new GetWifiProfileDetailsParams();
            {
                details.profileName = profileName;
                details.connectionString = storageConnectionString;
                details.containerName = storageContainer;
                details.blobName = blobName;
            }
            var parametersJson = JsonConvert.SerializeObject(details);
            Debug.WriteLine(parametersJson);

            var cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await this._deviceTwin.CallDeviceMethod(DMJSonConstants.DTWindowsIoTNameSpace + ".getWifiDetails", parametersJson, new TimeSpan(0, 0, 30), cancellationToken);
            System.Windows.MessageBox.Show("Get Wifi Profile Details Command Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void PopulateExternalStorageFromJson(JObject jRoot)
        {
            JToken jToken = jRoot.SelectToken("properties.desired." + DMJSonConstants.DTWindowsIoTNameSpace + ".externalStorage.connectionString");
            if (jToken != null && jToken is JValue)
            {
                JValue jConnectionString = (JValue)jToken;
                AzureStorageDesiredConnectionString.Text = (string)jConnectionString;
            }
        }

        private void OnLoadProfile(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".json";
            dlg.Filter = "json files (*.json)|*.json|All files (*.*)|*.*";
            bool? result = dlg.ShowDialog();
            if (result != true)
            {
                return;
            }

            object rootObject = JsonConvert.DeserializeObject(File.ReadAllText(dlg.FileName));
            if (!(rootObject is JObject))
            {
                System.Windows.MessageBox.Show("Invalid json file content!");
            }

            JObject jRoot = (JObject)rootObject;
            PopulateExternalStorageFromJson(jRoot);
            TheAppsConfigurator.FromJson(jRoot);
            DesiredDiagnosticLogs.FromJson(jRoot);
        }

        private async void StartUsoClientCmdAsync(string cmd)
        {
            var cmdParams = new UsoClientCmdDataContract.CmdParams();
            cmdParams.cmd = cmd;
            string cmdParamsString = cmdParams.ToJsonString();

            Debug.WriteLine("Cmd params : " + cmdParamsString);

            CancellationToken cancellationToken = new CancellationToken();
            DeviceMethodReturnValue result = await _deviceTwin.CallDeviceMethod(UsoClientCmdDataContract.StartUsoClientCmdAsync, cmdParamsString, new TimeSpan(0, 0, 30), cancellationToken);
            MessageBox.Show("FactoryReset Command Result:\nStatus: " + result.Status + "\nReason: " + result.Payload);
        }

        private void OnWUStartInteractiveScan(object sender, RoutedEventArgs e)
        {
            StartUsoClientCmdAsync(UsoClientCmdDataContract.JsonStartInteractiveScan);
        }

        private void OnWURestartDevice(object sender, RoutedEventArgs e)
        {
            StartUsoClientCmdAsync(UsoClientCmdDataContract.JsonRestartDevice);
        }

        private DeviceTwinAndMethod _deviceTwin;

    }
}
