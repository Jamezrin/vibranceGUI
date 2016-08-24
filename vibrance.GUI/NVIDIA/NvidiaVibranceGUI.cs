﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using vibrance.GUI.AMD.vendor.utils;
using vibrance.GUI.common;
using Application = System.Windows.Forms.Application;
using MessageBox = System.Windows.Forms.MessageBox;

namespace vibrance.GUI.NVIDIA
{
    public partial class NvidiaVibranceGUI : Form
    {
        private readonly IVibranceProxy v;
        private IRegistryController registryController;
        public bool silenced = false;
        private const string appName = "vibranceGUI";
        private const string twitterLink = "https://twitter.com/juvlarN";
        private const string paypalDonationLink = "https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=JDQFNKNNEW356";
        private const string steamDonationLink = "https://steamcommunity.com/tradeoffer/new/?partner=92410529&token=Oas6jXrc";

        private bool allowVisible;
        private List<ApplicationSetting> applicationSettings;
        private readonly List<ResolutionModeWrapper> supportedResolutionList;
        private readonly ResolutionModeWrapper WindowsResolutionSettings;

        public NvidiaVibranceGUI()
        {
            const string nvidiaAdapterName = "vibranceDLL.dll";
            string resourceName = $"{typeof(Program).Namespace}.NVIDIA.{nvidiaAdapterName}";
            
            string dllPath = CommonUtils.LoadUnmanagedLibraryFromResource(
                Assembly.GetExecutingAssembly(),
                resourceName,
                nvidiaAdapterName);

            allowVisible = true;
            InitializeComponent();
            Marshal.PrelinkAll(typeof(NvidiaDynamicVibranceProxy));

            supportedResolutionList = ResolutionHelper.EnumerateSupportedResolutionModes();

            Devmode currentResolutionMode;
            if (ResolutionHelper.GetCurrentResolutionSettings(out currentResolutionMode, null))
            {
                WindowsResolutionSettings = new ResolutionModeWrapper(currentResolutionMode);
            }
            else
            {
                MessageBox.Show("Current resolution mode could not be determined. Switching back to your Windows resolution will not work.");
            }

            applicationSettings = new List<ApplicationSetting>();
            v = new NvidiaDynamicVibranceProxy(applicationSettings, WindowsResolutionSettings);

            backgroundWorker.WorkerReportsProgress = true;
            settingsBackgroundWorker.WorkerReportsProgress = true;

            backgroundWorker.RunWorkerAsync();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                value = false;
                if (!this.IsHandleCreated)
                {
                    CreateHandle();
                }
            }
            base.SetVisibleCore(value);
        }

        public void SetAllowVisible(bool value)
        {
            allowVisible = value;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            setGuiEnabledFlag(false);
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                //this.notifyIcon.Visible = true;
                //this.notifyIcon.BalloonTipText = "Running minimized... Like the program? Consider donating!";
                //this.notifyIcon.ShowBalloonTip(250);
                this.Hide();
            }
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            int vibranceWindowsLevel = NvidiaVibranceProxy.NvapiDefaultLevel;
            bool affectPrimaryMonitorOnly = false;

