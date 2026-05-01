using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using SolidWorksAddinStudy.Services;
using cad_tools;

namespace SolidWorksAddinStudy
{
    [ComVisible(true)]
    [Guid("F95417AA-3B2E-42E0-8B0B-2A2A50E9FA0A")]
    [ProgId("SolidWorksAddinStudy.WorkProjectTaskPaneControl")]
    public class WorkProjectTaskPaneControl : UserControl
    {
        private readonly ListBox projectListBox;
        private readonly TextBox projectNameTextBox;
        private readonly Button addProjectButton;
        private readonly Button removeProjectButton;

        private readonly TextBox rollerOutputTextBox;
        private readonly TextBox rollerFollowUpTextBox;
        private readonly TextBox pipeOutputTextBox;
        private readonly TextBox pipeFollowUpTextBox;
        private readonly TextBox sheetMetalOutputTextBox;
        private readonly TextBox sheetMetalFollowUpTextBox;
        private readonly TextBox machiningOutputTextBox;
        private readonly TextBox machiningFollowUpTextBox;
        private readonly TextBox purchaseOutputTextBox;
        private readonly TextBox purchaseFollowUpTextBox;
        private readonly TextBox bearingOutputTextBox;
        private readonly TextBox bearingFollowUpTextBox;
        private readonly TextBox timingBeltOutputTextBox;
        private readonly TextBox timingBeltFollowUpTextBox;
        private readonly TextBox cylinderSelectionOutputTextBox;
        private readonly TextBox cylinderSelectionFollowUpTextBox;
        private readonly TextBox countersinkMarkingOutputTextBox;
        private readonly TextBox countersinkMarkingFollowUpTextBox;
        private readonly TextBox folderPathTextBox;
        private readonly Button chooseFolderButton;
        private readonly Button openFolderButton;
        private readonly Button toggleAddressModeButton;

        private readonly Label statusLabel;

        private readonly List<WorkProjectItem> projects = new List<WorkProjectItem>();
        private bool isLoadingProject;
        private bool isReadAddressMode;

        private readonly string saveFilePath;

        public WorkProjectTaskPaneControl()
        {
            saveFilePath = BuildSavePath();

            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new System.Drawing.Size(320, 240);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                Padding = new Padding(8)
            };

            var projectNameLabel = new Label
            {
                Text = "项目名:",
                AutoSize = true,
                Left = 4,
                Top = 12
            };

            projectNameTextBox = new TextBox
            {
                Left = 62,
                Top = 8,
                Width = 170
            };
            projectNameTextBox.KeyDown += ProjectNameTextBox_KeyDown;

            addProjectButton = new Button
            {
                Text = "新建项目",
                Left = 240,
                Top = 6,
                Width = 82,
                Height = 28
            };
            addProjectButton.Click += AddProjectButton_Click;

            removeProjectButton = new Button
            {
                Text = "删除项目",
                Left = 330,
                Top = 6,
                Width = 82,
                Height = 28
            };
            removeProjectButton.Click += RemoveProjectButton_Click;

            statusLabel = new Label
            {
                Text = "工作项目列表",
                AutoSize = true,
                Left = 4,
                Top = 42
            };

            header.Controls.Add(projectNameLabel);
            header.Controls.Add(projectNameTextBox);
            header.Controls.Add(addProjectButton);
            header.Controls.Add(removeProjectButton);
            header.Controls.Add(statusLabel);

            var body = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 150,
                FixedPanel = FixedPanel.Panel1
            };

            projectListBox = new ListBox
            {
                Dock = DockStyle.Fill
            };
            projectListBox.SelectedIndexChanged += ProjectListBox_SelectedIndexChanged;
            body.Panel1.Controls.Add(projectListBox);

