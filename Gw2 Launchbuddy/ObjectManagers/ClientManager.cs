﻿using Gw2_Launchbuddy.Interfaces;
using Gw2_Launchbuddy.Wrappers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Gw2_Launchbuddy.Helpers;
using System.IO;
using System.Xml;
using Gw2_Launchbuddy.Extensions;

namespace Gw2_Launchbuddy.ObjectManagers
{
    public static class ClientManager
    {
        private static ObservableCollection<Client> clientCollection { get; set; }
        public static ReadOnlyObservableCollection<Client> ClientCollection { get; set; }

        static ClientManager()
        {
            clientCollection = new ObservableCollection<Client>();
            ClientCollection = new ReadOnlyObservableCollection<Client>(clientCollection);
        }

        public static Client CreateClient()
        {
            var createdClient = new Client();
            clientCollection.Add(createdClient);
            return createdClient;
        }

        public static class ClientInfo
        {
            //Info about the client
            private static string executable;

            public static bool IsUpToDate { get; set; }
            public static string InstallPath { get; set; }
            public static string Executable { get { return executable; } set { executable = value; ProcessName = Regex.Replace(value, @"\.exe(?=$)", "", RegexOptions.IgnoreCase); } }
            public static string FullPath { get { return InstallPath + Executable; } }
            public static string ProcessName { get; private set; }
            public static string Version { get; set; }

            public static void LoadClientInfo()
            {
                //Find the newest XML file in APPDATA (the XML files share the same name as their XML files -> multiple .xml files possible!)
                string[] configfiles = new string[] { };
                try
                {
                    configfiles = Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Guild Wars 2\", "*.exe.xml");
                }
                catch (Exception e)
                {
                    //TODO: Handle corrupt/missing Guild Wars install
                    MessageBox.Show("Guild Wars may not be installed. \n " + e.Message);
                    return;
                }
                Globals.ClientXmlpath = "";
                long max = 0;

                foreach (string config in configfiles)
                {
                    if (System.IO.File.GetLastWriteTime(config).Ticks > max)
                    {
                        max = System.IO.File.GetLastWriteTime(config).Ticks;
                        Globals.ClientXmlpath = config;
                    }
                }

                //Read the GFX Settings
                Globals.SelectedGFX = GFXManager.ReadFile(Globals.ClientXmlpath);
                //lv_gfx.ItemsSource = Globals.SelectedGFX.Config;
                //lv_gfx.Items.Refresh();

                // Read the XML file
                try
                {
                    //if (Properties.Settings.Default.use_reshade) cb_reshade.IsChecked = true;

                    StreamReader stream = new System.IO.StreamReader(Globals.ClientXmlpath);
                    XmlTextReader reader = null;
                    reader = new XmlTextReader(stream);

                    while (reader.Read())
                    {
                        switch (reader.Name)
                        {
                            case "VERSIONNAME":
                                Regex filter = new Regex(@"\d*\d");
                                Version = filter.Match(reader.GetValue()).Value;
                                //lab_version.Content = "Client Version: " + ClientManager.ClientInfo.Version;
                                break;

                            case "INSTALLPATH":
                                InstallPath = reader.GetValue();
                                //lab_path.Content = "Install Path: " + ClientManager.ClientInfo.InstallPath;
                                break;

                            case "EXECUTABLE":
                                Executable = reader.GetValue();
                                //lab_path.Content += ClientManager.ClientInfo.Executable;
                                break;

                            case "EXECCMD":
                                //Filter arguments from path
                                //lab_para.Content = "Latest Start Parameters: ";
                                Regex regex = new Regex(@"(?<=^|\s)-(umbra.(\w)*|\w*)");
                                string input = reader.GetValue();
                                MatchCollection matchList = regex.Matches(input);

                                foreach (Argument argument in ArgumentManager.ArgumentCollection)
                                    foreach (Match parameter in matchList)
                                        if (argument.Flag == parameter.Value && !argument.Blocker)
                                            AccountArgumentManager.StopGap.IsSelected(parameter.Value, true);

                                //RefreshUI();
                                break;
                        }
                    }
                }
                catch
                {
                    MessageBox.Show("Gw2 info file not found! Please choose the Directory manually!");
                }
            }
        }

