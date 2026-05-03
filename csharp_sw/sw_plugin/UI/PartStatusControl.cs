using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksAddinStudy.Services;
using tools;

namespace SolidWorksAddinStudy
{
    /// <summary>
    /// 零件处理状态用户控件
    /// </summary>
    [ComVisible(true)]
    [Guid("36B3CA37-FEC7-4E85-9E9A-1A7561242B04")]
    [ProgId("SolidWorksAddinStudy.PartStatusControl")]
    public partial class PartStatusControl : UserControl
    {
        private DataGridView statusGrid;
        private Label infoLabel;
        private Label projectPathLabel;
        private Button refreshButton;
        private Label typeFilterLabel;
        private ComboBox typeFilterComboBox;
        
        // 存储零件状态数据
        private List<PartStatusInfo> partStatusList = new List<PartStatusInfo>();

        /// <summary>当前绑定的装配体完整路径，用于按项目持久化出图状态。</summary>
        private string boundAssemblyPath = string.Empty;
        
        // SolidWorks应用实例
        private SldWorks swApp;
        
        public PartStatusControl()
        {
            this.swApp = AddinStudy.GetSwApp();
            InitializeComponent();
        }

        public PartStatusControl(SldWorks swApp)
        {
            this.swApp = swApp;
            InitializeComponent();
        }

        public void SetSwApp(SldWorks swApp)
        {
            this.swApp = swApp;
        }

        private static void LogTaskPane(string message)
        {
            try
            {
                AddinStudy.ShowOutputWindow();
                Console.WriteLine($"[任务窗格] {message}");
            }
            catch
            {
                // 输出窗格日志失败不影响主流程
            }
        }
        
        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;
            this.MinimumSize = new System.Drawing.Size(400, 400);
            
            // 创建顶部面板
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.Padding = new Padding(10, 8, 10, 8);
            
            // 创建信息标签
            infoLabel = new Label();
            infoLabel.Text = "零件处理状态监控";
            infoLabel.Location = new System.Drawing.Point(0, 6);
            infoLabel.Size = new System.Drawing.Size(220, 18);
            infoLabel.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold);
            header.Controls.Add(infoLabel);

            projectPathLabel = new Label();
            projectPathLabel.Text = "当前装配体：未打开";
            projectPathLabel.Location = new System.Drawing.Point(0, 26);
            projectPathLabel.Size = new System.Drawing.Size(460, 18);
            projectPathLabel.Font = new System.Drawing.Font("Microsoft YaHei", 8.25F);
            projectPathLabel.ForeColor = System.Drawing.Color.DimGray;
            header.Controls.Add(projectPathLabel);
            
            // 创建刷新按钮
            refreshButton = new Button();
            refreshButton.Text = "刷新数据";
            refreshButton.Location = new System.Drawing.Point(480, 14);
            refreshButton.Size = new System.Drawing.Size(90, 30);
            refreshButton.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            refreshButton.Click += RefreshButton_Click;
            header.Controls.Add(refreshButton);

            typeFilterLabel = new Label();
            typeFilterLabel.Text = "类型筛选:";
            typeFilterLabel.Location = new System.Drawing.Point(580, 20);
            typeFilterLabel.Size = new System.Drawing.Size(60, 20);
            typeFilterLabel.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            header.Controls.Add(typeFilterLabel);

            typeFilterComboBox = new ComboBox();
            typeFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            typeFilterComboBox.Location = new System.Drawing.Point(645, 16);
            typeFilterComboBox.Size = new System.Drawing.Size(140, 24);
            typeFilterComboBox.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            header.Controls.Add(typeFilterComboBox);
            
            // 创建DataGridView显示状态
            statusGrid = new DataGridView();
            statusGrid.Dock = DockStyle.Fill;
            statusGrid.AllowUserToAddRows = false;
            statusGrid.AllowUserToDeleteRows = false;
            statusGrid.ReadOnly = true;
            statusGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            statusGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            statusGrid.MultiSelect = false;
            