            var detailsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(8)
            };
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var folderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                Padding = new Padding(8, 8, 8, 0)
            };

            var folderLabel = new Label
            {
                Text = "项目装配体:",
                AutoSize = true,
                Left = 0,
                Top = 8
            };
            folderPanel.Controls.Add(folderLabel);

            folderPathTextBox = new TextBox
            {
                Left = 0,
                Top = 28,
                Width = 185
            };
            folderPathTextBox.TextChanged += FolderPathTextBox_TextChanged;
            folderPanel.Controls.Add(folderPathTextBox);

            chooseFolderButton = new Button
            {
                Text = "选择装配体文件",
                Left = 192,
                Top = 26,
                Width = 110,
                Height = 26
            };
            chooseFolderButton.Click += ChooseFolderButton_Click;
            folderPanel.Controls.Add(chooseFolderButton);

            openFolderButton = new Button
            {
                Text = "打开位置",
                Left = 306,
                Top = 26,
                Width = 72,
                Height = 26
            };
            openFolderButton.Click += OpenFolderButton_Click;
            folderPanel.Controls.Add(openFolderButton);

            toggleAddressModeButton = new Button
            {
                Text = "模式: 写入",
                Left = 382,
                Top = 26,
                Width = 82,
                Height = 26
            };
            toggleAddressModeButton.Click += ToggleAddressModeButton_Click;
            folderPanel.Controls.Add(toggleAddressModeButton);

            rollerOutputTextBox = CreatePathTextBox();
            rollerFollowUpTextBox = CreateDetailTextBox();
            pipeOutputTextBox = CreatePathTextBox();
            pipeFollowUpTextBox = CreateDetailTextBox();
            sheetMetalOutputTextBox = CreatePathTextBox();
            sheetMetalFollowUpTextBox = CreateDetailTextBox();
            machiningOutputTextBox = CreatePathTextBox();
            machiningFollowUpTextBox = CreateDetailTextBox();
            purchaseOutputTextBox = CreatePathTextBox();
            purchaseFollowUpTextBox = CreateDetailTextBox();
            bearingOutputTextBox = CreatePathTextBox();
            bearingFollowUpTextBox = CreateDetailTextBox();
            timingBeltOutputTextBox = CreatePathTextBox();
            timingBeltFollowUpTextBox = CreateDetailTextBox();
            cylinderSelectionOutputTextBox = CreatePathTextBox();
            cylinderSelectionFollowUpTextBox = CreateDetailTextBox();
            countersinkMarkingOutputTextBox = CreatePathTextBox();
            countersinkMarkingFollowUpTextBox = CreateDetailTextBox();

            AddDetailCategoryBlock(detailsPanel, 0, "滚筒出图", rollerOutputTextBox, rollerFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 1, "管件出图", pipeOutputTextBox, pipeFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 2, "钣金出图", sheetMetalOutputTextBox, sheetMetalFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 3, "机加出图", machiningOutputTextBox, machiningFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 4, "外购采购", purchaseOutputTextBox, purchaseFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 5, "轴承采购", bearingOutputTextBox, bearingFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 6, "同步带采购", timingBeltOutputTextBox, timingBeltFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 7, "气缸选型", cylinderSelectionOutputTextBox, cylinderSelectionFollowUpTextBox);
            AddDetailCategoryBlock(detailsPanel, 8, "打标沉孔", countersinkMarkingOutputTextBox, countersinkMarkingFollowUpTextBox);

            var detailsScrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            detailsScrollPanel.Controls.Add(detailsPanel);
            // 挂到父容器后再定行高，否则部分宿主下 RowStyles 尚未与 RowCount 对齐，赋值会抛异常导致任务窗格整页创建失败。
            ApplyFixedDetailRowHeights(detailsPanel);
            detailsScrollPanel.Resize += (sender, args) =>
            {
                int w = detailsScrollPanel.ClientSize.Width;
                if (w > 0)
                {
                    detailsPanel.Width = w;
                }

                RefreshDetailsScrollRange(detailsScrollPanel, detailsPanel);
            };
            detailsPanel.SizeChanged += (sender, args) => RefreshDetailsScrollRange(detailsScrollPanel, detailsPanel);

            body.Panel2.Controls.Add(detailsScrollPanel);
            body.Panel2.Controls.Add(folderPanel);

            Controls.Add(body);
            Controls.Add(header);

            HookDetailTextChangedEvents();
            HookOutputPathOpenEvents();
            UpdateAddressModeUi();
            LoadProjectsFromLocal();
            RefreshProjectList();
            UpdateDetailAreaEnabledState();
            RefreshDetailsScrollRange(detailsScrollPanel, detailsPanel);
            WorkProjectContext.Changed += OnSharedProjectContextChanged;

        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                WorkProjectContext.Changed -= OnSharedProjectContextChanged;
            }
            base.Dispose(disposing);
        }

        private void OnSharedProjectContextChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnSharedProjectContextChanged));
                return;
            }
            SyncCurrentProjectAssemblyToContext();
        }

        private static string BuildSavePath()
        {
            string dir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksAddinStudy");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return Path.Combine(dir, "work_projects.xml");
        }

        /// <summary>
        /// 为每个分类块设置固定行高（标题 + 出图 + 跟进纵向排列）。
        /// 勿在添加子控件时用 RowStyles.Add 扩容：在 WinForms 中会增大 RowCount，与行号错位。
        /// 在 SolidWorks 等宿主里，RowCount 设好后 RowStyles 可能尚未与行数同步，直接 RowStyles[i] 会抛 ArgumentOutOfRangeException，导致整个任务窗格创建失败。
        /// </summary>
        private static void ApplyFixedDetailRowHeights(TableLayoutPanel panel)
        {
            const float categoryBlockHeight = 188F;
            int n = panel.RowCount;
            if (n <= 0)
            {
                return;
            }

            try
            {
                panel.SuspendLayout();
                if (panel.RowStyles.Count < n)
                {
                    panel.PerformLayout();
                }

                if (panel.RowStyles.Count < n)
                {
                    Debug.WriteLine(
                        $"WorkProjectTaskPane: RowStyles.Count={panel.RowStyles.Count} 小于 RowCount={n}，跳过固定行高（窗格仍可显示）。");
                    return;
                }

                for (int i = 0; i < n; i++)
                {
                    panel.RowStyles[i] = new RowStyle(SizeType.Absolute, categoryBlockHeight);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyFixedDetailRowHeights 失败: {ex.Message}");
            }
            finally
            {
                try
                {
                    panel.ResumeLayout(performLayout: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ApplyFixedDetailRowHeights ResumeLayout: {ex.Message}");
                }
            }
        }

        private static void AddDetailCategoryBlock(
            TableLayoutPanel outer,
            int rowIndex0Based,
            string title,
            TextBox outputTextBox,
            TextBox followUpTextBox)
        {
            var block = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 6)
            };
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 8.25f, System.Drawing.FontStyle.Bold)
            };
            block.Controls.Add(titleLabel, 0, 0);
            block.SetColumnSpan(titleLabel, 2);

            var outputCaption = new Label
            {
                Text = "地址",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 4, 4, 0)
            };
            block.Controls.Add(outputCaption, 0, 1);
            outputTextBox.Dock = DockStyle.Fill;
            outputTextBox.Margin = new Padding(0, 2, 0, 2);
            block.Controls.Add(outputTextBox, 1, 1);

            var followCaption = new Label
            {
                Text = "跟进",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 4, 4, 0)
            };
            block.Controls.Add(followCaption, 0, 2);
            followUpTextBox.Dock = DockStyle.Fill;
            followUpTextBox.Margin = new Padding(0, 2, 0, 2);
            block.Controls.Add(followUpTextBox, 1, 2);

            outer.Controls.Add(block, 0, rowIndex0Based);
        }

        private static TextBox CreateDetailTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
        }

        private static TextBox CreatePathTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = false
            };
        }

        private static void RefreshDetailsScrollRange(Panel scrollPanel, TableLayoutPanel detailsPanel)
        {
            int contentHeight = detailsPanel.PreferredSize.Height + 8;
            scrollPanel.AutoScrollMinSize = new System.Drawing.Size(0, contentHeight);
        }

        private void HookDetailTextChangedEvents()
        {
            rollerOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            rollerFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            pipeOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            pipeFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            sheetMetalOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            sheetMetalFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            machiningOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            machiningFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            purchaseOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            purchaseFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            bearingOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            bearingFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            timingBeltOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            timingBeltFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            cylinderSelectionOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            cylinderSelectionFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
            countersinkMarkingOutputTextBox.TextChanged += DetailTextBox_TextChanged;
            countersinkMarkingFollowUpTextBox.TextChanged += DetailTextBox_TextChanged;
        }

        private void HookOutputPathOpenEvents()
        {
            HookPathOpenEvent(rollerOutputTextBox);
            HookPathOpenEvent(pipeOutputTextBox);
            HookPathOpenEvent(sheetMetalOutputTextBox);
            HookPathOpenEvent(machiningOutputTextBox);
            HookPathOpenEvent(purchaseOutputTextBox);
            HookPathOpenEvent(bearingOutputTextBox);
            HookPathOpenEvent(timingBeltOutputTextBox);
            HookPathOpenEvent(cylinderSelectionOutputTextBox);
            HookPathOpenEvent(countersinkMarkingOutputTextBox);
        }

        private void HookPathOpenEvent(TextBox textBox)
        {
            textBox.Click += OutputPathTextBox_Click;
            textBox.DoubleClick += OutputPathTextBox_DoubleClick;
            textBox.KeyDown += OutputPathTextBox_KeyDown;
        }

        private void OutputPathTextBox_Click(object sender, EventArgs e)
        {
            ExecuteAddressAction(sender as TextBox);
        }

        private void OutputPathTextBox_DoubleClick(object sender, EventArgs e)
        {
            ExecuteAddressAction(sender as TextBox);
        }

        private void OutputPathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            ExecuteAddressAction(sender as TextBox);
        }

        private void ExecuteAddressAction(TextBox textBox)
        {
            if (isReadAddressMode)
            {
                OpenPathFromTextBox(textBox);
                return;
            }

            SelectPathForTextBox(textBox);
        }

        private void ToggleAddressModeButton_Click(object sender, EventArgs e)
        {
            isReadAddressMode = !isReadAddressMode;
            UpdateAddressModeUi();
        }

        private void UpdateAddressModeUi()
        {
            toggleAddressModeButton.Text = isReadAddressMode ? "模式: 读取" : "模式: 写入";
            string modeMessage = isReadAddressMode
                ? "地址模式: 读取（点击地址框会打开路径）"
                : "地址模式: 写入（点击地址框会选择并写入路径）";
            statusLabel.Text = modeMessage;
        }

        private void SelectPathForTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            string currentPath = (textBox.Text ?? string.Empty).Trim();
            bool pickFile = ShouldPickFilePath(textBox, currentPath);
            if (pickFile)
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择文件地址";
                    dialog.Filter = "所有文件 (*.*)|*.*";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;

                    if (File.Exists(currentPath))
                    {
                        dialog.FileName = currentPath;
                    }
                    else if (Directory.Exists(currentPath))
                    {
                        dialog.InitialDirectory = currentPath;
                    }
                    else if (!string.IsNullOrWhiteSpace(currentPath))
                    {
                        string? parent = Path.GetDirectoryName(currentPath);
                        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                        {
                            dialog.InitialDirectory = parent;
                        }
                    }

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }

                return;
            }

            string? selectedFolder = FolderPicker.SelectFolder();
            if (!string.IsNullOrWhiteSpace(selectedFolder))
            {
                textBox.Text = selectedFolder;
            }
        }

        private bool ShouldPickFilePath(TextBox textBox, string currentPath)
        {
            if (ReferenceEquals(textBox, sheetMetalOutputTextBox)
                || ReferenceEquals(textBox, machiningOutputTextBox)
                || ReferenceEquals(textBox, pipeOutputTextBox)
                || ReferenceEquals(textBox, countersinkMarkingOutputTextBox))
            {
                return false;
            }

            // 其余地址字段（含项目装配体）统一按文件选择，避免任务类别被误判为文件夹。
            if (!ReferenceEquals(textBox, rollerOutputTextBox)
                && !ReferenceEquals(textBox, purchaseOutputTextBox)
                && !ReferenceEquals(textBox, bearingOutputTextBox)
                && !ReferenceEquals(textBox, timingBeltOutputTextBox)
                && !ReferenceEquals(textBox, cylinderSelectionOutputTextBox)
                && !ReferenceEquals(textBox, folderPathTextBox))
            {
                // 未知文本框保持原有推断逻辑，兼容后续扩展。
                return InferPathTypeByCurrentValue(currentPath, forceFileForAssembly: false);
            }

            return true;
        }

        private bool InferPathTypeByCurrentValue(string currentPath, bool forceFileForAssembly)
        {
            if (File.Exists(currentPath))
            {
                return true;
            }

            if (Directory.Exists(currentPath))
            {
                return false;
            }

            // 项目装配体地址固定是文件路径。
            if (forceFileForAssembly)
            {
                return true;
            }

            // 文本中包含扩展名时按文件处理，例如 *.sldasm / *.txt / *.json。
            string extension = Path.GetExtension(currentPath);
            return !string.IsNullOrWhiteSpace(extension);
        }

        private void OpenPathFromTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            string path = (textBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("请先填写文件或文件夹地址", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OpenPath(path);
        }

        private void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("请先填写文件或文件夹地址", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string normalizedPath = path.Trim();

            try
            {
                if (File.Exists(normalizedPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{normalizedPath}\"");
                    return;
                }

                if (Directory.Exists(normalizedPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = normalizedPath,
                        UseShellExecute = true
                    });
                    return;
                }

                MessageBox.Show("地址不存在，请检查后重试", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开地址失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProjectNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AddProject();
            }
        }

        private void AddProjectButton_Click(object sender, EventArgs e)
        {
            AddProject();
        }

        private void RemoveProjectButton_Click(object sender, EventArgs e)
        {
            RemoveCurrentProject();
        }

        private void ProjectListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadSelectedProjectToEditor();
        }

        private void FolderPathTextBox_TextChanged(object sender, EventArgs e)
        {
            if (isLoadingProject)
            {
                return;
            }

            var current = GetCurrentProject();
            if (current == null)
            {
                return;
            }

            current.ProjectAssemblyPath = folderPathTextBox.Text?.Trim() ?? string.Empty;
            SaveProjectsToLocal();
            SyncCurrentProjectAssemblyToContext();
            statusLabel.Text = $"已保存: {current.ProjectName}";
        }

        private void DetailTextBox_TextChanged(object sender, EventArgs e)
        {
            if (isLoadingProject)
            {
                return;
            }

            var current = GetCurrentProject();
            if (current == null)
            {
                return;
            }

            current.RollerDrawing = rollerOutputTextBox.Text ?? string.Empty;
            current.RollerFollowUp = rollerFollowUpTextBox.Text ?? string.Empty;

            current.PipeDrawing = pipeOutputTextBox.Text ?? string.Empty;
            current.PipeFollowUp = pipeFollowUpTextBox.Text ?? string.Empty;

            current.SheetMetalDrawing = sheetMetalOutputTextBox.Text ?? string.Empty;
            current.SheetMetalFollowUp = sheetMetalFollowUpTextBox.Text ?? string.Empty;

            current.MachiningDrawing = machiningOutputTextBox.Text ?? string.Empty;
            current.MachiningFollowUp = machiningFollowUpTextBox.Text ?? string.Empty;

            current.PurchasedProcurement = purchaseOutputTextBox.Text ?? string.Empty;
            current.PurchasedFollowUp = purchaseFollowUpTextBox.Text ?? string.Empty;

            current.BearingProcurement = bearingOutputTextBox.Text ?? string.Empty;
            current.BearingFollowUp = bearingFollowUpTextBox.Text ?? string.Empty;

            current.TimingBeltProcurement = timingBeltOutputTextBox.Text ?? string.Empty;
            current.TimingBeltFollowUp = timingBeltFollowUpTextBox.Text ?? string.Empty;

            current.CylinderSelection = cylinderSelectionOutputTextBox.Text ?? string.Empty;
            current.CylinderSelectionFollowUp = cylinderSelectionFollowUpTextBox.Text ?? string.Empty;

            current.CountersinkMarkingDrawing = countersinkMarkingOutputTextBox.Text ?? string.Empty;
            current.CountersinkMarkingFollowUp = countersinkMarkingFollowUpTextBox.Text ?? string.Empty;

            SaveProjectsToLocal();
            statusLabel.Text = $"已保存: {current.ProjectName}";
        }

        private void ChooseFolderButton_Click(object sender, EventArgs e)
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择项目装配体";
                dialog.Filter = "SolidWorks 装配体 (*.sldasm)|*.sldasm|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                string currentPath = (folderPathTextBox.Text ?? string.Empty).Trim();
                if (File.Exists(currentPath))
                {
                    dialog.FileName = currentPath;
                }
                else if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    string? parent = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    {
                        dialog.InitialDirectory = parent;
                    }
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    folderPathTextBox.Text = dialog.FileName;
                    statusLabel.Text = $"已选择装配体: {Path.GetFileName(dialog.FileName)}";
                }
            }
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                return;
            }

            string path = (current.ProjectAssemblyPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("请先设置项目装配体", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show("项目装配体不存在，请检查路径", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开位置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteAddressButton_Click(object sender, EventArgs e)
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                MessageBox.Show("请先选中项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            WriteEditorAddressFieldsToProject(current);
            SaveProjectsToLocal();
            SyncCurrentProjectAssemblyToContext();
            statusLabel.Text = $"已写入地址: {current.ProjectName}";
        }

        private void ReadAddressButton_Click(object sender, EventArgs e)
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                MessageBox.Show("请先选中项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadProjectAddressFieldsToEditor(current);
            statusLabel.Text = $"已读取地址: {current.ProjectName}";
        }

        private void WriteEditorAddressFieldsToProject(WorkProjectItem current)
        {
            current.ProjectAssemblyPath = folderPathTextBox.Text?.Trim() ?? string.Empty;
            current.RollerDrawing = rollerOutputTextBox.Text ?? string.Empty;
            current.PipeDrawing = pipeOutputTextBox.Text ?? string.Empty;
            current.SheetMetalDrawing = sheetMetalOutputTextBox.Text ?? string.Empty;
            current.MachiningDrawing = machiningOutputTextBox.Text ?? string.Empty;
            current.PurchasedProcurement = purchaseOutputTextBox.Text ?? string.Empty;
            current.BearingProcurement = bearingOutputTextBox.Text ?? string.Empty;
            current.TimingBeltProcurement = timingBeltOutputTextBox.Text ?? string.Empty;
            current.CylinderSelection = cylinderSelectionOutputTextBox.Text ?? string.Empty;
            current.CountersinkMarkingDrawing = countersinkMarkingOutputTextBox.Text ?? string.Empty;
        }

        private void LoadProjectAddressFieldsToEditor(WorkProjectItem current)
        {
            isLoadingProject = true;
            try
            {
                folderPathTextBox.Text = current.ProjectAssemblyPath ?? string.Empty;
                rollerOutputTextBox.Text = current.RollerDrawing ?? string.Empty;
                pipeOutputTextBox.Text = current.PipeDrawing ?? string.Empty;
                sheetMetalOutputTextBox.Text = current.SheetMetalDrawing ?? string.Empty;
                machiningOutputTextBox.Text = current.MachiningDrawing ?? string.Empty;
                purchaseOutputTextBox.Text = current.PurchasedProcurement ?? string.Empty;
                bearingOutputTextBox.Text = current.BearingProcurement ?? string.Empty;
                timingBeltOutputTextBox.Text = current.TimingBeltProcurement ?? string.Empty;
                cylinderSelectionOutputTextBox.Text = current.CylinderSelection ?? string.Empty;
                countersinkMarkingOutputTextBox.Text = current.CountersinkMarkingDrawing ?? string.Empty;
            }
            finally
            {
                isLoadingProject = false;
            }
        }

        private void SyncCurrentProjectAssemblyToContext()
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                WorkProjectContext.ClearBoundAssembly();
                return;
            }

            string assemblyPath = current.ProjectAssemblyPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                WorkProjectContext.ClearBoundAssembly();
                return;
            }

            WorkProjectContext.SetBoundAssembly(assemblyPath);
        }

        private void AddProject()
        {
            string projectName = (projectNameTextBox.Text ?? string.Empty).Trim();
            AddProjectCore(projectName, clearInputAfterAdd: true, out _);
        }

        private bool AddProjectCore(string projectName, bool clearInputAfterAdd, out string message)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                message = "请输入项目名";
                return false;
            }

            if (projects.Exists(p => string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)))
            {
                message = "该项目已存在，请使用不同名称";
                return false;
            }

            var item = new WorkProjectItem
            {
                ProjectName = projectName
            };
            projects.Add(item);

            SaveProjectsToLocal();
            RefreshProjectList();

            projectListBox.SelectedItem = projectName;
            if (clearInputAfterAdd)
            {
                projectNameTextBox.Clear();
            }
            statusLabel.Text = $"已新建项目: {projectName}";
            message = statusLabel.Text;
            return true;
        }

        public bool TryCreateProjectFromManager(string projectName, out string message)
        {
            if (InvokeRequired)
            {
                object invokeResult = Invoke(new Func<(bool Ok, string Msg)>(() =>
                {
                    bool ok = AddProjectCore((projectName ?? string.Empty).Trim(), clearInputAfterAdd: false, out string msg);
                    return (ok, msg);
                }));

                if (invokeResult is ValueTuple<bool, string> tuple)
                {
                    message = tuple.Item2;
                    return tuple.Item1;
                }

                message = "新建项目失败：线程调度异常";
                return false;
            }

            return AddProjectCore((projectName ?? string.Empty).Trim(), clearInputAfterAdd: false, out message);
        }

        private void RemoveCurrentProject()
        {
            var current = GetCurrentProject();
            if (current == null)
            {
                return;
            }

            var result = MessageBox.Show(
                $"确认删除项目“{current.ProjectName}”？",
                "删除确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            projects.Remove(current);
            SaveProjectsToLocal();
            RefreshProjectList();

            statusLabel.Text = "项目已删除";
        }

        private WorkProjectItem GetCurrentProject()
        {
            if (projectListBox.SelectedIndex < 0 || projectListBox.SelectedIndex >= projects.Count)
            {
                return null;
            }

            return projects[projectListBox.SelectedIndex];
        }

        private void LoadSelectedProjectToEditor()
        {
            isLoadingProject = true;
            try
            {
                var current = GetCurrentProject();
                if (current == null)
                {
                    folderPathTextBox.Text = string.Empty;
                    WorkProjectContext.ClearBoundAssembly();
                    rollerOutputTextBox.Text = string.Empty;
                    rollerFollowUpTextBox.Text = string.Empty;
                    pipeOutputTextBox.Text = string.Empty;
                    pipeFollowUpTextBox.Text = string.Empty;
                    sheetMetalOutputTextBox.Text = string.Empty;
                    sheetMetalFollowUpTextBox.Text = string.Empty;
                    machiningOutputTextBox.Text = string.Empty;
                    machiningFollowUpTextBox.Text = string.Empty;
                    purchaseOutputTextBox.Text = string.Empty;
                    purchaseFollowUpTextBox.Text = string.Empty;
                    bearingOutputTextBox.Text = string.Empty;
                    bearingFollowUpTextBox.Text = string.Empty;
                    timingBeltOutputTextBox.Text = string.Empty;
                    timingBeltFollowUpTextBox.Text = string.Empty;
                    cylinderSelectionOutputTextBox.Text = string.Empty;
                    cylinderSelectionFollowUpTextBox.Text = string.Empty;
                    countersinkMarkingOutputTextBox.Text = string.Empty;
                    countersinkMarkingFollowUpTextBox.Text = string.Empty;
                    statusLabel.Text = "请选择或新建项目";
                    return;
                }

                folderPathTextBox.Text = current.ProjectAssemblyPath ?? string.Empty;
                rollerOutputTextBox.Text = current.RollerDrawing ?? string.Empty;
                rollerFollowUpTextBox.Text = current.RollerFollowUp ?? string.Empty;
                pipeOutputTextBox.Text = current.PipeDrawing ?? string.Empty;
                pipeFollowUpTextBox.Text = current.PipeFollowUp ?? string.Empty;
                sheetMetalOutputTextBox.Text = current.SheetMetalDrawing ?? string.Empty;
                sheetMetalFollowUpTextBox.Text = current.SheetMetalFollowUp ?? string.Empty;
                machiningOutputTextBox.Text = current.MachiningDrawing ?? string.Empty;
                machiningFollowUpTextBox.Text = current.MachiningFollowUp ?? string.Empty;
                purchaseOutputTextBox.Text = current.PurchasedProcurement ?? string.Empty;
                purchaseFollowUpTextBox.Text = current.PurchasedFollowUp ?? string.Empty;
                bearingOutputTextBox.Text = current.BearingProcurement ?? string.Empty;
                bearingFollowUpTextBox.Text = current.BearingFollowUp ?? string.Empty;
                timingBeltOutputTextBox.Text = current.TimingBeltProcurement ?? string.Empty;
                timingBeltFollowUpTextBox.Text = current.TimingBeltFollowUp ?? string.Empty;
                cylinderSelectionOutputTextBox.Text = current.CylinderSelection ?? string.Empty;
                cylinderSelectionFollowUpTextBox.Text = current.CylinderSelectionFollowUp ?? string.Empty;
                countersinkMarkingOutputTextBox.Text = current.CountersinkMarkingDrawing ?? string.Empty;
                countersinkMarkingFollowUpTextBox.Text = current.CountersinkMarkingFollowUp ?? string.Empty;
                statusLabel.Text = $"当前项目: {current.ProjectName}";
                SyncCurrentProjectAssemblyToContext();
            }
            finally
            {
                isLoadingProject = false;
                UpdateDetailAreaEnabledState();
            }
        }

        private void UpdateDetailAreaEnabledState()
        {
            bool enabled = projectListBox.SelectedIndex >= 0;
            folderPathTextBox.Enabled = enabled;
            chooseFolderButton.Enabled = enabled;
            openFolderButton.Enabled = enabled;
            toggleAddressModeButton.Enabled = enabled;
            rollerOutputTextBox.Enabled = enabled;
            rollerFollowUpTextBox.Enabled = enabled;
            pipeOutputTextBox.Enabled = enabled;
            pipeFollowUpTextBox.Enabled = enabled;
            sheetMetalOutputTextBox.Enabled = enabled;
            sheetMetalFollowUpTextBox.Enabled = enabled;
            machiningOutputTextBox.Enabled = enabled;
            machiningFollowUpTextBox.Enabled = enabled;
            purchaseOutputTextBox.Enabled = enabled;
            purchaseFollowUpTextBox.Enabled = enabled;
            bearingOutputTextBox.Enabled = enabled;
            bearingFollowUpTextBox.Enabled = enabled;
            timingBeltOutputTextBox.Enabled = enabled;
            timingBeltFollowUpTextBox.Enabled = enabled;
            cylinderSelectionOutputTextBox.Enabled = enabled;
            cylinderSelectionFollowUpTextBox.Enabled = enabled;
            countersinkMarkingOutputTextBox.Enabled = enabled;
            countersinkMarkingFollowUpTextBox.Enabled = enabled;
            removeProjectButton.Enabled = enabled;
        }

        private void RefreshProjectList()
        {
            int selectedIndex = projectListBox.SelectedIndex;

            projectListBox.BeginUpdate();
            try
            {
                projectListBox.Items.Clear();
                foreach (var project in projects)
                {
                    projectListBox.Items.Add(project.ProjectName);
                }
            }
            finally
            {
                projectListBox.EndUpdate();
            }

            if (projectListBox.Items.Count == 0)
            {
                projectListBox.SelectedIndex = -1;
                UpdateDetailAreaEnabledState();
                statusLabel.Text = "暂无项目，请先新建";
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= projectListBox.Items.Count)
            {
                selectedIndex = 0;
            }

            projectListBox.SelectedIndex = selectedIndex;
        }

        private void SaveProjectsToLocal()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(WorkProjectStore));
                var store = new WorkProjectStore { Projects = projects };

                using (var fs = new FileStream(saveFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(fs, store);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存工作项目失败: {ex.Message}");
            }
        }

        private void LoadProjectsFromLocal()
        {
            try
            {
                if (!File.Exists(saveFilePath))
                {
                    return;
                }

                var serializer = new XmlSerializer(typeof(WorkProjectStore));
                using (var fs = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var store = serializer.Deserialize(fs) as WorkProjectStore;
                    if (store?.Projects != null)
                    {
                        projects.Clear();
                        projects.AddRange(store.Projects);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取工作项目失败: {ex.Message}");
            }
        }

        public void PromptFollowUpRemindersAtStartup()
        {
            // 按需求关闭「跟进计时提醒」功能，保留方法以兼容既有调用方。
        }
    }

    [Serializable]
    public class WorkProjectStore
    {
        public List<WorkProjectItem> Projects { get; set; } = new List<WorkProjectItem>();
    }

    [Serializable]
    public class WorkProjectItem
    {
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectAssemblyPath { get; set; } = string.Empty;
        [XmlElement("FolderPath")]
        public string LegacyFolderPath
        {
            get => ProjectAssemblyPath;
            set
            {
                if (string.IsNullOrWhiteSpace(ProjectAssemblyPath))
                {
                    ProjectAssemblyPath = value ?? string.Empty;
                }
            }
        }
        public string RollerDrawing { get; set; } = string.Empty;
        public DateTime RollerDrawingUpdatedAt { get; set; } = DateTime.MinValue;
        public string RollerFollowUp { get; set; } = string.Empty;
        public string PipeDrawing { get; set; } = string.Empty;
        public DateTime PipeDrawingUpdatedAt { get; set; } = DateTime.MinValue;
        public string PipeFollowUp { get; set; } = string.Empty;
        public string SheetMetalDrawing { get; set; } = string.Empty;
        public DateTime SheetMetalDrawingUpdatedAt { get; set; } = DateTime.MinValue;
        public string SheetMetalFollowUp { get; set; } = string.Empty;
        public string MachiningDrawing { get; set; } = string.Empty;
        public DateTime MachiningDrawingUpdatedAt { get; set; } = DateTime.MinValue;
        public string MachiningFollowUp { get; set; } = string.Empty;
        public string PurchasedProcurement { get; set; } = string.Empty;
        public DateTime PurchasedProcurementUpdatedAt { get; set; } = DateTime.MinValue;
        public string PurchasedFollowUp { get; set; } = string.Empty;
        public string BearingProcurement { get; set; } = string.Empty;
        public DateTime BearingProcurementUpdatedAt { get; set; } = DateTime.MinValue;
        public string BearingFollowUp { get; set; } = string.Empty;
        public string TimingBeltProcurement { get; set; } = string.Empty;
        public DateTime TimingBeltProcurementUpdatedAt { get; set; } = DateTime.MinValue;
        public string TimingBeltFollowUp { get; set; } = string.Empty;
        public string CylinderSelection { get; set; } = string.Empty;
        public DateTime CylinderSelectionUpdatedAt { get; set; } = DateTime.MinValue;
        public string CylinderSelectionFollowUp { get; set; } = string.Empty;
        public string CountersinkMarkingDrawing { get; set; } = string.Empty;
        public DateTime CountersinkMarkingDrawingUpdatedAt { get; set; } = DateTime.MinValue;
        public string CountersinkMarkingFollowUp { get; set; } = string.Empty;
    }
}
