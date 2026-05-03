using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksAddinStudy.Services;
using tools;

namespace SolidWorksAddinStudy
{
    [ComVisible(true)]
    [Guid("B2E8F9C1-4D5A-4E8F-9B1C-2A3D4E5F6078")]
    [ProgId("SolidWorksAddinStudy.EquationModelTaskPaneControl")]
    public class EquationModelTaskPaneControl : UserControl
    {
        private readonly Panel pageHostPanel;
        private readonly Control managerPage;
        private readonly Panel modelDetailPageContainer;
        private Label modelDetailTitleLabel;
        private Button gotoModelDetailPageButton;
        private Label managerStatusLabel;

        private readonly TextBox assemblyPathTextBox;
        private readonly Button bindAssemblyButton;
        private TextBox variantNameTextBox;
        private Button addVariantButton;
        private Button renameVariantButton;
        private Button removeVariantButton;
        private ListBox variantListBox;
        private readonly DataGridView grid;
        private readonly Button captureAsmButton;
        private readonly Button applyButton;
        private readonly Button deleteSelectedButton;
        private readonly Button exportSldprtButton;
        private readonly Label statusLabel;
        private readonly Label persistPathLabel;

        private readonly string saveFilePath;
        private readonly ToolTip persistPathToolTip = new ToolTip();
        private bool isRefreshingVariantList;
        private EquationModelStoreRoot storeRoot = new EquationModelStoreRoot();
        private readonly List<VariantListItem> variantViewItems = new List<VariantListItem>();

        public EquationModelTaskPaneControl()
        {
            saveFilePath = BuildSavePath();

            Dock = DockStyle.Fill;
            MinimumSize = new System.Drawing.Size(380, 420);

            pageHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };

            managerPage = BuildManagerPage();

            var detailBody = new Panel
            {
                Dock = DockStyle.Fill
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                Padding = new Padding(8)
            };

            assemblyPathTextBox = new TextBox
            {
                Left = 4,
                Top = 8,
                Width = 270,
                ReadOnly = true
            };

            bindAssemblyButton = new Button
            {
                Text = "绑定当前装配体",
                Left = 280,
                Top = 6,
                Width = 118,
                Height = 26
            };
            bindAssemblyButton.Click += BindAssemblyButton_Click;

            statusLabel = new Label
            {
                Text = "机型方程式配置",
                Left = 4,
                Top = 40,
                Width = 460,
                AutoSize = false
            };

            persistPathLabel = new Label
            {
                Text = "",
                Left = 4,
                Top = 72,
                Width = 460,
                Height = 16,
                AutoEllipsis = true,
                ForeColor = System.Drawing.Color.DimGray,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 7.5f)
            };

