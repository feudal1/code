using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using cad_tools;

namespace tools
{
    public class opendwg
    {
        static public void run(ModelDoc2 swModel, SldWorks swApp)
        {

            string fullpath = swModel.GetPathName();

            string? directory = Path.GetDirectoryName(fullpath);
            if (string.IsNullOrEmpty(directory))
            {
                Console.WriteLine("错误：无法获取文件所在目录。");
                return;
            }




            var drawingDoc = (DrawingDoc)swModel;

            var swSheet = (Sheet)drawingDoc.IGetCurrentSheet();
            var swViews = (object[])swSheet.GetViews();
            var partDoc = ((SolidWorks.Interop.sldworks.View)swViews[1]).ReferencedDocument;

            string outputRoot = DwgOutputPaths.ResolveOutputRoot(directory);
            var pathSettings = DwgOutputPaths.Get();
            string outputfile;
            
            if (partDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                Debug.WriteLine($"{partDoc.GetPathName()},type:assembly");
                outputfile = Path.Combine(outputRoot, pathSettings.WeldmentFolder);
            }
            else
            {
                var thickness = get_thickness.run(partDoc);
                Debug.WriteLine($"{partDoc.GetPathName()},thickness:{thickness}");

                if (thickness == 0)
                {
                    outputfile = Path.Combine(outputRoot, pathSettings.CncFolder);
                }
                else
                {
                    string materialThickness = DwgOutputPaths.BuildMaterialThicknessFolderName(partDoc, thickness.ToString());
                    outputfile = Path.Combine(outputRoot, pathSettings.EngineeringFolder, materialThickness);
                }
            }

            string dwgFileName = Path.Combine(outputfile, Path.GetFileNameWithoutExtension(fullpath) + ".dwg");
            if (!File.Exists(dwgFileName))
            {
                // 目标 DWG 不存在时，先尝试从当前工程图导出
                dwgFileName = drw2dwg.run(swModel, swApp);
            }

            if (!File.Exists(dwgFileName))
            {
                Console.WriteLine($"错误：无法找到工程图。{dwgFileName}");
                return;
            }

            const string autoCadPath = @"C:\Program Files\Autodesk\AutoCAD 2022\acad.exe";
            const string caxaPath = @"C:\Program Files (x86)\CAXA\bin\Eb.exe";

            string? selectedApp = SelectOpenApp(autoCadPath, caxaPath);
            if (string.IsNullOrEmpty(selectedApp))
            {
                return;
            }

            if (!File.Exists(selectedApp))
            {
                MessageBox.Show($"未找到应用：{selectedApp}", "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = selectedApp,
                Arguments = $"\"{dwgFileName}\"",
                UseShellExecute = true
            });
            

        }

        private static string? SelectOpenApp(string autoCadPath, string caxaPath)
        {
            string? selectedApp = null;

            using (var form = new Form())
            using (var btnCad = new Button())
            using (var btnCaxa = new Button())
            using (var btnCancel = new Button())
            {
                form.Text = "选择打开应用";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new System.Drawing.Size(280, 90);

                btnCad.Text = "CAD";
                btnCad.SetBounds(15, 20, 75, 30);
                btnCad.Click += (_, __) =>
                {
                    selectedApp = autoCadPath;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                btnCaxa.Text = "CAXA";
                btnCaxa.SetBounds(102, 20, 75, 30);
                btnCaxa.Click += (_, __) =>
                {
                    selectedApp = caxaPath;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                btnCancel.Text = "取消";
                btnCancel.SetBounds(189, 20, 75, 30);
                btnCancel.Click += (_, __) =>
                {
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                };

                form.Controls.Add(btnCad);
                form.Controls.Add(btnCaxa);
                form.Controls.Add(btnCancel);
                form.ShowDialog();
            }

            return selectedApp;
        }
    }
}