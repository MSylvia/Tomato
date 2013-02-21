using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Tomato;
using Tomato.Hardware;
using System.Globalization;
using System.Reflection;
using System.IO;

namespace Lettuce
{
    public partial class Debugger : Form
    {
        public DCPU CPU { get; set; }
        private bool MayUpdateLayout = true;
        public List<Type> DeviceControllers;
        public List<string> Watches { get; set; }

        private List<bool> InterruptBreakDevices { get; set; }

        public const string FUNC_STEP_INTO = "step_into";
        public const string FUNC_STEP_OVER = "step_over";
        public const string FUNC_CHANGE_RUNNING = "change_run";
        public const string FUNC_GOTO_ADDRESS = "goto_addr";

        public Debugger(ref DCPU CPU)
        {
            InitializeComponent();
            if (KnownCode == null)
                KnownCode = new Dictionary<ushort, string>();
            if (KnownLabels == null)
                KnownLabels = new Dictionary<ushort, string>();
            InterruptBreakDevices = new List<bool>();
            FixKeyConfig();

            KeyPreview = true;
            this.CPU = CPU;
            this.CPU.BreakpointHit += this.CPU_BreakpointHit;
            this.CPU.InvalidInstruction += CpuOnInvalidInstruction; 
            rawMemoryDisplay.CPU = this.CPU;
            stackDisplay.CPU = this.CPU;
            disassemblyDisplay1.CPU = this.CPU;

            Watches = new List<string>();
            foreach (Device d in CPU.Devices)
            {
                listBoxConnectedDevices.Items.Add(d.FriendlyName);
                InterruptBreakDevices.Add(false);
                d.InterruptFired += OnDeviceInterrupt;
            }
            // Load device controllers
            DeviceControllers = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var types = asm.GetTypes().Where(t => typeof(DeviceController).IsAssignableFrom(t) && t.IsAbstract == false);
                foreach (var type in types)
                {
                    DeviceControllers.Add(type);
                }
            }
        }

        private void OnDeviceInterrupt(object sender, EventArgs eventArgs)
        {
            int index = CPU.Devices.IndexOf(sender as Device);
            if (InterruptBreakDevices[index])
            {
                CPU.IsRunning = false;
                SetUIWarning("Hardware interrupt fired on device #" + index);
                ResetLayout();
            }
        }

        private void CpuOnInvalidInstruction(object sender, InvalidInstructionEventArgs invalidInstructionEventArgs)
        {
            InvalidInstruction(invalidInstructionEventArgs);
            if (breakOnInvalidInstructionToolStripMenuItem.Checked)
            {
                invalidInstructionEventArgs.ContinueExecution = false;
                CPU.IsRunning = false;
            }
        }

        void InvalidInstruction(InvalidInstructionEventArgs eventArgs)
        {
            if (InvokeRequired)
                Invoke(new Action(() => InvalidInstruction(eventArgs)));
            else
            {
                pictureBox1.Visible = true;
                warningLabel.Text = "Invalid instruction at 0x" + eventArgs.Address.ToString("X4") +
                    ": 0x" + eventArgs.Instruction.ToString("X4");
                warningLabel.Visible = true;
                if (breakOnInvalidInstructionToolStripMenuItem.Checked)
                    ResetLayout();
            }
        }

        void SetUIWarning(string text)
        {
            if (InvokeRequired)
                Invoke(new Action(() => SetUIWarning(text)));
            else
            {
                pictureBox1.Visible = true;
                warningLabel.Text = text;
                warningLabel.Visible = true;
                ResetLayout();
            }
        }

        void CPU_BreakpointHit(object sender, BreakpointEventArgs e)
        {
            if (stepOverEnabled)
            {
                CPU.Breakpoints.Remove(CPU.Breakpoints.First(b => b.Address == CPU.PC));
                e.ContinueExecution = false;
                (sender as DCPU).IsRunning = false;
                disassemblyDisplay1.EnableUpdates = true;
                ResetLayout();
                stepOverEnabled = false;
                return;
            }
            if (breakpointHandled)
            {
                breakpointHandled = false;
                e.ContinueExecution = true;
                return;
            }
            (sender as DCPU).IsRunning = false;
            ResetLayout();
            breakpointHandled = true;
        }
        bool breakpointHandled = false;