            // 显式创建列，避免索引表达式在某些宿主环境下解析失败
            statusGrid.Columns.Clear();
            statusGrid.Columns.Add(CreateTextColumn("零件名称", 150));
            statusGrid.Columns.Add(CreateTextColumn("零件类型", 80));
            statusGrid.Columns.Add(CreateTextColumn("规格尺寸", 120));
            statusGrid.Columns.Add(CreateTextColumn("是否出图", 80));
            statusGrid.Columns.Add(CreateTextColumn("数量", 60));
            
            // 添加单元格点击事件
            statusGrid.CellClick += StatusGrid_CellClick;

            typeFilterComboBox.SelectedIndexChanged += TypeFilterComboBox_SelectedIndexChanged;
            RefreshTypeFilterOptions();
            
            // 添加控件（注意顺序：后添加的在上层）
            this.Controls.Add(statusGrid);
            this.Controls.Add(header);

            SyncAssemblyPathLabel();
            TryRestoreCachedStatusOnStartup();
        }

        /// <summary>顶部标签：显示当前关注的装配体（已刷新后的路径或活动文档）。</summary>
        private void SyncAssemblyPathLabel()
        {
            const string prefix = "当前装配体：";
            if (!string.IsNullOrWhiteSpace(boundAssemblyPath))
            {
                projectPathLabel.Text = prefix + Path.GetFileName(boundAssemblyPath.Trim());
                return;
            }

            ModelDoc2? active = swApp == null ? null : (ModelDoc2)swApp.ActiveDoc;
            if (active != null && active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                string p = active.GetPathName()?.Trim() ?? "";
                projectPathLabel.Text = string.IsNullOrEmpty(p)
                    ? prefix + "(请先保存装配体)"
                    : prefix + Path.GetFileName(p);
                return;
            }

            projectPathLabel.Text = prefix + "未打开装配体";
        }

        private void TryRestoreCachedStatusOnStartup()
        {
            try
            {
                string candidateAssemblyPath = string.Empty;
                List<PartStatusInfo> cached = new List<PartStatusInfo>();

                ModelDoc2 activeDoc = swApp == null ? null : (ModelDoc2)swApp.ActiveDoc;
                if (activeDoc != null &&
                    activeDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    candidateAssemblyPath = activeDoc.GetPathName()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(candidateAssemblyPath))
                    {
                        cached = PartStatusProjectStore.LoadSnapshot(candidateAssemblyPath);
                    }
                }

                if (cached.Count == 0 &&
                    PartStatusProjectStore.TryLoadLatestSnapshot(out string latestAssemblyPath, out List<PartStatusInfo> latest))
                {
                    candidateAssemblyPath = latestAssemblyPath;
                    cached = latest;
                }

                if (cached.Count == 0)
                {
                    return;
                }

                boundAssemblyPath = candidateAssemblyPath?.Trim() ?? string.Empty;
                LoadFromBomData(cached);
                SyncAssemblyPathLabel();
                infoLabel.Text = $"零件处理状态监控 (已恢复 {partStatusList.Count} 条缓存)";
                //LogTaskPane($"启动时已恢复零件状态缓存：{partStatusList.Count} 条");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动恢复零件状态缓存失败: {ex.Message}");
            }
        }

        /// <summary>本次刷新使用的装配体路径：始终为当前活动、已保存的装配体。</summary>
        private static bool TryGetActiveSavedAssemblyPath(ModelDoc2 activeAssembly, out string assemblyPath, out string errorMessage)
        {
            assemblyPath = string.Empty;
            errorMessage = string.Empty;

            if (activeAssembly == null || activeAssembly.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                errorMessage = "当前文档不是装配体。";
                return false;
            }

            assemblyPath = activeAssembly.GetPathName() ?? string.Empty;
            if (string.IsNullOrEmpty(assemblyPath))
            {
                errorMessage = "请先保存装配体文件，以便按装配体路径保存出图状态。";
                return false;
            }

            return true;
        }
        
        /// <summary>
        /// 刷新按钮点击事件
        /// </summary>
        private void RefreshButton_Click(object sender, EventArgs e)
        {
            try
            {
                LogTaskPane("开始执行：刷新数据");
                if (swApp == null)
                {
                    MessageBox.Show("SolidWorks未连接", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
                if (swModel == null)
                {
                    MessageBox.Show("没有打开的文档", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (swModel.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocASSEMBLY)
                {
                    MessageBox.Show("当前文档不是装配体", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!TryGetActiveSavedAssemblyPath(swModel, out string assemblyPath, out string pathError))
                {
                    MessageBox.Show(pathError, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Debug.WriteLine("开始为任务窗格采集 BOM 行（独立于 asm2bom 导出缓存）...");
                LogTaskPane("开始采集 BOM 行（任务窗格）");
                infoLabel.Text = "正在刷新数据...";
                refreshButton.Enabled = false;

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        List<TaskPaneBomPartRow>? rows = await asm2bom.CollectPartRowsForTaskPaneAsync(swApp, swModel);
                        if (rows == null)
                        {
                            throw new InvalidOperationException("采集失败（装配体或 BOM 插入异常）");
                        }

                        var partInfoList = new List<PartStatusInfo>();
                        foreach (TaskPaneBomPartRow r in rows)
                        {
                            partInfoList.Add(new PartStatusInfo(r.PartName, r.PartType, r.Dimension, r.IsDrawn, r.Quantity));
                        }

                        PartStatusProjectStore.MergePersistedDrawn(assemblyPath, partInfoList);

                        this.Invoke(new Action(() =>
                        {
                            boundAssemblyPath = assemblyPath;
                            SyncAssemblyPathLabel();
                            LoadFromBomData(partInfoList);
                            refreshButton.Enabled = true;
                            infoLabel.Text = $"零件处理状态监控 (共 {partStatusList.Count} 条记录)";
                            LogTaskPane($"刷新完成：共 {partStatusList.Count} 条记录");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"刷新任务窗格 BOM 失败: {ex.Message}");
                        this.Invoke(new Action(() =>
                        {
                            infoLabel.Text = $"刷新失败: {ex.Message}";
                            refreshButton.Enabled = true;
                            LogTaskPane($"刷新失败：{ex.Message}");
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"刷新按钮点击失败: {ex.Message}");
                MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                refreshButton.Enabled = true;
                LogTaskPane($"刷新异常：{ex.Message}");
            }
        }

        private void TypeFilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 从命令刷新BOM数据（供外部调用）
        /// </summary>
        public void RefreshFromCommand()
        {
            try
            {
                LogTaskPane("开始执行：RefreshFromCommand");
                if (swApp == null)
                {
                    MessageBox.Show("SolidWorks未连接", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
                if (swModel == null)
                {
                    MessageBox.Show("没有打开的文档", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                
                if (swModel.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocASSEMBLY)
                {
                    MessageBox.Show("当前文档不是装配体", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!TryGetActiveSavedAssemblyPath(swModel, out string assemblyPath, out string pathError))
                {
                    MessageBox.Show(pathError, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Debug.WriteLine("开始为任务窗格采集 BOM 行（独立于 asm2bom 导出缓存）...");
                LogTaskPane("开始采集 BOM 行（命令触发）");
                infoLabel.Text = "正在刷新数据...";
                refreshButton.Enabled = false;

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        List<TaskPaneBomPartRow>? rows = await asm2bom.CollectPartRowsForTaskPaneAsync(swApp, swModel);
                        if (rows == null)
                        {
                            throw new InvalidOperationException("采集失败（装配体或 BOM 插入异常）");
                        }

                        var partInfoList = new List<PartStatusInfo>();
                        foreach (TaskPaneBomPartRow r in rows)
                        {
                            partInfoList.Add(new PartStatusInfo(r.PartName, r.PartType, r.Dimension, r.IsDrawn, r.Quantity));
                        }

                        PartStatusProjectStore.MergePersistedDrawn(assemblyPath, partInfoList);

                        this.Invoke(new Action(() =>
                        {
                            boundAssemblyPath = assemblyPath;
                            SyncAssemblyPathLabel();
                            LoadFromBomData(partInfoList);
                            refreshButton.Enabled = true;
                            infoLabel.Text = $"零件处理状态监控 (共 {partStatusList.Count} 条记录)";
                            LogTaskPane($"RefreshFromCommand 完成：共 {partStatusList.Count} 条记录");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"刷新任务窗格 BOM 失败: {ex.Message}");
                        this.Invoke(new Action(() =>
                        {
                            infoLabel.Text = $"刷新失败: {ex.Message}";
                            refreshButton.Enabled = true;
                            LogTaskPane($"RefreshFromCommand 失败：{ex.Message}");
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshFromCommand 失败: {ex.Message}");
                MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                refreshButton.Enabled = true;
                LogTaskPane($"RefreshFromCommand 异常：{ex.Message}");
            }
        }
        
        /// <summary>
        /// 单元格点击事件 - 选中零件
        /// </summary>
        private void StatusGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return; // 只响应零件名称列的点击
            
            try
            {
                string partName = statusGrid.Rows[e.RowIndex].Cells[0].Value?.ToString();
                if (string.IsNullOrEmpty(partName) || swApp == null) return;
                
                ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
                if (swModel == null || swModel.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocASSEMBLY) return;
                
                AssemblyDoc swAssembly = (AssemblyDoc)swModel;
                
                // 遍历组件查找匹配的零件
                object[] components = (object[])swAssembly.GetComponents(false);
                foreach (object compObj in components)
                {
                    Component2 component = (Component2)compObj;
                    string componentName = component.Name2;
                    
                    // 去掉"/"号及之前的文字
                    int slashIndex = componentName.LastIndexOf('/');
                    if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
                    {
                        componentName = componentName.Substring(slashIndex + 1);
                    }
                    
                    // 去掉末尾的"-数字"部分
                    int lastDashIndex = componentName.LastIndexOf('-');
                    if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
                    {
                        string suffix = componentName.Substring(lastDashIndex + 1);
                        if (int.TryParse(suffix, out _))
                        {
                            componentName = componentName.Substring(0, lastDashIndex);
                        }
                    }
                    
                    // 找到匹配的组件并选中
                    if (componentName.Equals(partName, StringComparison.OrdinalIgnoreCase))
                    {
                        component.Select(false);
                        swModel.ViewZoomtofit2();
                        Debug.WriteLine($"已选中零件: {partName}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选中零件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 从 BOM 数据加载零件状态
        /// </summary>
        public void LoadFromBomData(List<PartStatusInfo> bomData)
        {
            partStatusList.Clear();
                    
            foreach (var bomItem in bomData)
            {
                partStatusList.Add(bomItem);
            }

            RefreshTypeFilterOptions();
            RefreshStatusDisplay();
            if (!string.IsNullOrWhiteSpace(boundAssemblyPath))
            {
                PartStatusProjectStore.SaveSnapshot(boundAssemblyPath, partStatusList);
            }
        }
        
        /// <summary>
        /// 获取零件数量
        /// </summary>
        public int GetPartCount()
        {
            return partStatusList.Count;
        }
        
        /// <summary>
        /// 更新零件的出图状态
        /// </summary>
        public void UpdatePartDrawnStatus(string partName, string isDrawn)
        {
            var part = partStatusList.Find(p => p.PartName == partName);
            if (part != null)
            {
                part.IsDrawn = isDrawn;
                if (!string.IsNullOrWhiteSpace(boundAssemblyPath))
                {
                    PartStatusProjectStore.SavePartDrawn(boundAssemblyPath, partName, isDrawn);
                    PartStatusProjectStore.SaveSnapshot(boundAssemblyPath, partStatusList);
                }

                RefreshStatusDisplay();
                Debug.WriteLine($"已更新零件 '{partName}' 的出图状态为: {isDrawn}");
            }
        }
        
        /// <summary>
        /// 删除零件
        /// </summary>
        public void RemovePart(string partName)
        {
            var part = partStatusList.Find(p => p.PartName == partName);
            if (part != null)
            {
                partStatusList.Remove(part);
                if (!string.IsNullOrWhiteSpace(boundAssemblyPath))
                {
                    PartStatusProjectStore.SaveSnapshot(boundAssemblyPath, partStatusList);
                }

                RefreshStatusDisplay();
                Debug.WriteLine($"已从任务窗格移除零件: {partName}");
            }
        }
        
        /// <summary>
        /// 获取指定类型的零件列表
        /// </summary>
        /// <param name="partType">零件类型（如：钣金件、管件、其他）</param>
        /// <returns>符合条件的零件列表</returns>
        public List<PartStatusInfo> GetPartsByType(string partType)
        {
            return partStatusList.FindAll(p => p.PartType == partType);
        }
        
        /// <summary>
        /// 刷新状态显示
        /// </summary>
        private void RefreshStatusDisplay()
        {
            if (statusGrid == null)
            {
                return;
            }

            statusGrid.Rows.Clear();

            string selectedType = typeFilterComboBox?.SelectedItem?.ToString() ?? "全部";
            int visibleCount = 0;

            foreach (var status in partStatusList)
            {
                if (!string.Equals(selectedType, "全部", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(status.PartType ?? string.Empty, selectedType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int rowIndex = statusGrid.Rows.Add();
                statusGrid.Rows[rowIndex].Cells[0].Value = status.PartName;
                statusGrid.Rows[rowIndex].Cells[1].Value = status.PartType;
                statusGrid.Rows[rowIndex].Cells[2].Value = status.Dimension;
                statusGrid.Rows[rowIndex].Cells[3].Value = status.IsDrawn;
                statusGrid.Rows[rowIndex].Cells[4].Value = status.Quantity;
                
                // 根据是否出图设置颜色
                if (status.IsDrawn == "已出图")
                {
                    statusGrid.Rows[rowIndex].DefaultCellStyle.BackColor = System.Drawing.Color.LightGreen;
                }
                else
                {
                    statusGrid.Rows[rowIndex].DefaultCellStyle.BackColor = System.Drawing.Color.LightYellow;
                }

                visibleCount++;
            }

            infoLabel.Text = $"零件处理状态监控 (显示 {visibleCount} / 总计 {partStatusList.Count})";
        }

        private void RefreshTypeFilterOptions()
        {
            if (typeFilterComboBox == null)
            {
                return;
            }

            string previous = typeFilterComboBox.SelectedItem?.ToString() ?? "全部";
            var uniqueTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in partStatusList)
            {
                string type = item.PartType?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(type))
                {
                    uniqueTypes.Add(type);
                }
            }

            typeFilterComboBox.BeginUpdate();
            try
            {
                typeFilterComboBox.Items.Clear();
                typeFilterComboBox.Items.Add("全部");
                foreach (string type in uniqueTypes)
                {
                    typeFilterComboBox.Items.Add(type);
                }
            }
            finally
            {
                typeFilterComboBox.EndUpdate();
            }

            if (typeFilterComboBox.Items.Contains(previous))
            {
                typeFilterComboBox.SelectedItem = previous;
            }
            else
            {
                typeFilterComboBox.SelectedIndex = 0;
            }
        }

        private DataGridViewTextBoxColumn CreateTextColumn(string name, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name,
                Width = width,
                ReadOnly = true
            };
        }

        private string NormalizeComponentName(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return string.Empty;
            }

            // 去掉"/"号及之前的文字
            int slashIndex = componentName.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
            {
                componentName = componentName.Substring(slashIndex + 1);
            }

            // 去掉末尾的"-数字"实例后缀
            int lastDashIndex = componentName.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
            {
                string suffix = componentName.Substring(lastDashIndex + 1);
                if (int.TryParse(suffix, out _))
                {
                    componentName = componentName.Substring(0, lastDashIndex);
                }
            }

            return componentName;
        }
    }

}
