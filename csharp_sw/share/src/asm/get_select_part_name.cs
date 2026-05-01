using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Getselectpartname
    {
        static public void run(ModelDoc2 swModel)
        {
            try
            {
                var swSelMgr = (SelectionMgr)swModel.SelectionManager;
               int selCount = swSelMgr.GetSelectedObjectCount(); // 获取选中对象的数量
  Console.WriteLine("获取选中对象的数量:"+selCount );
for (int i = 1; i <= selCount; i++)
{
    object selectedObj = swSelMgr.GetSelectedObject(i); // 获取第 i 个选中的对象
    int selType = swSelMgr.GetSelectedObjectType(i);   // 获取选中对象的类型

    // 判断是否为组件（零件）
    if (selType == (int)swSelectType_e.swSelCOMPONENTS)
    {
        Component2 component = (Component2)selectedObj;
        var doc=(ModelDoc2)component.GetModelDoc2();
        string docname=doc.GetPathName().Replace(@"\", "/");
        Console.WriteLine($"选中的零件名称: {docname}");
    }
    else
    {
        Console.WriteLine("请选择一个零件。");
    }
}

            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}