        public static string GetHexString(uint value, int numDigits)
        {
            string result = value.ToString("X4");
            return result;
        }

        public void ResetLayout()
        {
            if (this.IsDisposed || this.Disposing)
                return;
            if (this.InvokeRequired)
            {
                try
                {
                    this.Invoke(new Action(ResetLayout));
                }
                catch { }
            }
            else
            {
                if (!MayUpdateLayout)
                    return;
                MayUpdateLayout = false;
                SuspendLayout();
                textBoxRegisterA.Text = GetHexString(CPU.A, 4);
                textBoxRegisterB.Text = GetHexString(CPU.B, 4);
                textBoxRegisterC.Text = GetHexString(CPU.C, 4);
                textBoxRegisterX.Text = GetHexString(CPU.X, 4);
                textBoxRegisterY.Text = GetHexString(CPU.Y, 4);
                textBoxRegisterZ.Text = GetHexString(CPU.Z, 4);
                textBoxRegisterI.Text = GetHexString(CPU.I, 4);
                textBoxRegisterJ.Text = GetHexString(CPU.J, 4);
                textBoxRegisterPC.Text = GetHexString(CPU.PC, 4);
                textBoxRegisterSP.Text = GetHexString(CPU.SP, 4);
                textBoxRegisterEX.Text = GetHexString(CPU.EX, 4);
                textBoxRegisterIA.Text = GetHexString(CPU.IA, 4);
                checkBoxRunning.Checked = CPU.IsRunning;
                checkBoxInterruptQueue.Checked = CPU.InterruptQueueEnabled;
                labelQueuedInterrupts.Text = "Queued Interrupts: " + CPU.InterruptQueue.Count.ToString();
                checkBoxOnFire.Checked = CPU.IsOnFire;
                cycleCountLabel.Text = "Cycles: " + CPU.TotalCycles;
                rawMemoryDisplay.Invalidate();
                disassemblyDisplay1.Invalidate();
                propertyGrid1.SelectedObject = propertyGrid1.SelectedObject; // Forces update, intentionally redundant
                UpdateWatches();
                if (CPU.IsRunning)
                    DisableAll();
                else
                    EnableAll();
                ResumeLayout(true);
                MayUpdateLayout = true;
            }
        }

        private void UpdateWatches()
        {
            watchesListView.Items.Clear();
            foreach (var watch in Watches)
            {
                var item = new ListViewItem(watch);
                try
                {
                    item.SubItems.Add("0x" + Watch.Evaluate(watch, CPU).ToString("X4"));
                }
                catch (Exception e)
                {
                    item.SubItems.Add(e.Message);
                }
                watchesListView.Items.Add(item);
            }
        }

        private void DisableAll()
        {
            textBoxRegisterA.Enabled = false;
            textBoxRegisterB.Enabled = false;
            textBoxRegisterC.Enabled = false;
            textBoxRegisterX.Enabled = false;
            textBoxRegisterY.Enabled = false;
            textBoxRegisterZ.Enabled = false;
            textBoxRegisterI.Enabled = false;
            textBoxRegisterJ.Enabled = false;
            textBoxRegisterPC.Enabled = false;
            textBoxRegisterSP.Enabled = false;
            textBoxRegisterEX.Enabled = false;
            textBoxRegisterIA.Enabled = false;
            checkBoxOnFire.Enabled = false;
            checkBoxInterruptQueue.Enabled = false;
            buttonStepInto.Enabled = false;
            buttonStepOver.Enabled = false;
            stopToolStripMenuItem.Text = "Stop";
            rawMemoryDisplay.Enabled = false;
        }

