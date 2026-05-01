using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SolidWorksAddinStudy
{
    [ComVisible(true)]
    [Guid("A56C9D50-C8DF-4D9A-8AE4-0ECAF2BE09CC")]
    [ProgId("SolidWorksAddinStudy.MachineProjectTaskPaneControl")]
    public class MachineProjectTaskPaneControl : UserControl
    {
        private TextBox projectNameTextBox;
        private Button createProjectButton;
        private Button gotoFlowPageButton;
        private Button gotoPartStatusPageButton;
        private Label statusLabel;
        private readonly Panel pageHostPanel;
        private readonly Control managerPage;
        private readonly WorkProjectTaskPaneControl workflowPage;
        private readonly PartStatusControl partStatusPage;
        private readonly Panel workflowContainer;
        private readonly Panel partStatusContainer;

        public MachineProjectTaskPaneControl()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new System.Drawing.Size(320, 220);

            pageHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0)
            };

            managerPage = BuildManagerPage();
            workflowPage = new WorkProjectTaskPaneControl { Dock = DockStyle.Fill };
            workflowPage.PromptFollowUpRemindersAtStartup();
            SldWorks sw = AddinStudy.GetSwApp();
            partStatusPage = new PartStatusControl(sw) { Dock = DockStyle.Fill };
            workflowContainer = BuildChildPageContainer("项目流程", workflowPage);
            partStatusContainer = BuildChildPageContainer("零件状态", partStatusPage);

            pageHostPanel.Controls.Add(managerPage);
            pageHostPanel.Controls.Add(workflowContainer);
            pageHostPanel.Controls.Add(partStatusContainer);
            Controls.Add(pageHostPanel);

            ShowManagerPage();
        }

        private Control BuildManagerPage()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleLabel = new Label
            {
                Text = "项目管理（母窗格）",
                AutoSize = true,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold)
            };

            var inputLabel = new Label
            {
                Text = "项目名：",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray
            };

            projectNameTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                MaxLength = 128
            };
            projectNameTextBox.KeyDown += ProjectNameTextBox_KeyDown;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true
            };

            createProjectButton = new Button
            {
                Text = "新建项目",
                AutoSize = true
            };
            createProjectButton.Click += CreateProjectButton_Click;

            gotoFlowPageButton = new Button
            {
                Text = "进入项目流程",
                AutoSize = true
            };
            gotoFlowPageButton.Click += (sender, e) => ShowWorkflowPage();

            gotoPartStatusPageButton = new Button
            {
                Text = "进入零件状态",
                AutoSize = true
            };
            gotoPartStatusPageButton.Click += (sender, e) => ShowPartStatusPage();

            buttonPanel.Controls.Add(createProjectButton);
            buttonPanel.Controls.Add(gotoFlowPageButton);
            buttonPanel.Controls.Add(gotoPartStatusPageButton);

            statusLabel = new Label
            {
                Text = "项目窗格内可切换：母窗格 / 项目流程 / 零件状态。",
                AutoSize = true,
                ForeColor = System.Drawing.Color.DimGray
            };

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(inputLabel, 0, 1);
            root.Controls.Add(projectNameTextBox, 0, 2);
            root.Controls.Add(buttonPanel, 0, 3);
            root.Controls.Add(statusLabel, 0, 4);
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

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Left = 0,
                Top = 10,
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold)
            };

            var backButton = new Button
            {
                Text = "返回项目管理",
                Width = 110,
                Height = 26,
                Left = 140,
                Top = 6
            };
            backButton.Click += (sender, e) => ShowManagerPage();

            header.Controls.Add(titleLabel);
            header.Controls.Add(backButton);
            panel.Controls.Add(body);
            panel.Controls.Add(header);
            return panel;
        }

        private void ProjectNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            CreateProjectFromInput();
        }

        private void CreateProjectButton_Click(object sender, EventArgs e)
        {
            CreateProjectFromInput();
        }

        private void CreateProjectFromInput()
        {
            string projectName = (projectNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(projectName))
            {
                MessageBox.Show("请输入项目名", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (workflowPage == null)
            {
                MessageBox.Show("项目流程页面尚未初始化。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ok = workflowPage.TryCreateProjectFromManager(projectName, out string message);
            statusLabel.Text = message;
            if (ok)
            {
                projectNameTextBox.Clear();
                ShowWorkflowPage();
            }
        }

        public WorkProjectTaskPaneControl GetWorkProjectPageControl()
        {
            return workflowPage;
        }

        public PartStatusControl GetPartStatusPageControl()
        {
            return partStatusPage;
        }

        public void ShowManagerPage()
        {
            managerPage.BringToFront();
            statusLabel.Text = "当前页面：项目管理母窗格。";
        }

        public void ShowWorkflowPage()
        {
            workflowContainer.BringToFront();
        }

        public void ShowPartStatusPage()
        {
            partStatusContainer.BringToFront();
        }
    }
}
