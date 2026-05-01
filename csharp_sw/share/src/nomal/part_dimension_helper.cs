using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    /// <summary>
    /// 零件尺寸工具类 - 用于获取零件的长宽高尺寸
    /// </summary>
    public class PartDimensionHelper
    {
        /// <summary>
        /// 获取零件的边界框尺寸（长、宽、高）
        /// </summary>
        /// <param name="partDoc">零件文档对象</param>
        /// <returns>包含长、宽、高的元组 (length, width, height)，单位为毫米</returns>
        public static (double length, double width, double height) GetPartDimensions(PartDoc partDoc)
        {
            try
            {
                if (partDoc == null)
                {
                    Console.WriteLine("错误：零件文档为空");
                    return (0, 0, 0);
                }

                // 使用 GetPartBox 方法获取边界框
                // 参数 true 表示返回用户单位（通常是米），false 表示系统单位
                object boxObj = partDoc.GetPartBox(true);
                
                if (boxObj == null)
                {
                    Console.WriteLine("警告：无法获取零件边界框");
                    return (0, 0, 0);
                }

                double[] boxArray = (double[])boxObj;
                
                if (boxArray == null || boxArray.Length < 6)
                {
                    Console.WriteLine("警告：边界框数据格式不正确");
                    return (0, 0, 0);
                }

                // boxArray 包含 [Xmin, Ymin, Zmin, Xmax, Ymax, Zmax]
                // 计算长宽高（单位已经是用户单位，通常是米，需要转换为毫米）
                double length = Math.Abs(boxArray[3] - boxArray[0]) * 1000.0; // X方向
                double width = Math.Abs(boxArray[4] - boxArray[1]) * 1000.0;  // Y方向
                double height = Math.Abs(boxArray[5] - boxArray[2]) * 1000.0; // Z方向

                return (Math.Round(length, 2), Math.Round(width, 2), Math.Round(height, 2));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取零件尺寸时出错：{ex.Message}");
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// 根据零件名称获取零件文档并返回其尺寸
        /// </summary>
        /// <param name="swApp">SolidWorks应用程序对象</param>
        /// <param name="partName">零件名称（不含路径和扩展名）</param>
        /// <returns>包含长、宽、高的元组 (length, width, height)，单位为毫米</returns>
        public static (double length, double width, double height) GetPartDimensionsByName(SldWorks swApp, string partName)
        {
            try
            {
                if (swApp == null || string.IsNullOrEmpty(partName))
                {
                    Console.WriteLine("错误：参数无效");
                    return (0, 0, 0);
                }

                // 尝试查找已打开的文档
                ModelDoc2 modelDoc = null;
                
                // 遍历所有打开的文档查找匹配的零件
                object[] docs = (object[])swApp.GetDocuments();
                foreach (object docObj in docs)
                {
                    ModelDoc2 doc = (ModelDoc2)docObj;
                    if (doc != null && doc.GetType() == (int)swDocumentTypes_e.swDocPART)
                    {
                        string docTitle = System.IO.Path.GetFileNameWithoutExtension(doc.GetTitle());
                        if (docTitle.Equals(partName, StringComparison.OrdinalIgnoreCase))
                        {
                            modelDoc = doc;
                            break;
                        }
                    }
                }

                // 如果没有找到已打开的文档，尝试从文件系统查找
                if (modelDoc == null)
                {
                    // 这里可以添加从文件系统查找零件的逻辑
                    Console.WriteLine($"警告：未找到已打开的零件 '{partName}'");
                    return (0, 0, 0);
                }

                PartDoc partDoc = (PartDoc)modelDoc;
                return GetPartDimensions(partDoc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"根据名称获取零件尺寸时出错：{ex.Message}");
                return (0, 0, 0);
            }
        }
    }
}