        private void EnableAll()
        {
            textBoxRegisterA.Enabled = true;
            textBoxRegisterB.Enabled = true;
            textBoxRegisterC.Enabled = true;
            textBoxRegisterX.Enabled = true;
            textBoxRegisterY.Enabled = true;
            textBoxRegisterZ.Enabled = true;
            textBoxRegisterI.Enabled = true;
            textBoxRegisterJ.Enabled = true;
            textBoxRegisterPC.Enabled = true;
            textBoxRegisterSP.Enabled = true;
            textBoxRegisterEX.Enabled = true;
            textBoxRegisterIA.Enabled = true;
            checkBoxOnFire.Enabled = true;
            checkBoxInterruptQueue.Enabled = true;
            buttonStepInto.Enabled = true;
            buttonStepOver.Enabled = true;
            stopToolStripMenuItem.Text = "Start";
            rawMemoryDisplay.Enabled = true;
        }

        private void listBoxConnectedDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            checkBoxBreakOnInterrupt.Enabled = (listBoxConnectedDevices.SelectedIndex != -1);
            if (listBoxConnectedDevices.SelectedIndex == -1)
                return;
            Device selected = CPU.Devices[listBoxConnectedDevices.SelectedIndex];
            propertyGrid1.SelectedObject = selected;
            checkBoxBreakOnInterrupt.Checked = InterruptBreakDevices[listBoxConnectedDevices.SelectedIndex];
        }
        
        private void listBoxConnectedDevices_MouseDoubleClick(object sender, EventArgs e)
        {
            Device selected = (Device)propertyGrid1.SelectedObject;
            if (selected != null && Lettuce.Program.Windows.ContainsKey(selected))
            {
                Form window = Lettuce.Program.Windows[selected];
                window.Show();
                window.BringToFront();
                window.Focus();
            }
            
        }

        private void checkBoxRunning_CheckedChanged(object sender, EventArgs e)
        {
            if (MayUpdateLayout)
                CPU.IsRunning = !CPU.IsRunning;
            ResetLayout();
        }

        private void textBoxRegisterX_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control || e.Alt)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.D1 ||
                e.KeyCode == Keys.D2 ||
                e.KeyCode == Keys.D3 ||
                e.KeyCode == Keys.D4 ||
                e.KeyCode == Keys.D5 ||
                e.KeyCode == Keys.D6 ||
                e.KeyCode == Keys.D7 ||
                e.KeyCode == Keys.D8 ||
                e.KeyCode == Keys.D9 ||
                e.KeyCode == Keys.D0 ||
                e.KeyCode == Keys.NumPad1 ||
                e.KeyCode == Keys.NumPad2 ||
                e.KeyCode == Keys.NumPad3 ||
                e.KeyCode == Keys.NumPad4 ||
                e.KeyCode == Keys.NumPad5 ||
                e.KeyCode == Keys.NumPad6 ||
                e.KeyCode == Keys.NumPad7 ||
                e.KeyCode == Keys.NumPad8 ||
                e.KeyCode == Keys.NumPad9 ||
                e.KeyCode == Keys.NumPad0 ||
                e.KeyCode == Keys.A ||
                e.KeyCode == Keys.B ||
                e.KeyCode == Keys.C ||
                e.KeyCode == Keys.D ||
                e.KeyCode == Keys.E ||
                e.KeyCode == Keys.F ||
                e.KeyCode == Keys.Back ||
                e.KeyCode == Keys.Delete ||
                e.KeyCode == Keys.Left ||
                e.KeyCode == Keys.Right)
            {
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void textBoxRegisterA_TextChanged(object sender, EventArgs e)
        {
            if (textBoxRegisterA.Text.Length != 0)
                CPU.A = ushort.Parse(textBoxRegisterA.Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterB_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.B = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterC_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.C = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterX_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.X = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterY_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.Y = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterZ_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.Z = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterI_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.I = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterJ_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.J = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterPC_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.PC = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            if (!(disassemblyDisplay1.SelectedAddress < CPU.PC && disassemblyDisplay1.EndAddress > CPU.PC) && disassemblyDisplay1.EnableUpdates)
                disassemblyDisplay1.SelectedAddress = CPU.PC;
            rawMemoryDisplay.Invalidate();
            disassemblyDisplay1.Invalidate();
        }

        private void textBoxRegisterEX_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.EX = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        private void textBoxRegisterSP_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.SP = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
            stackDisplay.SelectedAddress = CPU.SP;
            stackDisplay.Invalidate();
        }

        private void textBoxRegisterIA_TextChanged(object sender, EventArgs e)
        {
            if ((sender as TextBox).Text.Length != 0)
                CPU.IA = ushort.Parse((sender as TextBox).Text, NumberStyles.HexNumber);
            rawMemoryDisplay.Invalidate();
        }

        ushort stepOverAddress = 0;
        bool stepOverEnabled = false;
        private void buttonStepOver_Click(object sender, EventArgs e)
        {
            // Set a breakpoint ahead of PC
            ushort length = CPU.InstructionLength(CPU.PC);
            CPU.Breakpoints.Add(new Breakpoint() { Address = (ushort)(CPU.PC + length) });
            stepOverAddress = (ushort)(CPU.PC + length);
            stepOverEnabled = true;
            disassemblyDisplay1.EnableUpdates = false;
            checkBoxRunning.Checked = !checkBoxRunning.Checked; // Run the CPU
        }

        private void buttonStepInto_Click(object sender, EventArgs e)
        {
            CPU.Execute(-1);
            ResetLayout();
        }

        private void FixKeyConfig()
        {
            // TODO: add Mac-Keys
            if (!Program.Configuration.Keybindings.ContainsKey(FUNC_STEP_INTO))
                Program.Configuration.Keybindings.Add(FUNC_STEP_INTO, new Tuple<Keys, Keys>(Keys.F6, Keys.None));
            if (!Program.Configuration.Keybindings.ContainsKey(FUNC_STEP_OVER))
                Program.Configuration.Keybindings.Add(FUNC_STEP_OVER, new Tuple<Keys, Keys>(Keys.F7, Keys.None));
            if (!Program.Configuration.Keybindings.ContainsKey(FUNC_CHANGE_RUNNING))
                Program.Configuration.Keybindings.Add(FUNC_CHANGE_RUNNING, new Tuple<Keys, Keys>(Keys.F5, Keys.None));
            if (!Program.Configuration.Keybindings.ContainsKey(FUNC_GOTO_ADDRESS))
                Program.Configuration.Keybindings.Add(FUNC_GOTO_ADDRESS, new Tuple<Keys, Keys>(Keys.G, Keys.Control));
        }

        private void Debugger_KeyDown(object sender, KeyEventArgs e)
        {
            // CMD+W should only close the current window,
            // but the emulator-window is the one and only window, so I don't care.
            if (RuntimeInfo.IsMacOSX && (e.KeyCode == Keys.W || e.KeyCode == Keys.Q) && e.Modifiers == Keys.Alt)
                this.Close();

            foreach (var keybinding in Program.Configuration.Keybindings)
            {
                if (e.KeyCode == keybinding.Value.Item1 && e.Modifiers == keybinding.Value.Item2)
                {
                    switch (keybinding.Key)
                    {
                        case FUNC_STEP_INTO:
                            buttonStepInto_Click(sender, e);
                            break;
                        case FUNC_STEP_OVER:
                            buttonStepOver_Click(sender, e);
                            break;
                        case FUNC_CHANGE_RUNNING:
                            checkBoxRunning.Checked = !checkBoxRunning.Checked;
                            break;
                        case FUNC_GOTO_ADDRESS:
                            gotoAddressToolStripMenuItem_Click(sender, e);
                            break;
                    }
                }
            }
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CPU.Reset();
            ResetLayout();
        }

        private void gotoAddressToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rawMemoryDisplay.gotoAddressToolStripMenuItem_Click(sender, e);
        }

        private void resetToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            CPU.Memory = new ushort[0x10000];
            ResetLayout();
        }

        private void checkBoxOnFire_CheckedChanged(object sender, EventArgs e)
        {
            CPU.IsOnFire = checkBoxOnFire.Checked;
        }

        private void buttonEditDevice_Click(object sender, EventArgs e)
        {
            if (listBoxConnectedDevices.SelectedIndex == -1)
                return;
            CPU.IsRunning = false;
            ResetLayout();
        }

        public static Dictionary<ushort, string> KnownLabels;
        public static Dictionary<ushort, string> KnownCode;

        public static void LoadOrganicListing(string file)
        {
            if (KnownCode == null)
                KnownCode = new Dictionary<ushort, string>();
            if (KnownLabels == null)
                KnownLabels = new Dictionary<ushort, string>();
            StreamReader reader = new StreamReader(file);
            string listing = reader.ReadToEnd();
            Program.lastlistingFilepath = file;
            reader.Close();
            string[] lines = listing.Replace("\r", "").Split('\n');
            foreach (var _line in lines)
            {
                string line = _line;
                if (line.Trim().Length == 0)
                    continue;
                line = line.Substring(line.IndexOf(')')).Trim();
                line = line.Substring(line.IndexOf(' ')).Trim();
                string addressText = line.Remove(line.IndexOf(']'));
                addressText = addressText.Substring(line.IndexOf('[') + 3).Trim();
                ushort address = 0;
                if (addressText != "NOLIST")
                    address = ushort.Parse(addressText, NumberStyles.HexNumber);
                if (line.Substring(line.IndexOf(" ")).Trim().StartsWith("ERROR"))
                    continue;
                if (line.Substring(line.IndexOf(" ")).Trim().StartsWith("WARNING"))
                    continue;
                line = line.Substring(line.IndexOf("  ")).Trim();
                if (line.SafeContains(':'))
                {
                    if (line.Contains(' '))
                        line = line.Remove(line.IndexOf(" ")).Trim();
                    line = line.Replace(":", "");
                    if (!KnownLabels.ContainsKey(address))
                        KnownLabels.Add(address, line);
                }
                else
                {
                    if (!_line.Contains("                      ") && !line.ToLower().StartsWith(".")) // .dat directive stuff
                    {
                        if (!KnownCode.ContainsKey(address))
                            KnownCode.Add(address, line);
                    }
                }
            }
        }

        private void defineValueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DefineValueForm dvf = new DefineValueForm();
            var res = dvf.ShowDialog();
            if (res == DialogResult.OK && !KnownLabels.ContainsKey(dvf.Value))
                KnownLabels.Add(dvf.Value, dvf.Name);
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            foreach (var item in speedToolStripMenuItem.DropDownItems)
                (item as ToolStripMenuItem).Checked = false;
            (sender as ToolStripMenuItem).Checked = true;
            CPU.ClockSpeed = 50000;
        }

        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            foreach (var item in speedToolStripMenuItem.DropDownItems)
                (item as ToolStripMenuItem).Checked = false;
            (sender as ToolStripMenuItem).Checked = true;
            CPU.ClockSpeed = 100000;
        }

        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            foreach (var item in speedToolStripMenuItem.DropDownItems)
                (item as ToolStripMenuItem).Checked = false;
            (sender as ToolStripMenuItem).Checked = true;
            CPU.ClockSpeed = 200000;
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            foreach (var item in speedToolStripMenuItem.DropDownItems)
                (item as ToolStripMenuItem).Checked = false;
            (sender as ToolStripMenuItem).Checked = true;
            CPU.ClockSpeed = 1000000;
        }

        private void customToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (var item in speedToolStripMenuItem.DropDownItems)
                (item as ToolStripMenuItem).Checked = false;
            (sender as ToolStripMenuItem).Checked = true;
            var csf = new ClockSpeedForm();
            csf.Value = CPU.ClockSpeed;
            var result = csf.ShowDialog();
            if(result == DialogResult.OK)
                CPU.ClockSpeed = csf.Value;
        }

        private void propertyGrid1_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            CPU.IsRunning = false;
            ResetLayout();
        }

        private void checkBoxInterruptQueue_CheckedChanged(object sender, EventArgs e)
        {
            CPU.InterruptQueueEnabled = checkBoxInterruptQueue.Checked;
            ResetLayout();
        }
        
        private void loadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string binFile = null;
            bool littleEndian = false;
            var mc = new MemoryConfiguration();
            if (mc.ShowDialog() == DialogResult.OK)
            {
                binFile = mc.FileName;
                littleEndian = mc.LittleEndian;
            }
            if (!string.IsNullOrEmpty(binFile))
            {
                // Load binary file
                List<ushort> data = new List<ushort>();
                using (Stream stream = File.OpenRead(binFile))
                {
                    for (int i = 0; i < stream.Length; i += 2)
                    {
                        byte a = (byte)stream.ReadByte();
                        byte b = (byte)stream.ReadByte();
                        if (littleEndian)
                            data.Add((ushort)(a | (b << 8)));
                        else
                            data.Add((ushort)(b | (a << 8)));
                    }
                }
                Program.CPU.FlashMemory(data.ToArray());
            }
        }

        private void reloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CPU.Reset();
            CPU.Memory = new ushort[0x10000];

            // Load binary file
            List<ushort> data = new List<ushort>();
            if (!string.IsNullOrEmpty(Lettuce.Program.lastbinFilepath))
            {
                using (Stream stream = File.OpenRead(Lettuce.Program.lastbinFilepath))
                {
                    for (int i = 0; i < stream.Length; i += 2)
                    {
                        byte a = (byte)stream.ReadByte();
                        byte b = (byte)stream.ReadByte();
                        if (Lettuce.Program.lastlittleEndian)
                            data.Add((ushort)(a | (b << 8)));
                        else
                            data.Add((ushort)(b | (a << 8)));
                    }
                }
            }
            Lettuce.Program.CPU.FlashMemory(data.ToArray());
            Clearlisting();
            if (!string.IsNullOrEmpty(Program.lastlistingFilepath))
                LoadOrganicListing(Program.lastlistingFilepath);
            ResetLayout();
        }

        private void Clearlisting()
        {
            KnownCode = new Dictionary<ushort,string>();
            KnownLabels = new Dictionary<ushort,string>();
        }

        private void Debugger_Resize(object sender, EventArgs e)
        {
            Invalidate(true);
        }

        private void breakOnInvalidInstructionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            breakOnInvalidInstructionToolStripMenuItem.Checked = !breakOnInvalidInstructionToolStripMenuItem.Checked;
        }

        private void keyboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var keyboardForm = new KeyboardConfiguration();
            keyboardForm.ShowDialog(this);
        }

        private void addWatchButton_Click(object sender, EventArgs e)
        {
            Watches.Add(watchTextBox.Text);
            watchTextBox.Text = string.Empty;
            ResetLayout();
        }

        private void watchTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                addWatchButton_Click(sender, e);
        }

        private void watchesContextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            if (watchesListView.SelectedIndices.Count == 0)
                e.Cancel = true;
        }

        private void removeWatchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (watchesListView.SelectedIndices.Count != 0)
                Watches.RemoveAt(watchesListView.SelectedIndices[0]);
            ResetLayout();
        }

        private void loadListingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Load organic listing
            var ofd = new OpenFileDialog();
            ofd.Filter = "Listing files (*.lst)|*.lst|Text files (*.txt)|*.txt|All files (*.*)|*.*";
            ofd.FileName = "";
            if (ofd.ShowDialog() != DialogResult.OK)
                return;
            LoadOrganicListing(ofd.FileName);
            Program.lastlistingFilepath = ofd.FileName;
            ResetLayout();
        }

        private void checkBoxBreakOnInterrupt_CheckedChanged(object sender, EventArgs e)
        {
            InterruptBreakDevices[listBoxConnectedDevices.SelectedIndex] = checkBoxBreakOnInterrupt.Checked;
        }
    }
}