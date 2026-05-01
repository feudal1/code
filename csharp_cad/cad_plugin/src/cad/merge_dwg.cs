using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;

using System.IO;

namespace cad_tools
{
    public class merge_dwg
    {
        /// <summary>
        /// 合并 DWG 文件，返回当前图形的最大 X 和最大 Y 值
        /// </summary>
        /// <param name="sourcePath">源文件路径</param>
        /// <param name="x">插入点 X 坐标</param>
        /// <param name="y">插入点 Y 坐标</param>
        /// <returns>返回 [maxX, maxY]，失败返回 null</returns>
        static public double[]? run(string sourcePath, double x, double y, bool isDim)
        {
            if (sourcePath.Contains(".dwl2") || sourcePath.Contains(".dwl"))
            {
                
                return null;
            }
            AcadApplication? acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
                return null;
            }

            AcadDocument? acadDoc = acadApp.ActiveDocument;
            if (acadDoc == null)
            {
                Console.WriteLine("错误：没有活动的 AutoCAD 文档。");
                return null;
            }

            try
            {
        

                string xrefPath = sourcePath;
                string baseName = Path.GetFileNameWithoutExtension(xrefPath);
                string xrefName = isDim ? $"{baseName}_{Guid.NewGuid():N}" : baseName;

                object insertionPoint = new double[] { 0, 0, 0.0 };
                var reference=acadDoc.ModelSpace.AttachExternalReference(
                    xrefPath,
                    xrefName,
                    insertionPoint,
                    1.0, 1.0, 1.0,
                    0.0,
                    false
                );
               
                reference.GetBoundingBox(out object minPointObj, out object maxPointObj);
                       
                Console.WriteLine($"获取外部参照边界框：{xrefName}");
                Console.WriteLine($"成功附加外部参照：{xrefName}，插入位置 X = {x:F2}, Y = {y:F2}");
                                
                double[] minPoint = (double[])minPointObj;
                double[] maxPoint = (double[])maxPointObj;
                                
                Console.WriteLine($"  最小点：({minPoint[0]:F2}, {minPoint[1]:F2}, {minPoint[2]:F2})");
                Console.WriteLine($"  最大点：({maxPoint[0]:F2}, {maxPoint[1]:F2}, {maxPoint[2]:F2})");
                   double offsetX = -minPoint[0];
                double offsetY = -minPoint[1];
                double offsetZ = -minPoint[2];
                
                // Move 方法需要一个点数组作为参数
             
                    object fromPoint = new double[] { 0.0, 0.0, 0.0 };
                object toPoint = new double[] { offsetX+x, offsetY+y, offsetZ };
                reference.Move(fromPoint, toPoint);
                // 归零操作：计算相对于插入点的实际尺寸
                // 因为插入点是按原点 (0,0,0) 插入的，需要减去最小点的偏移
                double relativeMaxX = maxPoint[0] - minPoint[0] + x;
                double relativeMaxY = maxPoint[1] - minPoint[1] + y;
                                
                Console.WriteLine($"  归零后尺寸：宽={relativeMaxX:F2}, 高={relativeMaxY:F2}");
                acadDoc.Blocks.Item(xrefName).Bind(false);
                    
                return new double[] { relativeMaxX, relativeMaxY };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"操作失败：{ex.Message},{sourcePath}");
                if (ex.InnerException != null)
                    Console.WriteLine($"内部异常：{ex.InnerException.Message}");
            }
            return null;
        }
    }
}