using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace SolidWorksAddinStudy
{
    public class ConsoleOutputForm : Form
    {
        private TextBox outputTextBox;
        private TextBox inputTextBox;
        private Panel inputPanel;
        private TextWriter? originalOut;
        private TaskCompletionSource<string?>? inputTcs;
        
        public ConsoleOutputForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "实时输出";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.Manual;
            
            // 设置窗口位置为屏幕右上角
            Screen primaryScreen = Screen.PrimaryScreen;
            this.Location = new Point(
                primaryScreen.WorkingArea.Right - this.Width,
                primaryScreen.WorkingArea.Top
            );

            outputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                BackColor = Color.White,
                ForeColor = Color.Black
            };

            this.Controls.Add(outputTextBox);
            
            // 创建输入面板
            inputPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Visible = false
            };
            
            // 创建输入框
            inputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Font = new Font("Consolas", 9F),
                AcceptsReturn = true
            };
            
            inputTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    SubmitInput();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    CancelInput();
                }
            };
            
            inputPanel.Controls.Add(inputTextBox);
            this.Controls.Add(inputPanel);
            
            this.FormClosing += (s, e) => {
                StopIntercept();
            };
        }
        
        /// <summary>
        /// 显示输入框并等待用户输入
        /// </summary>
        public async Task<string?> ShowInputDialogAsync(string prompt)
        {
            inputTcs = new TaskCompletionSource<string?>();
            
            // 在 UI 线程上显示输入框
            if (inputPanel.InvokeRequired)
            {
                inputPanel.Invoke(new Action(() => ShowInputDialogInternal(prompt)));
            }
            else
            {
                ShowInputDialogInternal(prompt);
            }
            
            // 等待用户输入
            return await inputTcs.Task;
        }
        
        private void ShowInputDialogInternal(string prompt)
        {
            AppendText($"\n{prompt}");
            inputPanel.Visible = true;
            inputTextBox.Focus();
            inputTextBox.Select();
        }
        
        private void SubmitInput()
        {
            var input = inputTextBox.Text;
            inputPanel.Visible = false;
            inputTextBox.Text = "";
            AppendText(input + "\n");
            inputTcs?.SetResult(input);
        }
        
        private void CancelInput()
        {
            inputPanel.Visible = false;
            inputTextBox.Text = "";
            inputTcs?.SetResult(null);
        }

        public void StartIntercept()
        {
            originalOut = Console.Out;
            Console.SetOut(new ConsoleOutputWriter(this));
        }

        public void StopIntercept()
        {
            if (originalOut != null)
            {
                Console.SetOut(originalOut);
            }
        }

        public void AppendText(string text)
        {
            if (outputTextBox.InvokeRequired)
            {
                outputTextBox.Invoke(new Action<string>(AppendText), text);
                return;
            }

            outputTextBox.AppendText(text);
            outputTextBox.SelectionStart = outputTextBox.Text.Length;
            outputTextBox.ScrollToCaret();
        }

        private class ConsoleOutputWriter : TextWriter
        {
            private ConsoleOutputForm parent;
            public ConsoleOutputWriter(ConsoleOutputForm parent) => this.parent = parent;
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) => parent.AppendText(value.ToString());
            public override void Write(string? value) => parent.AppendText(value ?? "");
            public override void WriteLine(string? value) => parent.AppendText((value ?? "") + Environment.NewLine);
        }
    }
}