            header.Controls.Add(assemblyPathTextBox);
            header.Controls.Add(bindAssemblyButton);
            header.Controls.Add(statusLabel);
            header.Controls.Add(persistPathLabel);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                MultiSelect = true,
                RowHeadersVisible = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PartDisplayLabel", HeaderText = "零件", FillWeight = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "EquationIndex", HeaderText = "#", FillWeight = 40 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VariableName", HeaderText = "变量", FillWeight = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ValueExpression", HeaderText = "值", FillWeight = 100 });
            grid.SelectionChanged += Grid_SelectionChanged;

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(6)
            };

            captureAsmButton = new Button
            {
                Text = "从装配体采集方程式",
                AutoSize = true
            };
            captureAsmButton.Click += CaptureAsmButton_Click;

            applyButton = new Button
            {
                Text = "一键应用当前机型",
                AutoSize = true
            };
            applyButton.Click += ApplyButton_Click;

            deleteSelectedButton = new Button
            {
                Text = "删除所选记录",
                AutoSize = true
            };
            deleteSelectedButton.Click += DeleteSelectedButton_Click;

            exportSldprtButton = new Button
            {
                Text = "导出选中子装配体为SLDPRT",
                AutoSize = true
            };
            exportSldprtButton.Click += ExportSldprtButton_Click;

            bottom.Controls.Add(captureAsmButton);
            bottom.Controls.Add(applyButton);
            bottom.Controls.Add(deleteSelectedButton);
            bottom.Controls.Add(exportSldprtButton);

            detailBody.Controls.Add(grid);
            detailBody.Controls.Add(bottom);
            detailBody.Controls.Add(header);

            modelDetailPageContainer = BuildChildPageContainer("机型配置详情", detailBody);
            pageHostPanel.Controls.Add(managerPage);
            pageHostPanel.Controls.Add(modelDetailPageContainer);
            Controls.Add(pageHostPanel);

            LoadStore();
            UpdatePersistPathLabel();
            RestoreUiFromStoreAfterLoad();
            MachineProjectContext.Changed += OnSharedMachineBindingChanged;
            ShowManagerPage();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MachineProjectContext.Changed -= OnSharedMachineBindingChanged;
                persistPathToolTip?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void OnSharedMachineBindingChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnSharedMachineBindingChanged));
                return;
            }

            string p = MachineProjectContext.BoundAssemblyFullPath?.Trim() ?? "";
            if (string.IsNullOrEmpty(p))
            {
                statusLabel.Text = "共享机型装配体已清除（本窗格路径未自动清空）";
                return;
            }

            assemblyPathTextBox.Text = p;
            EnsureBundle(p);
            SaveStore();
            RefreshVariantList();
            bool selected = SelectFirstVariantByAssemblyPath(p);
            if (!selected)
            {
                variantListBox.ClearSelected();
                RefreshGrid();
                UpdateModelDetailHeader();
            }
            statusLabel.Text = $"已与其他任务窗格同步共享机型装配体：{Path.GetFileName(p)}";
        }

        private Control BuildManagerPage()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = "机型管理（母窗格）",
                AutoSize = true,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold)
            };

            var inputLabel = new Label
            {
                Text = "机型名：",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray
            };

            variantNameTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                MaxLength = 128
            };
            variantNameTextBox.KeyDown += VariantNameTextBox_KeyDown;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true
            };

            addVariantButton = new Button
            {
                Text = "新建机型",
                AutoSize = true
            };
            addVariantButton.Click += AddVariantButton_Click;

            renameVariantButton = new Button
            {
                Text = "重命名机型",
                AutoSize = true
            };
            renameVariantButton.Click += RenameVariantButton_Click;

            removeVariantButton = new Button
            {
                Text = "删除机型",
                AutoSize = true
            };
            removeVariantButton.Click += RemoveVariantButton_Click;

            gotoModelDetailPageButton = new Button
            {
                Text = "进入机型详情",
                AutoSize = true
            };
            gotoModelDetailPageButton.Click += (sender, e) => ShowModelDetailPage();

            buttonPanel.Controls.Add(addVariantButton);
            buttonPanel.Controls.Add(renameVariantButton);
            buttonPanel.Controls.Add(removeVariantButton);
            buttonPanel.Controls.Add(gotoModelDetailPageButton);

            variantListBox = new ListBox
            {
                Dock = DockStyle.Fill
            };
            variantListBox.SelectedIndexChanged += VariantListBox_SelectedIndexChanged;

            var listLabel = new Label
            {
                Text = "机型列表：",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray
            };

            managerStatusLabel = new Label
            {
                Text = "机型窗格内可切换：母窗格 / 机型配置详情。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray
            };

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(inputLabel, 0, 1);
            root.Controls.Add(variantNameTextBox, 0, 2);
            root.Controls.Add(buttonPanel, 0, 3);
            root.Controls.Add(listLabel, 0, 4);
            root.Controls.Add(variantListBox, 0, 5);
            root.Controls.Add(managerStatusLabel, 0, 6);
            return root;
        }

        private Panel BuildChildPageContainer(string title, Control body)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 8, 8, 4)
            };

            modelDetailTitleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                Left = 0,
                Top = 10,
                Width = 220,
                Height = 20,
                AutoEllipsis = true,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold)
            };

            var backButton = new Button
            {
                Text = "返回机型管理",
                Width = 110,
                Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 260,
                Top = 6
            };
            backButton.Click += (sender, e) => ShowManagerPage();
            header.Resize += (sender, e) =>
            {
                backButton.Left = Math.Max(0, header.ClientSize.Width - backButton.Width - 8);
                modelDetailTitleLabel.Width = Math.Max(80, backButton.Left - 12);
            };

            header.Controls.Add(modelDetailTitleLabel);
            header.Controls.Add(backButton);
            panel.Controls.Add(body);
            panel.Controls.Add(header);
            return panel;
        }

        public void ShowManagerPage()
        {
            managerPage.BringToFront();
            managerStatusLabel.Text = "当前页面：机型管理母窗格。";
        }

        public void ShowModelDetailPage()
        {
            UpdateModelDetailHeader();
            modelDetailPageContainer.BringToFront();
        }

        private void UpdateModelDetailHeader()
        {
            if (modelDetailTitleLabel == null)
            {
                return;
            }

            var selected = GetSelectedVariant();
            string variantName = (selected?.Name ?? string.Empty).Trim();
            modelDetailTitleLabel.Text = string.IsNullOrWhiteSpace(variantName)
                ? "机型配置详情（未选择机型）"
                : $"机型配置详情（机型：{variantName}）";
        }

        private void UpdatePersistPathLabel()
        {
            persistPathLabel.Text = $"本地配置文件: {saveFilePath}";
            persistPathToolTip.SetToolTip(persistPathLabel, saveFilePath);
        }

        /// <summary>
        /// 启动时根据上次保存的装配体路径与机型名称恢复界面（数据已在 LoadStore 读入内存）。
        /// </summary>
        private void RestoreUiFromStoreAfterLoad()
        {
            string fromShared = MachineProjectContext.BoundAssemblyFullPath?.Trim() ?? "";
            string fromXml = storeRoot.LastFocusedAssemblyPath?.Trim() ?? "";
            string lastAsm = !string.IsNullOrEmpty(fromShared) ? fromShared : fromXml;

            if (string.IsNullOrEmpty(lastAsm))
            {
                var fallback0 = FindBestBundleAssemblyPath(null);
                if (!string.IsNullOrEmpty(fallback0))
                {
                    assemblyPathTextBox.Text = fallback0;
                    RefreshVariantList();
                    statusLabel.Text = $"已自动恢复有机型数据的装配体：{Path.GetFileNameWithoutExtension(fallback0)}";
                    MachineProjectContext.SetBoundAssembly(fallback0, notifyListeners: false);
                }
                else
                {
                    statusLabel.Text = storeRoot.Bundles.Count > 0
                        ? $"已载入 {storeRoot.Bundles.Count} 个装配体的机型数据（请点「绑定当前装配体」继续）"
                        : "机型方程式配置";
                }
                return;
            }

            EquationAssemblyBundle bundle = GetBundle(lastAsm);
            if (bundle == null && File.Exists(lastAsm))
            {
                EnsureBundle(lastAsm);
                bundle = GetBundle(lastAsm);
            }

            if (bundle == null)
            {
                string fallbackWhenInvalid = FindBestBundleAssemblyPath(lastAsm);
                if (!string.IsNullOrEmpty(fallbackWhenInvalid))
                {
                    assemblyPathTextBox.Text = fallbackWhenInvalid;
                    RefreshVariantList();
                    statusLabel.Text = $"上次路径失效，已切换到有机型数据的装配体：{Path.GetFileNameWithoutExtension(fallbackWhenInvalid)}";
                    MachineProjectContext.SetBoundAssembly(fallbackWhenInvalid, notifyListeners: false);
                }
                else
                {
                    statusLabel.Text = "上次绑定的装配体路径已失效，请重新绑定";
                }
                return;
            }

            if ((bundle.Variants?.Count ?? 0) == 0)
            {
                string fallbackWhenEmpty = FindBestBundleAssemblyPath(lastAsm);
                if (!string.IsNullOrEmpty(fallbackWhenEmpty) &&
                    !string.Equals(fallbackWhenEmpty, lastAsm, StringComparison.OrdinalIgnoreCase))
                {
                    assemblyPathTextBox.Text = fallbackWhenEmpty;
                    RefreshVariantList();
                    statusLabel.Text = $"已切换到有机型数据的装配体：{Path.GetFileNameWithoutExtension(fallbackWhenEmpty)}";
                    MachineProjectContext.SetBoundAssembly(fallbackWhenEmpty, notifyListeners: false);
                    return;
                }
            }

            assemblyPathTextBox.Text = lastAsm;
            RefreshVariantList();

            string lastVar = storeRoot.LastFocusedVariantName?.Trim() ?? "";
            if (!string.IsNullOrEmpty(lastVar))
            {
                SelectVariantByName(lastVar);
            }

            statusLabel.Text = $"已从本地恢复：{Path.GetFileNameWithoutExtension(lastAsm)}";

            // 旧版仅存在 XML 中的路径时，写入三窗格共享文件（不广播事件，避免其他窗格尚未订阅）
            if (string.IsNullOrEmpty(fromShared) && !string.IsNullOrEmpty(fromXml) && File.Exists(fromXml))
            {
                MachineProjectContext.SetBoundAssembly(fromXml, notifyListeners: false);
            }
        }

        private string FindBestBundleAssemblyPath(string currentAssemblyPath)
        {
            if (storeRoot?.Bundles == null || storeRoot.Bundles.Count == 0)
            {
                return string.Empty;
            }

            string lastVar = storeRoot.LastFocusedVariantName?.Trim() ?? string.Empty;
            string current = currentAssemblyPath?.Trim() ?? string.Empty;

            var preferred = storeRoot.Bundles.FirstOrDefault(b =>
                !string.IsNullOrWhiteSpace(b.AssemblyPath) &&
                !string.Equals(b.AssemblyPath, current, StringComparison.OrdinalIgnoreCase) &&
                (b.Variants?.Any(v => !string.IsNullOrWhiteSpace(v.Name) &&
                    string.Equals(v.Name, lastVar, StringComparison.OrdinalIgnoreCase)) ?? false));
            if (preferred != null)
            {
                return preferred.AssemblyPath;
            }

            var firstNonEmpty = storeRoot.Bundles.FirstOrDefault(b =>
                !string.IsNullOrWhiteSpace(b.AssemblyPath) &&
                !string.Equals(b.AssemblyPath, current, StringComparison.OrdinalIgnoreCase) &&
                (b.Variants?.Count ?? 0) > 0);
            return firstNonEmpty?.AssemblyPath ?? string.Empty;
        }

        /// <summary>
        /// 由修改方程式对话框「上传至机型」调用：写入当前绑定装配体下当前选中机型。
        /// </summary>
        public static string MergeSnapshotsIntoActiveVariant(List<EquationSnapshotEntry> snapshots)
        {
            var pane = AddinStudy.GetEquationModelTaskPaneControl();
            if (pane == null)
            {
                return "机型方程式任务窗格未初始化";
            }

            if (pane.InvokeRequired)
            {
                string result = "";
                pane.Invoke(new Action(() => { result = pane.MergeSnapshotsIntoActiveVariantCore(snapshots); }));
                return result;
            }

            return pane.MergeSnapshotsIntoActiveVariantCore(snapshots);
        }

        private string MergeSnapshotsIntoActiveVariantCore(List<EquationSnapshotEntry> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                return "没有可写入的快照";
            }

            string asmPath = assemblyPathTextBox.Text?.Trim() ?? "";
            var bundle = GetBundle(asmPath);
            var variant = GetSelectedVariant();
            if (string.IsNullOrEmpty(asmPath) || bundle == null || variant == null)
            {
                return "请先在任务窗格绑定装配体并选中机型格子";
            }

            variant.Entries = new List<EquationSnapshotEntry>(snapshots);
            SaveStore();
            RefreshGrid();
            statusLabel.Text = $"已从对话框上传 {snapshots.Count} 条 → {variant.Name}";
            return $"已上传 {snapshots.Count} 条方程式至机型「{variant.Name}」";
        }

        private static string BuildSavePath()
        {
            string dir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksAddinStudy");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return Path.Combine(dir, "equation_model_variants.xml");
        }

        private void VariantNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AddVariant();
            }
        }

        private void AddVariantButton_Click(object sender, EventArgs e)
        {
            AddVariant();
        }

        private void BindAssemblyButton_Click(object sender, EventArgs e)
        {
            try
            {
                var app = AddinStudy.GetSwApp();
                var active = app?.ActiveDoc as ModelDoc2;
                if (active == null || active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    MessageBox.Show("请先激活一个装配体文档。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string path = active.GetPathName() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show("当前装配体尚未保存，请先保存后再绑定。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!File.Exists(path))
                {
                    MessageBox.Show("当前装配体文件不存在，请检查路径后重试。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MachineProjectContext.SetBoundAssembly(path);
                assemblyPathTextBox.Text = path;
                var currentBundle = EnsureBundle(path);
                SaveStore();
                RefreshVariantList();
                bool selected = SelectFirstVariantByAssemblyPath(path);
                if (!selected)
                {
                    variantListBox.ClearSelected();
                    RefreshGrid();
                    UpdateModelDetailHeader();
                }
                int currentVariantCount = currentBundle?.Variants?.Count ?? 0;
                statusLabel.Text = $"已绑定装配体：{Path.GetFileName(path)}（机型数：{currentVariantCount}）";
                managerStatusLabel.Text = statusLabel.Text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"绑定装配体失败: {ex.Message}");
                MessageBox.Show($"绑定失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RenameVariantButton_Click(object sender, EventArgs e)
        {
            RenameVariant();
        }

        private void RenameVariant()
        {
            var selectedItem = GetSelectedVariantItem();
            if (selectedItem == null || selectedItem.Variant == null)
            {
                MessageBox.Show("请先在列表中选择要重命名的机型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string newName = (variantNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("请输入新的机型名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string oldName = selectedItem.Variant.Name?.Trim() ?? string.Empty;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("新旧机型名相同，无需重命名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bundle = GetBundle(selectedItem.AssemblyPath);
            if (bundle == null)
            {
                MessageBox.Show("机型所属装配体不存在，请重新选择。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool hasDuplicate = bundle.Variants.Any(v =>
                !ReferenceEquals(v, selectedItem.Variant) &&
                string.Equals(v.Name?.Trim() ?? string.Empty, newName, StringComparison.OrdinalIgnoreCase));
            if (hasDuplicate)
            {
                MessageBox.Show("该装配体下已存在同名机型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            selectedItem.Variant.Name = newName;
            SaveStore();
            RefreshVariantList();
            SelectVariantByAssemblyAndName(selectedItem.AssemblyPath, newName);
            variantNameTextBox.Clear();
            statusLabel.Text = $"已重命名机型：{oldName} -> {newName}";
            managerStatusLabel.Text = statusLabel.Text;
        }

        private void AddVariant()
        {
            string path = ResolveTargetAssemblyPathForVariantCreate();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("请先绑定装配体。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = (variantNameTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请输入机型名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bundle = EnsureBundle(path);
            if (bundle.Variants.Exists(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("该机型名称已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bundle.Variants.Add(new EquationVariantSlot { Name = name, Entries = new List<EquationSnapshotEntry>() });
            assemblyPathTextBox.Text = path;
            SaveStore();
            variantNameTextBox.Clear();
            RefreshVariantList();
            SelectVariantByAssemblyAndName(path, name);
            statusLabel.Text = $"已新建机型: {name}";
            managerStatusLabel.Text = statusLabel.Text;
        }

        public bool TryCreateVariantFromAi(string variantName, out string message)
        {
            if (InvokeRequired)
            {
                object? invokeResult = Invoke(new Func<(bool Ok, string Msg)>(() =>
                {
                    bool okInner = TryCreateVariantFromAiCore(variantName, out string msgInner);
                    return (okInner, msgInner);
                }));

                if (invokeResult is ValueTuple<bool, string> tuple)
                {
                    message = tuple.Item2;
                    return tuple.Item1;
                }

                message = "创建机型失败：线程调度异常";
                return false;
            }

            return TryCreateVariantFromAiCore(variantName, out message);
        }

        private bool TryCreateVariantFromAiCore(string variantName, out string message)
        {
            string path = assemblyPathTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(path))
            {
                message = "请先在任务窗格绑定装配体";
                return false;
            }

            string name = (variantName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                message = "机型名称不能为空";
                return false;
            }

            var bundle = EnsureBundle(path);
            if (bundle.Variants.Exists(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                message = $"机型已存在: {name}";
                return false;
            }

            bundle.Variants.Add(new EquationVariantSlot { Name = name, Entries = new List<EquationSnapshotEntry>() });
            SaveStore();
            RefreshVariantList();
            SelectVariantByName(name);
            statusLabel.Text = $"已新建机型: {name}";
            managerStatusLabel.Text = statusLabel.Text;
            message = $"已新建机型: {name}";
            return true;
        }

        private void RemoveVariantButton_Click(object sender, EventArgs e)
        {
            if (variantListBox.SelectedIndex < 0 || variantListBox.SelectedIndex >= variantViewItems.Count)
            {
                return;
            }

            var selectedItem = variantViewItems[variantListBox.SelectedIndex];
            var bundle = GetBundle(selectedItem.AssemblyPath);
            var v = selectedItem.Variant;
            if (bundle == null || v == null)
            {
                return;
            }

            if (MessageBox.Show($"删除机型「{v.Name}」？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            bundle.Variants.Remove(v);
            SaveStore();
            RefreshVariantList();
            managerStatusLabel.Text = $"已删除机型: {v.Name}";
        }

        private void VariantListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (variantListBox.SelectedIndex >= 0 && variantListBox.SelectedIndex < variantViewItems.Count)
            {
                var selectedItem = variantViewItems[variantListBox.SelectedIndex];
                if (!string.IsNullOrWhiteSpace(selectedItem.AssemblyPath))
                {
                    assemblyPathTextBox.Text = selectedItem.AssemblyPath;
                }
            }

            RefreshGrid();
            if (!isRefreshingVariantList && variantListBox.SelectedIndex >= 0)
            {
                SaveStore();
            }
            UpdateModelDetailHeader();
        }

        private void CaptureAsmButton_Click(object sender, EventArgs e)
        {
            try
            {
                var app = AddinStudy.GetSwApp();
                if (app == null)
                {
                    return;
                }

                string path = assemblyPathTextBox.Text?.Trim() ?? "";
                var bundle = GetBundle(path);
                var variant = GetSelectedVariant();
                if (bundle == null || variant == null)
                {
                    MessageBox.Show("请先绑定装配体并选择机型格子。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var active = app.ActiveDoc as ModelDoc2;
                if (active == null || active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    MessageBox.Show("请先打开装配体。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!string.Equals(active.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
                {
                    var r = MessageBox.Show(
                        "当前激活装配体与绑定路径不一致，仍要从当前装配体采集吗？",
                        "确认",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (r != DialogResult.Yes)
                    {
                        return;
                    }
                }

                var parts = EquationModifier.CollectDistinctPartDocsFromAssembly(app, active);
                if (parts.Count == 0)
                {
                    MessageBox.Show("未收集到零件（.sldprt）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var snaps = EquationModifier.CollectEquationSnapshots(app, parts);
                variant.Entries = snaps;
                SaveStore();
                RefreshGrid();
                statusLabel.Text = $"已采集 {snaps.Count} 条方程式 → {variant.Name}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                MessageBox.Show($"采集失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public bool TryCaptureAssemblyEquationsFromAi(out string message)
        {
            if (InvokeRequired)
            {
                object? invokeResult = Invoke(new Func<(bool Ok, string Msg)>(() =>
                {
                    bool okInner = TryCaptureAssemblyEquationsFromAiCore(out string msgInner);
                    return (okInner, msgInner);
                }));

                if (invokeResult is ValueTuple<bool, string> tuple)
                {
                    message = tuple.Item2;
                    return tuple.Item1;
                }

                message = "采集失败：线程调度异常";
                return false;
            }

            return TryCaptureAssemblyEquationsFromAiCore(out message);
        }

        private bool TryCaptureAssemblyEquationsFromAiCore(out string message)
        {
            var app = AddinStudy.GetSwApp();
            if (app == null)
            {
                message = "SolidWorks 未初始化";
                return false;
            }

            string path = assemblyPathTextBox.Text?.Trim() ?? "";
            var bundle = GetBundle(path);
            var variant = GetSelectedVariant();
            if (bundle == null || variant == null)
            {
                message = "请先绑定装配体并选择机型格子";
                return false;
            }

            var active = app.ActiveDoc as ModelDoc2;
            if (active == null || active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                message = "请先打开装配体";
                return false;
            }

            if (!string.Equals(active.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
            {
                message = "当前激活装配体与绑定路径不一致，请先绑定/切换到目标装配体";
                return false;
            }

            var parts = EquationModifier.CollectDistinctPartDocsFromAssembly(app, active);
            if (parts.Count == 0)
            {
                message = "未收集到零件（.sldprt）";
                return false;
            }

            var snaps = EquationModifier.CollectEquationSnapshots(app, parts);
            variant.Entries = snaps;
            SaveStore();
            RefreshGrid();
            statusLabel.Text = $"已采集 {snaps.Count} 条方程式 → {variant.Name}";
            message = statusLabel.Text;
            return true;
        }

        public bool TryGetCurrentVariantEquationsFromAi(
            string keyword,
            out List<EquationSnapshotEntry> equations,
            out string message)
        {
            equations = new List<EquationSnapshotEntry>();
            if (InvokeRequired)
            {
                object? invokeResult = Invoke(new Func<(bool Ok, List<EquationSnapshotEntry> List, string Msg)>(() =>
                {
                    bool okInner = TryGetCurrentVariantEquationsFromAiCore(keyword, out List<EquationSnapshotEntry> listInner, out string msgInner);
                    return (okInner, listInner, msgInner);
                }));

                if (invokeResult is ValueTuple<bool, List<EquationSnapshotEntry>, string> tuple)
                {
                    equations = tuple.Item2;
                    message = tuple.Item3;
                    return tuple.Item1;
                }

                message = "读取方程式失败：线程调度异常";
                return false;
            }

            return TryGetCurrentVariantEquationsFromAiCore(keyword, out equations, out message);
        }

        private bool TryGetCurrentVariantEquationsFromAiCore(
            string keyword,
            out List<EquationSnapshotEntry> equations,
            out string message)
        {
            equations = new List<EquationSnapshotEntry>();

            string path = assemblyPathTextBox.Text?.Trim() ?? "";
            var bundle = GetBundle(path);
            var variant = GetSelectedVariant();
            if (bundle == null || variant == null)
            {
                message = "请先绑定装配体并选择机型格子";
                return false;
            }

            var entries = variant.Entries ?? new List<EquationSnapshotEntry>();
            if (entries.Count == 0)
            {
                message = $"当前机型「{variant.Name}」没有方程式记录";
                return false;
            }

            string kw = (keyword ?? "").Trim();
            var filtered = entries.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(kw))
            {
                bool dimKw = string.Equals(kw, "尺寸", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(kw, "尺度", StringComparison.OrdinalIgnoreCase);
                filtered = filtered.Where(e =>
                    (e.PartDisplayLabel ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.VariableName ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.ValueExpression ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (dimKw && DimensionKeywordMatchesVariableName(e.VariableName)));
            }

            equations = filtered
                .OrderBy(e => e.PartDisplayLabel ?? "")
                .ThenBy(e => e.EquationIndex)
                .ToList();

            message = $"当前机型「{variant.Name}」共 {entries.Count} 条方程式，返回 {equations.Count} 条";
            return true;
        }

        /// <summary>
        /// 用户说「尺寸」时常指板长/外形等驱动尺寸，变量名未必含「尺寸」二字；用于关键词检索扩展。
        /// </summary>
        private static bool DimensionKeywordMatchesVariableName(string? variableName)
        {
            string v = (variableName ?? "").Trim();
            if (v.Length == 0)
            {
                return false;
            }

            return Regex.IsMatch(
                v,
                @"(长|宽|高|厚|深|径|距|直径|半径|外长|外宽|外高|内长|内宽|内高|长度|宽度|高度|总长|总宽|总高|外包|外形|外形尺寸|轮廓)");
        }

        public bool TryUpdateCurrentVariantEquationFromAi(
            string variableName,
            string newValue,
            bool applyToOpenModel,
            out string message)
        {
            if (InvokeRequired)
            {
                object? invokeResult = Invoke(new Func<(bool Ok, string Msg)>(() =>
                {
                    bool okInner = TryUpdateCurrentVariantEquationFromAiCore(variableName, newValue, applyToOpenModel, out string msgInner);
                    return (okInner, msgInner);
                }));

                if (invokeResult is ValueTuple<bool, string> tuple)
                {
                    message = tuple.Item2;
                    return tuple.Item1;
                }

                message = "更新方程式失败：线程调度异常";
                return false;
            }

            return TryUpdateCurrentVariantEquationFromAiCore(variableName, newValue, applyToOpenModel, out message);
        }

        private bool TryUpdateCurrentVariantEquationFromAiCore(
            string variableName,
            string newValue,
            bool applyToOpenModel,
            out string message)
        {
            string targetVar = (variableName ?? "").Trim();
            string targetVal = (newValue ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetVar))
            {
                message = "变量名不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetVal))
            {
                message = "变量值不能为空";
                return false;
            }

            string path = assemblyPathTextBox.Text?.Trim() ?? "";
            var bundle = GetBundle(path);
            var variant = GetSelectedVariant();
            if (bundle == null || variant == null)
            {
                message = "请先绑定装配体并选择机型格子";
                return false;
            }

            var entries = variant.Entries ?? new List<EquationSnapshotEntry>();
            if (entries.Count == 0)
            {
                message = $"当前机型「{variant.Name}」没有方程式记录";
                return false;
            }

            var exactHits = entries
                .Where(e => string.Equals((e.VariableName ?? "").Trim(), targetVar, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var hits = exactHits.Count > 0
                ? exactHits
                : entries.Where(e => (e.VariableName ?? "").IndexOf(targetVar, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (hits.Count == 0)
            {
                message = $"未找到变量「{targetVar}」";
                return false;
            }

            foreach (var hit in hits)
            {
                hit.ValueExpression = targetVal;
            }

            SaveStore();
            RefreshGrid();

            string applyMessage = "";
            if (applyToOpenModel)
            {
                var app = AddinStudy.GetSwApp();
                if (app != null)
                {
                    // 仅应用本次命中的变量，避免把机型内其它无关条目失败混入回执。
                    applyMessage = EquationModifier.ApplyEquationSnapshots(app, hits);
                    statusLabel.Text = applyMessage;
                }
                else
                {
                    applyMessage = "SolidWorks 未初始化，未应用到当前打开模型";
                }
            }

            string baseMessage = hits.Count == 1
                ? $"已更新变量「{hits[0].VariableName}」为 {targetVal}"
                : $"已更新 {hits.Count} 条变量（关键词「{targetVar}」）为 {targetVal}";
            message = string.IsNullOrWhiteSpace(applyMessage)
                ? baseMessage
                : $"{baseMessage}；应用结果：{applyMessage}";
            return true;
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            try
            {
                var app = AddinStudy.GetSwApp();
                if (app == null)
                {
                    return;
                }

                var variant = GetSelectedVariant();
                if (variant == null || variant.Entries == null || variant.Entries.Count == 0)
                {
                    MessageBox.Show("当前机型没有记录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string msg = EquationModifier.ApplyEquationSnapshots(app, variant.Entries);
                statusLabel.Text = msg;
                app.SendMsgToUser(msg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                MessageBox.Show($"应用失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private EquationAssemblyBundle EnsureBundle(string assemblyPath)
        {
            var found = storeRoot.Bundles.FirstOrDefault(
                b => string.Equals(b.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }

            found = new EquationAssemblyBundle
            {
                AssemblyPath = assemblyPath,
                Variants = new List<EquationVariantSlot>()
            };
            storeRoot.Bundles.Add(found);
            return found;
        }

        private EquationAssemblyBundle GetBundle(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
            {
                return null;
            }

            return storeRoot.Bundles.FirstOrDefault(
                b => string.Equals(b.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));
        }

        private EquationVariantSlot GetSelectedVariant()
        {
            if (variantListBox.SelectedIndex < 0 || variantListBox.SelectedIndex >= variantViewItems.Count)
            {
                return null;
            }

            return variantViewItems[variantListBox.SelectedIndex].Variant;
        }

        private VariantListItem GetSelectedVariantItem()
        {
            if (variantListBox.SelectedIndex < 0 || variantListBox.SelectedIndex >= variantViewItems.Count)
            {
                return null;
            }

            return variantViewItems[variantListBox.SelectedIndex];
        }

        private bool SelectFirstVariantByAssemblyPath(string assemblyPath)
        {
            string ap = (assemblyPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ap))
            {
                return false;
            }

            int i = variantViewItems.FindIndex(v =>
                string.Equals(v.AssemblyPath?.Trim() ?? string.Empty, ap, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                variantListBox.SelectedIndex = i;
                return true;
            }

            return false;
        }

        private string ResolveTargetAssemblyPathForVariantCreate()
        {
            // 以共享绑定为准，避免仍落到旧的列表选中装配体。
            string path = MachineProjectContext.BoundAssemblyFullPath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = assemblyPathTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var app = AddinStudy.GetSwApp();
            var active = app?.ActiveDoc as ModelDoc2;
            if (active != null && active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                path = active.GetPathName() ?? string.Empty;
            }

            return path.Trim();
        }

        private void RefreshVariantList()
        {
            string keepSelectedKey = null;
            if (variantListBox.SelectedIndex >= 0 && variantListBox.SelectedIndex < variantViewItems.Count)
            {
                keepSelectedKey = variantViewItems[variantListBox.SelectedIndex].UniqueKey;
            }

            isRefreshingVariantList = true;
            variantListBox.BeginUpdate();
            try
            {
                variantListBox.Items.Clear();
                variantViewItems.Clear();
                foreach (var b in storeRoot.Bundles)
                {
                    if (b?.Variants == null)
                    {
                        continue;
                    }

                    foreach (var v in b.Variants)
                    {
                        if (v == null)
                        {
                            continue;
                        }

                        string asmName = Path.GetFileNameWithoutExtension(b.AssemblyPath ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(asmName))
                        {
                            asmName = "未命名装配体";
                        }

                        string vn = string.IsNullOrWhiteSpace(v.Name) ? "未命名机型" : v.Name.Trim();
                        var item = new VariantListItem
                        {
                            AssemblyPath = b.AssemblyPath ?? string.Empty,
                            Variant = v,
                            DisplayText = $"{vn}  [{asmName}]",
                            UniqueKey = $"{(b.AssemblyPath ?? string.Empty).Trim()}|{vn}"
                        };
                        variantViewItems.Add(item);
                        variantListBox.Items.Add(item.DisplayText);
                    }
                }
            }
            finally
            {
                variantListBox.EndUpdate();
                isRefreshingVariantList = false;
            }

            if (variantListBox.Items.Count > 0 && variantListBox.SelectedIndex < 0)
            {
                int restoreIndex = -1;
                if (!string.IsNullOrWhiteSpace(keepSelectedKey))
                {
                    restoreIndex = variantViewItems.FindIndex(i =>
                        string.Equals(i.UniqueKey, keepSelectedKey, StringComparison.OrdinalIgnoreCase));
                }

                variantListBox.SelectedIndex = restoreIndex >= 0 ? restoreIndex : 0;
            }

            RefreshGrid();
            UpdateModelDetailHeader();
        }

        private void SelectVariantByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            int i = variantViewItems.FindIndex(v =>
                string.Equals(v.Variant?.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                variantListBox.SelectedIndex = i;
            }
        }

        private void SelectVariantByAssemblyAndName(string assemblyPath, string variantName)
        {
            string ap = (assemblyPath ?? string.Empty).Trim();
            string vn = (variantName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ap) || string.IsNullOrWhiteSpace(vn))
            {
                return;
            }

            int i = variantViewItems.FindIndex(v =>
                string.Equals(v.AssemblyPath?.Trim() ?? string.Empty, ap, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v.Variant?.Name?.Trim() ?? string.Empty, vn, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                variantListBox.SelectedIndex = i;
            }
        }

        private void RefreshGrid()
        {
            grid.Rows.Clear();
            var variant = GetSelectedVariant();
            if (variant?.Entries == null)
            {
                return;
            }

            foreach (var entry in variant.Entries.OrderBy(e => e.PartDisplayLabel).ThenBy(e => e.EquationIndex))
            {
                int row = grid.Rows.Add();
                grid.Rows[row].Tag = entry;
                grid.Rows[row].Cells[0].Value = entry.PartDisplayLabel;
                grid.Rows[row].Cells[1].Value = entry.EquationIndex + 1;
                grid.Rows[row].Cells[2].Value = entry.VariableName;
                grid.Rows[row].Cells[3].Value = entry.ValueExpression;
            }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count <= 0)
            {
                return;
            }

            var firstRow = grid.SelectedRows[0];
            if (!(firstRow.Tag is EquationSnapshotEntry entry))
            {
                return;
            }

            try
            {
                SelectPartByStorageKey(entry.PartStorageKey, entry.PartDisplayLabel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"联动选中零件失败: {ex.Message}");
            }
        }

        private void SelectPartByStorageKey(string partStorageKey, string partDisplayLabel)
        {
            if (string.IsNullOrWhiteSpace(partStorageKey))
            {
                return;
            }

            var app = AddinStudy.GetSwApp();
            var active = app?.ActiveDoc as ModelDoc2;
            if (app == null || active == null)
            {
                return;
            }

            if (active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                var assy = active as AssemblyDoc;
                object[] components = assy == null ? null : (object[])assy.GetComponents(false);
                if (components == null)
                {
                    return;
                }

                foreach (object obj in components)
                {
                    var comp = obj as Component2;
                    if (comp == null)
                    {
                        continue;
                    }

                    string compPath = comp.GetPathName() ?? string.Empty;
                    if (!string.Equals(compPath, partStorageKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    active.ClearSelection2(true);
                    comp.Select(false);
                    active.ViewZoomtofit2();
                    statusLabel.Text = $"已定位零件: {partDisplayLabel}";
                    return;
                }

                // 路径未命中时，按零件名称兜底（复用零件任务窗格同款归一化规则）
                string nameFromPath = Path.GetFileNameWithoutExtension(partStorageKey) ?? string.Empty;
                string nameFromLabel = ExtractCandidatePartName(partDisplayLabel);
                foreach (object obj in components)
                {
                    var comp = obj as Component2;
                    if (comp == null)
                    {
                        continue;
                    }

                    string normalizedCompName = NormalizeComponentName(comp.Name2 ?? string.Empty);
                    if (string.IsNullOrEmpty(normalizedCompName))
                    {
                        continue;
                    }

                    bool hitByPathName = !string.IsNullOrEmpty(nameFromPath) &&
                        normalizedCompName.Equals(nameFromPath, StringComparison.OrdinalIgnoreCase);
                    bool hitByLabelName = !string.IsNullOrEmpty(nameFromLabel) &&
                        normalizedCompName.Equals(nameFromLabel, StringComparison.OrdinalIgnoreCase);
                    if (!hitByPathName && !hitByLabelName)
                    {
                        continue;
                    }

                    active.ClearSelection2(true);
                    comp.Select(false);
                    active.ViewZoomtofit2();
                    statusLabel.Text = $"已按名称定位零件: {partDisplayLabel}";
                    return;
                }
            }

            // 非装配体或装配体中未命中时，尝试直接激活零件文档
            var resolvedDoc = ResolvePartDocByPath(app, partStorageKey);
            if (resolvedDoc == null)
            {
                return;
            }

            int errors = 0;
            string title = resolvedDoc.GetTitle();
            if (!string.IsNullOrWhiteSpace(title))
            {
                app.ActivateDoc3(
                    title,
                    true,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref errors);
            }
            statusLabel.Text = $"已激活零件: {partDisplayLabel}";
        }

        private static string ExtractCandidatePartName(string partDisplayLabel)
        {
            if (string.IsNullOrWhiteSpace(partDisplayLabel))
            {
                return string.Empty;
            }

            // 兼容 "文件名.SLDPRT  |  路径" 的显示格式
            string label = partDisplayLabel;
            int sep = label.IndexOf('|');
            if (sep > 0)
            {
                label = label.Substring(0, sep).Trim();
            }

            label = Path.GetFileNameWithoutExtension(label) ?? label;
            return label.Trim();
        }

        private static string NormalizeComponentName(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return string.Empty;
            }

            int slashIndex = componentName.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
            {
                componentName = componentName.Substring(slashIndex + 1);
            }

            int lastDashIndex = componentName.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
            {
                string suffix = componentName.Substring(lastDashIndex + 1);
                if (int.TryParse(suffix, out _))
                {
                    componentName = componentName.Substring(0, lastDashIndex);
                }
            }

            return componentName.Trim();
        }

        private static ModelDoc2 ResolvePartDocByPath(SldWorks app, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                object[] docs = (object[])app.GetDocuments();
                if (docs != null)
                {
                    foreach (object d in docs)
                    {
                        var md = d as ModelDoc2;
                        if (md == null)
                        {
                            continue;
                        }

                        string p = md.GetPathName() ?? string.Empty;
                        if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                        {
                            return md;
                        }
                    }
                }
            }
            catch
            {
                // ignore and fallback to open file
            }

            if (!File.Exists(path))
            {
                return null;
            }

            int errors = 0;
            int warnings = 0;
            return app.OpenDoc6(
                path,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings) as ModelDoc2;
        }

        private void DeleteSelectedButton_Click(object sender, EventArgs e)
        {
            var variant = GetSelectedVariant();
            if (variant?.Entries == null || variant.Entries.Count == 0)
            {
                MessageBox.Show("当前机型没有记录可删。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先按住 Ctrl 或 Shift 在表格中选中一行或多行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var toRemove = new List<EquationSnapshotEntry>();
            foreach (DataGridViewRow r in grid.SelectedRows)
            {
                if (r.Tag is EquationSnapshotEntry ent)
                {
                    toRemove.Add(ent);
                }
            }

            if (toRemove.Count == 0)
            {
                return;
            }

            if (MessageBox.Show(
                    $"确认从当前机型「{variant.Name}」删除 {toRemove.Count} 条记录？",
                    "删除确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            foreach (var ent in toRemove)
            {
                variant.Entries.Remove(ent);
            }

            SaveStore();
            RefreshGrid();
            statusLabel.Text = $"已删除 {toRemove.Count} 条";
        }

        private void ExportSldprtButton_Click(object sender, EventArgs e)
        {
            TryExportSelectedSubAssemblyToSldprt(out _);
        }

        public bool TryExportSelectedSubAssemblyToSldprt(out string message)
        {
            if (InvokeRequired)
            {
                object? invokeResult = Invoke(new Func<(bool Ok, string Msg)>(() =>
                {
                    bool okInner = TryExportSelectedSubAssemblyToSldprtCore(out string msgInner);
                    return (okInner, msgInner);
                }));

                if (invokeResult is ValueTuple<bool, string> tuple)
                {
                    message = tuple.Item2;
                    return tuple.Item1;
                }

                message = "导出失败：线程调度异常";
                return false;
            }

            return TryExportSelectedSubAssemblyToSldprtCore(out message);
        }

        private bool TryExportSelectedSubAssemblyToSldprtCore(out string message)
        {
            var app = AddinStudy.GetSwApp();
            if (app == null)
            {
                message = "SolidWorks 未初始化";
                return false;
            }

            var active = app.ActiveDoc as ModelDoc2;
            if (active == null || active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                message = "请先激活装配体文档。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string assemblyPath = active.GetPathName();
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                message = "当前装配体尚未保存或源文件不存在。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string assemblyDir = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyDir))
            {
                message = "无法解析当前装配体目录。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Component2 selectedSubAssembly = TryGetSelectedSubAssemblyComponent(active);
            if (selectedSubAssembly == null)
            {
                message = "请在装配体中先选中一个子装配体组件后再导出。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string defaultName = Path.GetFileNameWithoutExtension(selectedSubAssembly.GetPathName() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(defaultName))
            {
                defaultName = NormalizeComponentName(selectedSubAssembly.Name2 ?? "导出机型");
            }

            string inputName = PromptExportName(defaultName);
            if (string.IsNullOrWhiteSpace(inputName))
            {
                message = "已取消导出";
                statusLabel.Text = message;
                return false;
            }

            string safeName = SanitizeFileName(inputName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                message = "输入的文件名无效，请重试。";
                MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string targetPath = Path.Combine(assemblyDir, safeName + ".sldprt");
            if (File.Exists(targetPath))
            {
                var r = MessageBox.Show(
                    $"目标文件已存在，是否覆盖？\n{targetPath}",
                    "覆盖确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes)
                {
                    message = "已取消覆盖导出";
                    return false;
                }
            }

            try
            {
                Debug.WriteLine($"[ExportSubAsm] 开始导出，装配体: {assemblyPath}");
                Debug.WriteLine($"[ExportSubAsm] 目标路径: {targetPath}");
                Debug.WriteLine($"[ExportSubAsm] 选中组件: Name2={selectedSubAssembly.Name2}, Path={selectedSubAssembly.GetPathName()}");

                ModelDoc2 sourceDoc = EnsureSubAssemblyDocVisible(app, selectedSubAssembly);
                if (sourceDoc == null)
                {
                    message = "无法打开选中的子装配体文档。";
                    Debug.WriteLine("[ExportSubAsm] 无法获取子装配体文档对象");
                    MessageBox.Show(message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                Debug.WriteLine($"[ExportSubAsm] sourceDoc: title={sourceDoc.GetTitle()}, type={sourceDoc.GetType()}, path={sourceDoc.GetPathName()}");
                int activateErrors = 0;
                app.ActivateDoc3(
                    sourceDoc.GetTitle(),
                    true,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref activateErrors);
                Debug.WriteLine($"[ExportSubAsm] ActivateDoc3 errors={activateErrors}");

                sourceDoc.ClearSelection2(true);
                int saveErrors = 0;
                int saveWarnings = 0;
                bool saveSuccess = sourceDoc.Extension.SaveAs(
                    targetPath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null,
                    ref saveErrors,
                    ref saveWarnings);
                bool fileExists = File.Exists(targetPath);
                long fileSize = fileExists ? new FileInfo(targetPath).Length : 0;
                Debug.WriteLine($"[ExportSubAsm] SaveAs返回={saveSuccess}, errors={saveErrors}, warnings={saveWarnings}, 文件存在={fileExists}, 文件大小={fileSize}字节");

                if (!saveSuccess || saveErrors != 0 || !fileExists || fileSize <= 0)
                {
                    message = $"导出失败。\nSaveAs返回: {saveSuccess}\n错误码: {saveErrors}\n警告码: {saveWarnings}\n文件存在: {fileExists}\n目标路径: {targetPath}";
                    Debug.WriteLine("[ExportSubAsm] " + message.Replace("\n", " | "));
                    MessageBox.Show(message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                statusLabel.Text = $"已导出: {Path.GetFileName(targetPath)}";
                message = $"已导出 SLDPRT: {targetPath}";
                AddinStudy.LastExportedSubAsmSldprtPath = targetPath;

                app.SendMsgToUser(message);
                Debug.WriteLine("[ExportSubAsm] 导出成功");
                return true;
            }
            catch (Exception ex)
            {
                message = $"导出失败: {ex.Message}";
                Debug.WriteLine($"导出机型 SLDPRT 失败: {ex.Message}");
                MessageBox.Show(message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            string result = name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            return result;
        }

        private static Component2 TryGetSelectedSubAssemblyComponent(ModelDoc2 assemblyDoc)
        {
            var selMgr = assemblyDoc.SelectionManager as SelectionMgr;
            if (selMgr == null)
            {
                return null;
            }

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                var comp = selMgr.GetSelectedObjectsComponent3(i, -1) as Component2;
                if (comp == null)
                {
                    var selectedObj = selMgr.GetSelectedObject6(i, -1);
                    comp = selectedObj as Component2;
                }

                if (comp == null)
                {
                    continue;
                }

                var model = comp.GetModelDoc2() as ModelDoc2;
                if (model != null && model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    return comp;
                }

                string compPath = comp.GetPathName() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(compPath) &&
                    compPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                {
                    return comp;
                }
            }

            return null;
        }

        private static ModelDoc2 EnsureSubAssemblyDocVisible(SldWorks app, Component2 component)
        {
            if (component == null)
            {
                return null;
            }

            var model = component.GetModelDoc2() as ModelDoc2;
            if (model != null)
            {
                return model;
            }

            string path = component.GetPathName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.WriteLine($"[ExportSubAsm] 组件路径为空或不存在: {path}");
                return null;
            }

            int errors = 0;
            int warnings = 0;
            var opened = app.OpenDoc6(
                path,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings) as ModelDoc2;
            Debug.WriteLine($"[ExportSubAsm] OpenDoc6 path={path}, errors={errors}, warnings={warnings}, opened={(opened != null)}");
            return opened;
        }

        private static string PromptExportName(string defaultName)
        {
            using (var form = new Form())
            {
                form.Text = "导出 SLDPRT";
                form.Size = new System.Drawing.Size(460, 165);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label
                {
                    Text = "请输入导出文件名（不含扩展名）：",
                    Left = 12,
                    Top = 16,
                    Width = 420
                };

                var textBox = new TextBox
                {
                    Left = 12,
                    Top = 42,
                    Width = 420,
                    Text = defaultName ?? string.Empty
                };
                textBox.SelectAll();

                var okButton = new Button
                {
                    Text = "导出",
                    Left = 276,
                    Top = 76,
                    Width = 75,
                    DialogResult = DialogResult.OK
                };

                var cancelButton = new Button
                {
                    Text = "取消",
                    Left = 357,
                    Top = 76,
                    Width = 75,
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : string.Empty;
            }
        }

        private void LoadStore()
        {
            try
            {
                if (!File.Exists(saveFilePath))
                {
                    return;
                }

                var serializer = new XmlSerializer(typeof(EquationModelStoreRoot));
                using (var fs = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var root = serializer.Deserialize(fs) as EquationModelStoreRoot;
                    if (root?.Bundles != null)
                    {
                        storeRoot = root;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取机型方程式配置失败: {ex.Message}");
            }
        }

        private void SaveStore()
        {
            try
            {
                if (storeRoot == null)
                {
                    storeRoot = new EquationModelStoreRoot();
                }

                string ap = assemblyPathTextBox?.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(ap))
                {
                    storeRoot.LastFocusedAssemblyPath = ap;
                }

                var v = GetSelectedVariant();
                if (v != null && !string.IsNullOrEmpty(v.Name))
                {
                    storeRoot.LastFocusedVariantName = v.Name;
                }

                var serializer = new XmlSerializer(typeof(EquationModelStoreRoot));
                using (var fs = new FileStream(saveFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(fs, storeRoot);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存机型方程式配置失败: {ex.Message}");
                try
                {
                    MessageBox.Show($"保存到本地失败，请检查磁盘与权限：\n{ex.Message}", "机型方程式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    [Serializable]
    public class EquationModelStoreRoot
    {
        public List<EquationAssemblyBundle> Bundles { get; set; } = new List<EquationAssemblyBundle>();

        /// <summary>上次在任务窗格中选中的装配体完整路径，用于 SolidWorks 重启后恢复界面。</summary>
        public string LastFocusedAssemblyPath { get; set; } = "";

        /// <summary>上次选中的机型名称。</summary>
        public string LastFocusedVariantName { get; set; } = "";
    }

    [Serializable]
    public class EquationAssemblyBundle
    {
        public string AssemblyPath { get; set; } = "";
        public List<EquationVariantSlot> Variants { get; set; } = new List<EquationVariantSlot>();
    }

    [Serializable]
    [XmlInclude(typeof(EquationSnapshotEntry))]
    public class EquationVariantSlot
    {
        public string Name { get; set; } = "";
        public List<EquationSnapshotEntry> Entries { get; set; } = new List<EquationSnapshotEntry>();
    }

    internal sealed class VariantListItem
    {
        public string AssemblyPath { get; set; } = string.Empty;
        public EquationVariantSlot Variant { get; set; } = new EquationVariantSlot();
        public string DisplayText { get; set; } = string.Empty;
        public string UniqueKey { get; set; } = string.Empty;
    }
}