        public static class ClientReg
        {
            static ClientReg()
            {
                UpdateRegClients();
            }

            private static RegistryKey key { get { return Microsoft.Win32.Registry.CurrentUser.CreateSubKey("SOFTWARE").CreateSubKey("LaunchBuddy"); } }

            public static void UpdateRegClients()
            {
                try
                {
                    List<string> listClients = getClientListFromReg();

                    var gw2Proc = Client.GetClients();// Process.GetProcesses().ToList().Where(a => a.ProcessName == ClientInfo.ProcessName).ToList(); //Bad, but processes named same as client executable
                    var md5Gw2Proc = gw2Proc.ToList().Select(a => a.CalculateProcessMD5());
                    foreach (var t in listClients.Where(a => !md5Gw2Proc.Contains(a)).ToList()) listClients.Remove(t);
                    key.SetValue("Clients", listClients.ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    key.Close();
                }
            }

            public static bool CheckRegClients()
            {
                try
                {
                    UpdateRegClients();
                    List<string> listClients = getClientListFromReg();

                    var currentProcesses = Client.GetClients();
                    var gw2Procs = currentProcesses.ToList().Select(a => a.CalculateProcessMD5());
                    return gw2Procs.Count() == listClients.Count();
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    key.Close();
                }
            }

            public static void RegClient(Client Client)
            {
                try
                {
                    List<string> listClients = getClientListFromReg();

                    listClients.Add(Client.MD5);
                    key.SetValue("Clients", listClients.ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    key.Close();
                }
            }

            private static List<string> getClientListFromReg()
            {
                try
                {
                    return ((string[])key.GetValue("Clients")).ToList();
                }
                catch (Exception e)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("Reg key likely does not exist: " + e.Message);
                    return new List<string>();
#endif
                }
            }
        }
    }

    public class Client : ProcessWrapper, IProcessWrapper<Client>
    {
        public Client()
        {
            StartInfo = new ProcessStartInfo(ClientManager.ClientInfo.FullPath);
            Started += Client_Started;
        }

        private void Client_Started(object sender, EventArgs e)
        {
            ClientManager.ClientReg.RegClient(this);
        }

        new public bool Start()

        {
            EnableRaisingEvents = true;
            Exited += Client_Exited;
            return base.Start();
        }

        private void Client_Exited(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(delegate { AccountClientManager.Remove(this); });
            Dispose();
        }

        public static Process[] GetClients()
        {
            return GetProcessesByName(ClientManager.ClientInfo.ProcessName);
            //return temp.Select(a => ToDerivedHelper.ToDerived<Process, Client>(a)).ToArray();
        }
    }
    /*
    public class Client
    {
        public Process Process;
        public string md5;

        public Client()
        {
            Process = new Process() { StartInfo = new ProcessStartInfo(ClientManager.ClientInfo.FullPath) };
        }

        public string Arguments { get => Process.StartInfo.Arguments; set => Process.StartInfo.Arguments = value; }

        public void Start()
        {
            Process.EnableRaisingEvents = true;
            Process.Exited += ClientProcess_Exited;
            Process.Start();
            md5 = ClientManager.CalculateProcessMD5(this.Process);
            ClientManager.ClientReg.RegClient(this);
        }

        public void StartAndWait()
        {
            Start();
            Process.WaitForExit();
        }

        private void ClientProcess_Exited(object sender, EventArgs e)
        {
            Process.Exited -= ClientProcess_Exited;
            Application.Current.Dispatcher.Invoke(delegate { AccountClientManager.Remove(this); });
            Process.Dispose();
        }

        public void Stop()
        {
            try
            {
                if (!Process.CloseMainWindow())
                    Process.Kill();
                if (!Process.WaitForExit(1000))
                    Process.Kill();
            }
            catch { }
        }
    }*/
}