            while (!this.IsHandleCreated)
            {
                Thread.Sleep(500);
            }

            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    readVibranceSettings(out vibranceWindowsLevel, out affectPrimaryMonitorOnly);
                });
            }
            else
            {
                readVibranceSettings(out vibranceWindowsLevel, out affectPrimaryMonitorOnly);
            }

            if (v.GetVibranceInfo().isInitialized)
            {
                backgroundWorker.ReportProgress(1);

                setGuiEnabledFlag(true);

                v.SetApplicationSettings(applicationSettings);
                v.SetShouldRun(true);
                v.SetVibranceWindowsLevel(vibranceWindowsLevel);
                v.SetAffectPrimaryMonitorOnly(affectPrimaryMonitorOnly);
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            if (v != null && v.GetVibranceInfo().isInitialized)
            {
                setGuiEnabledFlag(true);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            cleanUp();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void trackBarWindowsLevel_Scroll(object sender, EventArgs e)
        {
            NvidiaVibranceValueWrapper vibranceValue = NvidiaVibranceValueWrapper.Find(trackBarWindowsLevel.Value);
            if (vibranceValue == null)
                return;
            v.SetVibranceWindowsLevel(trackBarWindowsLevel.Value);
            labelWindowsLevel.Text = vibranceValue.GetPercentage;
            if (!settingsBackgroundWorker.IsBusy)
            {
                settingsBackgroundWorker.RunWorkerAsync();
            }
        }

        private void settingsBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.Sleep(5000);
            ForceSaveVibranceSettings();
        }

        private void ForceSaveVibranceSettings()
        {
            int windowsLevel = 0;
            bool affectPrimaryMonitorOnly = false;
            this.Invoke((MethodInvoker)delegate
            {
                windowsLevel = trackBarWindowsLevel.Value;
                affectPrimaryMonitorOnly = checkBoxPrimaryMonitorOnly.Checked;
            });
            saveVibranceSettings(windowsLevel, affectPrimaryMonitorOnly);
        }

        private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == 1)
            {
                this.statusLabel.Text = "Running!";
                this.statusLabel.ForeColor = Color.Green;
            }
            else if (e.ProgressPercentage == 2)
            {
                this.statusLabel.Text = $"NVAPI Unloaded: {e.UserState}";
            }
        }

        private void settingsBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
        }

        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            allowVisible = true;
            this.Show();

            this.WindowState = FormWindowState.Normal;
            this.Visible = true;

            this.Refresh();
            this.ShowInTaskbar = true;
        }

        private void checkBoxPrimaryMonitorOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (this.v == null)
            {
                return;
            }

            this.v.SetAffectPrimaryMonitorOnly(this.checkBoxPrimaryMonitorOnly.Checked);
            if (!this.settingsBackgroundWorker.IsBusy)
            {
                this.settingsBackgroundWorker.RunWorkerAsync();
            }
            if (this.checkBoxPrimaryMonitorOnly.Checked)
            {
                this.notifyIcon.BalloonTipText = "vibranceGUI will only affect your primary monitor now.";
                this.notifyIcon.ShowBalloonTip(250);
            }
        }

        private void checkBoxAutostart_CheckedChanged(object sender, EventArgs e)
        {
            RegistryController autostartController = new RegistryController();
            if (this.checkBoxAutostart.Checked)
            {
                string pathToExe = "\"" + Application.ExecutablePath + "\" -minimized";
                if (!autostartController.IsProgramRegistered(appName))
                {
                    this.notifyIcon.BalloonTipText = autostartController.RegisterProgram(appName, pathToExe) 
                        ? "Registered to Autostart!" 
                        : "Registering to Autostart failed!";
                }
                else if (!autostartController.IsStartupPathUnchanged(appName, pathToExe))
                {
                    this.notifyIcon.BalloonTipText = autostartController.RegisterProgram(appName, pathToExe)
                        ? "Updated Autostart Path!"
                        : "Updating Autostart Path failed!";
                }
                else
                {
                    return;
                }
            }
            else
            {
                this.notifyIcon.BalloonTipText = autostartController.UnregisterProgram(appName) 
                    ? "Unregistered from Autostart!" 
                    : "Unregistering from Autostart failed!";
            }

            notifyIcon.ShowBalloonTip(250);
        }

        private void twitterToolStripTextBox_Click(object sender, EventArgs e)
        {
            Process.Start(twitterLink);
        }

        private void linkLabelTwitter_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(twitterLink);
        }

        private void setGuiEnabledFlag(bool flag)
        {
            this.Invoke((MethodInvoker)delegate
            {
                this.trackBarWindowsLevel.Enabled = flag;
                this.checkBoxAutostart.Enabled = flag;
                this.checkBoxPrimaryMonitorOnly.Enabled = flag;
            });
        }

        private void cleanUp()
        {
            try
            {
                this.statusLabel.Text = "Closing...";
                this.statusLabel.ForeColor = Color.Red;
                this.Update();
                if (v != null && v.GetVibranceInfo().isInitialized)
                {
                    v.SetShouldRun(false);
                    v.UnloadLibraryEx();
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        public static void Log(Exception ex)
        {
            using (StreamWriter w = File.AppendText("vibranceGUI_log.txt"))
            {
                w.Write("\r\nLog Entry : ");
                w.WriteLine("{0} {1}", DateTime.Now.ToLongTimeString(),
                    DateTime.Now.ToLongDateString());
                w.WriteLine("Exception Found:\nType: {0}", ex.GetType().FullName);
                w.WriteLine("Message: {0}", ex.Message);
                w.WriteLine("Source: {0}", ex.Source);
                w.WriteLine("Stacktrace: {0}", ex.StackTrace);
                w.WriteLine("Exception String: {0}", ex.ToString());

                w.WriteLine("-------------------------------");
            }
        }

        public static void Log(string msg)
        {
            using (StreamWriter w = File.AppendText("vibranceGUI_log.txt"))
            {
                w.Write("\r\nLog Entry : ");
                w.WriteLine("{0} {1}", DateTime.Now.ToLongTimeString(),
                    DateTime.Now.ToLongDateString());
                w.WriteLine(msg);
                w.WriteLine("-------------------------------");
            }
        }

        private void readVibranceSettings(out int vibranceWindowsLevel, out bool affectPrimaryMonitorOnly)
        {
            registryController = new RegistryController();
            this.checkBoxAutostart.Checked = registryController.IsProgramRegistered(appName);

            SettingsController settingsController = new SettingsController();
            settingsController.ReadVibranceSettings(GraphicsAdapter.Nvidia, out vibranceWindowsLevel, out affectPrimaryMonitorOnly, out applicationSettings);

            if (this.IsHandleCreated)
            {
                //no null check needed, SettingsController will always return matching values.
                labelWindowsLevel.Text = NvidiaVibranceValueWrapper.Find(vibranceWindowsLevel).GetPercentage;

                trackBarWindowsLevel.Value = vibranceWindowsLevel;
                checkBoxPrimaryMonitorOnly.Checked = affectPrimaryMonitorOnly;
                foreach (ApplicationSetting application in applicationSettings)
                {
                    if (!File.Exists(application.FileName))
                        continue;

                    InitializeApplicationList();

                    Icon icon = Icon.ExtractAssociatedIcon(application.FileName);
                    if (icon != null)
                    {
                        this.listApplications.LargeImageList.Images.Add(icon);
                        ListViewItem lvi = new ListViewItem(application.Name);
                        lvi.ImageIndex = this.listApplications.Items.Count;
                        lvi.Tag = application.FileName;
                        this.listApplications.Items.Add(lvi);
                    }
                }
            }
        }

        private void saveVibranceSettings(int windowsLevel, bool affectPrimaryMonitorOnly)
        {
            SettingsController settingsController = new SettingsController();

            settingsController.SetVibranceSettings(
                windowsLevel.ToString(),
                affectPrimaryMonitorOnly.ToString(),
                applicationSettings
            );
        }

        private void buttonPaypal_Click(object sender, EventArgs e)
        {
            Process.Start(paypalDonationLink);
        }

        private void buttonSteam_Click(object sender, EventArgs e)
        {
            Process.Start(steamDonationLink);
        }

        private void buttonAddProgram_Click(object sender, EventArgs e)
        {
            InitializeApplicationList();

            OpenFileDialog fileDialog = new OpenFileDialog();
            DialogResult result = fileDialog.ShowDialog();
            if (result == DialogResult.OK && fileDialog.CheckFileExists && fileDialog.SafeFileName != null 
                && !applicationSettings.Any(x => x.FileName.ToLower() == fileDialog.FileName.ToLower()))
            {
                Icon icon = Icon.ExtractAssociatedIcon(fileDialog.FileName);
                if (icon != null)
                {
                    this.listApplications.LargeImageList.Images.Add(icon);
                    ListViewItem lvi = new ListViewItem(Path.GetFileNameWithoutExtension(fileDialog.FileName));
                    lvi.ImageIndex = this.listApplications.Items.Count;
                    lvi.Tag = fileDialog.FileName;
                    this.listApplications.Items.Add(lvi);
                    ForceSaveVibranceSettings();
                }
            }
        }

        public void AddProgramExtern(ProcessExplorerEntry processExplorerEntry)
        {
            if(this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    AddProgramIntern(processExplorerEntry);
                });
            }
            else
            {
                AddProgramIntern(processExplorerEntry);
            }
        }

        private void AddProgramIntern(ProcessExplorerEntry processExplorerEntry)
        {
            InitializeApplicationList();
            
            if(!File.Exists(processExplorerEntry.Path) || applicationSettings.Any(x => x.FileName.ToLower() == processExplorerEntry.Path.ToLower()))
            {
                return; 
            }

            Icon icon = processExplorerEntry.Icon;
            string path = processExplorerEntry.Path;
            if (icon != null)
            {
                this.listApplications.LargeImageList.Images.Add(icon);
                ListViewItem lvi = new ListViewItem(Path.GetFileNameWithoutExtension(path));
                lvi.ImageIndex = this.listApplications.Items.Count;
                lvi.Tag = path;
                this.listApplications.Items.Add(lvi);
                ForceSaveVibranceSettings();
            }
        }

        private void InitializeApplicationList()
        {
            if (this.listApplications.LargeImageList == null)
            {
                ImageList imageList = new ImageList();
                imageList.ImageSize = new Size(48, 48);
                imageList.ColorDepth = ColorDepth.Depth32Bit;
                this.listApplications.LargeImageList = imageList;
                ListViewItem_SetSpacing(this.listApplications, 48 + 24, 48 + 6 + 16);
            }
        }

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public int MakeLong(short lowPart, short highPart)
        {
            return (int)(((ushort)lowPart) | (uint)(highPart << 16));
        }

        public void ListViewItem_SetSpacing(ListView listview, short leftPadding, short topPadding)
        {
            const int LVM_FIRST = 0x1000;
            const int LVM_SETICONSPACING = LVM_FIRST + 53;
            SendMessage(listview.Handle, LVM_SETICONSPACING, IntPtr.Zero, (IntPtr)MakeLong(leftPadding, topPadding));
        }

        private void listApplications_DoubleClick(object sender, EventArgs e)
        {
            ListViewItem selectedItem = this.listApplications.SelectedItems[0];
            if (selectedItem != null)
            {
                ApplicationSetting actualSetting = applicationSettings.FirstOrDefault(x => x.FileName == selectedItem.Tag.ToString());
                VibranceSettings settingsWindow = new VibranceSettings(v, 0, 63, selectedItem, actualSetting, supportedResolutionList, x => NvidiaVibranceValueWrapper.Find(x).GetPercentage);
                DialogResult result = settingsWindow.ShowDialog();
                if (result == DialogResult.OK)
                {
                    ApplicationSetting newSetting = settingsWindow.GetApplicationSetting();
                    if (applicationSettings.FirstOrDefault(x => x.FileName == newSetting.FileName) != null)
                        applicationSettings.Remove(applicationSettings.First(x => x.FileName == newSetting.FileName));
                    applicationSettings.Add(newSetting);
                    ForceSaveVibranceSettings();
                }
            }
        }

        private void buttonRemoveProgram_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem eachItem in listApplications.SelectedItems)
            {
                for (int i = eachItem.Index + 1; i < listApplications.Items.Count; i++)
                    listApplications.Items[i].ImageIndex--;
                Image img = this.listApplications.LargeImageList.Images[eachItem.ImageIndex];
                this.listApplications.LargeImageList.Images.RemoveAt(eachItem.ImageIndex);
                img.Dispose();

                listApplications.Items.Remove(eachItem);

                applicationSettings.Remove(applicationSettings.FirstOrDefault(x => x.FileName.Equals(eachItem.Tag.ToString())));
            }

            ForceSaveVibranceSettings();
        }

        private void buttonProcessExplorer_Click(object sender, EventArgs e)
        {
            ProcessExplorer ex = new ProcessExplorer(this);
            ex.Show();
        }
    }
}